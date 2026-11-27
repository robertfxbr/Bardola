using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    CancellationTokenSource? draftRequest;
    (long Game,string Role)? draftKey;
    string draftTier="",draftStatus="",draftRendered="";
    DraftPick[] draftPool=[];
    DateTimeOffset draftRetry;

    bool InDraft=>snapshot.Phase=="Seleção de campeão" && snapshot.Mode=="CLASSIC" && snapshot.Draft!=null;

    void UpdateDraft(){
        if(!InDraft){draftRequest?.Cancel();draftKey=null;draftRendered="";draftPool=[];return;}
        var key=(snapshot.Draft!.GameId,snapshot.Role);
        if(draftKey==key && (draftRequest!=null || DateTimeOffset.UtcNow<draftRetry))return;
        bool opening=draftKey is null;
        draftRequest?.Cancel();draftKey=key;draftPool=[];draftTier="";draftRendered="";
        draftStatus="Consultando sua função, maestria e campeões disponíveis…";
        var pending=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        draftRequest=pending;
        if(opening && AutoDetect.IsChecked==true)ShowGame(this,new RoutedEventArgs());
        _=LoadDraft(snapshot.Draft,snapshot.Role,pending);
    }

    async Task LoadDraft(DraftSession session,string role,CancellationTokenSource pending){
        using(pending){
            var ct=pending.Token;
            try{
                using var api=leagueApiFactory();
                var roles=await api.GetAsync("/lol-perks/v1/recommended-champion-positions",ct);
                var ids=await api.GetAsync("/lol-champ-select/v1/pickable-champion-ids",ct);
                async Task<JsonNode?> Optional(string path){
                    try{return await api.GetAsync(path,ct);}
                    catch(HttpRequestException){return null;}
                }
                var mastery=await Optional("/lol-champion-mastery/v1/local-player/champion-mastery");
                var ranked=await Optional("/lol-ranked/v1/current-ranked-stats");
                ct.ThrowIfCancellationRequested();
                var ranks=(ranked?["queues"] as JsonArray)?.OfType<JsonObject>().ToArray()??[];
                var solo=ranks.FirstOrDefault(x=>x["queueType"]?.ToString()=="RANKED_SOLO_5x5")??ranked?["queueMap"]?["RANKED_SOLO_5x5"];
                string? rank=solo?["tier"]?.ToString()?.ToLowerInvariant();
                draftTier=rank is "iron" or "bronze" or "silver" or "gold" or "platinum" or "emerald" or "diamond" or "master" or "grandmaster" or "challenger"?rank:"all";
                draftPool=DraftAdvice.Pool(roles,mastery,ids,role);
                draftStatus=draftPool.Length>0?"Comparando os campeões da sua função com o meta do elo…":
                    role=="default"?"Aguardando a definição da sua função.":"Nenhum campeão disponível foi confirmado para a sua função.";
                draftRendered="";
                if(InDraft && GamePage.IsVisible)RenderGame();
                var focus=DraftAdvice.Available(draftPool,session).Take(8).Select(p=>p.Champion).ToArray();
                bool missing=false;
                foreach(var group in focus.Chunk(3)){
                    var results=await Task.WhenAll(group.Select(async id=>{
                        var champ=catalog.Champions.FirstOrDefault(c=>c.Id==id);
                        if(champ==null)return (id,(BuildDocument?)null);
                        try{
                            var b=await builds.GetAsync(new(id,champ.Slug,GameMode.Rift,role,draftTier,catalog.Patch),false,ct,requireMatchups:true);
                            return (id,b.Document);
                        }catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or TimeoutException){return (id,null);}
                    }));
                    ct.ThrowIfCancellationRequested();
                    foreach(var (id,meta) in results){
                        if(meta==null){missing=true;continue;}
                        int i=Array.FindIndex(draftPool,p=>p.Champion==id);
                        if(i>=0)draftPool[i]=draftPool[i] with{Meta=meta};
                    }
                    draftRendered="";
                }
                draftStatus=draftPool.Length==0?draftStatus:missing?"Dados de meta parciais. Maestria e disponibilidade vêm do cliente.":"";
                draftRetry=DateTimeOffset.UtcNow.AddMinutes(missing || draftPool.Length==0?1:15);
            }catch(OperationCanceledException) when(ct.IsCancellationRequested){}
            catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException){
                if(!ct.IsCancellationRequested){draftStatus="O cliente ainda não entregou os dados da seleção.";draftRetry=DateTimeOffset.UtcNow.AddSeconds(10);}
            }finally{
                if(draftRequest==pending){draftRequest=null;draftRendered="";if(InDraft && !ct.IsCancellationRequested && GamePage.IsVisible)RenderGame();}
            }
        }
    }

    void RenderDraft(){
        var s=snapshot.Draft!;
        string fingerprint=$"{s.GameId}/{snapshot.Role}/{s.Banning}/{draftStatus}/{draftTier}/{string.Join(',',s.Allies)}/{string.Join(',',s.Enemies)}/{string.Join(',',s.Bans)}/{string.Join(',',s.AllyIntents)}";
        if(draftRendered==fingerprint && GameDetails.Children.Count>0)return;
        draftRendered=fingerprint;renderedScore=null;GameDetails.Children.Clear();
        string Names(IEnumerable<int> ids)=>string.Join(", ",ids.Select(id=>catalog.Champions.FirstOrDefault(c=>c.Id==id)?.Name??id.ToString()));
        var hero=new StackPanel();
        hero.Children.Add(Text(s.Banning?"Sugestões para a fase de ban":"Sugestões de escolha",24,true));
        string tier=draftTier=="all"?"Todos os elos":RankName(draftTier.ToUpperInvariant());
        hero.Children.Add(Text(RoleName(snapshot.Role)+(draftTier.Length>0?"  ·  "+tier:"")+"  ·  Patch "+catalog.Patch,13,true,Accent,top:6));
        hero.Children.Add(Text(s.Enemies.Length==0?"Aguardando o primeiro pick adversário confirmado.":"Adversários confirmados · "+Names(s.Enemies),12,false,Muted,top:8));
        if(draftStatus.Length>0)hero.Children.Add(Text(draftStatus,12,false,Muted,top:5));
        GameDetails.Children.Add(Card(hero));
        var picks=DraftAdvice.Available(draftPool,s).Take(3).ToArray();
        if(s.Banning){
            var bans=DraftAdvice.BanOptions(picks,s);
            if(bans.Length>0)GameDetails.Children.Add(Card(Text("Bans contra seus possíveis picks · "+Names(bans),15,true)));
        }
        foreach(var p in picks){
            var row=new DockPanel();var champ=catalog.Champions.FirstOrDefault(c=>c.Id==p.Champion);
            if(champ is null)continue;
            var icon=new Image{Width=48,Height=48,Margin=new Thickness(0,0,14,0)};
            LoadIcon(icon,$"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{champ.Key}.png",lifetime.Token);
            DockPanel.SetDock(icon,Dock.Left);row.Children.Add(icon);
            var body=new StackPanel();body.Children.Add(Text(champ.Name,18,true));
            string stats=p.Meta is {WinRate:double wr,Games:>0} meta?$"  ·  {wr.ToString("0.0",Pt)}% de vitórias em {meta.Games.ToString("N0",Pt)} jogos ranqueados":"";
            string experience=p.Points>=0?$"Maestria {p.Level} · {p.Points.ToString("N0",Pt)} pontos":"Maestria indisponível";
            body.Children.Add(Text(experience+stats,12,false,Muted,top:4));
            var good=p.Favorable(s);var bad=p.Difficult(s);
            if(good.Length>0)body.Children.Add(Text("Favorável contra "+Names(good),12,true,Accent,top:3));
            if(bad.Length>0)body.Children.Add(Text("Confronto difícil contra "+Names(bad),12,false,Muted,top:3));
            var observed=p.Matchups(s);
            if(observed.Length>0)body.Children.Add(Text(string.Join("   ·   ",observed.Select(m=>
                $"vs. {Names([m.Champion])} {m.WinRate.ToString("0.0",Pt)}% ({m.Games.ToString("N0",Pt)} jogos)")),11,false,Muted,top:3));
            row.Children.Add(body);GameDetails.Children.Add(Card(row));
        }
        if(draftPool.Any(p=>p.Meta!=null))GameDetails.Children.Add(Text("Meta e confrontos · LoLalytics, ranqueada Solo/Duo como referência para o Normal Game.",11,false,Muted));
    }
}
