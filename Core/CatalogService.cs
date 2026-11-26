using System.Text.Json;

namespace NexoLoL.Core;

public sealed class CatalogService(string directory,RemoteData remote)
{
    readonly SemaphoreSlim imageGate=new(6);
    public bool FromCache {get;private set;}
    public async Task<Catalog> LoadAsync(CancellationToken ct=default)
    {
        string path=Path.Combine(directory,"catalog.json");
        try {
            var versions=JsonSerializer.Deserialize<List<string>>(await remote.TextAsync("https://ddragon.leagueoflegends.com/api/versions.json",ct));
            string version=versions?.FirstOrDefault()??throw new InvalidDataException("Patch não disponível.");
            if(File.Exists(path)) {
                try {var saved=JsonSerializer.Deserialize<Catalog>(await File.ReadAllTextAsync(path,ct));if(saved?.Version==version && saved.Champions.Count>100 && saved.Items.Count>100 && saved.Spells is {Count:>5})return saved;}
                catch(JsonException) {}
            }
            string root=$"https://ddragon.leagueoflegends.com/cdn/{version}/data/pt_BR/";
            var tasks=new[]{remote.TextAsync(root+"champion.json",ct),remote.TextAsync(root+"item.json",ct),remote.TextAsync(root+"runesReforged.json",ct),remote.TextAsync(root+"summoner.json",ct)};
            await Task.WhenAll(tasks);var catalog=new Catalog{Version=version};
            using(var j=JsonDocument.Parse(tasks[0].Result))foreach(var p in j.RootElement.GetProperty("data").EnumerateObject()) {
                var c=p.Value;catalog.Champions.Add(new(int.Parse(c.GetProperty("key").GetString()!),c.GetProperty("id").GetString()!,c.GetProperty("name").GetString()!,c.GetProperty("title").GetString()!));
            }
            catalog.Champions=catalog.Champions.OrderBy(c=>c.Name).ToList();
            using(var j=JsonDocument.Parse(tasks[1].Result))foreach(var p in j.RootElement.GetProperty("data").EnumerateObject()) {
                var i=p.Value;int id=int.Parse(p.Name);
                catalog.Items[id]=new(id,i.GetProperty("name").GetString()!,BuildParsers.Clean(i.GetProperty("description").GetString()!),i.GetProperty("gold").GetProperty("total").GetInt32(),$"https://ddragon.leagueoflegends.com/cdn/{version}/img/item/{i.GetProperty("image").GetProperty("full").GetString()}");
            }
            using(var j=JsonDocument.Parse(tasks[2].Result))foreach(var style in j.RootElement.EnumerateArray()) {
                int sid=style.GetProperty("id").GetInt32();catalog.Runes[sid]=new(sid,style.GetProperty("name").GetString()!,"",RuneUrl(style),sid);
                foreach(var slot in style.GetProperty("slots").EnumerateArray())foreach(var rune in slot.GetProperty("runes").EnumerateArray()) {
                    int rid=rune.GetProperty("id").GetInt32();catalog.Runes[rid]=new(rid,rune.GetProperty("name").GetString()!,BuildParsers.Clean(rune.GetProperty("longDesc").GetString()!),RuneUrl(rune),sid);
                }
            }
            using(var j=JsonDocument.Parse(tasks[3].Result))foreach(var property in j.RootElement.GetProperty("data").EnumerateObject()){
                var spell=property.Value;int id=int.Parse(spell.GetProperty("key").GetString()!);
                catalog.Spells[id]=new(id,spell.GetProperty("name").GetString()!,BuildParsers.Clean(spell.GetProperty("description").GetString()!),$"https://ddragon.leagueoflegends.com/cdn/{version}/img/spell/{spell.GetProperty("image").GetProperty("full").GetString()}");
            }
            foreach(var (id,name) in new[]{(5001,"Vida"),(5002,"Armadura"),(5003,"Resistência mágica"),(5005,"Velocidade de ataque"),(5007,"Aceleração de habilidade"),(5008,"Força adaptativa"),(5010,"Velocidade de movimento"),(5011,"Vida por nível"),(5013,"Tenacidade e resistência a lentidão")})catalog.Runes[id]=new(id,name,"Fragmento de atributo","",0);
            await Storage.WriteAsync(path,JsonSerializer.Serialize(catalog),ct);return catalog;
        }catch(Exception e) when(e is HttpRequestException or IOException or JsonException or TimeoutException or TaskCanceledException) {
            if(ct.IsCancellationRequested)throw;
            try {var saved=JsonSerializer.Deserialize<Catalog>(await File.ReadAllTextAsync(path,ct));if(saved is not null && saved.Champions.Count>100 && saved.Items.Count>100){FromCache=true;return saved;}}
            catch(Exception err) when(err is IOException or JsonException) {}
            throw new InvalidDataException("Não foi possível carregar o catálogo do LoL. Verifique a internet e tente novamente.",e);
        }
    }
    static string RuneUrl(JsonElement e)=>"https://ddragon.leagueoflegends.com/cdn/img/"+e.GetProperty("icon").GetString();
    public async Task<byte[]?> IconAsync(string url,CancellationToken ct=default)
    {
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Host!="ddragon.leagueoflegends.com")return null;
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)));
        string path=Path.Combine(directory,"icons",hash+".png");
        await imageGate.WaitAsync(ct);
        try {
            if(File.Exists(path))return await File.ReadAllBytesAsync(path,ct);
            var bytes=await remote.BytesAsync(url,2_000_000,ct);Directory.CreateDirectory(Path.GetDirectoryName(path)!);await File.WriteAllBytesAsync(path,bytes,ct);return bytes;
        }catch(Exception e) when(e is HttpRequestException or IOException or TimeoutException or TaskCanceledException) {return null;}
        finally {imageGate.Release();}
    }
}
