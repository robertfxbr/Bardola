using System.Net;
using System.Net.Http.Json;
using Bardo.Api;
using NexoLoL.Core;

var builder=WebApplication.CreateBuilder();
builder.Configuration["urls"]="http://127.0.0.1:0";
builder.Configuration["RIOT_API_KEY"]="fixture";
var dataDir=Directory.CreateTempSubdirectory("bardo-regional-");
builder.Configuration["BARDO_COMPARISON_DB"]=Path.Combine(dataDir.FullName,"test.sqlite");
builder.Logging.ClearProviders();
var upstream=new RiotFixture();
builder.Services.AddSingleton(new HttpClient(upstream));
await using var app=ApiHost.Build(builder);
await app.StartAsync();
using var client=new HttpClient{BaseAddress=new Uri(app.Urls.Single())};
void Check(bool value,string name){
    if(!value)throw new Exception(name);
    Console.WriteLine("PASS "+name);
}
var profile=await client.GetFromJsonAsync<GameAccount>("/v1/br1/profile?riotId=Teste%20Um%23BR1");
Check(profile is {Level:42,RiotId:"Teste Um#BR1"} && profile.Ranks.Single().Points==55,"HTTP entrega nível e ranque sem cliente do LoL");
Check(profile!.Matches.Count==1 && profile.Matches[0] is {Kills:3,Cs:22,ChampionId:63},"histórico filtra personalizadas, fila incompatível e PUUID alheio");
var count=upstream.Count;
await client.GetStringAsync("/v1/br1/profile?riotId=Teste%20Um%23BR1");
Check(upstream.Count==count,"cache evita nova consulta à Riot");
Check((await client.GetAsync("/v1/br1/profile?riotId=sem-tag")).StatusCode==HttpStatusCode.BadRequest,"Riot ID inválido devolve 400");
Check((await client.GetAsync("/v1/br1/profile?riotId=Ausente%23BR1")).StatusCode==HttpStatusCode.NotFound,"conta inexistente devolve 404");
var limit=await client.GetAsync("/v1/br1/profile?riotId=Limite%23BR1");
Check(limit.StatusCode==HttpStatusCode.TooManyRequests && limit.Headers.RetryAfter?.Delta?.TotalSeconds==30,"propaga Retry-After");
var before=upstream.Count;
Check((await client.GetAsync("/v1/br1/profile?riotId=Outro%23BR1")).StatusCode==HttpStatusCode.TooManyRequests && before==upstream.Count,"respeita o bloqueio da Riot entre consultas");
var samples=app.Services.GetRequiredService<RegionalSamples>();
for(int i=0;i<50;i++){
    int player=i%10;
    var players=Enumerable.Range(0,10).Select(j=>new RiotProfiles.Player(j==0?$"bard-{player}":$"other-{j}",j==0?432:j,false,
        j==0?player+1:0,0,0,0,0,null,j<5?100:200,false)).ToArray();
    var info=new RiotProfiles.GameInfo(100+i,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),1800,"MATCHED_GAME","CLASSIC",420,players);
    var sample=new RiotProfiles.Match(new($"BR1_{100+i}"),info);
    samples.Record("BR1",100+i,sample);samples.Record("BR1",100+i,sample);
    samples.Record("KR",100+i,sample);
    samples.Record("BR1",300+i,new(new($"BR1_{300+i}"),info with{GameId=300+i,QueueId=450}));
}
var comparison=await client.GetFromJsonAsync<RegionalComparison>("/v1/comparisons/BR1/149/100/432");
Check(comparison is {Kda:10,Average:5,Matches:45,PlayerGames:5,RankedPlayers:10,PlayerRank:1}
    && Math.Abs(comparison.TopPercent!.Value-100d/46)<0.0001,"comparação por fila sem duplicatas, mortes zero, percentil e posição por jogador");
await app.StopAsync();
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
dataDir.Delete(true);

sealed class RiotFixture:HttpMessageHandler
{
    public int Count;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){
        Count++;
        if(req.Headers.GetValues("X-Riot-Token").Single()!="fixture")throw new Exception("chave não foi enviada no header");
        string path=req.RequestUri!.AbsolutePath;
        bool regional=path.StartsWith("/riot/") || path.Contains("/match/v5/");
        if(req.RequestUri.Host!=(regional?"americas":"br1")+".api.riotgames.com")throw new Exception("rota incorreta");
        HttpResponseMessage Reply(object o)=>new(HttpStatusCode.OK){Content=JsonContent.Create(o)};
        if(path.Contains("/Ausente/"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        if(path.Contains("/Limite/")){
            var r=new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter=new(TimeSpan.FromSeconds(30));return Task.FromResult(r);
        }
        if(path.EndsWith("/Teste%20Um/BR1"))return Task.FromResult(Reply(new{puuid="owner",gameName="Teste Um",tagLine="BR1"}));
        if(path=="/lol/summoner/v4/summoners/by-puuid/owner")return Task.FromResult(Reply(new{summonerLevel=42,profileIconId=1}));
        if(path=="/lol/league/v4/entries/by-puuid/owner")return Task.FromResult(Reply(new[]{new{queueType="RANKED_SOLO_5x5",tier="GOLD",rank="II",leaguePoints=55,wins=12,losses=10}}));
        if(path=="/lol/match/v5/matches/by-puuid/owner/ids")return Task.FromResult(Reply(new[]{"BR1_1","BR1_2","BR1_3","BR1_4"}));
        string match=path.Split('/').Last();
        long id=long.Parse(match.Split('_')[1]);
        var participants=new[]{new{puuid=id==3?"other":"owner",championId=63,win=true,kills=3,deaths=2,assists=6,totalMinionsKilled=20,neutralMinionsKilled=2}};
        return Task.FromResult(Reply(new{metadata=new{matchId=match},info=new{gameId=id,gameCreation=1700000000000L,gameType=id==2?"CUSTOM_GAME":"MATCHED_GAME",
            queueId=id==4?9999:420,gameDuration=1800,gameMode="CLASSIC",participants}}));
    }
}
