using System.Windows;

namespace NexoLoL.Desktop;
public partial class App : Application
{
    Mutex? instanceMutex;
    bool ownsMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex=new Mutex(true,@"Local\Bardo.gg",out ownsMutex);
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
