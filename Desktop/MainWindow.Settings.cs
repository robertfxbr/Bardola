using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NexoLoL.Updates;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    readonly GhostMode ghost=new();
    TextBlock? ghostWarning;
    void QueueGhostStatus()
    {
        if(ghostWarning!=null && !Dispatcher.HasShutdownStarted)Dispatcher.InvokeAsync(RefreshGhostStatus);
    }
    void RefreshGhostStatus()
    {
        if(ghostWarning==null)return;
        ghostWarning.Visibility=ghost.HasFailures?Visibility.Visible:Visibility.Collapsed;
        ghostWarning.Text=preferences.GhostMode
            ?"O Windows não confirmou a proteção de todas as janelas. Confira a prévia da transmissão."
            :"O Windows não liberou a captura de todas as janelas. Reinicie o Bardola para concluir.";
    }
    bool CanShowGhostOverlay(Window window)
    {
        if(!preferences.GhostMode || ghost.Protect(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle()))return true;
        if(window.IsVisible)window.Hide();
        return false;
    }
    void SetGhostMode(bool enabled)
    {
        preferences.GhostMode=enabled;
        ghost.SetEnabled(enabled);
        if(enabled){
            if(mapOverlay?.IsVisible==true)CanShowGhostOverlay(mapOverlay);
            if(cardOverlay?.IsVisible==true)CanShowGhostOverlay(cardOverlay);
        }
        RefreshGhostStatus();
        _=SavePreferences();
    }
    readonly DispatcherTimer updateTimer=new(){Interval=TimeSpan.FromMinutes(15)};
    TextBlock? updateStatus;
    Button? updateButton;
    string? readyVersion;
    bool updateBusy,updatesStarted;
    string updateMessage="As novas versões são baixadas automaticamente e usadas na próxima abertura.";
    string RunningVersion=>typeof(MainWindow).Assembly.GetName().Version?.ToString(3)??"0.3.0";
    void StartUpdates(){if(updatesStarted)return;updatesStarted=true;updateTimer.Tick+=async(_,_)=>await CheckUpdates();updateTimer.Start();_=CheckUpdates();}
    async Task CheckUpdates(){
        if(updateBusy)return;
        updateBusy=true;
        SetUpdateStatus("Procurando uma versão mais recente…");
        try{
            // Uma cópia de desenvolvimento não deve alterar uma instalação de produção.
            string running=Path.GetFullPath(Environment.ProcessPath??"");
            if(!running.StartsWith(Path.Combine(BardoPaths.Root,"app-versions")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){
                SetUpdateStatus("Use o instalador Bardola para ativar as atualizações automáticas.");return;
            }
            var manager=new UpdateManager(BardoPaths.Root,ReleaseTrust.Feed,ReleaseTrust.PublicKey);
            var result=await Task.Run(async ()=>{
                var staged=manager.ReadyVersionAfter(Version.Parse(RunningVersion));
                if(staged is not null)return new UpdateResult(false,staged,"");
                var checkedRelease=await manager.CheckAsync(lifetime.Token);
                if(IsNewer(checkedRelease.Version))manager.Executable();
                return checkedRelease;
            });
            readyVersion=IsNewer(result.Version)?result.Version:null;
            SetUpdateStatus(readyVersion is null?result.Message:$"Versão {readyVersion} pronta. Reinicie quando quiser aplicá-la.");
        }catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){
            readyVersion=null;
            SetUpdateStatus("Não foi possível verificar a atualização. Tente novamente em instantes.");
        }
        finally{updateBusy=false;RefreshUpdateButton();}
    }
    bool IsNewer(string? version)=>Version.TryParse(version,out var next)&&next>Version.Parse(RunningVersion);

    async Task UpdateClicked(){
        if(updateBusy)return;
        if(readyVersion is null){await CheckUpdates();return;}
        updateBusy=true;
        SetUpdateStatus("Abrindo a versão atualizada…");
        try{
            var manager=new UpdateManager(BardoPaths.Root,ReleaseTrust.Feed,ReleaseTrust.PublicKey);
            string executable=await Task.Run(manager.Executable,lifetime.Token);
            if(lifetime.IsCancellationRequested)return;
            if(!IsNewer(Path.GetFileName(Path.GetDirectoryName(executable)))){
                readyVersion=null;
                SetUpdateStatus("Você já está usando a versão instalada.");
                return;
            }
            using var next=Process.Start(new ProcessStartInfo(executable){
                WorkingDirectory=Path.GetDirectoryName(executable)!,UseShellExecute=false,ArgumentList={"--after-update"}
            })??throw new IOException("Falha ao iniciar o Bardola.");
            Application.Current.Shutdown();
        }catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException){
            readyVersion=null;
            SetUpdateStatus("Não foi possível abrir a atualização. Verifique a versão novamente.");
        }finally{updateBusy=false;RefreshUpdateButton();}
    }
    void RefreshUpdateButton(){
        if(updateButton is null)return;
        updateButton.Content=updateBusy?"Aguarde…":readyVersion is null?"Atualizar versão":$"Reiniciar e usar {readyVersion}";
        updateButton.IsEnabled=!updateBusy;
    }
    void SetUpdateStatus(string message){updateMessage=message;if(updateStatus is not null)updateStatus.Text=message;RefreshUpdateButton();}
    void BuildResearch(){
        ResearchDetails.Children.Clear();
        var updates=new DockPanel();
        updateButton=Button("Atualizar versão",()=>_=UpdateClicked());
        updateButton.VerticalAlignment=VerticalAlignment.Center;
        DockPanel.SetDock(updateButton,Dock.Right);
        updates.Children.Add(updateButton);
        var version=new StackPanel();
        version.Children.Add(Text("Bardola · "+RunningVersion,18,true));
        updateStatus=Text(updateMessage,13,false,Muted,top:6);
        version.Children.Add(updateStatus);
        updates.Children.Add(version);
        ResearchDetails.Children.Add(Card(updates));
        RefreshUpdateButton();
        var privacy=new StackPanel();
        privacy.Children.Add(SettingRow("Modo ghost","Oculta o Bardola e seus overlays de transmissões e gravações compatíveis com a proteção de captura do Windows.",preferences.GhostMode,SetGhostMode));
        ghostWarning=Text("",12,false,Accent,top:6);
        privacy.Children.Add(ghostWarning);
        RefreshGhostStatus();
        ResearchDetails.Children.Add(Card(privacy));
        var import=new StackPanel();import.Children.Add(Text("Importação de builds",18,true));
        import.Children.Add(SettingRow("Importar runas automaticamente","Aplicar as runas da build durante a seleção de campeão.",AutoRunes.IsChecked==true,value=>AutoRunes.IsChecked=value));
        import.Children.Add(SettingRow("Importar itens automaticamente","Preparar o conjunto de itens na loja do LoL.",AutoItems.IsChecked==true,value=>AutoItems.IsChecked=value));
        import.Children.Add(Button("Adicionar arsenal salvo do Blitz…",()=>_=ImportLocalArsenal(),top:10));
        import.Children.Add(SettingRow("Importar feitiços automaticamente","Usar os dois feitiços recomendados pela build, confirmando a aplicação no LoL.",AutoSpells.IsChecked==true,v=>AutoSpells.IsChecked=v));
        var flash=new StackPanel{Margin=new Thickness(0,14,0,0)};flash.Children.Add(Text("Posição do Flash",14,true));var flashPosition=new ComboBox{ItemsSource=new[]{"D · esquerda","F · direita"},SelectedIndex=preferences.FlashOnLeft?0:1,Margin=new Thickness(0,8,0,0)};flashPosition.SelectionChanged+=(_,_)=>{preferences.FlashOnLeft=flashPosition.SelectedIndex==0;importRequest?.Cancel();lastImportKey="";lastImportAttempt="";_=SavePreferences();};flash.Children.Add(flashPosition);import.Children.Add(flash);
        import.Children.Add(SettingRow("Reutilizar página ativa com backup","Quando não houver espaço, salvar a página antes de reutilizar.",preferences.ReuseActiveRunePage,value=>{preferences.ReuseActiveRunePage=value;importRequest?.Cancel();lastImportKey="";_=SavePreferences();}));
        import.Children.Add(SettingRow("Detectar campeão e modo","Acompanhar sua seleção no cliente do League.",AutoDetect.IsChecked==true,value=>AutoDetect.IsChecked=value));
        import.Children.Add(Button("Restaurar backup de runas",()=>RestoreRunes(this,new RoutedEventArgs()),top:14));ResearchDetails.Children.Add(Card(import));
        AddTimerSettings();
        ResearchDetails.Children.Add(Card(SettingRow("Tier das cartas · ARAM Desordem","Reconhece os títulos em inglês e, com o catálogo do patch, em português. Em modo sem bordas, mostra a nota da carta para o seu campeão.",preferences.CardTiers,v=>{preferences.CardTiers=v;if(!v)cardOverlay?.Hide();_=SavePreferences();})));
        var title=Text("Painel durante a partida",21,true,top:8);title.Margin=new Thickness(0,8,0,8);ResearchDetails.Children.Add(title);
        ResearchDetails.Children.Add(Text("Janela flutuante para modo sem bordas ou segundo monitor. Aparece só em partida, quando ativada abaixo.",12,false,Muted));
        var grid=new Grid{Margin=new Thickness(0,12,0,0)};grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());
        for(int i=0;i<2;i++)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        void Tile(int position,string symbol,string title,string description,bool enabled,Action<bool> changed){var p=new StackPanel();p.Children.Add(Text(symbol,26,true,Accent));p.Children.Add(Text(title,17,true,top:12));p.Children.Add(Text(description,12,false,Muted,top:8));var check=new CheckBox{Content="Ativado",IsChecked=enabled,Margin=new Thickness(0,18,0,0)};check.Checked+=(_,_)=>changed(true);check.Unchecked+=(_,_)=>changed(false);p.Children.Add(check);var card=Card(p);card.Margin=new Thickness(position%2==0?0:6,0,position%2==0?6:0,12);Grid.SetColumn(card,position%2);Grid.SetRow(card,position/2);grid.Children.Add(card);}
        Tile(0,"↗","Seu desempenho","KDA, CS/min, ouro e visão do seu jogador, atualizados durante a partida.",preferences.OverlayStats,v=>{preferences.OverlayStats=v;_=SavePreferences();UpdateOverlay();});
        Tile(1,"◈","Build ao seu lado","Itens principais da build do campeão que você está jogando.",preferences.OverlayBuild,v=>{preferences.OverlayBuild=v;_=SavePreferences();UpdateOverlay();});
        Tile(2,"Q  W  E","Prioridade de habilidades","Consulta à prioridade da build, com campeão e fonte conferidos.",preferences.OverlaySkills,v=>{preferences.OverlaySkills=v;_=SavePreferences();UpdateOverlay();});
        var goal=new StackPanel();goal.Children.Add(Text("◎",26,true,Accent));goal.Children.Add(Text("Meta pessoal de farm",17,true,top:12));goal.Children.Add(Text("Comparação com uma meta escolhida por você, em CS por minuto.",12,false,Muted,top:8));var target=new ComboBox{Margin=new Thickness(0,16,0,0),ItemsSource=new double[]{4,5,6,7,8,9,10},SelectedItem=preferences.CsTarget};target.SelectionChanged+=(_,_)=>{if(target.SelectedItem is double value){preferences.CsTarget=value;_=SavePreferences();UpdateOverlay();}};goal.Children.Add(target);var goalCard=Card(goal);goalCard.Margin=new Thickness(6,0,0,12);Grid.SetColumn(goalCard,1);Grid.SetRow(goalCard,1);grid.Children.Add(goalCard);ResearchDetails.Children.Add(grid);
        var pending=new StackPanel();pending.Children.Add(Text("Outros recursos das referências",18,true));pending.Children.Add(Text("Ultimates aliados, comparação de inventários e estatísticas da tela de carregamento ainda não estão disponíveis nesta versão. Cada recurso precisa de uma fonte de dados e validação próprias.",12,false,Muted,top:10));ResearchDetails.Children.Add(Card(pending));
        ResearchDetails.Children.Add(Button("Código-fonte e licença GPL-3.0 ↗",()=>OpenUrl("https://github.com/robertfxbr/Bardola"),top:12));
        ResearchDetails.Children.Add(Text("Bardola is not endorsed by Riot Games and does not reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games and all associated properties are trademarks or registered trademarks of Riot Games, Inc.",10,false,Muted,top:8));
    }
    FrameworkElement SettingRow(string title,string description,bool enabled,Action<bool> changed){
        var row=new Grid{Margin=new Thickness(0,16,0,0)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var labels=new StackPanel{Margin=new Thickness(0,0,20,0)};labels.Children.Add(Text(title,14,true));labels.Children.Add(Text(description,12,false,Muted,top:3));row.Children.Add(labels);
        var check=new CheckBox{IsChecked=enabled,VerticalAlignment=VerticalAlignment.Center,ToolTip=title};check.Checked+=(_,_)=>changed(true);check.Unchecked+=(_,_)=>changed(false);Grid.SetColumn(check,1);row.Children.Add(check);return row;
    }
}
