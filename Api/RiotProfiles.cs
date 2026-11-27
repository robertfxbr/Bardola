using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using NexoLoL.Core;

namespace Bardo.Api;

public sealed class RiotCooldown(int seconds):Exception
{
    public int Seconds {get;}=seconds;
}

public sealed class RiotProfiles(HttpClient http)
{
    const string Americas="https://americas.api.riotgames.com";
    const string Brazil="https://br1.api.riotgames.com";
    readonly object cooldownLock=new();
    DateTimeOffset retryAt;

    async Task<T> Read<T>(string url,string key,CancellationToken ct)
    {
        lock(cooldownLock){
            var remaining=(retryAt-DateTimeOffset.UtcNow).TotalSeconds;
            if(remaining>0)throw new RiotCooldown((int)Math.Ceiling(remaining));
        }
        using var request=new HttpRequestMessage(HttpMethod.Get,url);
        request.Headers.Add("X-Riot-Token",key);
        using var response=await http.SendAsync(request,ct);
        if(response.StatusCode==HttpStatusCode.TooManyRequests){
            var retry=response.Headers.RetryAfter;
            var delay=retry?.Delta ?? (retry?.Date-DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(60);
            int seconds=(int)Math.Clamp(Math.Ceiling(delay.TotalSeconds),1,int.MaxValue);
            lock(cooldownLock){
                var until=DateTimeOffset.UtcNow.AddSeconds(seconds);
                if(until>retryAt)retryAt=until;
            }
            throw new RiotCooldown(seconds);
        }
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct)??throw new InvalidDataException();
    }

    public async Task<GameAccount> Lookup(string name,string tag,string key,CancellationToken ct)
    {
        var identity=await Read<Identity>($"{Americas}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(tag)}",key,ct);
        if(string.IsNullOrWhiteSpace(identity.Puuid) || string.IsNullOrWhiteSpace(identity.GameName) || string.IsNullOrWhiteSpace(identity.TagLine))
            throw new InvalidDataException();
        var id=Uri.EscapeDataString(identity.Puuid);
        var summoner=await Read<Summoner>($"{Brazil}/lol/summoner/v4/summoners/by-puuid/{id}",key,ct);
        var entries=await Read<Standing[]>($"{Brazil}/lol/league/v4/entries/by-puuid/{id}",key,ct);
        var ranks=entries.Where(r=>r.QueueType is "RANKED_SOLO_5x5" or "RANKED_FLEX_SR")
            .Select(r=>new AccountRank(r.QueueType,r.Tier,r.Rank,r.LeaguePoints,r.Wins,r.Losses)).ToList();
        var ids=await Read<string[]>($"{Americas}/lol/match/v5/matches/by-puuid/{id}/ids?start=0&count=5",key,ct);
        var matches=new List<AccountMatch>();
        foreach(var matchId in ids.Distinct().Take(5)){
            if(!matchId.StartsWith("BR1_",StringComparison.Ordinal) || !long.TryParse(matchId.AsSpan(4),out var number) || number<=0)continue;
            Match data;
            try{data=await Read<Match>($"{Americas}/lol/match/v5/matches/{Uri.EscapeDataString(matchId)}",key,ct);}
            catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.NotFound){continue;}
            var info=data.Info;
            if(data.Metadata?.MatchId!=matchId || info?.GameType!="MATCHED_GAME" ||
                info.QueueId is not (400 or 420 or 430 or 440 or 450 or 490 or 2400))continue;
            var player=info.Participants?.FirstOrDefault(p=>p.Puuid==identity.Puuid);
            if(player is null || info.GameId!=number || info.GameCreation is <=0 or >253402300799999)continue;
            matches.Add(new(number,player.ChampionId,info.QueueId==2400?"KIWI":info.GameMode,player.Win,
                player.Kills,player.Deaths,player.Assists,player.TotalMinionsKilled+player.NeutralMinionsKilled,
                info.GameDuration,DateTimeOffset.FromUnixTimeMilliseconds(info.GameCreation),player.TotalDamageDealtToChampions));
        }
        return new(identity.GameName+"#"+identity.TagLine,summoner.SummonerLevel,summoner.ProfileIconId,"BR1",ranks,matches,
            "Filas públicas compatíveis entre as cinco partidas mais recentes · Riot Games");
    }
    sealed record Identity(string Puuid,string GameName,string TagLine);
    sealed record Summoner(int SummonerLevel,int ProfileIconId);
    sealed record Standing(string QueueType,string Tier,string Rank,int LeaguePoints,int Wins,int Losses);
    sealed record Match(Metadata? Metadata,GameInfo? Info);
    sealed record Metadata(string MatchId);
    sealed record GameInfo(long GameId,long GameCreation,int GameDuration,string GameType,string GameMode,int QueueId,Player[]? Participants);
    sealed record Player(string Puuid,int ChampionId,bool Win,int Kills,int Deaths,int Assists,int TotalMinionsKilled,int NeutralMinionsKilled,int? TotalDamageDealtToChampions);
}
