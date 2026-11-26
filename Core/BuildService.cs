using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexoLoL.Core;

public sealed class BuildService(string directory, Func<BuildQuery,CancellationToken,Task<string>> fetch)
{
    public static readonly TimeSpan FreshFor=TimeSpan.FromHours(6);
    public static readonly TimeSpan KeepFor=TimeSpan.FromDays(7);
    public async Task<BuildResult> GetAsync(BuildQuery query,bool refresh=false,CancellationToken ct=default)
    {
        string name=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("schema2:"+query.CacheKey)))+".json";
        string path=Path.Combine(directory,name);
        BuildDocument? cached=null;
        try {
            if(File.Exists(path)) {
                var d=JsonSerializer.Deserialize<BuildDocument>(await File.ReadAllTextAsync(path,ct));
                if(d is not null && d.Query==query && Valid(d) &&
                    (query.Patch=="" || BuildParsers.SamePatch(d.Patch,query.Patch)) &&
                    d.FetchedAt<=DateTimeOffset.UtcNow.AddMinutes(5) && DateTimeOffset.UtcNow-d.FetchedAt<KeepFor)cached=d;
            }
        }catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException or NullReferenceException) { /* Cache inválido não bloqueia a rede. */ }
        if(!refresh && cached is not null && DateTimeOffset.UtcNow-cached.FetchedAt<FreshFor)
            return new(cached,true,false,"Cache recente · mesmo campeão, modo e patch");
        try {
            string html=await fetch(query,ct);
            ct.ThrowIfCancellationRequested();
            var d=query.Mode==GameMode.Mayhem?BuildParsers.ParseMayhem(html,query):BuildParsers.ParseLolalytics(html,query);
            try { await Storage.WriteAsync(path,JsonSerializer.Serialize(d),ct); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) {return new(d,false,false,"Dados carregados · não foi possível salvar cache");}
            return new(d,false,false,"Fonte consultada agora");
        }catch(Exception e) when((e is HttpRequestException or IOException or InvalidDataException or TimeoutException or OperationCanceledException) && !ct.IsCancellationRequested) {
            if(cached is null)throw;
            return new(cached,true,true,"Fonte indisponível · mostrando última build validada deste contexto");
        }
    }
    static bool Valid(BuildDocument d)=>!string.IsNullOrWhiteSpace(d.Source) && !string.IsNullOrWhiteSpace(d.Patch) &&
        Uri.TryCreate(d.SourceUrl,UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.Host is "lolalytics.com" or "lazyelo.com" &&
        d.Variants is {Count:>0} && d.Variants.All(v=>v is not null && v.Name is not null && v.SkillPriority is not null && v.SkillOrder is not null && v.Start is {Count:>0} && v.Core is {Count:>0} && v.Alternatives is not null &&
            v.Start.Concat(v.Core).Concat(v.Alternatives).Concat(v.Boots??[]).All(i=>i is not null && i.Id>0 && i.Count>0) &&
            (v.Runes is null || v.Runes.Perks is {Count:>0}));
}
