using System.Text.Json.Nodes;

namespace NexoLoL.Core;

public static class FinalScore
{
    internal static int? Number(JsonNode? value)=>int.TryParse(value?.ToString(),out var result) && result>=0 ? result : null;

    public static AccountMatch? Parse(JsonNode? response,string owner,long gameId)
    {
        if(gameId<=0 || owner.Length==0 || response?["gameId"]?.ToString()!=gameId.ToString() ||
           response["localPlayer"] is not JsonObject player || player["puuid"]?.ToString()!=owner ||
           player["isLocalPlayer"]?.GetValue<bool>()!=true || player["stats"] is not JsonObject stats ||
           Number(player["championId"]) is not int champion || champion==0)return null;
        var team=(response["teams"] as JsonArray)?.FirstOrDefault(t=>t?["isPlayerTeam"]?.GetValue<bool>()==true &&
            t["teamId"]?.ToString()==player["teamId"]?.ToString());
        if(team?["isWinningTeam"]?.GetValue<bool>() is not bool win || Number(response["gameLength"]) is not int duration)return null;
        return new(gameId,champion,response["gameMode"]?.ToString()??"",win,Number(stats["CHAMPIONS_KILLED"])??0,
            Number(stats["NUM_DEATHS"])??0,Number(stats["ASSISTS"])??0,(Number(stats["MINIONS_KILLED"])??0)+(Number(stats["NEUTRAL_MINIONS_KILLED"])??0),
            duration,DateTimeOffset.MinValue,Number(stats["TOTAL_DAMAGE_DEALT_TO_CHAMPIONS"]),Number(stats["TOTAL_DAMAGE_TAKEN"]),
            Number(stats["TOTAL_HEAL_ON_TEAMMATES"])+Number(stats["TOTAL_DAMAGE_SHIELDED_ON_TEAMMATES"]),
            (player["items"] as JsonArray)?.Select(i=>Number(i)??0).Where(id=>id>0).ToList(),MatchRoster.FromEnd(response,owner));
    }
}
