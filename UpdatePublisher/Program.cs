using System.Security.Cryptography;
using System.Text.Json;
using NexoLoL.Updates;

string? Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
string keyPath=Arg("--key")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Bardo.gg-Publisher","release-key.pem");
if(args.Contains("--init-key")){
    Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    if(File.Exists(keyPath))key.ImportFromPem(File.ReadAllText(keyPath));else File.WriteAllText(keyPath,key.ExportECPrivateKeyPem());
    string pub=key.ExportSubjectPublicKeyInfoPem();
    if(ReleaseTrust.PublicKey!="UNINITIALIZED" && pub!=ReleaseTrust.PublicKey)throw new InvalidOperationException("A chave privada não corresponde à chave pública já fixada no app.");
    string output=Arg("--trust")??"UpdateCore/ReleaseTrust.cs";
    string source="namespace NexoLoL.Updates;\n\npublic static class ReleaseTrust\n{\n    public const string Repository=\"robertfxbr/Bardo.gg\";\n    public const string Feed=\"https://github.com/robertfxbr/Bardo.gg/releases/latest/download/latest.json\";\n    public const string PublicKey="+JsonSerializer.Serialize(pub)+";\n}\n";
    // A chave privada nunca é escrita no workspace ou mostrada em stdout.
    File.WriteAllText(output,source);Console.WriteLine(JsonSerializer.Serialize(new{initialized=true,publicKeyPinned=output}));return 0;
}
if(!File.Exists(keyPath))throw new InvalidOperationException("Chave de publicação não encontrada. Execute --init-key nesta máquina.");
using(var key=ECDsa.Create()){key.ImportFromPem(File.ReadAllText(keyPath));if(key.ExportSubjectPublicKeyInfoPem()!=ReleaseTrust.PublicKey)throw new InvalidOperationException("A chave de publicação não corresponde à chave pública do app.");}
var release=ReleasePublisher.Publish(Arg("--app")??throw new ArgumentException("--app ausente"),Arg("--feed")??throw new ArgumentException("--feed ausente"),Arg("--version")??throw new ArgumentException("--version ausente"),File.ReadAllText(keyPath));
Console.WriteLine(JsonSerializer.Serialize(new{release.Version,release.Package,release.Sha256,release.Size}));return 0;
