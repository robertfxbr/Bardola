using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoLoL.Updates;

int passed=0,failed=0;
void True(bool condition){if(!condition)throw new Exception("comportamento esperado não observado");}
async Task Test(string name,Func<Task> body){try{await body();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
string root=Path.Combine(Path.GetTempPath(),"nexo-update-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);string secret=key.ExportECPrivateKeyPem(),pub=key.ExportSubjectPublicKeyInfoPem();
(string app,string feed,string install) Fixture(){string d=Path.Combine(root,Guid.NewGuid().ToString("N"));string app=Path.Combine(d,"app"),feed=Path.Combine(d,"feed"),install=Path.Combine(d,"installed");Directory.CreateDirectory(app);File.WriteAllBytes(Path.Combine(app,"BardoApp.exe"),Encoding.UTF8.GetBytes("MZ-test-build-A"));Directory.CreateDirectory(install);return(app,feed,install);}
await Test("instala pacote assinado e mantém configurações do usuário",async()=>{var f=Fixture();File.WriteAllText(Path.Combine(f.install,"preferences-v2.json"),"minhas preferências");ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);True((await m.CheckAsync()).Updated);True(m.Current()!.Version=="0.3.0");True(File.Exists(m.Executable()));True(File.ReadAllText(Path.Combine(f.install,"preferences-v2.json"))=="minhas preferências");});
await Test("segunda versão usa a mesma instalação e guarda anterior",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();File.WriteAllText(Path.Combine(f.app,"BardoApp.exe"),"MZ-build-B");ReleasePublisher.Publish(f.app,f.feed,"0.3.1",secret);True((await m.CheckAsync()).Updated);True(m.Current()!.PreviousVersion=="0.3.0");True(File.ReadAllText(m.Executable())=="MZ-build-B");});
await Test("versão igual não reinstala",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();True(!(await m.CheckAsync()).Updated);});
await Test("instalador substitui a prévia local da mesma versão por uma compilação assinada",async()=>{
    var f=Fixture();
    ReleasePublisher.Publish(f.app,f.feed,"0.8.2",secret);
    await new UpdateManager(f.install,f.feed,pub).CheckAsync();
    string publicFeed=Path.Combine(f.feed,"official");
    File.WriteAllText(Path.Combine(f.app,"BardoApp.exe"),"MZ-Bardola");
    ReleasePublisher.Publish(f.app,publicFeed,"0.8.2",secret);
    var installer=new UpdateManager(f.install,publicFeed,pub);
    True(!(await installer.CheckAsync()).Updated);
    True((await installer.CheckAsync(reinstallSignedBuild:true)).Updated);
    True(File.ReadAllText(installer.Executable())=="MZ-Bardola");
    True(!(await installer.CheckAsync(reinstallSignedBuild:true)).Updated);
});
await Test("downgrade assinado não substitui versão atual",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.1",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);True(!(await m.CheckAsync()).Updated);True(m.Current()!.Version=="0.3.1");});
await Test("manifesto adulterado é recusado",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);string p=Path.Combine(f.feed,"latest.json");File.WriteAllText(p,File.ReadAllText(p).Replace("0.3.0","0.9.0"));bool rejected=false;try{await new UpdateManager(f.install,f.feed,pub).CheckAsync();}catch(InvalidDataException){rejected=true;}True(rejected);True(!File.Exists(Path.Combine(f.install,"current.json")));});
await Test("assinatura de outra chave é recusada",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);bool rejected=false;try{await new UpdateManager(f.install,f.feed,other.ExportSubjectPublicKeyInfoPem()).CheckAsync();}catch(InvalidDataException){rejected=true;}True(rejected);});
await Test("pacote corrompido preserva versão anterior",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();var release=ReleasePublisher.Publish(f.app,f.feed,"0.3.1",secret);File.AppendAllText(Path.Combine(f.feed,release.Package),"corrompido");try{await m.CheckAsync();}catch(InvalidDataException){}True(m.Current()!.Version=="0.3.0");});
await Test("canal indisponível mantém versão instalada utilizável",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();File.Delete(Path.Combine(f.feed,"latest.json"));var result=await m.CheckAsync();True(!result.Updated);True(File.Exists(m.Executable()));});
await Test("ZIP com traversal não grava fora da instalação",async()=>{var f=Fixture();Directory.CreateDirectory(f.feed);string p=Path.Combine(f.feed,"evil.zip");using(var zip=ZipFile.Open(p,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../outside.txt").Open());writer.Write("evil");}Sign(p,f.feed,"0.3.0");bool rejected=false;try{await new UpdateManager(f.install,f.feed,pub).CheckAsync();}catch(InvalidDataException){rejected=true;}True(rejected);True(!File.Exists(Path.Combine(f.install,"outside.txt")));True(!File.Exists(Path.Combine(Path.GetDirectoryName(f.install)!,"outside.txt")));});
await Test("cancelamento não promove instalação parcial",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);using var c=new CancellationTokenSource();c.Cancel();try{await new UpdateManager(f.install,f.feed,pub).CheckAsync(c.Token);}catch(OperationCanceledException){}True(!File.Exists(Path.Combine(f.install,"current.json")));});
await Test("diretório de versão preexistente incompleto é reparado",async()=>{var f=Fixture();Directory.CreateDirectory(Path.Combine(f.install,"app-versions","0.3.0"));ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();True(File.Exists(m.Executable()));});
await Test("mesmo conteúdo gera pacote estável apesar de nova data de arquivo",()=>{var f=Fixture();var a=ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);File.SetLastWriteTimeUtc(Path.Combine(f.app,"BardoApp.exe"),DateTime.UtcNow.AddHours(2));var b=ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);True(a.Sha256==b.Sha256);return Task.CompletedTask;});
await Test("rollback só promove versão anterior assinada e íntegra",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();File.WriteAllText(Path.Combine(f.app,"BardoApp.exe"),"MZ-build-B");ReleasePublisher.Publish(f.app,f.feed,"0.3.1",secret);await m.CheckAsync();m.Rollback();True(m.Current()!.Version=="0.3.0");True(File.ReadAllText(m.Executable())=="MZ-test-build-A");});
await Test("executável instalado adulterado não pode ser iniciado",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);await m.CheckAsync();File.AppendAllText(m.Executable(),"evil");bool rejected=false;try{m.Executable();}catch(InvalidDataException){rejected=true;}True(rejected);});
await Test("duas verificações concorrentes promovem pacote apenas uma vez",async()=>{var f=Fixture();ReleasePublisher.Publish(f.app,f.feed,"0.3.0",secret);var m=new UpdateManager(f.install,f.feed,pub);var results=await Task.WhenAll(Task.Run(()=>m.CheckAsync()),Task.Run(()=>m.CheckAsync()));True(results.Count(r=>r.Updated)==1);True(File.Exists(m.Executable()));});
await Test("migração copia configurações e backup sem sobrescrever dados atuais",()=>{var f=Fixture();string old=Path.Combine(f.app,"legacy");Directory.CreateDirectory(Path.Combine(old,"rune-backups"));File.WriteAllText(Path.Combine(old,"preferences-v2.json"),"antiga");File.WriteAllText(Path.Combine(f.install,"preferences-v2.json"),"atual");File.WriteAllText(Path.Combine(old,"rune-backups","backup.json"),"backup");BardoPaths.MigrateLegacyData(f.install,old);BardoPaths.MigrateLegacyData(f.install,old);True(File.ReadAllText(Path.Combine(f.install,"preferences-v2.json"))=="atual");True(File.ReadAllText(Path.Combine(f.install,"rune-backups","backup.json"))=="backup");True(File.Exists(Path.Combine(old,"preferences-v2.json")));return Task.CompletedTask;});
await Test("só oferece reinício para uma versão local mais nova e íntegra, mesmo offline",async()=>{
    var f=Fixture();
    ReleasePublisher.Publish(f.app,f.feed,"0.7.1",secret);
    var manager=new UpdateManager(f.install,f.feed,pub);
    await manager.CheckAsync();
    File.Delete(Path.Combine(f.feed,"latest.json"));
    True(manager.ReadyVersionAfter(new Version("0.7.0"))=="0.7.1");
    True(manager.ReadyVersionAfter(new Version("0.7.1")) is null);
    File.AppendAllText(manager.Executable(),"corrompido");
    True(manager.ReadyVersionAfter(new Version("0.7.0")) is null);
});
Directory.Delete(root,true);Console.WriteLine($"{passed} passed, {failed} failed");return failed>0?1:0;

void Sign(string package,string feed,string version)
{
    var bytes=File.ReadAllBytes(package);var manifest=new ReleaseManifest(1,"Bardo.gg",version,Path.GetFileName(package),Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length,"BardoApp.exe",DateTimeOffset.UtcNow.ToString("O"),"",new string('0',64));
    byte[] payload=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest with{Signature=""}));manifest=manifest with{Signature=Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256))};File.WriteAllText(Path.Combine(feed,"latest.json"),JsonSerializer.Serialize(manifest));
}
