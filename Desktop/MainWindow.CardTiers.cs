using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexoLoL.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    readonly DispatcherTimer cardPoll = new() { Interval=TimeSpan.FromSeconds(1) };
    Window? cardOverlay;
    readonly Canvas cardCanvas = new();
    OcrEngine? cardOcr;
    bool readingCards;
    string translatedPatch="";
    DateTimeOffset nextTranslationCheck;
    bool loadingTranslations;
    Dictionary<string,string> cardAliases=new();

    async Task LoadCardAliases(string patch)
    {
        if(loadingTranslations || translatedPatch==patch || DateTimeOffset.UtcNow<nextTranslationCheck ||
            !Version.TryParse(patch,out var version) || version.ToString(2)!=patch)return;
        loadingTranslations=true;
        try{
            string root=$"https://raw.communitydragon.org/{patch}/plugins/rcp-be-lol-game-data/global";
            var names=await Task.WhenAll(new[]{"default","pt_br"}.Select(locale=>
                remote.TextAsync($"{root}/{locale}/v1/cherry-augments.json",lifetime.Token)));
            var aliases=CardTranslations.Pair(names[0],names[1]);
            if(aliases.Count==0)throw new System.IO.InvalidDataException();
            cardAliases=aliases;
            translatedPatch=patch;
        }catch(Exception e) when(e is System.Net.Http.HttpRequestException or System.IO.IOException or System.IO.InvalidDataException or
            System.Text.Json.JsonException or TimeoutException or OperationCanceledException){
            nextTranslationCheck=DateTimeOffset.UtcNow.AddMinutes(5);
        }finally{loadingTranslations=false;}
    }

    void StartCardReader()
    {
        cardPoll.Tick += async (_,_) => await ReadVisibleCards();
        cardPoll.Start();
    }

    bool CanReadCards(BuildDocument? doc) => ready && preferences.CardTiers && snapshot.GameTime > 0 &&
        GameService.DetectMode(snapshot.Mode)==GameMode.Mayhem && doc?.Query.Mode==GameMode.Mayhem &&
        doc.Query.ChampionId==snapshot.ChampionId && BuildParsers.SamePatch(doc.Patch,catalog.Patch) &&
        doc.AugmentTiers is {Count:>0};

    internal record CardHit(string Name, string Grade, double X, double Y);
    static string CardKey(string text) => string.Concat(text.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    internal static async Task<List<CardHit>> MatchCards(BitmapSource band, Dictionary<string,string> tiers, OcrEngine engine)
    {
        var names=tiers.GroupBy(k=>CardKey(k.Key)).Where(g=>g.Key.Length>0 && g.Count()==1)
            .ToDictionary(g=>g.Key,g=>g.First());
        var hits=new List<CardHit>();
        foreach (var (left,right) in new[]{(0.10,0.40),(0.40,0.60),(0.60,0.90)})
        {
            int x=(int)(band.PixelWidth*left), width=(int)(band.PixelWidth*right)-x;
            var crop=new CroppedBitmap(band,new Int32Rect(x,0,width,band.PixelHeight));
            var bitmap=new FormatConvertedBitmap(crop,PixelFormats.Bgra32,null,0);
            int stride=width*4;
            var buffer=new byte[stride*bitmap.PixelHeight];
            bitmap.CopyPixels(buffer,stride,0);
            using var input=SoftwareBitmap.CreateCopyFromBuffer(buffer.AsBuffer(), BitmapPixelFormat.Bgra8,width,bitmap.PixelHeight);
            var lines=(await engine.RecognizeAsync(input)).Lines;
            for(int i=0;i<lines.Count;i++) {
                var line=lines[i];
                if(line.Words.Count==0)continue;
                var key=CardKey(line.Text);
                if(!names.TryGetValue(key,out var match)) {
                    if(i+1>=lines.Count || lines[i+1].Words.Count==0 ||
                        lines[i+1].Words[0].BoundingRect.Top-line.Words[0].BoundingRect.Top > line.Words[0].BoundingRect.Height*2.6 ||
                        !names.TryGetValue(key+CardKey(lines[i+1].Text),out match))continue;
                }
                var first=line.Words[0].BoundingRect;
                var last=line.Words[line.Words.Count-1].BoundingRect;
                hits.Add(new(match.Key,match.Value,x+(first.Left+last.Right)/2,first.Top));
                break;
            }
        }
        // Two distinct titles on the same horizontal row distinguish the choice screen from the HUD.
        return hits.Where(h=>hits.Any(other=>other.Name!=h.Name && Math.Abs(other.Y-h.Y)<band.PixelHeight*0.12)).ToList();
    }

    async Task ReadVisibleCards()
    {
        if(readingCards)return;
        var doc=current?.Document;
        if(!CanReadCards(doc) || !ScoreboardReader.GameBounds(out var bounds)) {cardOverlay?.Hide();return;}
        readingCards=true;
        try {
            var band=ScoreboardReader.CaptureCardBand();
            cardOcr??=OcrEngine.TryCreateFromUserProfileLanguages();
            if(cardOcr is null || band is null){cardOverlay?.Hide();return;}
            _=LoadCardAliases(catalog.Patch);
            var tiers=new Dictionary<string,string>(doc!.AugmentTiers!,StringComparer.OrdinalIgnoreCase);
            if(translatedPatch==catalog.Patch){
                foreach(var (title,original) in cardAliases)
                    if(doc.AugmentTiers!.TryGetValue(original,out var grade))tiers.TryAdd(title,grade);
            }
            var hits=await MatchCards(band,tiers,cardOcr);
            if(hits.Count==0 || current?.Document!=doc || !CanReadCards(doc) ||
                !ScoreboardReader.GameBounds(out var now) || now!=bounds){cardOverlay?.Hide();return;}
            if(cardOverlay is null) {
                cardOverlay=new Window{WindowStyle=WindowStyle.None,AllowsTransparency=true,Background=Brushes.Transparent,
                    Topmost=true,ShowActivated=false,ShowInTaskbar=false,IsHitTestVisible=false,ResizeMode=ResizeMode.NoResize,
                    Content=cardCanvas,Width=1,Height=1};
                cardOverlay.SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(cardOverlay).Handle;SetStyle(handle,-20,GetStyle(handle,-20)|0x20|0x08000000|0x80);};
            }
            cardCanvas.Children.Clear();
            if(!cardOverlay.IsVisible)cardOverlay.Show();
            SetWindowPos(new WindowInteropHelper(cardOverlay).Handle,new IntPtr(-1),bounds.X,bounds.Y,bounds.Width,bounds.Height,0x10);
            var dpi=VisualTreeHelper.GetDpi(cardOverlay);
            foreach(var hit in hits) {
                var label=new Border{Background=Brush("#EF171B23"),CornerRadius=new CornerRadius(5),Padding=new Thickness(7,2,7,2),
                    Child=Text("Tier "+hit.Grade,14,true,hit.Grade=="S"?Accent:Brushes.White)};
                label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                Canvas.SetLeft(label,((bounds.Width-band.PixelWidth)/2.0+hit.X)/dpi.DpiScaleX-label.DesiredSize.Width/2);
                Canvas.SetTop(label,(bounds.Height*ScoreboardReader.CardBandTop+hit.Y)/dpi.DpiScaleY-label.DesiredSize.Height-3);
                cardCanvas.Children.Add(label);
            }
        } catch(Exception e) when(e is COMException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) {
            cardOverlay?.Hide();
        } finally {readingCards=false;}
    }
}
