using System.Text.Json.Nodes;
using NexoLoL.Core;

namespace Bardo.Api;

internal static class LocalHistoryImport
{
    // Private local bootstrap. Runs only when explicitly selected on the command line.
    public static async Task<int> Run(RegionalSamples samples, CancellationToken token)
    {
        using var client = LeagueClient.Open();
        var owner = await client.GetAsync("/lol-summoner/v1/current-summoner", token);
        var puuid = owner?["puuid"]?.ToString();
        if (string.IsNullOrWhiteSpace(puuid)) throw new InvalidDataException("Conta do League indisponível.");
        var seen = new HashSet<long>();
        int imported = 0;
        for (int start = 0; start < 100; start += 20)
        {
            var path = $"/lol-match-history/v1/products/lol/{Uri.EscapeDataString(puuid)}/matches?begIndex={start}&endIndex={start + 19}";
            var history = await client.GetAsync(path, token);
            var games = history?["games"] is JsonObject collection ? collection["games"] as JsonArray : history?["games"] as JsonArray;
            if (games == null || games.Count == 0) break;
            foreach (var summary in games.OfType<JsonObject>())
            {
                if (!long.TryParse(summary["gameId"]?.ToString(), out long id) || !seen.Add(id)) continue;
                if (!long.TryParse(summary["gameCreation"]?.ToString(), out var at) ||
                    at < DateTimeOffset.UtcNow.AddDays(-RegionalComparison.WindowDays).ToUnixTimeMilliseconds()) continue;
                if (await ImportGame(samples,client,id,puuid,null,token)) imported++;
            }
            if (games.Count < 19) break;
        }
        Console.WriteLine($"{imported} partidas regionais válidas processadas entre {seen.Count} registros do histórico local.");
        return imported;
    }

    internal static async Task<bool> ImportGame(RegionalSamples samples, LeagueClient client, long id, string owner,
        string? expectedRegion, CancellationToken token)
    {
        JsonNode? detail;
        try { detail = await client.GetAsync($"/lol-match-history/v1/games/{id}", token); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
        if (detail is not JsonObject game || game["gameId"]?.ToString() != id.ToString() ||
            !long.TryParse(game["gameCreation"]?.ToString(),out var at) ||
            game["participantIdentities"] is not JsonArray identities || game["participants"] is not JsonArray players) return false;
        var platform = LeaguePlatform.Find(game["platformId"]?.ToString());
        if (platform == null || (expectedRegion != null && expectedRegion != platform.Id)) return false;
        var owners = identities.OfType<JsonObject>().ToLookup(i => i["participantId"]?.ToString(), i => i["player"]?["puuid"]?.ToString());
        if (!owners.Any(g => g.Contains(owner))) return false;
        int Number(JsonNode? n) => int.TryParse(n?.ToString(), out int value) ? value : -1;
        var roster = players.OfType<JsonObject>().Select(p => {
            var s = p["stats"];
            return new RiotProfiles.Player(owners[p["participantId"]?.ToString()].FirstOrDefault() ?? "",
                Number(p["championId"]), false, Number(s?["kills"]), Number(s?["deaths"]), Number(s?["assists"]),
                0, 0, null, Number(p["teamId"]), s?["gameEndedInEarlySurrender"]?.GetValue<bool>() == true);
        }).ToArray();
        var info = new RiotProfiles.GameInfo(id, at, Number(game["gameDuration"]), game["gameType"]?.ToString() ?? "",
            game["gameMode"]?.ToString() ?? "", Number(game["queueId"]), roster);
        return samples.Record(platform.Id, id, new(new($"{platform.Id}_{id}"), info));
    }
}
