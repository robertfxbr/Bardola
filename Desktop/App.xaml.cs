using System.Windows;
using System.IO;
using NexoLoL.Updates;

namespace NexoLoL.Desktop;
public partial class App : Application
{
    Mutex? instanceMutex;
    bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        bool restarting=e.Args.Contains("--after-update");
        instanceMutex=new Mutex(false,@"Local\Bardo.gg");
        // A nova versão pode iniciar antes que a janela antiga termine de fechar.
        try{ownsMutex=instanceMutex.WaitOne(restarting?TimeSpan.FromSeconds(30):TimeSpan.Zero);}
        catch(AbandonedMutexException){ownsMutex=true;}
        if(restarting&&!ownsMutex)
            MessageBox.Show("Feche o Bardola e abra pelo atalho para usar a atualização.","Bardola");
        if(!ownsMutex){Shutdown();return;}
        if (Environment.ProcessPath?.StartsWith(Path.Combine(BardoPaths.Root,"app-versions")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)==true)
            BardolaShortcuts.Update(File.Exists(BardoPaths.Launcher)?BardoPaths.Launcher:Path.Combine(BardoPaths.Root,"Bardo.gg.exe"),migrateOnly:true);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e){if(ownsMutex)instanceMutex?.ReleaseMutex();instanceMutex?.Dispose();base.OnExit(e);}
    public App()
    {
        DispatcherUnhandledException+=(_,e)=>{
            System.Diagnostics.Debug.WriteLine(e.Exception);
            MessageBox.Show("O Bardola encontrou um erro inesperado. Feche e abra o app; suas builds salvas continuam disponíveis.","Bardola");e.Handled=true;
        };
    }
}
