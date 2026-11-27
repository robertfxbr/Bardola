using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public record MatchEvent(int Seconds,string Kind,int KillerChampion,int VictimChampion,string Target);

public static class MatchTimeline
{
    public static List<MatchEvent> Read(JsonNode? timeline,JsonNode? detail,string owner,long gameId)
    {
        if(MatchRoster.FromHistory(detail,owner,gameId).Count==0 || timeline?["frames"] is not JsonArray frames)return [];
        var champions=new Dictionary<int,int>();
        foreach(var p in (detail!["participants"] as JsonArray)!.OfType<JsonObject>())
            if(FinalScore.Number(p["participantId"]) is int id && FinalScore.Number(p["championId"]) is int champ)champions[id]=champ;
        var result=new List<MatchEvent>();
        foreach(var frame in frames.OfType<JsonObject>()){
            if(frame["events"] is not JsonArray events)continue;
            foreach(var e in events.OfType<JsonObject>()){
                string kind=e["type"]?.ToString()??"";
                if(kind is not ("CHAMPION_KILL" or "BUILDING_KILL" or "ELITE_MONSTER_KILL") || FinalScore.Number(e["timestamp"]) is not int ms)continue;
                int killer=champions.GetValueOrDefault(FinalScore.Number(e["killerId"])??0);
                int victim=champions.GetValueOrDefault(FinalScore.Number(e["victimId"])??0);
                string target=(kind=="BUILDING_KILL"?e["buildingType"]:e["monsterType"])?.ToString()??"";
                result.Add(new(ms/1000,kind,killer,victim,target));
            }
        }
        return result.OrderBy(e=>e.Seconds).ToList();
    }
}
