using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexoLoL.Updates;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromMinutes(15)};
    TextBlock? updateStatus;
    bool updateBusy,updatesStarted;
    string updateMessage="As novas versões são baixadas automaticamente e usadas na próxima abertura.";
    string RunningVersion=>typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"0.3.0";
    void StartUpdates(){if(updatesStarted)return;updatesStarted=true;updateTimer.Tick+=async(_,_)=>await CheckUpdates();updateTimer.Start();_=CheckUpdates();}
    async Task CheckUpdates(){
        if(updateBusy)return;updateBusy=true;SetUpdateStatus("Verificando atualizações…");
        try{
            // Uma cópia de desenvolvimento não deve alterar uma instalação de produção.
            string running=Path.GetFullPath(Environment.ProcessPath??"");
            if(!running.StartsWith(Path.Combine(BardoPaths.Root,"app-versions")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){
                SetUpdateStatus("Use o instalador Bardo.gg para ativar as atualizações automáticas.");return;
            }
            var result=await Task.Run(()=>new UpdateManager(BardoPaths.Root,ReleaseTrust.Feed,ReleaseTrust.PublicKey).CheckAsync(lifetime.Token));
            bool pending=Version.TryParse(result.Version,out var next)&&Version.TryParse(RunningVersion,out var now)&&next>now;
            SetUpdateStatus(pending?$"Versão {result.Version} pronta. Feche e abra o Bardo.gg quando terminar de jogar.":result.Message);
        }catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){SetUpdateStatus("Não foi possível atualizar agora. O Bardo.gg continua funcionando; tentaremos novamente.");}
        finally{updateBusy=false;}
    }
    void SetUpdateStatus(string message){updateMessage=message;if(updateStatus is not null)updateStatus.Text=message;}
    void BuildResearch(){
        ResearchDetails.Children.Clear();
        var header=new StackPanel();header.Children.Add(Text("Do seu jeito.",29,true));header.Children.Add(Text("Importação, painel de partida e atualizações do Bardo.gg.",13,false,Muted,top:6));ResearchDetails.Children.Add(Card(header));
        var import=new StackPanel();import.Children.Add(Text("Importação de builds",18,true));
        import.Children.Add(SettingRow("Importar runas automaticamente","Aplicar as runas da build durante a seleção de campeão.",AutoRunes.IsChecked==true,value=>AutoRunes.IsChecked=value));
        import.Children.Add(SettingRow("Importar itens automaticamente","Preparar o conjunto de itens na loja do LoL.",AutoItems.IsChecked==true,value=>AutoItems.IsChecked=value));
        import.Children.Add(SettingRow("Reutilizar página ativa com backup","Quando não houver espaço, salvar a página antes de reutilizar.",preferences.ReuseActiveRunePage,value=>{preferences.ReuseActiveRunePage=value;importRequest?.Cancel();lastImportKey="";_=SavePreferences();}));
        import.Children.Add(SettingRow("Detectar campeão e modo","Acompanhar sua seleção no cliente do League.",AutoDetect.IsChecked==true,value=>AutoDetect.IsChecked=value));
        import.Children.Add(Button("Restaurar backup de runas",()=>RestoreRunes(this,new RoutedEventArgs()),top:14));ResearchDetails.Children.Add(Card(import));
        var title=Text("Painel durante a partida",21,true,top:8);title.Margin=new Thickness(0,8,0,8);ResearchDetails.Children.Add(title);
        ResearchDetails.Children.Add(Text("Janela flutuante para modo sem bordas ou segundo monitor. Aparece só em partida, quando ativada abaixo.",12,false,Muted));
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
        for(int i=0;i<2;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        void Tile(int position,string symbol,string title,string description,bool enabled,Action<bool> changed){var p=new StackPanel();p.Children.Add(Text(symbol,26,true,Accent));p.Children.Add(Text(title,17,true,top:12));p.Children.Add(Text(description,12,false,Muted,top:8));var check=new CheckBox{Content="Ativado",IsChecked=enabled,Margin=new Thickness(0,18,0,0)};check.Checked+=(_,_)=>changed(true);check.Unchecked+=(_,_)=>changed(false);p.Children.Add(check);var card=Card(p);card.Margin=new Thickness(position%2==0?0:6,0,position%2==0?6:0,12);Grid.SetColumn(card,position%2);Grid.SetRow(card,position/2);grid.Children.Add(card);}
        Tile(0,"↗","Seu desempenho","KDA, CS/min, ouro e visão do seu jogador, atualizados durante a partida.",preferences.OverlayStats,v=>{preferences.OverlayStats=v;_=SavePreferences();UpdateOverlay();});
        Tile(1,"◈","Build ao seu lado","Itens principais da build do campeão que você está jogando.",preferences.OverlayBuild,v=>{preferences.OverlayBuild=v;_=SavePreferences();UpdateOverlay();});
        Tile(2,"Q  W  E","Prioridade de habilidades","Consulta à prioridade da build, com campeão e fonte conferidos.",preferences.OverlaySkills,v=>{preferences.OverlaySkills=v;_=SavePreferences();UpdateOverlay();});
        var goal=new StackPanel();goal.Children.Add(Text("◎",26,true,Accent));goal.Children.Add(Text("Meta pessoal de farm",17,true,top:12));goal.Children.Add(Text("Comparação com uma meta escolhida por você, em CS por minuto.",12,false,Muted,top:8));var target=new ComboBox{Margin=new Thickness(0,16,0,0),ItemsSource=new double[]{4,5,6,7,8,9,10},SelectedItem=preferences.CsTarget};target.SelectionChanged+=(_,_)=>{if(target.SelectedItem is double value){preferences.CsTarget=value;_=SavePreferences();UpdateOverlay();}};goal.Children.Add(target);var goalCard=Card(goal);goalCard.Margin=new Thickness(6,0,0,12);Grid.SetColumn(goalCard,1);Grid.SetRow(goalCard,1);grid.Children.Add(goalCard);ResearchDetails.Children.Add(grid);
        var pending=new StackPanel();pending.Children.Add(Text("Outros recursos das referências",18,true));pending.Children.Add(Text("Feitiços automáticos, rotas da selva, cronômetros de objetivos e do ARAM, ultimates aliados, comparação de inventários e estatísticas da tela de carregamento ainda não estão disponíveis nesta versão. Cada recurso precisa de uma fonte de dados e validação próprias.",12,false,Muted,top:10));ResearchDetails.Children.Add(Card(pending));
        var updates=new StackPanel();updates.Children.Add(Text("Bardo.gg · "+RunningVersion,18,true));updateStatus=Text(updateMessage,13,false,Muted,top:10);updates.Children.Add(updateStatus);updates.Children.Add(Button("Verificar atualizações",()=>_=CheckUpdates(),top:14));updates.Children.Add(Button("Código-fonte e licença GPL-3.0 ↗",()=>OpenUrl("https://github.com/robertfxbr/Bardo.gg"),top:12));ResearchDetails.Children.Add(Card(updates));
        ResearchDetails.Children.Add(Text("Bardo.gg is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.",10,false,Muted,top:8));
    }
    FrameworkElement SettingRow(string title,string description,bool enabled,Action<bool> changed){
        var row=new Grid{Margin=new Thickness(0,16,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var labels=new StackPanel{Margin=new Thickness(0,0,20,0)};labels.Children.Add(Text(title,14,true));labels.Children.Add(Text(description,12,false,Muted,top:3));row.Children.Add(labels);
        var check=new CheckBox{IsChecked=enabled,VerticalAlignment=VerticalAlignment.Center,ToolTip=title};check.Checked+=(_,_)=>changed(true);check.Unchecked+=(_,_)=>changed(false);Grid.SetColumn(check,1);row.Children.Add(check);return row;
    }
}
