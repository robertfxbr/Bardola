using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    void RenderBuildV2()
    {
        if(current is null)return;
        var doc=current.Document;var v=doc.Variants[variantIndex];BuildDetails.Children.Clear();RunePanel.Children.Clear();MatchupPanel.Children.Clear();SourcePanel.Children.Clear();
        for(int i=0;i<VariantButtons.Children.Count;i++)if(VariantButtons.Children[i] is Button b){b.Background=i==variantIndex?Theme("latao-fundo"):Theme("noite-controle");b.BorderBrush=i==variantIndex?Accent:Theme("linha");b.Foreground=i==variantIndex?Accent:Muted;b.Padding=new Thickness(13,8,13,8);}
        if(v.PurchaseOrder is {Count:>0} order)BuildDetails.Children.Add(Card(ItemContent("Ordem de compra",order,true)));
        var corePanel=ItemContent(v.PurchaseOrder is null?"Itens principais":"Build final",v.Core,true);
        if(v.ArsenalSource is not null)corePanel.Children.Add(Text(v.ArsenalSource+" · "+v.Name,12,false,Accent,top:8));
        if(v.Games>0)corePanel.Children.Insert(1,Text($"{v.WinRate:0.0}% de vitórias   ·   {v.Games.ToString("N0",Pt)} jogos com este núcleo",11,false,Brush("#8ECABE"),top:6));
        BuildDetails.Children.Add(Card(corePanel));
        var preparation=new Grid();preparation.ColumnDefinitions.Add(new());preparation.ColumnDefinitions.Add(new(){Width=new GridLength(12)});preparation.ColumnDefinitions.Add(new());
        preparation.Children.Add(Card(ItemContent("Começo da partida",v.Start)));
        if(v.Boots?.Count>0){var boots=Card(ItemContent("Botas",v.Boots));Grid.SetColumn(boots,2);preparation.Children.Add(boots);}
        else if(v.SkillPriority!=""){var skill=Card(SkillContent(v));Grid.SetColumn(skill,2);preparation.Children.Add(skill);}
        BuildDetails.Children.Add(preparation);
        if(v.Alternatives.Count>0)BuildDetails.Children.Add(Card(ItemContent("Escolhas situacionais",v.Alternatives)));
        if(v.FrequentItems is {Count:>0} frequent)BuildDetails.Children.Add(Card(ItemContent("Mais comprados",frequent)));
        var runes=new StackPanel();runes.Children.Add(Text("Página de runas",17,true));
        if(v.Runes is { } page) {
            runes.Children.Add(Text(RuneName(page.PrimaryStyle)+"  +  "+RuneName(page.SecondaryStyle),12,false,Accent,top:6));
            runes.Children.Add(RuneRow(page.Perks.Take(4),true));
            var lower=new Grid{Margin=new Thickness(0,5,0,0)};lower.ColumnDefinitions.Add(new());lower.ColumnDefinitions.Add(new());
            var secondary=new StackPanel();secondary.Children.Add(Text(RuneName(page.SecondaryStyle),11,false,Muted));secondary.Children.Add(RuneRow(page.Perks.Skip(4).Take(2),false));lower.Children.Add(secondary);
            var shards=new StackPanel();shards.Children.Add(Text("Fragmentos",11,false,Muted));var wrap=new WrapPanel{Margin=new Thickness(0,10,0,0)};
            foreach(int id in page.Perks.Skip(6)){var info=catalog.Runes.GetValueOrDefault(id);var badge=Badge(info?.Name??$"Runa {id}");badge.ToolTip=info?.Description;wrap.Children.Add(badge);}shards.Children.Add(wrap);Grid.SetColumn(shards,1);lower.Children.Add(shards);runes.Children.Add(lower);
        }else {
            runes.Children.Add(Text(doc.Query.Mode==GameMode.Mayhem?"SEM RUNAS NESTE MODO":"RUNAS INDISPONÍVEIS",12,true,Accent,top:18));
            runes.Children.Add(Text(doc.Query.Mode==GameMode.Mayhem?"Desordem desativa runas. A importação automática envia os itens e preserva sua página.":"A fonte não retornou uma página validada. O Bardola não vai substituir suas runas por uma sugestão incompleta.",12,false,Muted,top:10));
        }
        var spells=new StackPanel();spells.Children.Add(Text("Feitiços de invocador",17,true));
        if(v.SummonerSpells is {Count:2}){
            try{foreach(int id in SpellRules.Order(v.SummonerSpells,doc.Query.Mode,preferences.FlashOnLeft)){var info=catalog.Spells.GetValueOrDefault(id);var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,10,0,0)};var icon=new Image{Width=34,Height=34,Margin=new Thickness(0,0,10,0)};if(info is not null)LoadIcon(icon,info.Icon,lifetime.Token);row.Children.Add(icon);row.Children.Add(Text(info?.Name??SpellRules.Name(id),14,true));spells.Children.Add(row);}}
            catch(System.IO.InvalidDataException){spells.Children.Add(Text("Par de feitiços não validado para este modo.",12,false,Muted,top:10));}
        }else spells.Children.Add(Text("Atualize a build para consultar os feitiços desta fonte.",12,false,Muted,top:10));
        RunePanel.Children.Add(Card(spells));
        RunePanel.Children.Add(Card(runes));
        if(v.Boots?.Count>0 && v.SkillPriority!="")RunePanel.Children.Add(Card(SkillContent(v)));
        if(doc.Augments?.Count>0){var a=new StackPanel();a.Children.Add(Text("Aprimoramentos em destaque",16,true));var rows=new WrapPanel{Margin=new Thickness(0,12,0,0)};foreach(string name in doc.Augments){var grade=doc.AugmentTiers?.GetValueOrDefault(name);rows.Children.Add(Badge(grade is null?name:$"{grade}  ·  {name}"));}a.Children.Add(rows);a.Children.Add(Text(doc.Source+" · "+doc.Query.Slug+" · patch "+doc.Patch,10,false,Muted,top:7));MatchupPanel.Children.Add(Card(a));}
        if(doc.StrongAgainst?.Count>0 || doc.WeakAgainst?.Count>0){var p=new StackPanel();p.Children.Add(Text("Matchups do campeão",16,true));var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());var strong=new StackPanel();CounterList(strong,"Favoráveis",doc.StrongAgainst??[]);grid.Children.Add(strong);var weak=new StackPanel();CounterList(weak,"Atenção contra",doc.WeakAgainst??[]);Grid.SetColumn(weak,1);grid.Children.Add(weak);p.Children.Add(grid);MatchupPanel.Children.Add(Card(p));}
        var source=new StackPanel();source.Children.Add(Text(doc.Source+"  ·  patch "+doc.Patch,12,true,Accent));
        source.Children.Add(Text("Consulta: "+LocalTime(doc.FetchedAt)+"  ·  "+(doc.Query.Mode==GameMode.Rift?RoleName(doc.Role)+" / "+TierName(doc.Query.Tier):ModeName(doc.Query.Mode)),11,false,Muted,top:7));
        source.Children.Add(Text(doc.Note,11,false,Muted,top:7));
        if(variantIndex>0)source.Children.Add(Text("Uma amostra menor pode elevar a taxa de vitórias. Compare as duas opções.",11,false,Accent,top:7));
        var links=new WrapPanel{Margin=new Thickness(0,12,0,0)};foreach(var (name,url) in SourceLinks(doc.Query)){var b=Button(name+" ↗",()=>OpenUrl(url));b.FontSize=10;b.Padding=new Thickness(9,6,9,6);links.Children.Add(b);}source.Children.Add(links);SourcePanel.Children.Add(Card(source));UpdateCompact();
    }
    StackPanel SkillContent(BuildVariant variant)
    {
        var p=new StackPanel();p.Children.Add(Text("Prioridade de habilidades",14,true));var wrap=new WrapPanel{Margin=new Thickness(0,12,0,0)};
        foreach(char key in variant.SkillPriority){wrap.Children.Add(new Border{Width=36,Height=34,Background=Brush("#30323A"),BorderBrush=Brush("#6D5A3A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Margin=new Thickness(0,0,8,0),Child=new TextBlock{Text=key.ToString(),FontSize=18,FontWeight=FontWeights.Bold,Foreground=Accent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});}p.Children.Add(wrap);
        if(variant.SkillOrder!=""){var order=new WrapPanel{Margin=new Thickness(0,12,0,0)};for(int i=0;i<variant.SkillOrder.Length;i++){var cell=new StackPanel{Width=23,Margin=new Thickness(0,0,3,4)};cell.Children.Add(Text((i+1).ToString(),9,false,Muted));cell.Children.Add(Text(variant.SkillOrder[i].ToString(),12,true,Accent,top:3));order.Children.Add(cell);}p.Children.Add(order);}return p;
    }
    FrameworkElement RuneRow(IEnumerable<int> ids,bool primary)
    {
        var row=new WrapPanel{Margin=new Thickness(0,12,0,8)};
        foreach(int id in ids){var info=catalog.Runes.GetValueOrDefault(id);var p=new StackPanel{Width=primary?78:76,Margin=new Thickness(0,0,5,0)};var img=new Image{Width=primary?42:35,Height=primary?42:35,HorizontalAlignment=HorizontalAlignment.Left};if(info?.Icon is {Length:>0} icon)LoadIcon(img,icon,request?.Token??default);p.Children.Add(img);var label=Text(info?.Name??$"Runa {id}",10,false,Theme("papiro"),top:6);label.MaxHeight=30;p.Children.Add(label);p.ToolTip=new TextBlock{Text=(info?.Name??"")+"\n\n"+(info?.Description??""),TextWrapping=TextWrapping.Wrap,MaxWidth=340,Foreground=Theme("papiro")};row.Children.Add(p);}return row;
    }
    StackPanel ItemContent(string title,List<ItemChoice> items,bool core=false)
    {
        var panel=new StackPanel();panel.Children.Add(Text(title,core?18:15,true));var wrap=new WrapPanel{Margin=new Thickness(0,14,0,0)};
        foreach(var it in items){var info=catalog.Items.GetValueOrDefault(it.Id);var p=new StackPanel{Width=core?112:94,Margin=new Thickness(0,0,9,8)};int size=core?58:43;
            var image=new Image{Width=size,Height=size};if(info is not null)LoadIcon(image,info.Icon,request?.Token??default);
            var border=new Border{Child=image,Width=size+4,Height=size+4,BorderBrush=Brush(core?"#9C7D43":"#4A525F"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Left};p.Children.Add(border);
            p.Children.Add(Text((info?.Name??$"Item {it.Id}")+(it.Count>1?" ×"+it.Count:""),core?12:11,true,top:8));
            if(info is not null)p.Children.Add(Text(info.Gold.ToString("N0",Pt)+" ouro",10,false,Brush("#BAA37A"),top:4));
            if(it.Games>0)p.Children.Add(Text($"{it.WinRate:0.0}% · {it.Games:N0} jogos",9,false,Muted,top:4));
            if(info is not null)p.ToolTip=new TextBlock{Text=info.Name+"\n\n"+info.Description,TextWrapping=TextWrapping.Wrap,MaxWidth=340,Foreground=Theme("papiro")};wrap.Children.Add(p);
        }panel.Children.Add(wrap);return panel;
    }
    Border ItemGroupV2(string title,List<ItemChoice> items,bool core=false)=>Card(ItemContent(title,items,core));

    void ShowChampions(object sender,RoutedEventArgs e){ShowPage(ChampionPage,ChampionNav,"Catálogo de campeões","LEAGUE OF LEGENDS");RenderChampions();}
    void GallerySearchChanged(object sender,TextChangedEventArgs e){if(ready)RenderChampions();}
    void RenderChampions()
    {
        ChampionGrid.Children.Clear();string search=GallerySearch.Text.Trim();
        foreach(var c in catalog.Champions.Where(c=>c.Name.Contains(search,StringComparison.CurrentCultureIgnoreCase)||c.Key.Contains(search,StringComparison.OrdinalIgnoreCase))){var panel=new StackPanel();var img=new Image{Width=57,Height=57,HorizontalAlignment=HorizontalAlignment.Left};LoadIcon(img,$"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{c.Key}.png",lifetime.Token);panel.Children.Add(img);panel.Children.Add(Text(c.Name,13,true,top:9));var b=Button("",()=>{ChampionBox.SelectedItem=c;ShowBuilds(this,new());});b.Content=panel;b.Width=137;b.Height=116;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,12,12);b.Padding=new Thickness(12);b.Background=Brush("#171D24");b.BorderBrush=Brush("#3C444D");ChampionGrid.Children.Add(b);}
    }
    void RenderAccountV2()
    {
        var saved=preferences.LinkAccount?preferences.SavedProfile:null;
        var profile=publicProfile??account??saved;
        var top=new StackPanel();
        AccountDetails.Children.Clear();
        if(profile is not null && profileChampion.HasValue && profile.Matches.Count>0 && profileOwner==profile.Region+"/"+profile.RiotId){
            AccountDetails.Children.Add(Text($"{profile.RiotId}   ·   {profile.Region}   ·   Nível {profile.Level}",16,true));
            AddChampionProfileNavigation(profile);
            return;
        }
        top.Children.Add(Text(profile?.RiotId??"Perfil e histórico",29,true));
        string source=publicProfile is not null?"Perfil público · API do Bardola":account is not null?"Cliente do LoL":
            $"Salvo neste PC · {preferences.ProfileSavedAt?.ToLocalTime():dd/MM/yyyy HH:mm}";
        top.Children.Add(Text(profile is null?"Conecte a conta aberta no cliente ou consulte um Riot ID com o LoL fechado.":
            $"Nível {profile.Level}  ·  {profile.Region}  ·  {source}",13,false,Muted,top:8));
        var buttons=new WrapPanel{Margin=new Thickness(0,12,0,0)};
        var connect=Button(accountBusy?"Conectando…":account is null?"Conectar conta do LoL":"Sincronizar conta local",()=>_=ConnectAccount());
        connect.Name="ConnectAccountButton";connect.IsEnabled=!accountBusy;buttons.Children.Add(connect);
        if(account is not null || preferences.LinkAccount)buttons.Children.Add(Button("Desconectar",DisconnectAccount));
        top.Children.Add(buttons);
        if(accountMessage!="" && publicProfile is null && (account is not null || saved is null))
            top.Children.Add(Text(accountMessage,11,false,Muted,top:7));
        AddProfileLookup(top);AccountDetails.Children.Add(Card(top));
        if(profile is null)return;
        if(AddChampionProfileNavigation(profile))return;
        var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(250)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(18)});layout.ColumnDefinitions.Add(new());
        var ranks=new StackPanel();foreach(var rank in profile.Ranks){var p=new StackPanel();p.Children.Add(Text(rank.Queue=="RANKED_SOLO_5x5"?"RANQUEADA SOLO / DUO":"RANQUEADA FLEX",10,true,Muted));p.Children.Add(Text(RankName(rank.Tier)+" "+rank.Division,25,true,Accent,top:12));p.Children.Add(Text($"{rank.Points} PdL   ·   {rank.Wins}V / {rank.Losses}D",12,false,Muted,top:8));ranks.Children.Add(Card(p));}if(profile.Ranks.Count==0)ranks.Children.Add(Card(Text("Sem classificação disponível.",13,false,Muted)));layout.Children.Add(ranks);
        var history=new StackPanel();history.Children.Add(Text("Histórico recente",19,true));history.Children.Add(Text(profile.HistoryMessage,11,false,Muted,top:6));
        if(profile.Matches.Count>0){int wins=profile.Matches.Count(m=>m.Win);history.Children.Add(Text($"{wins} VITÓRIAS   /   {profile.Matches.Count-wins} DERROTAS    ·    ÚLTIMAS {profile.Matches.Count} PARTIDAS",11,true,Brush("#40CCB9"),top:15));
            foreach(var match in profile.Matches){var c=catalog.Champions.FirstOrDefault(c=>c.Id==match.ChampionId);var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(59)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=new GridLength(120)});
                var portrait=new Image{Width=45,Height=45,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};if(c is not null)LoadIcon(portrait,$"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{c.Key}.png",lifetime.Token);grid.Children.Add(portrait);
                var body=new StackPanel();body.Children.Add(Text((match.Win?"Vitória":"Derrota")+"   ·   "+(c?.Name??"Campeão"),15,true,Brush(match.Win?"#3AD4C0":"#EB8977")));body.Children.Add(Text(ModeLabel(match.Mode)+"   ·   "+TimeSpan.FromSeconds(match.Duration).ToString(@"mm\:ss")+(match.At>DateTimeOffset.MinValue?"   ·   "+LocalTime(match.At):""),11,false,Muted,top:7));Grid.SetColumn(body,1);grid.Children.Add(body);
                var stats=new StackPanel{HorizontalAlignment=HorizontalAlignment.Right};stats.Children.Add(Text($"{match.Kills}/{match.Deaths}/{match.Assists}",20,true));stats.Children.Add(Text($"{match.Cs} CS",11,false,Muted,top:7));Grid.SetColumn(stats,2);grid.Children.Add(stats);
                body.Children.Add(Button("Mais informações do campeão",()=>OpenChampionProfile(match.ChampionId)));
                var card=Card(grid);card.Margin=new Thickness(0,12,0,0);card.Background=Brush(match.Win?"#172326":"#271E22");card.BorderBrush=Brush(match.Win?"#26413E":"#49302E");history.Children.Add(card);
            }
        }else history.Children.Add(Card(Text("Nenhuma partida disponível nesta consulta.",13,false,Muted)));Grid.SetColumn(history,2);layout.Children.Add(history);AccountDetails.Children.Add(layout);
    }
}
