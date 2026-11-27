using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using NexoLoL.Core;
using NexoLoL.Desktop;

// Teste WPF em memória: NÃO chama Show, Application.Run, entrada de teclado ou captura do desktop.
static class Program
{
    static int passed,failed;
    static MainWindow window=null!;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static object? Get(string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(window);
    static void Set(string name,object? value)=>typeof(MainWindow).GetField(name,Flags)!.SetValue(window,value);
    static object? Invoke(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(window,args);
    static T Control<T>(string name)=>(T)window.FindName(name);
    static void Check(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+(e.InnerException??e).Message);}}
    static void True(bool condition){if(!condition)throw new Exception("comportamento esperado não observado");}
    static void Pump(Task task){if(!task.IsCompleted){var frame=new DispatcherFrame();task.ContinueWith(_=>Dispatcher.CurrentDispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.FromCurrentSynchronizationContext());Dispatcher.PushFrame(frame);}task.GetAwaiter().GetResult();}
    [STAThread]
    static int Main(string[] args)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source=XDocument.Load("Desktop/App.xaml").Root!.Element(ns+"Application.Resources")!;
        var resourceXml=new XElement(ns+"ResourceDictionary",new XAttribute("xmlns",ns.NamespaceName),new XAttribute(XNamespace.Xmlns+"x","http://schemas.microsoft.com/winfx/2006/xaml"),source.Elements());
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(resourceXml.ToString());
        string directory=Path.Combine(Path.GetTempPath(),"nexo-wpf-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var remote=new RemoteData(new EmptyHandler());var fakeClient=new FakeApi{Phase="Lobby"};window=new MainWindow(remote,directory,()=>fakeClient);
        var catalog=new Catalog{Version="16.23.1",Champions=[new(26,"Zilean","Zilean","o Guardião do Tempo"),new(103,"Ahri","Ahri","a Raposa de Nove Caudas")]};
        Set("catalog",catalog);Set("catalogService",new CatalogService(directory,remote));
        Control<ComboBox>("ChampionBox").ItemsSource=catalog.Champions;Control<ComboBox>("ChampionBox").SelectedIndex=0;
        Control<ComboBox>("ModeBox").SelectedIndex=0;Control<ComboBox>("RoleBox").SelectedIndex=0;Control<ComboBox>("TierBox").SelectedIndex=0;
        var q=new BuildQuery(26,"zilean",GameMode.Rift,Patch:"16.23");
        var d=new BuildDocument(q,"16.23","LoLalytics",BuildParsers.Url(q),DateTimeOffset.UtcNow,[new("Mais jogada",[new(2003)],[new(6655)],[])]);
        void Reset(){Set("ready",false);Control<ComboBox>("ModeBox").SelectedIndex=0;Set("selected",q);Set("current",new BuildResult(d,false,false,"ok"));Control<Button>("ExportButton").IsEnabled=true;Set("ready",true);}
        Check("botão de atualização em Ajustes reflete verificação e reinício disponível",()=>{
            Invoke("BuildResearch");
            Button button=(Button)Get("updateButton")!;
            True(button.IsEnabled && Equals(button.Content,"Atualizar versão"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            True(((TextBlock)Get("updateStatus")!).Text.Contains("instalador"));
            Set("updateBusy",true);
            Invoke("BuildResearch");
            True(!((Button)Get("updateButton")!).IsEnabled);
            Set("updateBusy",false);
            Set("readyVersion","9.9.9");
            Invoke("RefreshUpdateButton");
            button=(Button)Get("updateButton")!;
            True(button.IsEnabled && Equals(button.Content,"Reiniciar e usar 9.9.9"));
            Set("readyVersion",null);
            Invoke("RefreshUpdateButton");
        });
        Check("troca manual de modo limpa imediatamente build e exportação",()=>{Reset();Control<ComboBox>("ModeBox").SelectedIndex=1;True(Get("current") is null);True(!Control<Button>("ExportButton").IsEnabled);});
        Check("saguão sem campeão atualiza contexto e invalida build anterior",()=>{Reset();Set("snapshot",new GameSnapshot(true,"Saguão",Mode:"ARAM"));Set("lastDetection","");Invoke("ApplyDetection",false);True(Control<ComboBox>("ModeBox").SelectedIndex==1);True(Get("current") is null);True(!Control<Button>("ExportButton").IsEnabled);});
        Check("timeout sem cache mostra alternativa de recuperação",()=>{
            Reset();Set("builds",new BuildService(directory,(_,_)=>Task.FromException<string>(new TimeoutException("sem resposta"))));
            var task=(Task)Invoke("LoadBuild",true)!;if(!task.IsCompleted)throw new Exception("timeout não foi tratado de forma imediata no teste");task.GetAwaiter().GetResult();
            True(Control<StackPanel>("BuildDetails").Children.Count>0);True(!Control<Button>("ExportButton").IsEnabled);True(Control<TextBlock>("BuildNotice").Text.Contains("demorou"));
        });
        ((DispatcherTimer)Get("selectionTimer")!).Stop();Set("ready",false);
        Check("tela de conta oferece conexão explícita sem abrir login",()=>{Invoke("RenderAccount");var panel=Control<StackPanel>("AccountDetails");True(Descendants<Button>(panel).Any(b=>b.Name=="ConnectAccountButton"));True(Get("account") is null);});
        Check("perfil conectado renderiza elo e histórico do próprio jogador",()=>{
            Set("account",new GameAccount("Conta de teste#BR1",82,1,"BR",[new("RANKED_SOLO_5x5","GOLD","II",55,18,12)],[new(123,26,"CLASSIC",true,4,2,18,105,1800,DateTimeOffset.UtcNow)]));
            Invoke("RenderAccount");var panel=Control<StackPanel>("AccountDetails");var texts=Descendants<TextBlock>(panel).Select(t=>t.Text).ToList();
            True(texts.Any(t=>t.Contains("Conta de teste#BR1")));True(texts.Any(t=>t.Contains("Ouro II")));True(texts.Any(t=>t.Contains("4/2/18")));
        });
        Check("desconectar remove vínculo e limpa dados em memória",()=>{
            Set("account",new GameAccount("Teste#BR1",50,1,"BR",[],[]));var prefs=(Preferences)Get("preferences")!;prefs.LinkAccount=true;Invoke("DisconnectAccount");True(Get("account") is null);True(!prefs.LinkAccount);
        });
        Check("gravação pendente não reativa vínculo após desconectar",()=>{
            Set("ready",true);Pump((Task)Invoke("SavePreferences")!);var gate=(SemaphoreSlim)Get("preferencesGate")!;gate.Wait();var prefs=(Preferences)Get("preferences")!;prefs.LinkAccount=true;
            var old=(Task)Invoke("SavePreferences")!;Invoke("DisconnectAccount");gate.Release();var latest=(Task)Invoke("SavePreferences")!;Pump(Task.WhenAll(old,latest));
            using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"preferences-v2.json")));True(!json.RootElement.GetProperty("LinkAccount").GetBoolean());Set("ready",false);
        });
        var root=(FrameworkElement)window.Content;root.Measure(new Size(1264,780));root.Arrange(new Rect(0,0,1264,780));root.UpdateLayout();
        Check("seletores mantêm contraste legível no tema escuro",()=>{var box=Control<ComboBox>("ModeBox");string text=((ComboBoxItem)box.SelectedItem).Content.ToString()!;var labels=Descendants<TextBlock>(box).Where(t=>t.Text==text).ToList();True(labels.Count>0);True(labels.All(t=>t.Foreground is SolidColorBrush b && box.Background is SolidColorBrush bg && Contrast(b.Color,bg.Color)>=4.5));});
        Check("autoimportação de itens e runas vem ligada",()=>{True(Control<CheckBox>("AutoItems").IsChecked==true);True(Control<CheckBox>("AutoRunes").IsChecked==true);});
        Check("galeria filtra campeões e abre consulta sem controlar o cliente",()=>{Control<TextBox>("GallerySearch").Text="Ahri";Invoke("RenderChampions");True(Control<WrapPanel>("ChampionGrid").Children.Count==1);True(Control<WrapPanel>("ChampionGrid").Children[0] is Button);});
        Check("automático não escreve enquanto o cliente está no saguão",()=>{Set("ready",true);Pump((Task)Invoke("RunAutoImport",false,false)!);True(fakeClient.Writes.Count==0);Set("ready",false);});
        Check("automático usa campeão da seleção mesmo consultando outro na tela",()=>{
            Control<ComboBox>("ChampionBox").SelectedItem=catalog.Champions[1];Control<ComboBox>("ModeBox").SelectedIndex=0;
            foreach(int id in new[]{2003,6655,3158})catalog.Items[id]=new(id,"Item de teste","Descrição",1000,"");
            BuildQuery? requested=null;Set("builds",new BuildService(Path.Combine(directory,"auto"),(query,_)=>{requested=query;return Task.FromResult(BuildFixture.Html());}));
            fakeClient.Phase="ChampSelect";fakeClient.Champion=26;Set("ready",true);Pump((Task)Invoke("RunAutoImport",false,false)!);
            True(requested?.ChampionId==26);True(requested?.Role=="support");True(fakeClient.Writes.Any(w=>w.Path.Contains("item-sets")));True(fakeClient.Writes.Any(w=>w.Path=="/lol-perks/v1/currentpage"));Set("ready",false);
        });
        Check("controlador volta a A corretamente depois de falha parcial em B",()=>{
            Set("builds",new BuildService(Path.Combine(directory,"switch"),(query,_)=>Task.FromResult(BuildFixture.Html(query.ChampionId,query.Slug,query.ChampionId==103))));Set("ready",true);
            fakeClient.Champion=103;fakeClient.OnWrite=(m,p,b)=>{if(p=="/lol-perks/v1/currentpage")throw new HttpRequestException("falha de confirmação");};Pump((Task)Invoke("RunAutoImport",false,false)!);
            fakeClient.OnWrite=null;fakeClient.Champion=26;int before=fakeClient.Writes.Count;Pump((Task)Invoke("RunAutoImport",false,false)!);True(fakeClient.Writes.Count>before);True(fakeClient.Pages.First(p=>p!["id"]!.ToString()==fakeClient.CurrentId.ToString())!["primaryStyleId"]!.ToString()=="8200");Set("ready",false);
        });
        Check("ajustes refletem importação e deixam recursos indisponíveis explícitos",()=>{
            Invoke("BuildResearch");var panel=Control<StackPanel>("ResearchDetails");var texts=Descendants<TextBlock>(panel).Select(t=>t.Text).ToList();True(texts.Any(t=>t.Contains("ainda não estão disponíveis")));var toggle=Descendants<CheckBox>(panel).First(c=>c.ToolTip?.ToString()=="Importar itens automaticamente");toggle.IsChecked=false;True(Control<CheckBox>("AutoItems").IsChecked==false);toggle.IsChecked=true;True(Control<CheckBox>("AutoItems").IsChecked==true);
        });
        Check("overlay vem desligado e não cria janela fora de partida",()=>{
            var prefs=(Preferences)Get("preferences")!;True(!prefs.OverlayStats&&!prefs.OverlayBuild&&!prefs.OverlaySkills);Set("snapshot",new GameSnapshot(true,"Saguão"));Invoke("UpdateOverlay");True(Get("overlay") is null);
        });
        Check("overlay não mostra build de outro campeão",()=>{
            var prefs=(Preferences)Get("preferences")!;prefs.OverlayBuild=true;Set("snapshot",new GameSnapshot(true,"Partida em andamento",ChampionId:103,Mode:"CLASSIC",GameTime:600));var panel=new StackPanel();Invoke("FillOverlay",panel);True(Descendants<TextBlock>(panel).Any(t=>t.Text.Contains("Selecione a build deste campeão")));prefs.OverlayBuild=false;
        });
        Check("overlay calcula CS por minuto com os dados do jogador",()=>{
            var prefs=(Preferences)Get("preferences")!;prefs.OverlayStats=true;Set("snapshot",new GameSnapshot(true,"Partida em andamento",26,"CLASSIC",600,Kills:3,Deaths:1,Assists:5,Cs:80));var panel=new StackPanel();Invoke("FillOverlay",panel);True(Descendants<TextBlock>(panel).Any(t=>t.Text.Contains("8,0 CS/min")||t.Text.Contains("8.0 CS/min")));prefs.OverlayStats=false;
        });
        Check("importação só de feitiços funciona com itens e runas desligados",()=>{
            Control<CheckBox>("AutoItems").IsChecked=false;Control<CheckBox>("AutoRunes").IsChecked=false;Control<CheckBox>("AutoSpells").IsChecked=true;fakeClient.Spell1=3;fakeClient.Spell2=14;Set("lastImportKey","");Set("lastImportAttempt","");Set("ready",true);Pump((Task)Invoke("RunAutoImport",true,false)!);True(fakeClient.Spell1==4&&fakeClient.Spell2==7);True(Control<TextBlock>("SpellImportText").Text.Contains("confirmados"));Set("ready",false);
        });
        Check("ajustes oferecem posição do Flash e cronômetros com marcação manual explícita",()=>{Invoke("BuildResearch");var texts=Descendants<TextBlock>(Control<StackPanel>("ResearchDetails")).Select(t=>t.Text).ToList();True(texts.Any(t=>t=="Posição do Flash"));True(texts.Any(t=>t.Contains("Todos os cronômetros no minimapa")));True(texts.Any(t=>t.Contains("contadores são manuais")));});
        Check("OCR lê um tempo em recorte sintético sem capturar a tela",()=>{
            var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen()){
                dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1920,360));
                var glyph=new FormattedText("0:37",System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),14,Brushes.White,1);
                dc.DrawText(glyph,new Point(960-glyph.Width/2,180-glyph.Height/2));
            }
            var frame=new RenderTargetBitmap(1920,360,96,96,PixelFormats.Pbgra32);frame.Render(visual);frame.Freeze();
            var engine=Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()??throw new Exception("OCR não disponível");
            var reader=typeof(MainWindow).Assembly.GetType("NexoLoL.Desktop.ScoreboardReader")!;
            var task=(Task<int?>)reader.GetMethod("ReadAsync")!.Invoke(null,[frame,new ClockPoint(.5,.5),engine])!;
            Pump(task);True(task.Result==37);
        });
        Check("OCR das cartas reconhece posições e descarta a nota após rerrolar",()=>{
            var engine=Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()??throw new Exception("OCR indisponível");
            var tiers=new Dictionary<string,string>{{"Tank Engine","A"},{"Mad Scientist","S"},{"Final Form","D"}};
            var match=typeof(MainWindow).GetMethod("MatchCards",BindingFlags.Static|BindingFlags.NonPublic)!;
            string[] Read(string middle){
                var visual=new DrawingVisual();
                using(var dc=visual.RenderOpen()){
                    dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,1600,460));
                    foreach(var (x,title) in new[]{(480,"Tank Engine"),(800,middle),(1120,"Final Form")}){
                        var glyph=new FormattedText(title,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),23,Brushes.White,1);
                        dc.DrawText(glyph,new Point(x-glyph.Width/2,90));
                    }
                }
                var band=new RenderTargetBitmap(1600,460,96,96,PixelFormats.Pbgra32);band.Render(visual);band.Freeze();
                var task=(Task)match.Invoke(null,[band,tiers,engine])!;Pump(task);
                var hits=(System.Collections.IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
                return hits.Cast<object>().Select(h=>(string)h.GetType().GetProperty("Grade")!.GetValue(h)!).ToArray();
            }
            True(Read("Mad Scientist").SequenceEqual(new[]{"A","S","D"}));True(Read("Unknown Title").SequenceEqual(new[]{"A","D"}));
        });
        if(args.Length==2 && args[0] is "--render" or "--render-rift" or "--render-settings") {
            var realCatalogPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NexoLoL","catalog.json");
            if(File.Exists(realCatalogPath)) {
                var real=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(realCatalogPath))!;Set("catalog",real);var iconService=new CatalogService(Path.GetDirectoryName(realCatalogPath)!,new RemoteData());Set("catalogService",iconService);
                var images=new[]{8214,8226,8210,8232,8105,8126}.Where(real.Runes.ContainsKey).Select(id=>iconService.IconAsync(real.Runes[id].Icon)).ToArray();Pump(Task.WhenAll(images));Console.WriteLine("Rune icons ready: "+images.Count(t=>t.Result is {Length:>0}));
                Control<CheckBox>("AutoItems").IsChecked=true;Control<CheckBox>("AutoRunes").IsChecked=true;Control<CheckBox>("AutoSpells").IsChecked=true;
                bool rift=args[0]!="--render";
                q=new(26,"zilean",rift?GameMode.Rift:GameMode.Mayhem,Tier:rift?"emerald_plus":"all",Patch:real.Patch);string page=File.ReadAllText(Path.GetFullPath(rift?"Tests/Fixtures/lolalytics-zilean-factual.html":"Tests/Fixtures/mayhem-zilean-factual.html"));
                Control<ComboBox>("ModeBox").SelectedIndex=rift?0:2;Control<ComboBox>("ChampionBox").ItemsSource=real.Champions;Control<ComboBox>("ChampionBox").SelectedItem=real.Champions.First(c=>c.Id==26);
                Set("builds",new BuildService(directory,(_,_)=>Task.FromResult(page)));Set("ready",true);Invoke("UpdateModeControls");Invoke("ShowBuilds",window,new RoutedEventArgs());
                var load=(Task)Invoke("LoadBuild",true)!;
                Pump(load);Set("ready",false);
                Control<TextBlock>("ConnectionText").Text="Prévia da interface";
                Invoke("ImportStatus","Itens: aguardando seleção de campeão","Runas: aguardando seleção de campeão",false,"Feitiços: aguardando seleção de campeão");
                var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
                if(args[0]=="--render-settings"){Invoke("BuildResearch");Invoke("ShowResearch",window,new RoutedEventArgs());}
                int renderHeight=args[0]=="--render-settings"?1120:780;
                root.Measure(new Size(1264,renderHeight));root.Arrange(new Rect(0,0,1264,renderHeight));root.UpdateLayout();var bitmap=new RenderTargetBitmap(1264,renderHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(args[1]);encoder.Save(file);Console.WriteLine("Rendered in memory: "+args[1]);
            }
        }
        Check("dois times, cartas e itens cabem na janela mínima",()=>{
            bool preview=args.Length==3 && args[0]=="--render-postgame";
            int[] ids=[3084,3111,3083,6664,3065,4401,220013];
            List<ScoreTeam> teams;
            if(preview){
                string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Bardo.gg");
                var real=JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(cache,"catalog.json")))!;
                Set("catalog",real);Set("catalogService",new CatalogService(cache,new RemoteData()));
                var data=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(args[2]))!;
                teams=MatchRoster.FromHistory(data,"preview",long.Parse(data["gameId"]!.ToString()));
                True(teams.Count==2 && teams.All(t=>t.Players.Count==5));
            }else{
                catalog.Champions.Add(new(98,"Shen","Shen",""));Set("catalog",catalog);
                teams=Enumerable.Range(1,2).Select(i=>new ScoreTeam(i*100,i==2,Enumerable.Range(1,5)
                    .Select(j=>new ScorePlayer("Jogador "+j,98,i==1 && j==1,13,12,37,39,52694,[..ids],[1053,1324,1181,3134])).ToList())).ToList();
            }
            var match=new AccountMatch(42,98,"KIWI",false,13,12,37,39,1245,DateTimeOffset.UtcNow,52694,106533,2865,[..ids],teams);
            Set("snapshot",new GameSnapshot(true,"Fim de partida"));Set("finalScore",match);
            Invoke("ShowGame",window,new RoutedEventArgs());
            var page=Control<ScrollViewer>("GamePage");var details=Control<StackPanel>("GameDetails");
            var images=Descendants<Image>(details).ToList();
            if(preview){
                var end=DateTime.UtcNow.AddSeconds(25);
                while(images.Any(i=>i.Source==null) && DateTime.UtcNow<end)Pump(Task.Delay(150));
                Console.WriteLine($"  {images.Count(i=>i.Source!=null)}/{images.Count} ícones e artes carregados");
                True(images.All(i=>i.Source!=null));
                Control<TextBlock>("ConnectionText").Text="Pós-jogo";Control<ComboBox>("ChampionBox").SelectedIndex=-1;
            }else images[0].Source=new RenderTargetBitmap(1215,717,96,96,PixelFormats.Pbgra32);
            double w=window.MinWidth,h=window.MinHeight;
            root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();
            var labels=Descendants<TextBlock>(details).Select(t=>t.Text).ToList();
            True(teams.SelectMany(t=>t.Players).All(p=>labels.Contains(p.Name)));
            double lastBottom=images.Last().TranslatePoint(new Point(0,images.Last().ActualHeight),page).Y;
            True(page.ViewportHeight>0 && page.ScrollableHeight==0 && lastBottom>0 && lastBottom<=page.ViewportHeight);
            Console.WriteLine($"  {w} × {h} · viewport {page.ViewportHeight:0} · rolagem {page.ScrollableHeight:0}");
            if(preview){
                var frame=new RenderTargetBitmap((int)w,(int)h,96,96,PixelFormats.Pbgra32);frame.Render(root);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));using var file=File.Create(args[1]);png.Save(file);
            }
        });
        window.Close();app.Shutdown();Directory.Delete(directory,true);Console.WriteLine($"{passed} passed, {failed} failed · nenhuma janela exibida");return failed>0?1:0;
    }
    static IEnumerable<T> Descendants<T>(DependencyObject obj) where T:DependencyObject
    {for(int i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++){var child=VisualTreeHelper.GetChild(obj,i);if(child is T match)yield return match;foreach(var d in Descendants<T>(child))yield return d;}}
    sealed class EmptyHandler : HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));}
    static double Contrast(Color a,Color b){static double L(Color c){static double S(byte v){double n=v/255.0;return n<=0.04045?n/12.92:Math.Pow((n+0.055)/1.055,2.4);}return 0.2126*S(c.R)+0.7152*S(c.G)+0.0722*S(c.B);}double x=L(a),y=L(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
}
