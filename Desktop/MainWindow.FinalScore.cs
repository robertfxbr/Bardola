using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    AccountMatch? finalScore,renderedScore;
    DateTimeOffset retryScoreAt;

    async Task UpdateFinalScore()
    {
        if(snapshot.Phase!="Fim de partida"){finalScore=null;retryScoreAt=default;return;}
        if(finalScore!=null || DateTimeOffset.UtcNow<retryScoreAt)return;
        retryScoreAt=DateTimeOffset.UtcNow.AddSeconds(4);
        var token=lifetime.Token;
        try {
            using var api=leagueApiFactory();
            var session=await api.GetAsync("/lol-gameflow/v1/session",token);
            if(!long.TryParse(session?["gameData"]?["gameId"]?.ToString(),out var id))return;
            var owner=await api.GetAsync("/lol-summoner/v1/current-summoner",token);
            AccountMatch? score=null;
            try {
                var data=await api.GetAsync("/lol-end-of-game/v1/eog-stats-block",token);
                score=FinalScore.Parse(data,owner?["puuid"]?.ToString()??"",id);
            }catch(HttpRequestException e) when(e.StatusCode==System.Net.HttpStatusCode.NotFound){}
            if(score is null && preferences.LinkAccount)score=account?.Matches.FirstOrDefault(m=>m.Id==id &&
                account.RiotId==string.Concat(owner?["gameName"],"#",owner?["tagLine"]));
            if(score is null || token.IsCancellationRequested || (await api.GetAsync("/lol-gameflow/v1/gameflow-phase",token))?.ToString()!="EndOfGame")return;
            finalScore=score;
            ShowGame(this,new RoutedEventArgs());
        }catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException){}
    }

    readonly Dictionary<int,(string Name,string Path)> cardCatalog=new();
    readonly Dictionary<int,ImageSource> cardImages=new();
    readonly SemaphoreSlim cardArtGate=new(1,1);

    async Task FillCardArt(List<(int Id,Grid Tile,Image Icon)> slots)
    {
        if(slots.Count==0)return;
        var ct=lifetime.Token;
        try {
            await cardArtGate.WaitAsync(ct);
            try{
                using var api=LeagueClient.Open();
                if(cardCatalog.Count==0 && await api.GetAsync("/lol-game-data/assets/v1/cherry-augments.json",ct) is System.Text.Json.Nodes.JsonArray list)
                    foreach(var entry in list.OfType<System.Text.Json.Nodes.JsonObject>())
                        if(int.TryParse(entry["id"]?.ToString(),out int id) && entry["augmentSmallIconPath"]?.ToString() is string path &&
                            entry["nameTRA"]?.ToString() is {Length:>0} name)cardCatalog.TryAdd(id,(name,path));
                async Task Load(int id){
                    if(cardImages.ContainsKey(id)||!cardCatalog.TryGetValue(id,out var data))return;
                    try{
                        using var stream=new MemoryStream(await api.AugmentIconAsync(data.Path,ct));
                        var image=new System.Windows.Media.Imaging.BitmapImage();image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        image.StreamSource=stream;image.EndInit();image.Freeze();cardImages[id]=image;
                    }catch(Exception e) when(e is IOException or HttpRequestException or FileFormatException or NotSupportedException or InvalidDataException){}
                }
                foreach(var group in slots.Select(s=>s.Id).Distinct().Chunk(8))await Task.WhenAll(group.Select(Load));
            }finally{cardArtGate.Release();}
        }catch(Exception e) when(e is IOException or HttpRequestException or OperationCanceledException or InvalidDataException or System.Text.Json.JsonException){}
        foreach(var slot in slots){
            if(cardCatalog.TryGetValue(slot.Id,out var data)){
                slot.Tile.ToolTip=data.Name;System.Windows.Automation.AutomationProperties.SetName(slot.Tile,data.Name);
            }
            if(cardImages.TryGetValue(slot.Id,out var icon)){slot.Icon.Source=icon;slot.Tile.Children[0].Visibility=Visibility.Hidden;}
        }
    }

    void AddFinalScore(AccountMatch match)
    {
        var champions=catalog.Champions.ToDictionary(c=>c.Id);
        var artByUrl=new Dictionary<string,Image>();
        var cardSlots=new List<(int Id,Grid Tile,Image Icon)>();
        Brush gold=Brush("#F0C478"),muted=Brush("#A7AFBF");
        var surface=new StackPanel{MaxWidth=1380};
        Image Art(string url){
            var image=new Image{Stretch=Stretch.UniformToFill};
            if(artByUrl.TryGetValue(url,out var source))
                image.SetBinding(Image.SourceProperty,new System.Windows.Data.Binding("Source"){Source=source});
            else {artByUrl.Add(url,image);LoadIcon(image,url,lifetime.Token);}
            return image;
        }
        TextBlock Line(string value,int size,bool strong,Brush color){
            var t=Text(value,size,strong,color);t.TextWrapping=TextWrapping.NoWrap;t.TextTrimming=TextTrimming.CharacterEllipsis;
            return t;
        }
        FrameworkElement Badges(IEnumerable<int> ids,bool cards,int size){
            var strip=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            foreach(int id in ids){
                var tile=new Grid{Width=size,Height=size,Margin=new Thickness(0,0,2,0),Background=Brush(cards?"#292438":"#202735")};
                if(cards){
                    var fallback=Line("◇",Math.Max(12,size-5),true,gold);fallback.HorizontalAlignment=HorizontalAlignment.Center;
                    tile.Children.Add(fallback);var icon=new Image();tile.Children.Add(icon);
                    tile.ToolTip=$"Carta {id}";cardSlots.Add((id,tile,icon));
                }else if(catalog.Items.TryGetValue(id,out var item)){
                    tile.Children.Add(Art(item.Icon));tile.ToolTip=item.Name;System.Windows.Automation.AutomationProperties.SetName(tile,item.Name);
                }else{tile.Children.Add(Line("·",14,true,muted));tile.ToolTip=$"Item {id}";}
                strip.Children.Add(tile);
            }
            return strip;
        }

        var teams=match.Teams??[];
        var local=teams.SelectMany(t=>t.Players).FirstOrDefault(p=>p.Local);
        var champion=champions.GetValueOrDefault(match.ChampionId);
        var hero=new Grid{Height=176,ClipToBounds=true,Background=Brush("#151A24")};
        if(champion!=null){
            var art=Art($"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{champion.Key}_0.jpg");
            var crop=new Grid{ClipToBounds=true};crop.Children.Add(art);
            // Anchor the artwork to the right while the dark side stays wide enough for the statistics.
            var band=new Grid();band.ColumnDefinitions.Add(new(){Width=new GridLength(0.30,GridUnitType.Star)});band.ColumnDefinitions.Add(new(){Width=new GridLength(0.70,GridUnitType.Star)});
            Grid.SetColumn(crop,1);band.Children.Add(crop);hero.Children.Add(band);
        }
        var shade=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,0)};
        shade.GradientStops.Add(new GradientStop(Color.FromRgb(20,24,34),0.28));shade.GradientStops.Add(new GradientStop(Color.FromArgb(238,20,24,34),0.50));
        shade.GradientStops.Add(new GradientStop(Color.FromArgb(42,20,24,34),0.90));hero.Children.Add(new Border{Background=shade});
        var content=new StackPanel{Margin=new Thickness(19,12,18,10)};
        var context=new StackPanel{Orientation=Orientation.Horizontal};
        var outcome=match.Win?Brush("#83E9C6"):Brush("#FFA998");
        context.Children.Add(new Border{CornerRadius=new CornerRadius(3),Padding=new Thickness(7,1,7,2),Background=Brush(match.Win?"#263D34":"#452E33"),
            Child=Line(match.Win?"VITÓRIA":"DERROTA",10,true,outcome)});
        var mode=Line($"{ModeLabel(match.Mode)}    ·    {match.Duration/60}:{match.Duration%60:00}",12,true,muted);mode.Margin=new Thickness(10,0,0,0);context.Children.Add(mode);content.Children.Add(context);
        var title=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,0)};
        title.Children.Add(Line(champion?.Name??$"Campeão {match.ChampionId}",30,true,Brushes.White));
        var kda=Line($"{match.Kills} / {match.Deaths} / {match.Assists} KDA    ·    {match.Cs} CS",13,true,muted);
        kda.VerticalAlignment=VerticalAlignment.Center;kda.Margin=new Thickness(14,4,0,0);title.Children.Add(kda);content.Children.Add(title);
        var metrics=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,0,0)};
        void Metric(string label,int? value){if(value is null)return;var box=new StackPanel{Margin=new Thickness(0,0,26,0)};
            box.Children.Add(Line(value.Value.ToString("N0",Pt),21,true,Brushes.White));box.Children.Add(Line(label,11,false,muted));metrics.Children.Add(box);}
        Metric("Dano a campeões",match.ChampionDamage);Metric("Dano recebido",match.DamageTaken);Metric("Cura e escudo em aliados",match.AllyHealingAndShielding);
        if(match.DamageTaken is int taken)Metric("Dano recebido por vida",(int)Math.Round(taken/(double)(match.Deaths+1)));
        Metric("Dano mitigado",local?.Mitigated);
        content.Children.Add(metrics);
        var loadout=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};
        void Group(string label,List<int>? ids,bool cards){if(ids is not {Count:>0})return;
            var text=Line(label,11,true,cards?gold:muted);text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new Thickness(0,0,7,0);
            loadout.Children.Add(text);var icons=Badges(ids,cards,25);icons.Margin=new Thickness(0,0,13,0);loadout.Children.Add(icons);}
        Group("Cartas",local?.Cards,true);Group("Itens finais",match.Items,false);
        content.Children.Add(loadout);hero.Children.Add(content);
        var timeline=Button("Linha do tempo",()=>ShowMatchTimeline(match));
        timeline.HorizontalAlignment=HorizontalAlignment.Right;timeline.VerticalAlignment=VerticalAlignment.Top;
        timeline.Margin=new Thickness(0,10,12,0);hero.Children.Add(timeline);
        surface.Children.Add(hero);

        if(teams.Count>0){
            var board=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,10,0,0)};
            foreach(var team in teams.OrderBy(t=>t.Id)){
                bool blue=team.Id==100;var color=Brush(blue?"#73C9F8":"#FB9B8F");
                var list=new StackPanel();
                var heading=new DockPanel{Margin=new Thickness(10,7,10,6)};
                long? damageSum=team.Players.All(p=>p.Damage.HasValue)?team.Players.Sum(p=>(long)p.Damage!.Value):null;
                long kills=team.Players.Sum(p=>(long)p.Kills);
                bool fullTeam=team.Players.Count==5;
                bool knownKills=fullTeam && kills>0 && team.Players.All(p=>p.HasKills);
                string totalDamage=damageSum?.ToString("N0",Pt) is string amount?amount+" dano    ·    ":"";
                var total=Line($"{totalDamage}{team.Players.Sum(p=>p.Kills)} / {team.Players.Sum(p=>p.Deaths)} / {team.Players.Sum(p=>p.Assists)}",12,true,muted);
                DockPanel.SetDock(total,Dock.Right);heading.Children.Add(total);
                heading.Children.Add(Line((team.Id switch{100=>"Equipe azul",200=>"Equipe vermelha",_=>$"Equipe {team.Id}"})+
                    (team.Win is bool won?(won?"   ·   Vitória":"   ·   Derrota"):""),14,true,color));list.Children.Add(heading);
                Grid ScoreRow(int height,Brush background){
                    var row=new Grid{Height=height,Background=background};
                    foreach(var width in new[]{35,0,155,63,60})row.ColumnDefinitions.Add(new(){Width=width==0?new GridLength(1,GridUnitType.Star):new GridLength(width)});
                    return row;
                }
                var columns=ScoreRow(20,Brush("#141922"));
                foreach(var (name,col) in new[]{("JOGADOR",1),(team.Players.Any(p=>p.Cards.Count>0)?"ITENS / CARTAS":"ITENS",2),("KDA",3),("DANO",4)}){
                    var text=Line(name,10,true,muted);text.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(text,col);columns.Children.Add(text);
                }
                list.Children.Add(columns);
                foreach(var player in team.Players){
                    var row=ScoreRow(50,Brush(player.Local?"#302D29":"#191E28"));
                    void Cell(UIElement el,int col){Grid.SetColumn(el,col);row.Children.Add(el);}
                    var champ=champions.GetValueOrDefault(player.Champion);
                    if(champ!=null){
                        var portrait=Art($"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{champ.Key}.png");
                        portrait.Width=28;portrait.Height=28;portrait.ToolTip=champ.Name;Cell(portrait,0);
                    }
                    var who=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(2,0,4,0),ToolTip=player.Name};
                    who.Children.Add(Line(player.Name.Length>0?player.Name:champ?.Name??"Jogador",12,true,player.Local?gold:Brushes.White));
                    who.Children.Add(Line(champ?.Name??"",10,false,muted));Cell(who,1);
                    var kit=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
                    kit.Children.Add(Badges(player.Items,false,18));
                    if(player.Cards.Count>0){var cards=Badges(player.Cards,true,17);cards.Margin=new Thickness(0,3,0,0);kit.Children.Add(cards);}Cell(kit,2);
                    var stat=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
                    stat.Children.Add(Line($"{player.Kills}/{player.Deaths}/{player.Assists}",12,true,Brushes.White));
                    stat.Children.Add(Line($"{player.Cs} CS",10,false,muted));
                    long involved=(long)player.Kills+player.Assists;
                    if(knownKills && player.HasAssists && involved<=kills){
                        var kp=Line((100.0*involved/kills).ToString("0",Pt)+"% PA",10,true,color);
                        kp.ToolTip=$"Participação em {involved} dos {kills} abates da equipe";
                        stat.Children.Add(kp);
                    }
                    Cell(stat,3);
                    var damage=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
                    damage.Children.Add(Line(player.Damage?.ToString("N0",Pt)??"—",12,true,Brushes.White));
                    if(fullTeam && damageSum is >0 && player.Damage is int dealt){
                        double share=dealt/(double)damageSum.Value;
                        var bar=new Grid{Height=3,Margin=new Thickness(0,4,0,0),Background=Brush("#333947")};
                        bar.ColumnDefinitions.Add(new(){Width=new GridLength(share,GridUnitType.Star)});
                        bar.ColumnDefinitions.Add(new(){Width=new GridLength(1-share,GridUnitType.Star)});
                        bar.Children.Add(new Border{Background=color});damage.Children.Add(bar);
                        damage.Children.Add(Line((100*share).ToString("0.#",Pt)+"%",10,true,color));
                    }
                    var breakdown=new List<string>{"Dano a campeões · participação no dano da equipe"};
                    if(player.Physical is int physical)breakdown.Add($"Físico   {physical.ToString("N0",Pt)}");
                    if(player.Magic is int magic)breakdown.Add($"Mágico   {magic.ToString("N0",Pt)}");
                    if(player.TrueDamage is int pure)breakdown.Add($"Verdadeiro   {pure.ToString("N0",Pt)}");
                    damage.ToolTip=string.Join(Environment.NewLine,breakdown);Cell(damage,4);
                    list.Children.Add(new Border{BorderBrush=Brush("#2C323F"),BorderThickness=new Thickness(0,1,0,0),Child=row});
                }
                board.Children.Add(new Border{Background=Brush("#1D2431"),BorderBrush=color,BorderThickness=new Thickness(0,2,0,0),
                    Margin=new Thickness(blue?0:4,0,blue?4:0,0),Child=list});
            }
            surface.Children.Add(board);
        }
        GameDetails.Children.Add(surface);GamePage.ScrollToTop();
        _=FillCardArt(cardSlots);
    }
}
