using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public record ScorePlayer(string Name,int Champion,bool Local,int Kills,int Deaths,int Assists,int Cs,int? Damage,List<int> Items,List<int> Cards,
    int? Physical=null,int? Magic=null,int? TrueDamage=null,int? Mitigated=null,bool HasKills=true,bool HasAssists=true);
public record ScoreTeam(int Id,bool? Win,List<ScorePlayer> Players);

public static class MatchRoster
{
    static int N(JsonNode? n)=>FinalScore.Number(n)??0;
    static IEnumerable<JsonObject> Rows(JsonNode? n)=>(n as JsonArray)?.OfType<JsonObject>()??[];
    static ScorePlayer Player(JsonObject p,string name,bool local,bool history)
    {
        var s=p["stats"];
        int Stat(string a,string b)=>N(s?[history?a:b]);
        int? Optional(string a,string b)=>FinalScore.Number(s?[history?a:b]);
        var items=history?Enumerable.Range(0,7).Select(i=>N(s?["item"+i])):(p["items"] as JsonArray)?.Select(N)??[];
        var cards=Enumerable.Range(1,6).Select(i=>N(s?["playerAugment"+i]??s?["PLAYER_AUGMENT_"+i])).Where(id=>id>0).ToList();
        return new(name,N(p["championId"]),local,Stat("kills","CHAMPIONS_KILLED"),Stat("deaths","NUM_DEATHS"),
            Stat("assists","ASSISTS"),Stat("totalMinionsKilled","MINIONS_KILLED")+Stat("neutralMinionsKilled","NEUTRAL_MINIONS_KILLED"),
            Optional("totalDamageDealtToChampions","TOTAL_DAMAGE_DEALT_TO_CHAMPIONS"),items.Where(id=>id>0).ToList(),cards,
            Optional("physicalDamageDealtToChampions","PHYSICAL_DAMAGE_DEALT_TO_CHAMPIONS"),
            Optional("magicDamageDealtToChampions","MAGIC_DAMAGE_DEALT_TO_CHAMPIONS"),
            Optional("trueDamageDealtToChampions","TRUE_DAMAGE_DEALT_TO_CHAMPIONS"),
            Optional("damageSelfMitigated","DAMAGE_SELF_MITIGATED"),
            Optional("kills","CHAMPIONS_KILLED").HasValue,Optional("assists","ASSISTS").HasValue);
    }

    public static List<ScoreTeam> FromEnd(JsonNode doc,string owner) =>
        Rows(doc["teams"]).Select(t=>new ScoreTeam(N(t["teamId"]),t["isWinningTeam"]?.GetValue<bool>(),
            Rows(t["players"]).Select(p=>Player(p,(p["riotIdGameName"]??p["summonerName"])?.ToString()??"",
                p["puuid"]?.ToString()==owner,false)).ToList())).Where(t=>t.Players.Count>0).ToList();

    public static List<ScoreTeam> FromHistory(JsonNode? doc,string owner,long id)
    {
        if(string.IsNullOrWhiteSpace(owner)||doc?["gameId"]?.ToString()!=id.ToString())return [];
        var identities=Rows(doc["participantIdentities"]).ToLookup(i=>N(i["participantId"]),i=>i["player"]);
        var teams=new List<ScoreTeam>();
        foreach(var team in Rows(doc["teams"])){
            int teamId=N(team["teamId"]);
            var players=new List<ScorePlayer>();
            foreach(var p in Rows(doc["participants"]).Where(x=>N(x["teamId"])==teamId)){
                var who=identities[N(p["participantId"])].FirstOrDefault();
                players.Add(Player(p,(who?["gameName"]??who?["summonerName"])?.ToString()??"",who?["puuid"]?.ToString()==owner,true));
            }
            if(players.Count>0)teams.Add(new(teamId,team["win"]?.ToString()?.ToLowerInvariant() switch{"win" or "true"=>true,"fail" or "false"=>false,_=>(bool?)null},players));
        }
        return teams.SelectMany(t=>t.Players).Count(p=>p.Local)==1?teams:[];
    }
}
