using System.Windows;

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
            MessageBox.Show("Feche o Bardo.gg e abra pelo atalho para usar a atualização.","Bardo.gg");
        if(!ownsMutex){Shutdown();return;}
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e){if(ownsMutex)instanceMutex?.ReleaseMutex();instanceMutex?.Dispose();base.OnExit(e);}
    public App()
    {
        DispatcherUnhandledException+=(_,e)=>{
            System.Diagnostics.Debug.WriteLine(e.Exception);
            MessageBox.Show("O Bardo.gg encontrou um erro inesperado. Feche e abra o app; suas builds salvas continuam disponíveis.","Bardo.gg");e.Handled=true;
        };
    }
}
