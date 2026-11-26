using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace NexoLoL.Core;

public interface ILeagueApi : IDisposable
{
    Task<JsonNode?> GetAsync(string path,CancellationToken ct=default);
    Task<JsonNode?> SendAsync(HttpMethod method,string path,JsonNode? body,CancellationToken ct=default);
}
public record ImportSelection(string Session,int ChampionId,GameMode Mode,string Role,string Owner,long SummonerId);
public record ImportOptions(bool Items=true,bool Runes=true,bool ReuseActivePage=true);
public record ImportOutcome(string Items,string Runes,bool Success,bool Changed);

public sealed class ClientImporter(string backupDirectory)
{
    readonly SemaphoreSlim gate=new(1);
    string? completedKey;
    public static async Task<ImportSelection?> SelectionAsync(ILeagueApi api,CancellationToken ct=default)
    {
        if(S(await api.GetAsync("/lol-gameflow/v1/gameflow-phase",ct))!="ChampSelect")return null;
        var session=await api.GetAsync("/lol-champ-select/v1/session",ct);
        var self=(session?["myTeam"] as JsonArray)?.FirstOrDefault(x=>N(x?["cellId"])==N(session?["localPlayerCellId"]));
        int champion=(int)N(self?["championId"]);if(champion<=0)return null;
        var flow=await api.GetAsync("/lol-gameflow/v1/session",ct);int queue=(int)N(flow?["gameData"]?["queue"]?["id"]);
        string mode=S(flow?["gameData"]?["gameMode"]);if(mode=="")mode=S(flow?["gameData"]?["queue"]?["gameMode"]);
        if(queue==2400)mode="KIWI";else if(queue==450)mode="ARAM";else if(mode=="" && queue is 400 or 420 or 430 or 440 or 490)mode="CLASSIC";
        var mapped=GameService.DetectMode(mode);if(mapped is null)return null;
        var owner=await api.GetAsync("/lol-summoner/v1/current-summoner",ct);
        if(S(owner?["puuid"])=="" || N(owner?["summonerId"])<=0)return null;
        return new(S(session?["gameId"]),champion,mapped.Value,NormalizeRole(S(self?["assignedPosition"])),S(owner?["puuid"]),N(owner?["summonerId"]));
    }
    public static string NormalizeRole(string role)=>role switch{"utility"=>"support","top" or "jungle" or "middle" or "bottom" or "support"=>role,_=>"default"};
    public async Task<ImportOutcome> ApplyAsync(ILeagueApi api,BuildDocument document,int variant,ImportOptions options,bool force=false,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try {
            var selection=await SelectionAsync(api,ct);
            if(selection is null)return new("Aguardando seleção","Aguardando seleção",false,false);
            try{Match(selection,document);}catch(InvalidDataException e){return new(e.Message,e.Message,false,false);}
            if(variant<0 || variant>=document.Variants.Count)return new("Build inválida","Build inválida",false,false);
            var build=document.Variants[variant];
            string key=selection.Owner+":"+selection.Session+":"+document.Query.CacheKey+":"+variant+":"+options;
            if(!force && Volatile.Read(ref completedKey)==key)return new(options.Items?"Já aplicado":"Desligado",document.Query.Mode==GameMode.Mayhem?"Modo sem runas":options.Runes?"Já aplicadas":"Desligado",true,false);
            Interlocked.Exchange(ref completedKey,null); // Uma tentativa parcial pode modificar a página compartilhada.
            string items=options.Items?"Aguardando":"Desligado",runes=options.Runes?"Aguardando":"Desligado";bool success=true,changed=false;
            if(options.Items) {
                try{await ApplyItems(api,selection,document,build,ct);items="Itens aplicados no LoL";changed=true;}
                catch(Exception e) when(Recoverable(e,ct)){items="Itens: "+e.Message;success=false;}
            }
            if(options.Runes && selection.Mode!=GameMode.Mayhem) {
                try{await ApplyRunes(api,selection,build.Runes,options.ReuseActivePage,ct);runes="Runas aplicadas e selecionadas";changed=true;}
                catch(Exception e) when(Recoverable(e,ct)){runes="Runas: "+e.Message;success=false;}
            }else if(selection.Mode==GameMode.Mayhem)runes="Desordem não usa runas";
            if(success)Interlocked.Exchange(ref completedKey,key);
            return new(items,runes,success,changed);
        }finally{gate.Release();}
    }
    async Task ApplyItems(ILeagueApi api,ImportSelection expected,BuildDocument doc,BuildVariant variant,CancellationToken ct)
    {
        string path=$"/lol-item-sets/v1/item-sets/{expected.SummonerId}/sets";
        var bundle=await api.GetAsync(path,ct) as JsonObject??throw new InvalidDataException("coleção de itens indisponível");
        if(bundle["itemSets"] is not JsonArray)throw new InvalidDataException("formato de coleção não reconhecido");
        await Revalidate(api,expected,ct);
        bundle=await api.GetAsync(path,ct) as JsonObject??throw new InvalidDataException("coleção de itens indisponível");
        if(bundle["itemSets"] is not JsonArray sets)throw new InvalidDataException("formato de coleção não reconhecido");
        string uid=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"NexoLoL:{expected.Owner}:{doc.Query.ChampionId}:{doc.Query.Mode}")).AsSpan(0,16)).ToString();
        var generated=JsonNode.Parse(ExportService.CreateItemSet(doc,variant))!;generated["uid"]=uid;
        generated["title"]=$"Bardo.gg · {doc.Query.Slug} · {doc.Patch}";
        int index=-1;for(int i=0;i<sets.Count;i++)if(S(sets[i]?["uid"])==uid){index=i;break;}
        if(index>=0)sets[index]=generated;else sets.Add(generated);
        await Revalidate(api,expected,ct);
        await api.SendAsync(HttpMethod.Put,path,bundle,ct);
        var confirmed=await api.GetAsync(path,ct);
        var set=(confirmed?["itemSets"] as JsonArray)?.FirstOrDefault(x=>S(x?["uid"])==uid);
        if(set is null || new[]{"uid","associatedChampions","associatedMaps","blocks"}.Any(k=>!JsonNode.DeepEquals(set[k],generated[k])))throw new InvalidDataException("o cliente não confirmou o conjunto importado");
    }
    async Task ApplyRunes(ILeagueApi api,ImportSelection expected,RunePage? runes,bool reuse,CancellationToken ct)
    {
        await Revalidate(api,expected,ct);
        int[] styles=[8000,8100,8200,8300,8400];
        if(runes is null || runes.Perks.Count!=9 || runes.Perks.Any(x=>x<=0) || !styles.Contains(runes.PrimaryStyle) || !styles.Contains(runes.SecondaryStyle) || runes.PrimaryStyle==runes.SecondaryStyle)throw new InvalidDataException("não há página de runas validada nesta build");
        var pages=await api.GetAsync("/lol-perks/v1/pages",ct) as JsonArray??throw new InvalidDataException("páginas indisponíveis");
        string statePath=StatePath(expected.Owner);var state=await ReadState(statePath,ct);
        long managed=N(state?["managedPageId"]);
        JsonNode? page=pages.FirstOrDefault(p=>N(p?["id"])==managed && B(p?["isEditable"]));
        page??=pages.FirstOrDefault(p=>(S(p?["name"]).StartsWith("Bardo.gg ·",StringComparison.Ordinal) || S(p?["name"]).StartsWith("Nexo LoL ·",StringComparison.Ordinal)) && B(p?["isEditable"]));
        var payload=new JsonObject{["name"]="Bardo.gg · seleção atual",["primaryStyleId"]=runes.PrimaryStyle,["subStyleId"]=runes.SecondaryStyle,["selectedPerkIds"]=new JsonArray(runes.Perks.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())};
        long id=0;
        if(page is null) {
            var inventory=await api.GetAsync("/lol-perks/v1/inventory",ct);
            if(B(inventory?["canAddCustomPage"])) {
                await Revalidate(api,expected,ct);
                var created=await api.SendAsync(HttpMethod.Post,"/lol-perks/v1/pages",payload,ct);id=N(created?["id"]);
                if(id<=0)throw new InvalidDataException("o cliente não confirmou a nova página");
                state??=new JsonObject();state["managedPageId"]=id;await Storage.WriteAsync(statePath,state.ToJsonString(),ct);
            }else {
                if(!reuse)throw new InvalidDataException("sem vaga para uma página; libere uma vaga ou permita reutilizar a ativa");
                var active=await api.GetAsync("/lol-perks/v1/currentpage",ct);id=N(active?["id"]);
                page=pages.FirstOrDefault(p=>N(p?["id"])==id && B(p?["isEditable"]));
                if(page is null)throw new InvalidDataException("a página ativa não é editável; selecione uma página pessoal no cliente");
                state??=new JsonObject();state["managedPageId"]=id;
                if(state["original"] is null || N(state["original"]?["id"])!=id)state["original"]=page.DeepClone();
                await Revalidate(api,expected,ct);
                await Storage.WriteAsync(statePath,state.ToJsonString(),ct); // Backup durável ANTES da primeira alteração.
            }
        }
        if(page is not null) {
            id=N(page["id"]);payload["id"]=id;
            await Revalidate(api,expected,ct);await api.SendAsync(HttpMethod.Put,$"/lol-perks/v1/pages/{id}",payload,ct);
        }
        await Revalidate(api,expected,ct);
        await api.SendAsync(HttpMethod.Put,"/lol-perks/v1/currentpage",JsonValue.Create(id),ct);
        var actual=await api.GetAsync("/lol-perks/v1/currentpage",ct);
        if(N(actual?["id"])!=id || N(actual?["primaryStyleId"])!=runes.PrimaryStyle || N(actual?["subStyleId"])!=runes.SecondaryStyle || !JsonNode.DeepEquals(actual?["selectedPerkIds"],payload["selectedPerkIds"]))throw new InvalidDataException("o cliente não confirmou a página selecionada");
    }
    public async Task RestoreRunesAsync(ILeagueApi api,CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try {
            string phase=S(await api.GetAsync("/lol-gameflow/v1/gameflow-phase",ct));if(phase is "InProgress" or "Reconnect")throw new InvalidDataException("restaure as runas depois da partida");
            var account=await api.GetAsync("/lol-summoner/v1/current-summoner",ct);string owner=S(account?["puuid"]);
            var state=await ReadState(StatePath(owner),ct);var original=state?["original"];
            if(original is null)throw new InvalidDataException("não há página anterior para restaurar nesta conta");
            var pages=await api.GetAsync("/lol-perks/v1/pages",ct) as JsonArray;long id=N(original["id"]);
            if(pages?.Any(p=>N(p?["id"])==id && B(p?["isEditable"]))!=true)throw new InvalidDataException("a página original não está mais disponível");
            var payload=new JsonObject();foreach(string k in new[]{"id","name","primaryStyleId","subStyleId","selectedPerkIds"})payload[k]=original[k]?.DeepClone();
            await GuardAccountAndPhase(api,owner,ct,true);await api.SendAsync(HttpMethod.Put,$"/lol-perks/v1/pages/{id}",payload,ct);
            await GuardAccountAndPhase(api,owner,ct,true);await api.SendAsync(HttpMethod.Put,"/lol-perks/v1/currentpage",JsonValue.Create(id),ct);
            var check=await api.GetAsync("/lol-perks/v1/currentpage",ct);
            if(new[]{"id","name","primaryStyleId","subStyleId","selectedPerkIds"}.Any(k=>!JsonNode.DeepEquals(check?[k],original[k])))throw new InvalidDataException("restauração não confirmada pelo cliente; backup preservado");
            state!["managedPageId"]=0;state["original"]=null;await Storage.WriteAsync(StatePath(owner),state.ToJsonString(),ct);ResetSession();
        }finally{gate.Release();}
    }
    static void Match(ImportSelection s,BuildDocument d)
    {
        if(s.ChampionId!=d.Query.ChampionId || s.Mode!=d.Query.Mode)throw new InvalidDataException("a build não corresponde ao campeão/modo da seleção");
        string role=d.Query.Role=="default"?d.Role:d.Query.Role;
        if(s.Mode==GameMode.Rift && s.Role!="default" && role!="" && role!="default" && s.Role!=role)throw new InvalidDataException("a função da build difere da seleção");
    }
    static async Task Revalidate(ILeagueApi api,ImportSelection expected,CancellationToken ct)
    {if(await SelectionAsync(api,ct)!=expected)throw new InvalidDataException("a seleção ou a conta mudou; aguardando a nova build");}
    static async Task GuardAccountAndPhase(ILeagueApi api,string owner,CancellationToken ct,bool restore=false)
    {
        var current=await api.GetAsync("/lol-summoner/v1/current-summoner",ct);if(S(current?["puuid"])!=owner)throw new InvalidDataException("a conta mudou; operação cancelada");
        string phase=S(await api.GetAsync("/lol-gameflow/v1/gameflow-phase",ct));
        if(restore?phase is "InProgress" or "Reconnect":phase!="ChampSelect")throw new InvalidDataException("a fase do cliente mudou; operação cancelada");
    }
    string StatePath(string owner)=>Path.Combine(backupDirectory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner)))+".json");
    static async Task<JsonNode?> ReadState(string path,CancellationToken ct){if(!File.Exists(path))return null;return JsonNode.Parse(await File.ReadAllTextAsync(path,ct));}
    static bool Recoverable(Exception e,CancellationToken ct)=>(e is IOException or InvalidDataException or HttpRequestException or System.Text.Json.JsonException) && !ct.IsCancellationRequested;
    public void ResetSession()=>Interlocked.Exchange(ref completedKey,null);
    static string S(JsonNode? n)=>n?.ToString()??"";
    static long N(JsonNode? n)=>long.TryParse(S(n),out long x)?x:0;
    static bool B(JsonNode? n)=>bool.TryParse(S(n),out bool x)&&x;
}
