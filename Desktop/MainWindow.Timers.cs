using System.IO;
using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;
public partial class MainWindow
{
    readonly ManualRelicTimers relicTimers=new();
    TextBlock? nativeTimerStatus;
    string nativeTimerMessage="Aguardando o cliente do LoL para conferir os cronômetros.";
    bool configuringNativeTimers;
    DateTimeOffset lastNativeTimerAttempt=DateTimeOffset.MinValue;
    CancellationTokenSource? nativeTimerRequest;
    double lastTimerGameTime;
    async Task ApplyNativeTimerPreference(){
        if(!ready||configuringNativeTimers||DateTimeOffset.UtcNow-lastNativeTimerAttempt<TimeSpan.FromSeconds(30))return;
        configuringNativeTimers=true;lastNativeTimerAttempt=DateTimeOffset.UtcNow;
        nativeTimerRequest?.Dispose();nativeTimerRequest=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=nativeTimerRequest.Token;
        try{
            using var api=leagueApiFactory();bool enabled=preferences.NativeMinimapTimers;
            await NativeTimerSettings.ConfigureAsync(api,enabled,token);
            await NativeTimerSettings.ConfigureJungleAsync(api,preferences.JunglePath,token);
            if(!token.IsCancellationRequested)SetNativeTimerStatus(enabled?"Ativados no minimapa do LoL · configuração confirmada":"Modo básico do LoL · configuração confirmada");
        }catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception e) when(e is IOException or InvalidDataException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException){
            if(!token.IsCancellationRequested)SetNativeTimerStatus(e is InvalidDataException?e.Message:"Cliente indisponível. O Bardo.gg tentará configurar quando ele estiver aberto.");
        }finally{configuringNativeTimers=false;}
    }
    void SetNativeTimerStatus(string text){nativeTimerMessage=text;if(nativeTimerStatus is not null)nativeTimerStatus.Text=text;}
    void SetNativeTimers(bool value){preferences.NativeMinimapTimers=value;nativeTimerRequest?.Cancel();lastNativeTimerAttempt=DateTimeOffset.MinValue;SetNativeTimerStatus("Configuração pendente · será aplicada fora de uma partida.");_=SavePreferences();_=ApplyNativeTimerPreference();}
    void UpdateTimerClock(){
        if(snapshot.GameTime>0){if(lastTimerGameTime>snapshot.GameTime+1){relicTimers.Reset();ClearRelicReadings();}lastTimerGameTime=snapshot.GameTime;}
        else if(snapshot.Phase is "Saguão" or "Seleção de campeão" or "Fim de partida" or "Cliente aberto"){relicTimers.Reset();ClearRelicReadings();lastTimerGameTime=0;}
    }
    void AddTimerSettings(){
        var panel=new StackPanel();panel.Children.Add(Text("Cronômetros",21,true));
        panel.Children.Add(SettingRow("Todos os cronômetros no minimapa","Ativa o modo avançado do próprio LoL para acampamentos e objetivos.",preferences.NativeMinimapTimers,SetNativeTimers));
        panel.Children.Add(SettingRow("Rota recomendada da jungle","Mostra no minimapa a sequência inicial que o LoL recomenda para o seu campeão.",preferences.JunglePath,v=>{preferences.JunglePath=v;nativeTimerRequest?.Cancel();lastNativeTimerAttempt=DateTimeOffset.MinValue;_=SavePreferences();_=ApplyNativeTimerPreference();}));
        AddAutoRelicSettings(panel);
        nativeTimerStatus=Text(nativeTimerMessage,12,false,Accent,top:12);panel.Children.Add(nativeTimerStatus);
        panel.Children.Add(SettingRow("Relíquias de cura do ARAM · manual","Um contador para cada relíquia. Clique após o clarão de cura para iniciar.",preferences.OverlayRelics,v=>{preferences.OverlayRelics=v;_=SavePreferences();UpdateOverlay();}));
        panel.Children.Add(Text("A coleta não é informada pela interface pública do jogo; estes contadores são manuais. O tempo é ajustável e segue o relógio da partida.",12,false,Muted,top:8));
        var duration=new ComboBox{ItemsSource=new double[]{60,90,120,150,180},SelectedItem=preferences.RelicSeconds,Margin=new Thickness(0,12,0,0),ToolTip="Duração do contador manual em segundos"};duration.SelectionChanged+=(_,_)=>{if(duration.SelectedItem is double value){preferences.RelicSeconds=value;_=SavePreferences();}};panel.Children.Add(duration);
        ResearchDetails.Children.Add(Card(panel));
    }
    void AddRelicClocks(StackPanel panel){
        if(!preferences.OverlayRelics||GameService.DetectMode(snapshot.Mode) is not (GameMode.Aram or GameMode.Mayhem))return;
        panel.Children.Add(Text("CURAS · MARCAÇÃO MANUAL",11,true,Accent,top:16));
        string[] names=["Azul · externa","Azul · interna","Vermelha · externa","Vermelha · interna"];
        for(int i=0;i<4;i++){
            int index=i;double? remaining=relicTimers.Remaining(i,snapshot.GameTime);
            string timer=remaining is null?"Sem marcação":remaining>0?TimeSpan.FromSeconds(Math.Ceiling(remaining.Value)).ToString(@"mm\:ss"):"Contagem concluída";
            panel.Children.Add(Text(names[i]+"  ·  "+timer,12,true,top:10));
            panel.Children.Add(Button("Marcar após a cura",()=>{if(snapshot.GameTime<=0||GameService.DetectMode(snapshot.Mode) is not (GameMode.Aram or GameMode.Mayhem))return;relicTimers.Mark(index,snapshot.GameTime,preferences.RelicSeconds);UpdateOverlay();},top:4));
        }
    }
}
