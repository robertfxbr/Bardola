using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NexoLoL.Updates;

string? Arg(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
string root=Path.GetFullPath(Arg("--root")??BardoPaths.Root);
string feed=Arg("--feed")??ReleaseTrust.Feed;
string log=Path.Combine(root,"launcher-status.json");
try{
    Directory.CreateDirectory(root);
    var manager=new UpdateManager(root,feed,ReleaseTrust.PublicKey);
    if(args.Contains("--check-only")){
        var result=await manager.CheckAsync();await File.WriteAllTextAsync(log,JsonSerializer.Serialize(result));return 0;
    }
    // O instalador inclui uma versão assinada e também funciona sem internet.
    bool needsBootstrap;
    try{manager.Executable();needsBootstrap=false;}
    catch(Exception e) when(e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException){needsBootstrap=true;}
    if(needsBootstrap){
        string scratch=Path.Combine(root,".bootstrap-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        try{
            var assembly=Assembly.GetExecutingAssembly();
            foreach(var name in assembly.GetManifestResourceNames().Where(n=>n.StartsWith("Bootstrap.",StringComparison.Ordinal))){
                string filename=name["Bootstrap.".Length..];if(Path.GetFileName(filename)!=filename)throw new InvalidDataException("Recurso inválido.");
                using var input=assembly.GetManifestResourceStream(name)!;using var output=File.Create(Path.Combine(scratch,filename));await input.CopyToAsync(output);
            }
            await new UpdateManager(root,scratch,ReleaseTrust.PublicKey).CheckAsync();
            try{manager.Executable();}
            catch(Exception e) when(e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException){
                try{await manager.CheckAsync();manager.Executable();}
                catch{manager.Rollback();manager.Executable();}
            }
        }finally{Directory.Delete(scratch,true);}
    }
    manager.Executable(); // Instalação só informa sucesso depois de validar o executável.
    BardoPaths.MigrateLegacyData(root);
    string self=Environment.ProcessPath??throw new InvalidOperationException("Executável não encontrado.");
    string launcher=Path.Combine(root,"Bardo.gg.exe");
    if(!Path.GetFullPath(self).Equals(launcher,StringComparison.OrdinalIgnoreCase)){
        string staged=launcher+".new";File.Copy(self,staged,true);File.Move(staged,launcher,true);
    }
    if(Arg("--root") is null)CreateShortcuts(launcher);
    await File.WriteAllTextAsync(log,JsonSerializer.Serialize(new{installed=true,version=manager.Current()?.Version,launcher}));
    if(args.Contains("--install-only"))return 0;
    string executable;
    try{executable=manager.Executable();}catch(Exception e) when(e is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException or JsonException){manager.Rollback();executable=manager.Executable();}
    Process.Start(new ProcessStartInfo(executable){WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=false});
    return 0;
}catch(Exception e){
    try{await File.WriteAllTextAsync(log,JsonSerializer.Serialize(new{error=e.Message}));}catch{}
    if(!args.Contains("--install-only")&&!args.Contains("--check-only"))MessageBoxW(IntPtr.Zero,"Não foi possível abrir o Bardo.gg. "+e.Message,"Bardo.gg",0x10);
    return 1;
}

static void CreateShortcuts(string target){
    foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)}){
        if(string.IsNullOrWhiteSpace(folder))continue;
        object? shell=null,link=null;
        try{
            var type=Type.GetTypeFromProgID("WScript.Shell");if(type is null)continue;shell=Activator.CreateInstance(type);
            string path=Path.Combine(folder,"Bardo.gg.lnk");
            link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,[path]);
            var t=link!.GetType();
            if(File.Exists(path)){
                string old=(string?)t.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null)??"";
                if(!old.Equals(target,StringComparison.OrdinalIgnoreCase))continue;
            }
            t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,[target]);t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,[Path.GetDirectoryName(target)]);t.InvokeMember("Description",BindingFlags.SetProperty,null,link,["Bardo.gg · League of Legends"]);t.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
        }catch(COMException){}finally{if(link is not null)Marshal.ReleaseComObject(link);if(shell is not null)Marshal.ReleaseComObject(shell);}
    }
}
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBoxW(IntPtr hwnd,string text,string caption,uint type);
