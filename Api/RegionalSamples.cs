using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoLoL.Core;
using static NexoLoL.Core.RegionalComparison;

namespace Bardo.Api;

public sealed class RegionalSamples
{
    readonly string connectionString;
    static long Cutoff => DateTimeOffset.UtcNow.AddDays(-WindowDays).ToUnixTimeMilliseconds();

    public RegionalSamples(IConfiguration config)
    {
        var path = Path.GetFullPath(config["BARDO_COMPARISON_DB"] ?? "data/regional.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS scores (
                region TEXT NOT NULL, game INTEGER NOT NULL, team INTEGER NOT NULL, champ INTEGER NOT NULL,
                queue INTEGER NOT NULL, played INTEGER NOT NULL, player TEXT NOT NULL, kda REAL NOT NULL,
                PRIMARY KEY(region, game, player));
            CREATE INDEX IF NOT EXISTS cohort ON scores(region, champ, queue, played);
            CREATE INDEX IF NOT EXISTS expiry ON scores(played);
            """;
        cmd.ExecuteNonQuery();
    }

    SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    internal bool Record(string region, long game, RiotProfiles.Match match)
    {
        var info = match.Info;
        if (match.Metadata?.MatchId != $"{region}_{game}" || info?.GameId != game ||
            info.GameType != "MATCHED_GAME" || info.QueueId is not (400 or 420 or 430 or 440 or 450 or 490 or 2400) ||
            info.GameCreation < Cutoff || info.GameCreation > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() ||
            info.GameDuration < 300 || info.Participants is not {Length:10} players ||
            players.Any(p => string.IsNullOrWhiteSpace(p.Puuid) || p.ChampionId <= 0 || p.TeamId is not (100 or 200) ||
                p.Kills < 0 || p.Assists < 0 || p.Deaths < 0 || p.GameEndedInEarlySurrender) ||
            players.Select(p => p.Puuid).Distinct().Count() != 10) return false;

        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM scores WHERE played < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", Cutoff);
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
        cmd.CommandText = "INSERT OR IGNORE INTO scores VALUES ($region,$game,$team,$champ,$queue,$played,$player,$kda)";
        cmd.Parameters.AddWithValue("$region", region);
        cmd.Parameters.AddWithValue("$game", game);
        cmd.Parameters.AddWithValue("$queue", info.QueueId);
        cmd.Parameters.AddWithValue("$played", info.GameCreation);
        var team = cmd.Parameters.Add("$team", SqliteType.Integer);
        var champ = cmd.Parameters.Add("$champ", SqliteType.Integer);
        var player = cmd.Parameters.Add("$player", SqliteType.Text);
        var kda = cmd.Parameters.Add("$kda", SqliteType.Real);
        foreach (var p in players)
        {
            team.Value = p.TeamId;
            champ.Value = p.ChampionId;
            player.Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.Puuid)));
            kda.Value = ((double)p.Kills + p.Assists) / Math.Max(1, p.Deaths);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return true;
    }

    RegionalComparison? Compare(string region, long game, int team, int champion)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            WITH target AS (
                SELECT player, kda, queue FROM scores WHERE region=$region AND game=$game
                    AND team=$team AND champ=$champ AND played >= $cutoff
            ), cohort AS (
                SELECT s.* FROM scores s JOIN target t ON s.queue=t.queue
                WHERE region=$region AND champ=$champ AND played >= $cutoff
            ), peers AS (
                SELECT c.kda FROM cohort c, target t WHERE c.game <> $game AND c.player <> t.player
            ), averages AS (
                SELECT player, COUNT(*) n, ROUND(AVG(kda),6) mean FROM cohort GROUP BY player
            )
            SELECT t.queue, t.kda, (SELECT COUNT(*) FROM peers), (SELECT AVG(kda) FROM peers),
                (SELECT COUNT(*) FROM peers WHERE kda >= t.kda), a.n, a.mean,
                (SELECT COUNT(*) FROM averages WHERE n >= $min),
                1+(SELECT COUNT(*) FROM averages WHERE n >= $min AND mean > a.mean)
            FROM target t JOIN averages a ON a.player=t.player
            """;
        cmd.Parameters.AddWithValue("$region", region);
        cmd.Parameters.AddWithValue("$game", game);
        cmd.Parameters.AddWithValue("$team", team);
        cmd.Parameters.AddWithValue("$champ", champion);
        cmd.Parameters.AddWithValue("$cutoff", Cutoff);
        cmd.Parameters.AddWithValue("$min", MinimumPlayerGames);
        using var result = cmd.ExecuteReader();
        if (!result.Read()) return null;
        int n = result.GetInt32(2), games = result.GetInt32(5), ranked = result.GetInt32(7);
        var comparison = new RegionalComparison(result.GetInt32(0), result.GetDouble(1), n,
            n >= MinimumMatches ? result.GetDouble(3) : null,
            n >= MinimumMatches ? 100d * (1 + result.GetInt32(4)) / (1 + n) : null,
            games, result.GetDouble(6), ranked,
            games >= MinimumPlayerGames && ranked >= MinimumPlayers ? result.GetInt32(8) : null);
        return result.Read() ? null : comparison;
    }

    public static async Task<IResult> Lookup(string region, long gameId, int team, int champion,
        RegionalSamples samples, RiotProfiles riot, IConfiguration config, HttpContext context)
    {
        var platform = LeaguePlatform.Find(region);
        if (platform is null || gameId <= 0 || team is not (100 or 200) || champion <= 0) return Results.BadRequest();
        var cached = samples.Compare(platform.Id, gameId, team, champion);
        if (cached != null) return Results.Ok(cached);
        var key = config["RIOT_API_KEY"];
        try
        {
            if (config.GetValue<bool>("BARDO_LOCAL_HISTORY") &&
                context.Connection.RemoteIpAddress is {} address && IPAddress.IsLoopback(address))
            {
                try
                {
                    using var client = LeagueClient.Open();
                    var user = await client.GetAsync("/lol-summoner/v1/current-summoner", context.RequestAborted);
                    if (user?["puuid"]?.ToString() is {Length:>0} owner &&
                        await LocalHistoryImport.ImportGame(samples, client, gameId, owner, platform.Id, context.RequestAborted) &&
                        samples.Compare(platform.Id,gameId,team,champion) is {} local) return Results.Ok(local);
                    if (string.IsNullOrWhiteSpace(key)) return Results.NotFound();
                }
                catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or IOException) { }
            }
            if (string.IsNullOrWhiteSpace(key)) return Results.StatusCode(503);
            var match = await riot.Read<RiotProfiles.Match>(
                $"https://{platform.Route}.api.riotgames.com/lol/match/v5/matches/{platform.Id}_{gameId}",
                key, context.RequestAborted);
            if (samples.Record(platform.Id, gameId, match) &&
                samples.Compare(platform.Id, gameId, team, champion) is {} comparison)
                return Results.Ok(comparison);
            return Results.NotFound();
        }
        catch (RiotCooldown limit)
        {
            context.Response.Headers.RetryAfter = limit.Seconds.ToString();
            return Results.StatusCode(429);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return Results.NotFound(); }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) { return Results.StatusCode(503); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested) { return Results.StatusCode(504); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException) { return Results.StatusCode(502); }
    }
}
