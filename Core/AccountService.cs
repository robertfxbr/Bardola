using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public record AccountRank(string Queue,string Tier,string Division,int Points,int Wins,int Losses);
public record AccountMatch(long Id,int ChampionId,string Mode,bool Win,int Kills,int Deaths,int Assists,int Cs,int Duration,DateTimeOffset At);
public record GameAccount(string RiotId,int Level,int IconId,string Region,List<AccountRank> Ranks,List<AccountMatch> Matches,string HistoryMessage="");

public static class AccountParser
{
    public static void AssertSameAccount(JsonNode before,JsonNode after)
    {
        string owner=S(before["puuid"]);
        if(owner=="" || owner!=S(after["puuid"]))throw new InvalidDataException("A conta ativa no LoL mudou durante a consulta. Atualize o perfil para conectar a conta atual.");
    }
    public static GameAccount Parse(JsonNode profile,JsonNode? ranked,JsonNode? region,JsonNode? history)
    {
        string name=S(profile["gameName"]);if(name=="")name=S(profile["displayName"]);
        if(name=="" || N(profile["summonerLevel"])<1)throw new InvalidDataException("O cliente ainda não disponibilizou um perfil conectado.");
        string tag=S(profile["tagLine"]),puuid=S(profile["puuid"]);long summonerId=N(profile["summonerId"]);
        var ranks=new List<AccountRank>();
        var queues=(ranked?["queues"] as JsonArray)?.OfType<JsonNode>().ToList()??[];
        if(queues.Count==0 && ranked?["queueMap"] is JsonObject map)queues=map.Select(x=>x.Value).OfType<JsonNode>().ToList();
        foreach(var r in queues) {
            string queue=S(r["queueType"]);if(queue is not "RANKED_SOLO_5x5" and not "RANKED_FLEX_SR")continue;
            ranks.Add(new(queue,S(r["tier"]),S(r["division"]),(int)N(r["leaguePoints"]),(int)N(r["wins"]),(int)N(r["losses"])));
        }
        var matches=new List<AccountMatch>();
        JsonArray? games=history?["games"] is JsonObject collection?collection["games"] as JsonArray:history?["games"] as JsonArray;
        foreach(var m in games?.OfType<JsonNode>()??[]) {
            if(m["participants"] is not JsonArray participants)continue;
            var identity=(m["participantIdentities"] as JsonArray)?.FirstOrDefault(p=>(puuid!="" && S(p?["player"]?["puuid"])==puuid)||(summonerId>0 && N(p?["player"]?["summonerId"])==summonerId));
            JsonNode? self=identity is not null?participants.FirstOrDefault(p=>N(p?["participantId"])==N(identity["participantId"])):null;
            if(self is null || self["stats"] is not { } stats || N(m["gameId"])<=0)continue;
            DateTimeOffset at=DateTimeOffset.MinValue;long timestamp=N(m["gameCreation"]);
            if(timestamp>0 && timestamp<253402300799000)at=DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
            matches.Add(new(N(m["gameId"]),(int)N(self["championId"]),S(m["gameMode"]),S(stats["win"]).Equals("true",StringComparison.OrdinalIgnoreCase),
                (int)N(stats["kills"]),(int)N(stats["deaths"]),(int)N(stats["assists"]),(int)(N(stats["totalMinionsKilled"])+N(stats["neutralMinionsKilled"])),(int)N(m["gameDuration"]),at));
        }
        return new(name+(tag!=""?"#"+tag:""),(int)N(profile["summonerLevel"]),(int)N(profile["profileIconId"]),S(region?["region"]),ranks,matches.Take(20).ToList());
    }
    static string S(JsonNode? n)=>n?.ToString()??"";
    static long N(JsonNode? n)=>long.TryParse(S(n),out long x)?x:0;
}

public sealed class AccountService
{
    public async Task<GameAccount> ConnectAsync(CancellationToken ct=default)
    {
        using var client=LeagueClient.Open();
        var profile=await client.GetAsync("/lol-summoner/v1/current-summoner",ct)??throw new InvalidDataException("Perfil indisponível.");
        async Task<JsonNode?> Optional(string path) {
            try{return await client.GetAsync(path,ct);}
            catch(Exception e) when((e is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException) && !ct.IsCancellationRequested){return null;}
        }
        var rankedTask=Optional("/lol-ranked/v1/current-ranked-stats");var regionTask=Optional("/riotclient/region-locale");
        string puuid=profile["puuid"]?.ToString()??"";
        var historyTask=puuid!=""?Optional("/lol-match-history/v1/products/lol/"+Uri.EscapeDataString(puuid)+"/matches?begIndex=0&endIndex=19"):Task.FromResult<JsonNode?>(null);
        await Task.WhenAll(rankedTask,regionTask,historyTask);
        var latest=await client.GetAsync("/lol-summoner/v1/current-summoner",ct)??throw new InvalidDataException("A sessão do LoL foi encerrada durante a consulta.");
        AccountParser.AssertSameAccount(profile,latest);
        return AccountParser.Parse(profile,rankedTask.Result,regionTask.Result,historyTask.Result) with {
            HistoryMessage=historyTask.Result is null?"O cliente não forneceu histórico nesta consulta. Seu perfil continua conectado; tente atualizar depois.":"Histórico fornecido pelo cliente do LoL · até 20 partidas."
        };
    }
}
