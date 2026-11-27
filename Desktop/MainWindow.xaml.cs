using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NexoLoL.Core;
using NexoLoL.Updates;

namespace NexoLoL.Desktop;

public partial class MainWindow : Window
{
    readonly string dataRoot;
    readonly RemoteData remote;
    readonly GameService game=new();
    readonly AccountService accountService=new();
    readonly CancellationTokenSource lifetime=new();
    readonly DispatcherTimer pollTimer=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly DispatcherTimer selectionTimer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    CatalogService catalogService=null!;
    BuildService builds=null!;
    Catalog catalog=new();
    Preferences preferences=new();
    BuildResult? current;
    BuildQuery? selected;
    CancellationTokenSource? request;
    int variantIndex;
    bool ready,polling,applyingDetection;
    string lastDetection="";
    GameSnapshot snapshot=new(false,"Aguardando o LoL");
    GameSnapshot? lastLive;
    Window? compact;
    StackPanel? compactContent;
    readonly List<LocalSession> sessions=[];
    readonly SemaphoreSlim preferencesGate=new(1);
    GameAccount? account;
    bool accountBusy;
    string accountMessage="";
    CancellationTokenSource? accountRequest;
    int accountGeneration;
    DateTimeOffset lastAccountRefresh=DateTimeOffset.MinValue;
    static Brush Accent => Theme("latao");
    static Brush Muted => Theme("papiro-apagado");

    public MainWindow():this(new RemoteData()) {}
    public MainWindow(RemoteData remoteData,string? directory=null,Func<ILeagueApi>? apiFactory=null)
    {
        dataRoot=directory??BardoPaths.Root;if(directory is null)BardoPaths.MigrateLegacyData();
        preferences=Read<Preferences>("preferences-v2.json")??Read<Preferences>("preferences.json")??new();
        ghost.StatusChanged+=QueueGhostStatus;
        if(preferences.GhostMode)ghost.SetEnabled(true);
        SourceInitialized+=(_,_)=>ghost.Protect(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        importer=new ClientImporter(Path.Combine(dataRoot,"rune-backups"));leagueApiFactory=apiFactory??LeagueClient.Open;
        remote=remoteData;
        ApplyBrandMode(preferences.GhostMode);
        InitializeComponent();
        Title="Bardola "+RunningVersion;VersionLabel.Text=RunningVersion;
        pollTimer.Tick+=async(_,_)=>await PollGame();
        selectionTimer.Tick+=async(_,_)=>{selectionTimer.Stop();await LoadBuild();};
    }

    async void OnLoaded(object sender,RoutedEventArgs e)
    {
        StartUpdates();
        catalogService=new(dataRoot,remote);builds=new(Path.Combine(dataRoot,"builds"),remote.BuildHtmlAsync);
        try {
            if(preferences.RelicSeconds is <30 or >300 || !double.IsFinite(preferences.RelicSeconds))preferences.RelicSeconds=90;
            sessions.AddRange((Read<List<LocalSession>>("sessions.json")??[]).Take(30));
            catalog=await catalogService.LoadAsync(lifetime.Token);
            ChampionBox.ItemsSource=catalog.Champions;
            ChampionBox.SelectedItem=catalog.Champions.FirstOrDefault(c=>c.Id==preferences.Last?.ChampionId)??catalog.Champions.FirstOrDefault(c=>c.Id==26)??catalog.Champions[0];
            ModeBox.SelectedIndex=(int)(preferences.Last?.Mode??GameMode.Rift);
            SelectTag(RoleBox,preferences.Last?.Role??"default");SelectTag(TierBox,preferences.Last?.Tier??"emerald_plus");
            AutoDetect.IsChecked=preferences.AutoDetect;
            AutoItems.IsChecked=preferences.AutoImportItems;AutoRunes.IsChecked=preferences.AutoImportRunes;AutoSpells.IsChecked=preferences.AutoImportSpells;
            snapshot=await game.PollAsync(catalog,lifetime.Token);UpdateConnection();
            if(AutoDetect.IsChecked==true)ApplyDetection(true);
            ready=true;UpdateModeControls();SetNav(BuildNav);BuildResearch();RenderAccount();pollTimer.Start();if(!relicPoll.IsEnabled)StartRelicPolling();if(!cardPoll.IsEnabled)StartCardReader();StartUpdates();
            UpdateDraft();
            if(preferences.LinkAccount)_=ConnectAccount();
            await LoadBuild();
        }catch(OperationCanceledException) {}
        catch(Exception err) {
            Notice(err.Message+" Use Atualizar para tentar novamente.",true);
            RefreshButton.IsEnabled=true;
        }
    }

    T? Read<T>(string name)
    {
        try{return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(dataRoot,name)));}
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return default;}
    }
    async Task SavePreferences()
    {
        if(!ready)return;
        var token=lifetime.Token;bool entered=false;
        try{
            await preferencesGate.WaitAsync(token);entered=true;
            preferences.Last=selected;preferences.AutoDetect=AutoDetect.IsChecked==true;
            await Storage.WriteAsync(Path.Combine(dataRoot,"preferences-v2.json"),JsonSerializer.Serialize(preferences),token);
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or OperationCanceledException) {}
        finally{if(entered)preferencesGate.Release();}
    }
    void ContextChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!ready || applyingDetection)return;
        if(sender==TierBox){importRequest?.Cancel();lastImportKey="";lastImportAttempt="";}
        if(sender==ChampionBox && BuildPage.Visibility!=Visibility.Visible)ShowBuilds(this,new RoutedEventArgs());
        UpdateModeControls();MarkSelectionPending();selectionTimer.Stop();selectionTimer.Start();
    }
    void UpdateModeControls()
    {
        bool rift=ModeBox.SelectedIndex==0;RoleBox.IsEnabled=rift;TierBox.IsEnabled=rift;
    }
    static string SelectedTag(ComboBox box)=>((box.SelectedItem as ComboBoxItem)?.Tag as string)??"default";
    static void SelectTag(ComboBox box,string tag)
    {box.SelectedItem=box.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>(string)i.Tag==tag)??box.Items[0];}
    BuildQuery? Query()
    {
        if(ChampionBox.SelectedItem is not Champion c)return null;
        var mode=(GameMode)Math.Max(0,ModeBox.SelectedIndex);
        return new(c.Id,c.Slug,mode,mode==GameMode.Rift?SelectedTag(RoleBox):"default",mode==GameMode.Rift?SelectedTag(TierBox):"all",catalog.Patch);
    }
    async Task LoadBuild(bool force=false)
    {
        if(!ready)return;
        var query=Query();if(query is null){Notice("Selecione um campeão da lista para consultar a build.",true);return;}
        request?.Cancel();request?.Dispose();request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        CancellationToken token=request.Token;selected=query;current=null;variantIndex=0;ExportButton.IsEnabled=false;
        BuildDetails.Children.Clear();RunePanel.Children.Clear();MatchupPanel.Children.Clear();SourcePanel.Children.Clear();AbilityPanel.Children.Clear();VariantButtons.Children.Clear();HeroTags.Children.Clear();
        var champion=catalog.Champions.First(c=>c.Id==query.ChampionId);
        HeroName.Text=champion.Name;HeroSubtitle.Text=champion.Title;HeroPortrait.Source=null;
        HeroSplash.Source=null;LoadIcon(HeroSplash,$"https://ddragon.leagueoflegends.com/cdn/img/champion/splash/{champion.Key}_0.jpg",token);
        LoadIcon(HeroPortrait,$"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{champion.Key}.png",token);
        HeroTags.Children.Add(Badge(ModeName(query.Mode)));HeroTags.Children.Add(Badge("Patch "+catalog.Patch));
        Notice($"Buscando build de {champion.Name} para {ModeName(query.Mode)}…");UpdateFavorite();UpdateCompact();
        _=LoadAbilities(champion,query,token);_=SavePreferences();
        try {
            var result=LocalArsenals.Include(await builds.GetAsync(query,force,token),dataRoot,catalog.Items);
            if(token.IsCancellationRequested || selected!=query)return;
            var missing=result.Document.Variants.SelectMany(v=>v.Start.Concat(v.Core)).FirstOrDefault(i=>!catalog.Items.ContainsKey(i.Id));
            if(missing is not null)throw new InvalidDataException("A fonte trouxe itens que não constam no catálogo atual. Tente Atualizar ou abra a fonte.");
            current=result;
            HeroTags.Children.Clear();HeroTags.Children.Add(Badge(ModeName(query.Mode)));HeroTags.Children.Add(Badge("Patch "+result.Document.Patch));
            if(result.Document.WinRate is double wr)HeroTags.Children.Add(Badge($"{wr:0.0}% de vitórias"));
            if(result.Document.Games>0)HeroTags.Children.Add(Badge(result.Document.Games.ToString("N0",Pt)+" jogos"));
            Notice(result.Message+(result.Stale?" · consulta em "+LocalTime(result.Document.FetchedAt):""),result.Stale);
            for(int i=0;i<result.Document.Variants.Count;i++) {
                int index=i;var button=Button(result.Document.Variants[i].Name,()=>{variantIndex=index;RenderBuild();});VariantButtons.Children.Add(button);
            }
            ExportButton.IsEnabled=true;RenderBuild();
        }catch(OperationCanceledException) when(token.IsCancellationRequested) {}
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or TimeoutException or OperationCanceledException or JsonException) {
            if(token.IsCancellationRequested || selected!=query)return;
            Notice("Não foi possível carregar uma build validada deste contexto. "+Friendly(e),true);
            var panel=new StackPanel();panel.Children.Add(Text("Sua seleção foi preservada.",16,true));
            panel.Children.Add(Text("Tente novamente ou consulte a página original. O Bardola não vai preencher esta tela com uma build de outro modo.",12,false,Muted,top:10));
            panel.Children.Add(Button("Consultar fonte original ↗",()=>OpenUrl(BuildParsers.Url(query)),top:14));BuildDetails.Children.Add(Card(panel));
            UpdateCompact();
        }
    }
    static string Friendly(Exception e)=>e is HttpRequestException?"A fonte não respondeu ou bloqueou esta consulta.":e is TimeoutException or OperationCanceledException?"A consulta demorou demais; tente novamente.":e.Message;
    async void RefreshBuild(object sender,RoutedEventArgs e)
    {if(!ready)OnLoaded(sender,e);else await LoadBuild(true);}
    void Notice(string message,bool warn=false)
    {BuildNotice.Text=message;NoticeBorder.Background=Theme(warn?"argila-funda":"patina-funda");BuildNotice.Foreground=Theme(warn?"argila":"patina");}

    void RenderBuild()=>RenderBuildV2();
    Border ItemGroup(string number,string title,List<ItemChoice> items,bool sequence=false)=>ItemGroupV2(title,items,sequence);
    void CounterList(StackPanel panel,string title,List<int> ids)
    {
        panel.Children.Add(Text(title,11,false,Muted,top:13));
        foreach(int id in ids.Take(3)) {
            var c=catalog.Champions.FirstOrDefault(c=>c.Id==id);if(c is null)continue;
            var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)};var img=new Image{Width=25,Height=25};LoadIcon(img,$"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/img/champion/{c.Key}.png",request?.Token??default);row.Children.Add(img);var t=Text(c.Name,12);t.Margin=new Thickness(9,4,0,0);row.Children.Add(t);panel.Children.Add(row);
        }
    }
    async Task LoadAbilities(Champion c,BuildQuery q,CancellationToken token)
    {
        try {
            string json=await remote.TextAsync($"https://ddragon.leagueoflegends.com/cdn/{catalog.Version}/data/pt_BR/champion/{c.Key}.json",token);
            if(token.IsCancellationRequested || selected!=q)return;
            using var data=JsonDocument.Parse(json);var champ=data.RootElement.GetProperty("data").GetProperty(c.Key);var p=new StackPanel();p.Children.Add(Text("CONHEÇA O CAMPEÃO",10,true,Accent));
            var passive=champ.GetProperty("passive");p.Children.Add(Ability("P",passive));
            int i=0;foreach(var spell in champ.GetProperty("spells").EnumerateArray())p.Children.Add(Ability("QWER"[i++].ToString(),spell));
            p.Children.Add(Text("Descrições oficiais do Data Dragon. Passe o mouse para ler.",10,false,Muted,top:12));AbilityPanel.Children.Add(Card(p));
        }catch(Exception e) when(e is HttpRequestException or IOException or JsonException or TimeoutException or OperationCanceledException) {}
    }
    FrameworkElement Ability(string key,JsonElement spell)
    {
        var t=Text(key+"   "+spell.GetProperty("name").GetString(),12,true,top:12);
        t.ToolTip=new TextBlock{Text=BuildParsers.Clean(spell.GetProperty("description").GetString()??""),TextWrapping=TextWrapping.Wrap,MaxWidth=340,Foreground=Theme("papiro")};return t;
    }

    async Task PollGame()
    {
        if(polling || !ready)return;polling=true;
        try {
            var next=await game.PollAsync(catalog,lifetime.Token);
            if(next.GameTime>0)lastLive=next;
            else if(lastLive is {GameTime:>60} last && next.Phase is not "InProgress" and not "Reconnect") {
                sessions.Insert(0,new(DateTimeOffset.UtcNow,last));lastLive=null;
                if(sessions.Count>30)sessions.RemoveRange(30,sessions.Count-30);
                try{await Storage.WriteAsync(Path.Combine(dataRoot,"sessions.json"),JsonSerializer.Serialize(sessions),lifetime.Token);}catch(IOException) {}
            }
            snapshot=next;UpdateConnection();UpdateTimerClock();UpdateOverlay();_=ApplyNativeTimerPreference();if(AutoDetect.IsChecked==true)ApplyDetection();
            UpdateDraft();
            await UpdateFinalScore();
            if(importBusy && activeImportSelection is { } importing && (next.Phase!="Seleção de campeão" || next.ChampionId!=importing.ChampionId || GameService.DetectMode(next.Mode)!=importing.Mode))importRequest?.Cancel();
            _=RunAutoImport();
            if(GamePage.Visibility==Visibility.Visible)RenderGame();
            if(preferences.LinkAccount && !accountBusy && DateTimeOffset.UtcNow-lastAccountRefresh>TimeSpan.FromMinutes(snapshot.GameTime>0?3:1))_=ConnectAccount();
        }catch(OperationCanceledException) {}finally{polling=false;}
    }
    void UpdateConnection()
    {ConnectionText.Text=snapshot.Phase;ConnectionDot.Fill=Theme(snapshot.Connected?"patina":"latao");}
    void ApplyDetection(bool initial=false)
    {
        var mode=GameService.DetectMode(snapshot.Mode,snapshot.Map);
        string key=$"{snapshot.ChampionId}-{snapshot.Mode}-{snapshot.Role}";
        if(!initial && key==lastDetection)return;lastDetection=key;
        if(mode is null)return;
        applyingDetection=true;
        try {
            if(snapshot.ChampionId>0 && catalog.Champions.FirstOrDefault(c=>c.Id==snapshot.ChampionId) is { } c)ChampionBox.SelectedItem=c;
            ModeBox.SelectedIndex=(int)mode;
            if(snapshot.ChampionId>0 && mode==GameMode.Rift && snapshot.Role!="default")SelectTag(RoleBox,snapshot.Role);
            UpdateModeControls();
        }finally{applyingDetection=false;}
        if(ready && Query()!=selected){MarkSelectionPending();selectionTimer.Stop();selectionTimer.Start();}
    }
    void MarkSelectionPending()
    {
        request?.Cancel();current=null;ExportButton.IsEnabled=false;selected=Query();
        BuildDetails.Children.Clear();RunePanel.Children.Clear();MatchupPanel.Children.Clear();SourcePanel.Children.Clear();AbilityPanel.Children.Clear();VariantButtons.Children.Clear();HeroTags.Children.Clear();HeroPortrait.Source=null;
        if(selected is not null){var c=catalog.Champions.First(x=>x.Id==selected.ChampionId);HeroName.Text=c.Name;HeroSubtitle.Text=c.Title;HeroTags.Children.Add(Badge(ModeName(selected.Mode)));}
        Notice("Atualizando a build para a seleção atual…");UpdateFavorite();UpdateCompact();
    }
    void AutoChanged(object sender,RoutedEventArgs e)
    {if(!ready)return;ApplyDetection(true);_=SavePreferences();}
    void RenderGame()
    {
        if(InDraft){RenderDraft();return;}
        var recap=snapshot.Connected && snapshot.GameTime<=0 ? finalScore ?? (snapshot.Phase=="Fim de partida"?null:account?.Matches.FirstOrDefault()) : null;
        if(recap is not null){
            if(ReferenceEquals(recap,renderedScore))return;
            renderedScore=recap;GameDetails.Children.Clear();AddFinalScore(recap);return;
        }
        renderedScore=null;
        GameDetails.Children.Clear();var hero=new StackPanel();hero.Children.Add(Text(snapshot.Phase,25,true));
        hero.Children.Add(Text(snapshot.GameTime>0?"Dados do seu jogador, lidos localmente durante esta partida.":snapshot.Phase=="Fim de partida"?"Aguardando o placar final do cliente do League.":"O painel se atualiza quando uma partida começar. Você pode consultar builds manualmente a qualquer momento.",13,false,Muted,top:10));GameDetails.Children.Add(Card(hero));
        if(snapshot.GameTime>0) {
            var stats=new WrapPanel();
            foreach(var (label,value) in new[]{("CAMPEÃO",catalog.Champions.FirstOrDefault(c=>c.Id==snapshot.ChampionId)?.Name??"Detectando"),("K / D / A",$"{snapshot.Kills} / {snapshot.Deaths} / {snapshot.Assists}"),("CS / MIN",(snapshot.Cs/Math.Max(1,snapshot.GameTime/60)).ToString("0.0",Pt)),("FARM",snapshot.Cs.ToString()),("OURO DISPONÍVEL",snapshot.Gold.ToString("N0",Pt)),("VISÃO",snapshot.Vision.ToString("0.#",Pt)),("NÍVEL",snapshot.Level.ToString()),("TEMPO",TimeSpan.FromSeconds(snapshot.GameTime).ToString(@"mm\:ss"))}) {
                var p=new StackPanel{MinWidth=140};p.Children.Add(Text(label,10,false,Accent));p.Children.Add(Text(value,23,true,top:10));var card=Card(p);card.Margin=new Thickness(0,0,12,12);stats.Children.Add(card);
            }GameDetails.Children.Add(stats);
            if(snapshot.Items?.Count>0)GameDetails.Children.Add(ItemGroup("•","Seus itens atuais",snapshot.Items.Select(id=>new ItemChoice(id)).ToList()));
        }
        var history=new StackPanel();history.Children.Add(Text("Suas últimas sessões locais",18,true));history.Children.Add(Text("Última leitura capturada antes de voltar ao cliente; não equivale ao placar final oficial. Nenhum nome de conta é salvo.",12,false,Muted,top:8));
        foreach(var session in sessions.Take(10)) {
            var s=session.Snapshot;string champion=catalog.Champions.FirstOrDefault(c=>c.Id==s.ChampionId)?.Name??"Campeão";
            history.Children.Add(Text($"{LocalTime(session.At)}  ·  {champion}  ·  {s.Kills}/{s.Deaths}/{s.Assists}  ·  {s.Cs} CS  ·  {s.Cs/Math.Max(1,s.GameTime/60):0.0} CS/min",12,top:14));
        }
        if(sessions.Count==0)history.Children.Add(Text("A primeira sessão aparecerá depois de o app acompanhar uma partida.",12,false,Muted,top:14));GameDetails.Children.Add(Card(history));
    }

    bool IsFavorite(BuildQuery q)=>preferences.Favorites.Any(f=>f.ChampionId==q.ChampionId && f.Mode==q.Mode && f.Role==q.Role && f.Tier==q.Tier);
    void UpdateFavorite()=>FavoriteButton.Content=selected is not null && IsFavorite(selected)?"★  Salvo":"☆  Salvar";
    async void ToggleFavorite(object sender,RoutedEventArgs e)
    {
        if(selected is null)return;
        if(IsFavorite(selected))preferences.Favorites.RemoveAll(f=>f.ChampionId==selected.ChampionId && f.Mode==selected.Mode && f.Role==selected.Role && f.Tier==selected.Tier);
        else preferences.Favorites.Add(selected);UpdateFavorite();await SavePreferences();
    }
    void RenderSaved()
    {
        SavedDetails.Children.Clear();
        var intro=new StackPanel();intro.Children.Add(Text("Seu repertório, sempre perto.",24,true));intro.Children.Add(Text("Salve campeão + modo + função para voltar à build certa. O cache mantém separadas todas as suas escolhas.",13,false,Muted,top:10));SavedDetails.Children.Add(Card(intro));
        foreach(var f in preferences.Favorites.ToList()) {
            var c=catalog.Champions.FirstOrDefault(c=>c.Id==f.ChampionId);if(c is null)continue;
            var p=new StackPanel();p.Children.Add(Text(c.Name,20,true));p.Children.Add(Text(ModeName(f.Mode)+(f.Mode==GameMode.Rift?" · "+RoleName(f.Role)+" · "+TierName(f.Tier):""),12,false,Muted,top:7));
            var row=new WrapPanel{Margin=new Thickness(0,13,0,0)};row.Children.Add(Button("Consultar build",()=>{
                applyingDetection=true;ChampionBox.SelectedItem=c;ModeBox.SelectedIndex=(int)f.Mode;SelectTag(RoleBox,f.Role);SelectTag(TierBox,f.Tier);applyingDetection=false;UpdateModeControls();ShowBuilds(this,new());_=LoadBuild();
            }));row.Children.Add(Button("Remover",()=>{preferences.Favorites.Remove(f);_=SavePreferences();RenderSaved();UpdateFavorite();}));p.Children.Add(row);SavedDetails.Children.Add(Card(p));
        }
        if(preferences.Favorites.Count==0)SavedDetails.Children.Add(Card(Text("Use ☆ Salvar na tela de builds para montar seu repertório.",14,false,Muted)));
    }

    async Task ConnectAccount()
    {
        if(accountBusy)return;accountBusy=true;int generation=++accountGeneration;
        accountRequest?.Cancel();accountRequest?.Dispose();accountRequest=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token=accountRequest.Token;lastAccountRefresh=DateTimeOffset.UtcNow;accountMessage="Lendo a conta conectada no cliente do LoL…";RenderAccount();
        try {
            var linked=await accountService.ConnectAsync(token);
            if(token.IsCancellationRequested || generation!=accountGeneration)return;
            account=linked;preferences.LinkAccount=true;
            preferences.SavedProfile=linked with{Matches=linked.Matches.Select(m=>m with{Teams=null}).ToList()};
            preferences.ProfileSavedAt=DateTimeOffset.UtcNow;accountMessage="Conectada à sessão local do LoL. Atualização automática enquanto o app estiver aberto.";
            AccountBadge.Text=linked.RiotId;await SavePreferences();
        }catch(OperationCanceledException) when(token.IsCancellationRequested) {}
        catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or JsonException) {
            if(generation!=accountGeneration)return;
            account=null;AccountBadge.Text="Conexão indisponível";
            accountMessage=e is HttpRequestException or OperationCanceledException?"O cliente do LoL não respondeu. Abra o jogo, entre na sua conta e tente novamente.":e.Message;
        }finally {if(generation==accountGeneration){accountBusy=false;RenderAccount();}}
    }
    void DisconnectAccount()
    {
        ++accountGeneration;accountRequest?.Cancel();accountBusy=false;account=null;preferences.LinkAccount=false;
        ClearPublicProfile();preferences.SavedProfile=null;preferences.ProfileSavedAt=null;AccountBadge.Text="Ainda não conectada";
        accountMessage="Conta desconectada do Bardola. Sua sessão no LoL continua aberta.";_=SavePreferences();RenderAccount();
    }
    void RenderAccount()=>RenderAccountV2();
    static string RankName(string tier)=>tier switch {"IRON"=>"Ferro","BRONZE"=>"Bronze","SILVER"=>"Prata","GOLD"=>"Ouro","PLATINUM"=>"Platina","EMERALD"=>"Esmeralda","DIAMOND"=>"Diamante","MASTER"=>"Mestre","GRANDMASTER"=>"Grão-Mestre","CHALLENGER"=>"Desafiante",_=>"Sem classificação"};
    static string ModeLabel(string mode)=>GameService.DetectMode(mode) is { } m?ModeName(m):mode;

    void ExportBuild(object sender,RoutedEventArgs e)
    {
        if(current is null)return;
        var d=current.Document;var dialog=new SaveFileDialog{Filter="Conjunto de itens do LoL (*.json)|*.json",FileName=$"Bardola-{d.Query.Slug}-{d.Query.Mode}-{d.Patch}.json"};
        if(dialog.ShowDialog()!=true)return;
        try{File.WriteAllText(dialog.FileName,ExportService.CreateItemSet(d,d.Variants[variantIndex]));Notice("Conjunto exportado. No LoL, abra Coleção → Itens → Importar conjunto de itens.");}
        catch(Exception err) when(err is IOException or UnauthorizedAccessException){Notice("Não foi possível salvar o arquivo. Escolha outra pasta.",true);}
    }
    void OpenSource(object sender,RoutedEventArgs e){if(selected is not null)OpenUrl(current?.Document.SourceUrl??BuildParsers.Url(selected));}
    void OpenCompact(object sender,RoutedEventArgs e)
    {
        if(compact is not null){compact.Activate();return;}
        compactContent=new StackPanel{Margin=new Thickness(18)};
        compact=new Window{Title="Bardola · Compacto",Width=480,Height=590,MinWidth=330,MinHeight=260,Background=Theme("noite"),Foreground=Theme("papiro"),Topmost=true,ShowInTaskbar=true,Content=new ScrollViewer{Content=compactContent},Owner=this};
        compact.Closed+=(_,_)=>{compact=null;compactContent=null;};UpdateCompact();compact.Show();
    }
    void UpdateCompact()
    {
        if(compactContent is null)return;compactContent.Children.Clear();compactContent.Children.Add(BrandBadge());
        if(selected is not null)compactContent.Children.Add(Text((catalog.Champions.FirstOrDefault(c=>c.Id==selected.ChampionId)?.Name??"")+" · "+ModeName(selected.Mode),20,true,top:10));
        if(current is null){compactContent.Children.Add(Text("Carregando ou aguardando uma build validada. Consulte o painel principal.",13,false,Muted,top:20));return;}
        var d=current.Document;var v=d.Variants[variantIndex];compactContent.Children.Add(Text(v.Name+" · patch "+d.Patch,11,false,Muted,top:8));
        compactContent.Children.Add(ItemGroup("01","Núcleo",v.Core));compactContent.Children.Add(ItemGroup("02","Início",v.Start));
        if(v.SkillPriority!="")compactContent.Children.Add(Text("Habilidades: "+string.Join(" → ",v.SkillPriority.ToCharArray()),18,true,Accent,top:8));
        if(v.Alternatives.Count>0)compactContent.Children.Add(ItemGroup("+","Alternativas",v.Alternatives));
        compactContent.Children.Add(Text(d.Source+" · "+(current.Stale?"cache da última consulta":"consulta "+LocalTime(d.FetchedAt)),10,false,Muted,top:8));
    }

    void ShowPage(UIElement target,Button nav,string title,string eyebrow)
    {foreach(var p in new UIElement[]{BuildPage,GamePage,SavedPage,ResearchPage,AccountPage,ChampionPage})p.Visibility=p==target?Visibility.Visible:Visibility.Collapsed;PageTitle.Text=title;PageEyebrow.Text=eyebrow;SetNav(nav);}
    void SetNav(Button nav)
    {foreach(var b in new[]{BuildNav,ChampionNav,GameNav,AccountNav,SavedNav,ResearchNav}){b.Background=Theme("noite-painel");b.Foreground=b==nav?Accent:Muted;b.BorderBrush=b==nav?Accent:Theme("noite-painel");}}
    void ShowBuilds(object sender,RoutedEventArgs e)=>ShowPage(BuildPage,BuildNav,"Build do campeão","LEAGUE OF LEGENDS");
    void ShowGame(object sender,RoutedEventArgs e){ShowPage(GamePage,GameNav,"Partida","LEAGUE OF LEGENDS");RenderGame();}
    void ShowSaved(object sender,RoutedEventArgs e){ShowPage(SavedPage,SavedNav,"Seu repertório.","CAMPEÕES FAVORITOS");RenderSaved();}
    void ShowResearch(object sender,RoutedEventArgs e){BuildResearch();ShowPage(ResearchPage,ResearchNav,"Configurações","PREFERÊNCIAS DO BARDOLA");}
    void ShowAccount(object sender,RoutedEventArgs e){ShowPage(AccountPage,AccountNav,"Perfil e histórico","LEAGUE OF LEGENDS");RenderAccount();}
    void OnClosed(object? sender,EventArgs e)
    {ready=false;cardPoll.Stop();cardOverlay?.Close();relicPoll.Stop();ClearRelicReadings();mapOverlay?.Close();calibrationWindow?.Close();pollTimer.Stop();selectionTimer.Stop();updateTimer.Stop();lifetime.Cancel();nativeTimerRequest?.Cancel();overlay?.Close();request?.Cancel();accountRequest?.Cancel();importRequest?.Cancel();compact?.Close();ghost.Dispose();remote.Dispose();game.Dispose();lifetime.Dispose();}

    async void LoadIcon(Image target,string url,CancellationToken token)
    {
        try{var bytes=await catalogService.IconAsync(url,token);if(bytes is null || token.IsCancellationRequested)return;using var stream=new MemoryStream(bytes);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=stream;bitmap.EndInit();bitmap.Freeze();target.Source=bitmap;}
        catch(Exception e) when(e is IOException or OperationCanceledException or NotSupportedException or FileFormatException) {}
    }
    static readonly CultureInfo Pt=CultureInfo.GetCultureInfo("pt-BR");
    static string LocalTime(DateTimeOffset date)=>TimeZoneInfo.ConvertTime(date,TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time")).ToString("dd/MM HH:mm",Pt);
    static string RuneName(int id)=>id switch{8000=>"Precisão",8100=>"Dominação",8200=>"Feitiçaria",8300=>"Inspiração",8400=>"Determinação",_=>"Runas"};
    static string RoleName(string r)=>r switch{"top"=>"Topo","jungle"=>"Selva","middle"=>"Meio","bottom"=>"Atirador","support"=>"Suporte",_=>"Função mais jogada"};
    static string TierName(string r)=>r switch{"emerald_plus"=>"Esmeralda+","diamond_plus"=>"Diamante+","master_plus"=>"Mestre+",_=>"Todos os elos"};
    static string ModeName(GameMode m)=>m switch{GameMode.Rift=>"Summoner's Rift",GameMode.Aram=>"ARAM",_=>"ARAM: Desordem"};
    static Brush Brush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    static TextBlock Text(string value,int size=13,bool bold=false,Brush? color=null,int top=0)=>new(){Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=color??Theme("papiro"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,top,0,0)};
    static Border Card(UIElement child)=>new(){Background=Theme("noite-painel"),BorderBrush=Theme("linha"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(18),Margin=new Thickness(0,0,0,12),Child=child};
    static Border Badge(string text)=>new(){Background=Theme("noite-controle"),CornerRadius=new CornerRadius(4),Padding=new Thickness(8,4,8,4),Margin=new Thickness(0,0,7,4),Child=Text(text,11,false,Theme("papiro"))};
    static Button Button(string label,Action action,int top=0)
    {var b=new Button{Content=label,Margin=new Thickness(0,top,8,0)};b.Click+=(_,_)=>action();return b;}
    static void OpenUrl(string url)
    {try{if(Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme=="https")System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){MessageBox.Show("Não foi possível abrir o navegador.","Bardola");}}
    static IEnumerable<(string,string)> SourceLinks(BuildQuery q)
    {
        if(q.Mode==GameMode.Mayhem) {
            yield return ("LazyElo · Desordem",BuildParsers.Url(q));
            yield return ("DeepARAM · Desordem",$"https://deeparam.com/champions/{q.Slug}");
            yield return ("Lum.gg · Desordem",$"https://lum.gg/lol/champions/{q.Slug}/aram-mayhem");yield break;
        }
        yield return ("LoLalytics",BuildParsers.Url(q));
        yield return ("OP.GG",$"https://op.gg/lol/champions/{q.Slug}/build");
        yield return ("U.GG",$"https://u.gg/lol/champions/{q.Slug}/build");
        yield return ("League of Graphs",$"https://www.leagueofgraphs.com/pt/champions/builds/{q.Slug}");
    }
}

public sealed class Preferences
{
    public bool GhostMode {get;set;}
    public BuildQuery? Last {get;set;}
    public bool AutoDetect {get;set;}=true;
    public bool LinkAccount {get;set;}
    public GameAccount? SavedProfile {get;set;}
    public DateTimeOffset? ProfileSavedAt {get;set;}
    public bool AutoImportItems {get;set;}=true;
    public bool AutoImportRunes {get;set;}=true;
    public bool ReuseActiveRunePage {get;set;}=true;
    public bool AutoImportSpells {get;set;}=true;
    public bool FlashOnLeft {get;set;}=true;
    public bool CardTiers {get;set;}
    public bool AutoMapRelics {get;set;}
    public ClockPoint[]? ClockLocations {get;set;}
    public ClockPoint[]? MapLocations {get;set;}
    public int CalibrationWidth {get;set;}
    public int CalibrationHeight {get;set;}
    public bool JunglePath {get;set;}=true;
    public bool NativeMinimapTimers {get;set;}=true;
    public bool OverlayRelics {get;set;}
    public double RelicSeconds {get;set;}=90;
    public bool OverlayStats {get;set;}
    public bool OverlayBuild {get;set;}
    public bool OverlaySkills {get;set;}
    public double CsTarget {get;set;}=7;
    public List<BuildQuery> Favorites {get;set;}=[];
}
public record LocalSession(DateTimeOffset At,GameSnapshot Snapshot);
