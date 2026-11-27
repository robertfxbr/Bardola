using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    void ShowMatchTimeline(AccountMatch match)
    {
        var panel=new DockPanel{Margin=new Thickness(18)};
        var status=Text("Carregando os eventos da partida…",13,false,Muted);
        DockPanel.SetDock(status,Dock.Top);panel.Children.Add(status);
        var list=new ListBox{Margin=new Thickness(0,10,0,0),Background=Theme("noite-painel"),Foreground=Theme("papiro")};
        panel.Children.Add(list);
        var dialog=new Window{Title="Linha do tempo · Bardola",Owner=this,Content=panel,Width=470,Height=450,
            Background=Brush("#141922"),WindowStartupLocation=WindowStartupLocation.CenterOwner};
        using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var ct=request.Token;
        dialog.Closed+=(_,_)=>request.Cancel();
        dialog.Loaded+=async (_,_)=>{
            try{
                using var api=leagueApiFactory();
                async Task<string> OwnerId()=>(await api.GetAsync("/lol-summoner/v1/current-summoner",ct))?["puuid"]?.ToString()??"";
                string owner=await OwnerId();
                var detail=await api.GetAsync($"/lol-match-history/v1/games/{match.Id}",ct);
                var timeline=await api.GetAsync($"/lol-match-history/v1/game-timelines/{match.Id}",ct);
                var events=MatchTimeline.Read(timeline,detail,owner,match.Id);
                if(owner!=await OwnerId()){
                    status.Text="A conta do League mudou. Abra a partida novamente pelo perfil.";
                    return;
                }
                if(ct.IsCancellationRequested)return;
                var names=catalog.Champions.ToDictionary(c=>c.Id,c=>c.Name);
                string Champion(int id)=>names.GetValueOrDefault(id,id>0?$"Campeão {id}":"");
                string Label(MatchEvent e){
                    string killer=Champion(e.KillerChampion);
                    string action=e.Kind switch{
                        "CHAMPION_KILL"=>killer.Length>0?$"{killer} abateu {Champion(e.VictimChampion)}":$"{Champion(e.VictimChampion)} foi abatido",
                        "BUILDING_KILL"=>e.Target switch{"TOWER_BUILDING"=>"Torre destruída","INHIBITOR_BUILDING"=>"Inibidor destruído",_=>"Estrutura destruída"},
                        _=>e.Target switch{"DRAGON"=>"Dragão abatido","BARON_NASHOR"=>"Barão abatido","RIFTHERALD"=>"Arauto abatido",_=>"Objetivo épico abatido"}
                    };
                    if(e.Kind!="CHAMPION_KILL" && killer.Length>0)action+=" · "+killer;
                    return $"{e.Seconds/60:00}:{e.Seconds%60:00}     {action}";
                }
                status.Text=events.Count>0?$"{events.Count} eventos · {ModeLabel(match.Mode)}":"O histórico ainda não tem eventos disponíveis para esta partida.";
                list.ItemsSource=events.Select(Label).ToList();
            }catch(OperationCanceledException) when(ct.IsCancellationRequested){}
            catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.Text.Json.JsonException){
                status.Text="Eventos indisponíveis. Você pode consultar a partida novamente mais tarde.";
            }
        };
        dialog.ShowDialog();
    }
}
