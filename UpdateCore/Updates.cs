using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexoLoL.Updates;

public record ReleaseManifest(int Format,string Product,string Version,string Package,string Sha256,long Size,string EntryPoint,string Published,string Signature,string ExeSha256="");
public record InstalledRelease(string Version,string EntryPoint,string ExeSha256,string? PreviousVersion=null);
public record UpdateResult(bool Updated,string Version,string Message);
public sealed class UpdateManager(string installation,string feed,string publicKey)
{
    const long MaxPackage=64L*1024*1024,MaxExtracted=128L*1024*1024;
    string Root=>Path.GetFullPath(installation);
    public InstalledRelease? Current()
    {
        try{var value=JsonSerializer.Deserialize<InstalledRelease>(File.ReadAllText(Path.Combine(Root,"current.json")));if(value is not null && ReleaseSecurity.Version(value.Version) is not null && value.EntryPoint=="BardoApp.exe")return value;}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){}
        return null;
    }
    public string Executable()
    {
        var current=Current()??throw new InvalidDataException("Nenhuma versão válida do Bardola está instalada.");
        return VerifiedExecutable(current.Version);
    }
    public string? ReadyVersionAfter(Version running)
    {
        var installed=Current();
        if(installed is null || Version.Parse(installed.Version)<=running)return null;
        return IsUsable(installed.Version)?installed.Version:null;
    }
    public async Task<UpdateResult> CheckAsync(CancellationToken ct=default,bool reinstallSignedBuild=false)
    {
        ct.ThrowIfCancellationRequested();Directory.CreateDirectory(Root);
        using var mutex=await Lock(ct);
        var current=Current();byte[] manifestBytes;
        try{manifestBytes=await Read("latest.json",256_000,ct);}
        catch(Exception e) when(e is FileNotFoundException or DirectoryNotFoundException){return new(false,current?.Version??"","Canal de atualização ainda sem versão publicada.");}
        ReleaseManifest release;
        try{release=JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes)??throw new InvalidDataException("Manifesto vazio.");}
        catch(JsonException e){throw new InvalidDataException("Manifesto de atualização inválido.",e);}
        ReleaseSecurity.Verify(release,publicKey);
        var next=ReleaseSecurity.Version(release.Version)!;var installed=current is null?null:ReleaseSecurity.Version(current.Version);
        if(installed is not null && (next<installed || (next==installed && IsUsable(current!.Version)
            && (!reinstallSignedBuild || ReadInstalled(current.Version).Sha256==release.Sha256))))return new(false,current!.Version,"Bardola está atualizado.");
        byte[] package=await Read(release.Package,MaxPackage,ct);
        if(package.LongLength!=release.Size || !ReleaseSecurity.Hash(package).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("O pacote não passou na verificação de integridade.");
        string versions=Path.Combine(Root,"app-versions");Directory.CreateDirectory(versions);
        string stage=Path.Combine(versions,".stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try {
            using var zip=new ZipArchive(new MemoryStream(package),ZipArchiveMode.Read);long total=0;int count=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in zip.Entries){ct.ThrowIfCancellationRequested();if(++count>256 || (total+=entry.Length)>MaxExtracted)throw new InvalidDataException("Pacote excede os limites de extração.");
                string name=entry.FullName.Replace('\\','/');
                if(string.IsNullOrWhiteSpace(name)||name.StartsWith('/')||name.Contains(':')||name.Split('/').Any(s=>s is ".." or ".")||((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Caminho inseguro no pacote.");
                string destination=Path.GetFullPath(Path.Combine(stage,name));if(!destination.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!seen.Add(destination))throw new InvalidDataException("Entrada inválida no pacote.");
                if(name.EndsWith('/')){Directory.CreateDirectory(destination);continue;}
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input=entry.Open();await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                byte[] buffer=new byte[81920];long actual=0;int read;while((read=await input.ReadAsync(buffer,ct))>0){actual+=read;if(actual>entry.Length||actual>MaxExtracted)throw new InvalidDataException("Tamanho descompactado inválido.");await output.WriteAsync(buffer.AsMemory(0,read),ct);}
                if(actual!=entry.Length)throw new InvalidDataException("Arquivo incompleto no pacote.");
            }
            string binary=Path.Combine(stage,release.EntryPoint);
            if(!File.Exists(binary)||!ReleaseSecurity.Hash(await File.ReadAllBytesAsync(binary,ct)).Equals(release.ExeSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Executável da atualização inválido.");
            await File.WriteAllTextAsync(Path.Combine(stage,"release.json"),JsonSerializer.Serialize(release),ct);
            ct.ThrowIfCancellationRequested();string target=Path.Combine(versions,release.Version);
            if(Directory.Exists(target))Directory.Move(target,Path.Combine(versions,release.Version+".replaced-"+Guid.NewGuid().ToString("N")));
            Directory.Move(stage,target);ct.ThrowIfCancellationRequested();
            var pointer=new InstalledRelease(release.Version,release.EntryPoint,release.ExeSha256,current?.Version==release.Version?current?.PreviousVersion:current?.Version);
            await Atomic(Path.Combine(Root,"current.json"),JsonSerializer.Serialize(pointer),ct);
            return new(true,release.Version,"Atualização instalada. Será usada na próxima abertura.");
        }finally{if(Directory.Exists(stage)&&Path.GetFullPath(stage).StartsWith(versions+Path.DirectorySeparatorChar+".stage-",StringComparison.OrdinalIgnoreCase))Directory.Delete(stage,true);}
    }
    public void Rollback()
    {
        using var mutex=Lock(CancellationToken.None).GetAwaiter().GetResult();
        var current=Current();if(current?.PreviousVersion is not { } previous)throw new InvalidDataException("Não há versão anterior para recuperar.");
        string path=VerifiedExecutable(previous);var manifest=ReadInstalled(previous);
        Atomic(Path.Combine(Root,"current.json"),JsonSerializer.Serialize(new InstalledRelease(previous,"BardoApp.exe",manifest.ExeSha256)),CancellationToken.None).GetAwaiter().GetResult();
    }
    bool IsUsable(string version){try{VerifiedExecutable(version);return true;}catch(Exception e) when(e is IOException or InvalidDataException or CryptographicException or JsonException){return false;}}
    ReleaseManifest ReadInstalled(string version)
    {
        if(ReleaseSecurity.Version(version) is null)throw new InvalidDataException("Versão instalada inválida.");
        var manifest=JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(Path.Combine(Root,"app-versions",version,"release.json")))??throw new InvalidDataException("Metadados ausentes.");ReleaseSecurity.Verify(manifest,publicKey);if(manifest.Version!=version)throw new InvalidDataException("Metadados de outra versão.");return manifest;
    }
    string VerifiedExecutable(string version)
    {
        var manifest=ReadInstalled(version);string path=Path.Combine(Root,"app-versions",version,manifest.EntryPoint);
        if(!File.Exists(path)||!ReleaseSecurity.Hash(File.ReadAllBytes(path)).Equals(manifest.ExeSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A instalação não passou na verificação de integridade.");return path;
    }
    async Task<byte[]> Read(string name,long limit,CancellationToken ct)
    {
        if(Uri.TryCreate(feed,UriKind.Absolute,out var uri)&&uri.Scheme=="https"){
            var url=name=="latest.json"?uri:new Uri(uri,name);using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(45));
            using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("Bardola-Updater/1.0");
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,budget.Token);response.EnsureSuccessStatusCode();if(response.RequestMessage?.RequestUri?.Scheme!="https"||response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Download de atualização inválido.");
            await using var input=await response.Content.ReadAsStreamAsync(budget.Token);using var output=new MemoryStream();byte[] buffer=new byte[81920];int n;while((n=await input.ReadAsync(buffer,budget.Token))>0){if(output.Length+n>limit)throw new InvalidDataException("Atualização excede o limite de tamanho.");await output.WriteAsync(buffer.AsMemory(0,n),budget.Token);}return output.ToArray();
        }
        if(Uri.TryCreate(feed,UriKind.Absolute,out var unsupported)&&!unsupported.IsFile)throw new InvalidDataException("O canal precisa ser local ou HTTPS.");
        string path=Path.Combine(Path.GetFullPath(feed),name);var info=new FileInfo(path);if(info.Length>limit)throw new InvalidDataException("Atualização excede o limite de tamanho.");return await File.ReadAllBytesAsync(path,ct);
    }
    async Task<FileStream> Lock(CancellationToken ct)
    {
        var deadline=DateTime.UtcNow.AddSeconds(10);while(true){ct.ThrowIfCancellationRequested();try{return new FileStream(Path.Combine(Root,".update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException) when(DateTime.UtcNow<deadline){await Task.Delay(100,ct);}}
    }
    internal static async Task Atomic(string path,string json,CancellationToken ct)
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllTextAsync(temporary,json,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,path,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}}
}
public static class ReleasePublisher
{
    public static ReleaseManifest Publish(string appDirectory,string feedDirectory,string version,string privateKey)
    {
        if(ReleaseSecurity.Version(version) is null)throw new InvalidDataException("Use uma versão como 0.3.1.");
        string exe=Path.Combine(appDirectory,"BardoApp.exe");if(!File.Exists(exe))throw new InvalidDataException("BardoApp.exe não foi compilado.");
        Directory.CreateDirectory(feedDirectory);string name=$"bardola-{version}.zip",path=Path.Combine(feedDirectory,name),temp=Path.Combine(feedDirectory,Guid.NewGuid()+".tmp");
        try{
            using(var zip=ZipFile.Open(temp,ZipArchiveMode.Create))foreach(var file in Directory.EnumerateFiles(appDirectory,"*",SearchOption.AllDirectories).Order()){
                string relative=Path.GetRelativePath(appDirectory,file).Replace('\\','/');if(Path.GetExtension(file).Equals(".pdb",StringComparison.OrdinalIgnoreCase))continue;
                var entry=zip.CreateEntry(relative,CompressionLevel.Optimal);entry.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);using var input=File.OpenRead(file);using var output=entry.Open();input.CopyTo(output);
            }
            var bytes=File.ReadAllBytes(temp);string hash=ReleaseSecurity.Hash(bytes);
            if(File.Exists(path)&&!ReleaseSecurity.Hash(File.ReadAllBytes(path)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Essa versão já foi publicada com outro conteúdo; aumente o número da versão.");
            File.Move(temp,path,true);
            var manifest=new ReleaseManifest(1,"Bardo.gg",version,name,hash,bytes.LongLength,"BardoApp.exe",DateTimeOffset.UtcNow.ToString("O"),"",ReleaseSecurity.Hash(File.ReadAllBytes(exe)));
            using var key=ECDsa.Create();key.ImportFromPem(privateKey);manifest=manifest with{Signature=Convert.ToBase64String(key.SignData(ReleaseSecurity.Payload(manifest),HashAlgorithmName.SHA256))};
            UpdateManager.Atomic(Path.Combine(feedDirectory,"latest.json"),JsonSerializer.Serialize(manifest),CancellationToken.None).GetAwaiter().GetResult();return manifest;
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
public static class ReleaseSecurity
{
    public static byte[] Payload(ReleaseManifest manifest)=>Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest with{Signature=""}));
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static System.Version? Version(string value)=>System.Version.TryParse(value,out var v)&&v.Build>=0&&v.Revision==-1&&value==v.ToString(3)?v:null;
    public static void Verify(ReleaseManifest manifest,string publicKey)
    {
        if(manifest.Format!=1||manifest.Product!="Bardo.gg"||Version(manifest.Version) is null||manifest.EntryPoint!="BardoApp.exe"||manifest.Size<2||manifest.Size>64L*1024*1024||!Regex.IsMatch(manifest.Package??"",@"^[a-zA-Z0-9._-]{1,100}\.zip$")||!Regex.IsMatch(manifest.Sha256??"",@"^[a-fA-F0-9]{64}$")||!Regex.IsMatch(manifest.ExeSha256??"",@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("Manifesto de atualização não reconhecido.");
        try{using var key=ECDsa.Create();key.ImportFromPem(publicKey);if(!key.VerifyData(Payload(manifest),Convert.FromBase64String(manifest.Signature),HashAlgorithmName.SHA256))throw new InvalidDataException("A assinatura da atualização não é válida.");}
        catch(Exception e) when(e is FormatException or CryptographicException or ArgumentException){throw new InvalidDataException("Assinatura de atualização inválida.",e);}
    }
}
