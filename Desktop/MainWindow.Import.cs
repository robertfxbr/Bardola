using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    readonly ClientImporter importer;
    readonly Func<ILeagueApi> leagueApiFactory;
    CancellationTokenSource? importRequest;
    ImportSelection? activeImportSelection;
    bool importBusy;
    string lastImportKey="",lastImportAttempt="";
    DateTimeOffset lastImportAt=DateTimeOffset.MinValue;

    async Task RunAutoImport(bool force=false,bool displayed=false)
    {
        if(!ready || importBusy)return;
        var options=new ImportOptions(AutoItems.IsChecked==true,AutoRunes.IsChecked==true,preferences.ReuseActiveRunePage,AutoSpells.IsChecked==true,preferences.FlashOnLeft);
        if(!options.Items && !options.Runes && !options.Spells){ImportStatus("Itens: desativado","Runas: desativado");return;}
        importBusy=true;importRequest?.Dispose();importRequest=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);var token=importRequest.Token;
        try {
            using var api=leagueApiFactory();var selection=await ClientImporter.SelectionAsync(api,token);
            if(selection is null){lastImportKey="";lastImportAttempt="";importer.ResetSession();ImportStatus(options.Items?"Itens: aguardando seleção de campeão":"Itens: desativado",options.Runes?"Runas: aguardando seleção de campeão":"Runas: desativado");return;}
            activeImportSelection=selection;
            var champion=catalog.Champions.FirstOrDefault(c=>c.Id==selection.ChampionId);
            if(champion is null){ImportStatus("Campeão ainda não consta no catálogo","Atualize os dados para continuar",true);return;}
            var query=new BuildQuery(champion.Id,champion.Slug,selection.Mode,selection.Mode==GameMode.Rift?selection.Role:"default",selection.Mode==GameMode.Rift?SelectedTag(TierBox):"all",catalog.Patch);
            string key=$"{selection.Owner}:{selection.Session}:{query.CacheKey}:{options}";
            if(!force && (key==lastImportKey || (key==lastImportAttempt && DateTimeOffset.UtcNow-lastImportAt<TimeSpan.FromSeconds(12))))return;
            lastImportKey=""; // Não reutilizar confirmação anterior depois de falha/cancelamento em outro contexto.
            lastImportAttempt=key;lastImportAt=DateTimeOffset.UtcNow;
            ImportStatus(options.Items?$"Itens: preparando {champion.Name}…":"Itens: desativado",selection.Mode==GameMode.Mayhem?"Runas: Desordem não usa runas":options.Runes?"Runas: preparando página…":"Runas: desativado");
            BuildDocument document;int variant=0;
            if(displayed) {
                if(current is null){ImportStatus("Carregue uma build antes de aplicar","A seleção do LoL será conferida",true);return;}
                document=current.Document;variant=variantIndex;
            }else document=(await builds.GetAsync(query,false,token,requireSpells:options.Spells)).Document;
            if(token.IsCancellationRequested)return;
            var unknown=document.Variants[variant].Core.Concat(document.Variants[variant].Start).Any(i=>!catalog.Items.ContainsKey(i.Id));
            if(options.Items&&unknown)throw new InvalidDataException("A build contém itens incompatíveis com o catálogo atual.");
            var outcome=await importer.ApplyAsync(api,document,variant,options,force,token);
            if(token.IsCancellationRequested)return;
            ImportStatus("Itens: "+outcome.Items,"Runas: "+outcome.Runes,!outcome.Success,"Feitiços: "+outcome.Spells);
            if(outcome.Success)lastImportKey=key;
        }catch(OperationCanceledException) when(token.IsCancellationRequested) {}
        catch(Exception e) when(e is IOException or InvalidDataException or HttpRequestException or TimeoutException or System.Text.Json.JsonException or OperationCanceledException) {
            if(token.IsCancellationRequested)return;
            string message=e is HttpRequestException?"Cliente indisponível; tentando novamente na seleção":e is TimeoutException or OperationCanceledException?"A fonte demorou; tentando novamente":e.Message;
            ImportStatus("Importação pendente",message,true);
        }finally{importBusy=false;activeImportSelection=null;}
    }
    void ImportStatus(string items,string runes,bool warning=false,string? spells=null)
    {
        ItemImportText.Text=items;RuneImportText.Text=runes;SpellImportText.Text=spells??(AutoSpells.IsChecked==true?"Feitiços: aguardando seleção de campeão":"Feitiços: desativado");
        var color=Brush(warning?"#E5B477":"#91C8BC");ItemImportText.Foreground=color;RuneImportText.Foreground=color;SpellImportText.Foreground=color;
    }
    async void ApplyNow(object sender,RoutedEventArgs e)=>await RunAutoImport(true,true);
    void ImportSettingsChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;
        preferences.AutoImportItems=AutoItems.IsChecked==true;preferences.AutoImportRunes=AutoRunes.IsChecked==true;preferences.AutoImportSpells=AutoSpells.IsChecked==true;
        importRequest?.Cancel();lastImportKey="";lastImportAttempt="";_=SavePreferences();
        ImportStatus(preferences.AutoImportItems?"Itens: automático ligado":"Itens: desativado",preferences.AutoImportRunes?"Runas: automático ligado":"Runas: desativado");
    }
    async void RestoreRunes(object sender,RoutedEventArgs e)
    {
        if(importBusy){Notice("Aguarde a importação atual terminar antes de restaurar.",true);return;}
        importRequest?.Cancel();AutoRunes.IsChecked=false;
        try{using var api=leagueApiFactory();await importer.RestoreRunesAsync(api,lifetime.Token);ImportStatus(ItemImportText.Text,"Runas anteriores restauradas · automático desligado");}
        catch(Exception err) when(err is IOException or InvalidDataException or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException){if(!lifetime.IsCancellationRequested)Notice("Não foi possível restaurar: "+err.Message,true);}
    }
    void MinimizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void MaximizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    void CloseWindow(object sender,RoutedEventArgs e)=>Close();
}
