using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public sealed class GameService : IDisposable
{
    readonly HttpClient local=new(new HttpClientHandler {
        AllowAutoRedirect=false,UseProxy=false,
        ServerCertificateCustomValidationCallback=(request,_,_,_)=>request.RequestUri is {IsLoopback:true,Scheme:"https"}
    }){Timeout=TimeSpan.FromMilliseconds(1500)};
    public async Task<GameSnapshot> PollAsync(Catalog catalog,CancellationToken ct=default)
    {
        try {
            var stats=await Get("https://127.0.0.1:2999/liveclientdata/gamestats",null,ct);
            var active=await Get("https://127.0.0.1:2999/liveclientdata/activeplayer",null,ct);
            var players=await Get("https://127.0.0.1:2999/liveclientdata/playerlist",null,ct);
            string identity=Text(active?["riotId"]),name=Text(active?["summonerName"]);
            var self=(players as JsonArray)?.FirstOrDefault(p=>(identity!="" && Text(p?["riotId"])==identity)||(name!="" && Text(p?["summonerName"])==name));
            string key=Text(self?["rawChampionName"]).Split('_').Last();
            var champion=catalog.Champions.FirstOrDefault(c=>c.Key.Equals(key,StringComparison.OrdinalIgnoreCase)||c.Name==Text(self?["championName"]));
            var scores=self?["scores"];var items=(self?["items"] as JsonArray)?.Select(x=>Int(x?["itemID"])).Where(x=>x>0).ToList()??[];
            return new(true,"Partida em andamento",champion?.Id??0,Text(stats?["gameMode"]),Num(stats?["gameTime"]),Int(active?["level"]),Num(active?["currentGold"]),Int(scores?["kills"]),Int(scores?["deaths"]),Int(scores?["assists"]),Int(scores?["creepScore"]),Num(scores?["wardScore"]),items,Map:Int(stats?["mapNumber"]));
        } catch(Exception e) when(e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) {if(ct.IsCancellationRequested)throw;}
        foreach(string folder in new[]{@"C:\Riot Games\League of Legends",@"D:\Riot Games\League of Legends",@"E:\Riot Games\League of Legends"}) {
            string path=Path.Combine(folder,"lockfile");if(!File.Exists(path))continue;
            try {
                using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                using var reader=new StreamReader(stream);var parts=(await reader.ReadToEndAsync(ct)).Split(':');
                if(parts.Length<5 || !int.TryParse(parts[2],out int port) || port is <1 or >65535)continue;
                string auth=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+parts[3]));
                string root=$"https://127.0.0.1:{port}";
                var phase=await Get(root+"/lol-gameflow/v1/gameflow-phase",auth,ct);
                string phaseName=Text(phase);int champion=0;string mode="",role="default";int map=0;
                if(phaseName=="ChampSelect") {
                    var session=await Get(root+"/lol-champ-select/v1/session",auth,ct);
                    var self=(session?["myTeam"] as JsonArray)?.FirstOrDefault(p=>Int(p?["cellId"])==Int(session?["localPlayerCellId"]));
                    champion=Int(self?["championId"]);role=ClientImporter.NormalizeRole(Text(self?["assignedPosition"]));
                }
                var flow=await Get(root+"/lol-gameflow/v1/session",auth,ct);mode=Text(flow?["gameData"]?["gameMode"]);
                int queue=Int(flow?["gameData"]?["queue"]?["id"]);map=Int(flow?["gameData"]?["map"]?["id"]);
                if(queue==2400)mode="KIWI";else if(queue is 450 or 2400 || map==12)mode=queue==2400?"KIWI":"ARAM";else if(queue is 400 or 420 or 430 or 440 or 490)mode="CLASSIC";
                return new(true,phaseName switch {"ChampSelect"=>"Seleção de campeão","Lobby"=>"Saguão","Matchmaking"=>"Buscando partida","ReadyCheck"=>"Confirmação de partida","None"=>"Cliente aberto","EndOfGame"=>"Fim de partida",_=>phaseName},champion,mode,Role:role,Map:map);
            }catch(Exception e) when(e is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) {if(ct.IsCancellationRequested)throw;}
        }
        return new(false,"Aguardando o LoL",Message:"Abra o cliente para detecção automática. A busca manual de builds continua disponível.");
    }
    async Task<JsonNode?> Get(string url,string? auth,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);if(auth is not null)request.Headers.Authorization=new AuthenticationHeaderValue("Basic",auth);
        using var response=await local.SendAsync(request,ct);response.EnsureSuccessStatusCode();return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    static string Text(JsonNode? n)=>n?.ToString()??"";
    static double Num(JsonNode? n)=>double.TryParse(Text(n),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double x)?x:0;
    static int Int(JsonNode? n)=>(int)Num(n);
    public static GameMode? DetectMode(string mode,int map=0)=>mode switch {"KIWI"=>GameMode.Mayhem,"ARAM"=>GameMode.Aram,"CLASSIC"=>GameMode.Rift,_=>null};
    public void Dispose()=>local.Dispose();
}
