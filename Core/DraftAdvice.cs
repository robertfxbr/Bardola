using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public record DraftSession(long GameId,int[] Enemies,int[] Allies,int[] Bans,int[] AllyIntents,bool Banning)
{
    public static DraftSession Read(JsonNode session)
    {
        var actions=(session["actions"] as JsonArray)?.OfType<JsonArray>().SelectMany(g=>g.OfType<JsonObject>()).ToList()??[];
        int[] Done(string type,bool? ally=null)=>actions.Where(a=>a["completed"]?.GetValue<bool>()==true &&
            a["type"]?.ToString()==type && (ally==null || a["isAllyAction"]?.GetValue<bool>()==ally))
            .Select(a=>FinalScore.Number(a["championId"])??0).Where(id=>id>0).Distinct().ToArray();
        var intents=(session["myTeam"] as JsonArray)?.OfType<JsonObject>()
            .Select(p=>FinalScore.Number(p["championPickIntent"])??0).Where(id=>id>0).ToArray()??[];
        long.TryParse(session["gameId"]?.ToString(),out var game);
        return new(game,Done("pick",false),Done("pick",true),Done("ban"),intents,
            actions.Any(a=>a["type"]?.ToString()=="ban" && a["isInProgress"]?.GetValue<bool>()==true && a["completed"]?.GetValue<bool>()!=true));
    }
}

public record DraftPick(int Champion,int Points,int Level,BuildDocument? Meta=null)
{
    public DraftMatchup[] Matchups(DraftSession s)=>Meta?.DraftMatchups?.Where(m=>m.Games>=100 && s.Enemies.Contains(m.Champion)).ToArray()??[];
    public int[] Favorable(DraftSession s)=>Meta?.StrongAgainst?.Intersect(s.Enemies).ToArray()??[];
    public int[] Difficult(DraftSession s)=>Meta?.WeakAgainst?.Intersect(s.Enemies).ToArray()??[];
    public double Priority(DraftSession session){
        double experience=Math.Min(5,Math.Log10(1d+Math.Max(0,Points)));
        double meta=Meta is {Games:>=100,WinRate:double wr}?Math.Clamp(wr-50,-5,5)*.35:0;
        var observed=Matchups(session);
        double matchup=observed.Length>0 && Meta?.WinRate is double baseline?
            observed.Average(m=>Math.Clamp(m.WinRate-baseline,-6,6)*m.Games/(m.Games+200d))*.65:
            1.4*(Favorable(session).Length-Difficult(session).Length);
        return experience+meta+matchup;
    }
}

public static class DraftAdvice
{
    public static DraftPick[] Pool(JsonNode? roles,JsonNode? mastery,JsonNode? pickable,string role){
        if(roles is not JsonObject positions || pickable is not JsonArray ids)return [];
        var points=(mastery as JsonArray)?.OfType<JsonObject>().ToLookup(m=>FinalScore.Number(m["championId"])??0);
        var result=new List<DraftPick>();
        foreach(int id in ids.Select(x=>FinalScore.Number(x)??0).Where(id=>id>0).Distinct()){
            if(positions[id.ToString()]?["recommendedPositions"] is not JsonArray lanes ||
                !lanes.Any(x=>ClientImporter.NormalizeRole(x?.ToString().ToLowerInvariant()??"")==role))continue;
            var m=points?[id].FirstOrDefault();
            result.Add(new(id,FinalScore.Number(m?["championPoints"])??(points is null?-1:0),FinalScore.Number(m?["championLevel"])??0));
        }
        return result.OrderByDescending(p=>p.Points).ThenBy(p=>p.Champion).ToArray();
    }

    public static DraftPick[] Available(IEnumerable<DraftPick> pool,DraftSession s)=>pool
        .Where(p=>!s.Bans.Contains(p.Champion) && !s.Enemies.Contains(p.Champion) && !s.Allies.Contains(p.Champion))
        .OrderByDescending(p=>p.Priority(s)).ThenByDescending(p=>p.Points).ThenBy(p=>p.Champion).ToArray();

    public static int[] BanOptions(IEnumerable<DraftPick> picks,DraftSession s)=>picks
        .SelectMany(p=>p.Meta?.WeakAgainst??[]).Where(id=>!s.AllyIntents.Contains(id) && !s.Bans.Contains(id) &&
            !s.Allies.Contains(id) && !s.Enemies.Contains(id)).GroupBy(id=>id)
        .OrderByDescending(g=>g.Count()).Select(g=>g.Key).Take(3).ToArray();
}
