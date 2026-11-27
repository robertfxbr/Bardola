using System.Text.Json;
using NexoLoL.Core;

int passed = 0, failed = 0;
void Test(string name, Action body) { try { body(); passed++; Console.WriteLine($"PASS {name}"); } catch(Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
void Equal<T>(T want, T actual) { if (!EqualityComparer<T>.Default.Equals(want,actual)) throw new Exception($"esperado {want}; recebido {actual}"); }
void True(bool value) { if(!value) throw new Exception("condição falsa"); }
void Reject(Action action) { try { action(); } catch(InvalidDataException) { return; } throw new Exception("dados incompatíveis foram aceitos"); }
var q = new BuildQuery(26,"zilean",GameMode.Rift,Patch:"16.23");
// Fixture pequena com referências Qwik reais; números de itens/contagens são fatos, não conteúdo editorial.
var objs = new List<object?>();
string Add(object? v) { int n=objs.Count; objs.Add(v); const string chars="0123456789abcdefghijklmnopqrstuvwxyz"; string s=""; do {s=chars[n%36]+s;n/=36;}while(n>0);return s; }
object? Encode(object? v) {
    if(v is Dictionary<string,object?> d) return d.ToDictionary(x=>x.Key,x=>Add(Encode(x.Value)));
    if(v is object?[] a) return a.Select(x=>Add(Encode(x))).ToArray();
    return v;
}
Dictionary<string,object?> Obj(params object?[] pairs) { var d=new Dictionary<string,object?>(); for(int i=0;i<pairs.Length;i+=2)d[(string)pairs[i]!]=pairs[i+1];return d; }
var rune = Obj("set",Obj("pri",new object?[]{8214,8226,8210,8232},"sec",new object?[]{8105,8126},"mod",new object?[]{5007,5008,5001}));
var variant = Obj("skillpriority",Obj("id","QEW"),"skillorder",Obj("id",123114131343322L),"runes",rune,"items",Obj("start",Obj("set",new object?[]{3865,2003,2003}),"core",Obj("set",new object?[]{3876,3158,2065},"n",9209,"wr",54.98),"item4",new object?[]{Obj("id",2524,"n",10071,"wr",55.48)},"item5",new object?[]{Obj("id",2524)},"item6",new object?[]{Obj("id",3190)}));
var root=Obj("header",Obj("cid",26,"queue",420,"patch","16.23","lane","support","wr",53.13,"n",49788,"counters",Obj("strong",new object?[]{164,3,101},"weak",new object?[]{37,517,267})),"summary",Obj("pick",variant,"win",variant));
objs.Add(Encode(root));
objs.Add(Encode(Obj("path","/lol/zilean/build/","cid",26,"tier","emerald_plus","mode","ranked","patch","16.23","lane","all","champion","zilean")));
string html="<script type=\"qwik/json\">"+JsonSerializer.Serialize(new {objs})+"</script>";
Test("Qwik resolve IDs e agrupa poções repetidas",()=>{var d=BuildParsers.ParseLolalytics(html,q);Equal(2065,d.Variants[0].Core[2].Id);Equal(2,d.Variants[0].Start.Single(x=>x.Id==2003).Count);Equal(49788,d.Games);});
Test("runas pertencem às árvores corretas",()=>{var r=BuildParsers.ParseLolalytics(html,q).Variants[0].Runes!;Equal(8200,r.PrimaryStyle);Equal(8100,r.SecondaryStyle);Equal(9,r.Perks.Count);});
Test("prioridade e sequência não são confundidas",()=>{var v=BuildParsers.ParseLolalytics(html,q).Variants[0];Equal("QEW",v.SkillPriority);Equal("QWEQQRQEQEREEWW",v.SkillOrder);});
Test("rejeita outro campeão",()=>Reject(()=>BuildParsers.ParseLolalytics(html,q with {ChampionId=103})));
Test("rejeita ARAM servido como Rift",()=>Reject(()=>BuildParsers.ParseLolalytics(html,q with {Mode=GameMode.Aram})));
Test("rejeita patch anterior",()=>Reject(()=>BuildParsers.ParseLolalytics(html,q with {Patch="16.24"})));
Test("rejeita função diferente da solicitada",()=>Reject(()=>BuildParsers.ParseLolalytics(html,q with {Role="middle"})));
Test("não rotula dados Esmeralda como Mestre+",()=>Reject(()=>BuildParsers.ParseLolalytics(html,q with {Tier="master_plus"})));
Test("fonte alterada não inventa build",()=>Reject(()=>BuildParsers.ParseLolalytics("<html>manutenção</html>",q)));
string mayhem="<link rel=\"canonical\" href=\"https://lazyelo.com/aram-mayhem/builds/zilean\"/><title>Zilean ARAM Mayhem Build (Patch 26.23)</title><h4>Start</h4><ol><img src=\"https://ddragon.leagueoflegends.com/cdn/16.23.1/img/item/3802.png\"/></ol><h4>Core items, in buying order</h4><ol><img src=\"https://ddragon.leagueoflegends.com/cdn/16.23.1/img/item/6655.png\"/><img src=\"https://ddragon.leagueoflegends.com/cdn/16.23.1/img/item/3003.png\"/><img src=\"https://ddragon.leagueoflegends.com/cdn/16.23.1/img/item/3089.png\"/></ol><h4>Situational, after the core</h4><div><img src=\"/img/item/4645.png\"/></div><h2>Most bought items</h2>";
Test("Desordem usa build própria e não runas de outro modo",()=>{var d=BuildParsers.ParseMayhem(mayhem,q with {Mode=GameMode.Mayhem});Equal(6655,d.Variants[0].Core[0].Id);True(d.Variants[0].Runes is null);Equal("26.23",d.Patch);});
Test("Desordem rejeita página de outro campeão",()=>Reject(()=>BuildParsers.ParseMayhem(mayhem,q with {ChampionId=103,Slug="ahri",Mode=GameMode.Mayhem})));
Test("Desordem não passa como ARAM",()=>Reject(()=>BuildParsers.ParseMayhem(mayhem,q with {Mode=GameMode.Aram})));
Test("núcleo vazio deve causar erro em vez de tela de build vazia",()=>Reject(()=>BuildParsers.ParseMayhem(mayhem.Replace("Core items, in buying order","Mudou"),q with {Mode=GameMode.Mayhem})));
Test("Desordem não mistura botas alternativas com sequência principal",()=>{var page=mayhem.Replace("<h4>Situational", "<table><tr><td><img src=\"/img/item/3158.png\"/></td></tr><tr><td><img src=\"/img/item/3020.png\"/></td></tr></table><h4>Situational");var d=BuildParsers.ParseMayhem(page,q with{Mode=GameMode.Mayhem});Equal(3,d.Variants[0].Core.Count);});
string temp=Path.Combine(Path.GetTempPath(),"nexo-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
void AsyncTest(string name,Func<Task> body)=>Test(name,()=>body().GetAwaiter().GetResult());
AsyncTest("cache mantém build íntegra ao perder rede",async()=>{int calls=0;var service=new BuildService(temp,(_,_)=>++calls==1?Task.FromResult(html):Task.FromException<string>(new HttpRequestException("offline")));var live=await service.GetAsync(q);var cached=await service.GetAsync(q,true);True(!live.FromCache);True(cached.FromCache);True(cached.Stale);Equal(2065,cached.Document.Variants[0].Core[2].Id);});
AsyncTest("cache não troca campeão ao perder rede",async()=>{var service=new BuildService(temp,(_,_)=>Task.FromException<string>(new HttpRequestException("offline")));try{await service.GetAsync(q with{ChampionId=103,Slug="ahri"});}catch(HttpRequestException){return;}throw new Exception("cache de Zilean foi usado para Ahri");});
AsyncTest("cache não mistura modos",async()=>{var service=new BuildService(temp,(_,_)=>Task.FromException<string>(new HttpRequestException("offline")));try{await service.GetAsync(q with{Mode=GameMode.Aram});}catch(HttpRequestException){return;}throw new Exception("cache de Rift foi usado para ARAM");});
AsyncTest("cache fresco evita repetir consulta à fonte",async()=>{var service=new BuildService(temp,(_,_)=>throw new Exception("rede não deveria ser chamada"));True((await service.GetAsync(q)).FromCache);});
Test("item set tem mapa e contagem de poções corretos",()=>{var d=BuildParsers.ParseLolalytics(html,q);using var j=JsonDocument.Parse(ExportService.CreateItemSet(d,d.Variants[0]));Equal(11,j.RootElement.GetProperty("associatedMaps")[0].GetInt32());Equal(2,j.RootElement.GetProperty("blocks")[0].GetProperty("items")[1].GetProperty("count").GetInt32());Equal(26,j.RootElement.GetProperty("associatedChampions")[0].GetInt32());});
Test("item set de Desordem usa Howling Abyss",()=>{var d=BuildParsers.ParseMayhem(mayhem,q with{Mode=GameMode.Mayhem});using var j=JsonDocument.Parse(ExportService.CreateItemSet(d,d.Variants[0]));Equal(12,j.RootElement.GetProperty("associatedMaps")[0].GetInt32());});
Test("item set exporta escolhas de botas separadas",()=>{var d=BuildParsers.ParseMayhem(mayhem,q with{Mode=GameMode.Mayhem});var v=d.Variants[0] with{Boots=[new(3158),new(3020)]};using var j=JsonDocument.Parse(ExportService.CreateItemSet(d,v));True(j.RootElement.GetProperty("blocks").EnumerateArray().Any(b=>b.GetProperty("items").EnumerateArray().Any(i=>i.GetProperty("id").GetString()=="3158")));});
foreach(var file in Directory.EnumerateFiles(temp)){var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;node["Variants"]![0]!["Alternatives"]=null;File.WriteAllText(file,node.ToJsonString());}
AsyncTest("cache com coleção nula é reparado em vez de travar a UI",async()=>{var service=new BuildService(temp,(_,_)=>Task.FromResult(html));True(!(await service.GetAsync(q)).FromCache);});
foreach(var file in Directory.EnumerateFiles(temp)){var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;node["Variants"]![0]!["SkillPriority"]=null;File.WriteAllText(file,node.ToJsonString());}
AsyncTest("cache com habilidade nula é reparado antes de renderizar",async()=>{var service=new BuildService(temp,(_,_)=>Task.FromResult(html));True(!(await service.GetAsync(q)).FromCache);});
foreach(var file in Directory.EnumerateFiles(temp))File.WriteAllText(file,"{quebrou");
AsyncTest("cache corrompido é descartado e reparado pela rede",async()=>{var service=new BuildService(temp,(_,_)=>Task.FromResult(html));True(!(await service.GetAsync(q)).FromCache);});
Directory.Delete(temp,true);
Test("KIWI é Desordem, não ARAM comum",()=>Equal(GameMode.Mayhem,GameService.DetectMode("KIWI")!.Value));
Test("modo desconhecido não recebe build de outro modo",()=>True(GameService.DetectMode("CHERRY",30) is null));
var profile=System.Text.Json.Nodes.JsonNode.Parse("""{"gameName":"Jogador de teste","tagLine":"BR1","summonerLevel":82,"profileIconId":10,"puuid":"own-puuid","summonerId":42}""")!;
var ranked=System.Text.Json.Nodes.JsonNode.Parse("""{"queueMap":{"RANKED_SOLO_5x5":{"queueType":"RANKED_SOLO_5x5","tier":"GOLD","division":"II","leaguePoints":55,"wins":18,"losses":12}}}""")!;
var history=System.Text.Json.Nodes.JsonNode.Parse("""{"games":{"games":[{"gameId":123,"gameDuration":1800,"gameCreation":1795700000000,"gameMode":"CLASSIC","participantIdentities":[{"participantId":1,"player":{"puuid":"other"}},{"participantId":2,"player":{"puuid":"own-puuid"}}],"participants":[{"participantId":1,"championId":103,"stats":{"win":false,"kills":99}},{"participantId":2,"championId":26,"stats":{"win":true,"kills":4,"deaths":2,"assists":18,"totalMinionsKilled":100,"neutralMinionsKilled":5}}]}]}}""")!;
Test("conta mostra Riot ID e nível sem depender de senha",()=>{var account=AccountParser.Parse(profile,ranked,null,null);Equal("Jogador de teste#BR1",account.RiotId);Equal(82,account.Level);Equal("GOLD",account.Ranks[0].Tier);Equal(55,account.Ranks[0].Points);});
Test("histórico seleciona jogador dono da conta e não adversário",()=>{var account=AccountParser.Parse(profile,ranked,null,history);Equal(26,account.Matches[0].ChampionId);Equal(4,account.Matches[0].Kills);Equal(105,account.Matches[0].Cs);True(account.Matches[0].Win);});
Test("perfil indisponível não cria conexão fictícia",()=>Reject(()=>AccountParser.Parse(System.Text.Json.Nodes.JsonNode.Parse("{}")!,null,null,null)));
Test("histórico sem dono identificado não expõe outro jogador",()=>{var unknown=profile.DeepClone();unknown["puuid"]="unknown";unknown["summonerId"]=999;Equal(0,AccountParser.Parse(unknown,null,null,history).Matches.Count);});
Test("um participante com identidade alheia não pertence ao usuário",()=>{var other=history.DeepClone();var match=other["games"]!["games"]![0]!;(match["participants"] as System.Text.Json.Nodes.JsonArray)!.RemoveAt(1);(match["participantIdentities"] as System.Text.Json.Nodes.JsonArray)!.RemoveAt(1);Equal(0,AccountParser.Parse(profile,null,null,other).Matches.Count);});
Test("troca da conta ativa é detectada antes de mostrar os dados",()=>{var other=profile.DeepClone();other["puuid"]="other";Reject(()=>AccountParser.AssertSameAccount(profile,other));});
AsyncTest("limite de consulta também interrompe corpo travado após headers",async()=>{
    using var remote=new RemoteData(new FixedResponseHandler(new StreamContent(new WaitingStream())),TimeSpan.FromMilliseconds(50));
    using var safety=new CancellationTokenSource(500);
    try{await remote.BytesAsync("https://example.test/data",ct:safety.Token);}catch(TimeoutException){return;}
    throw new Exception("o corpo travado não respeitou o prazo");
});
AsyncTest("cancelamento solicitado não vira timeout nem usa cache",async()=>{
    using var remote=new RemoteData(new FixedResponseHandler(new StreamContent(new WaitingStream())),TimeSpan.FromSeconds(5));
    using var cancel=new CancellationTokenSource(20);
    try{await remote.BytesAsync("https://example.test/data",ct:cancel.Token);}catch(OperationCanceledException){return;}
    throw new Exception("cancelamento ignorado");
});
AsyncTest("resposta maior que limite não entra no parser",async()=>{
    using var remote=new RemoteData(new FixedResponseHandler(new ByteArrayContent(new byte[1024])));
    try{await remote.BytesAsync("https://example.test/data",100);}catch(InvalidDataException){return;}
    throw new Exception("resposta grande foi aceita");
});
if(Array.IndexOf(args,"--captured-shen") is int fixture && fixture>=0) {
    var capture=BuildParsers.ParseMayhem(File.ReadAllText(args[fixture+1]),new(98,"shen",GameMode.Mayhem,Patch:"16.23"));
    Test("Shen: notas por campeão no HTML publicado e dez itens mais comprados",()=>{
        True(capture.AugmentTiers!.Count>=150);Equal("D",capture.AugmentTiers["Empowered By The Faithful"]);
        Equal("S",capture.AugmentTiers["Mad Scientist"]);Equal(10,capture.Variants[0].FrequentItems!.Count);
    });
    Test("Shen: dois arsenais salvos conservam ordem de compra, itens repetidos e seis itens finais",()=>{
        var result=LocalArsenals.Include(new(capture,false,false,""),args[fixture+2]);
        Equal(3,result.Document.Variants.Count);
        var sizes=result.Document.Variants.Take(2).Select(v=>v.PurchaseOrder!.Count).Order().ToArray();
        True(sizes.SequenceEqual(new[]{8,11}));
        foreach(var v in result.Document.Variants.Take(2)){
            Equal(6,v.Core.Count);Equal(16,v.Alternatives.Count);
            using var bundle=JsonDocument.Parse(ExportService.CreateItemSet(capture,v));
            var order=bundle.RootElement.GetProperty("blocks")[1].GetProperty("items");
            True(order.EnumerateArray().Select(i=>int.Parse(i.GetProperty("id").GetString()!)).SequenceEqual(v.PurchaseOrder!.Select(i=>i.Id)));
        }
        True(LocalArsenals.DirectoryFor(args[fixture+2],capture.Query with{Mode=GameMode.Aram})!=LocalArsenals.DirectoryFor(args[fixture+2],capture.Query));
    });
}
if(args.Contains("--account-live")) {
    AsyncTest("LIVE conta do cliente LoL sem salvar credenciais",async()=>{var a=await new AccountService().ConnectAsync();True(a.RiotId.Length>0);True(a.Level>0);Console.WriteLine($"  perfil disponível, região {a.Region}, {a.Ranks.Count} filas ranqueadas, {a.Matches.Count} partidas");});
}
if(args.Contains("--live")) {
    string liveDir=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../live-cache"));
    using var remote=new RemoteData();var catalogService=new CatalogService(liveDir,remote);
    Catalog? catalog=null;
    AsyncTest("LIVE catálogo oficial em português",async()=>{catalog=await catalogService.LoadAsync();True(catalog.Champions.Count>170);True(catalog.Items.ContainsKey(6655));True(catalog.Spells.ContainsKey(4));Console.WriteLine($"  patch {catalog.Version}, {catalog.Champions.Count} campeões, {catalog.Items.Count} itens");});
    foreach(var mode in new[]{GameMode.Rift,GameMode.Aram,GameMode.Mayhem}) {
        AsyncTest("LIVE build Zilean "+mode,async()=>{
            var query=new BuildQuery(26,"zilean",mode,Tier:mode==GameMode.Rift?"emerald_plus":"all",Patch:catalog!.Patch);
            string page=await remote.BuildHtmlAsync(query,CancellationToken.None);
            var d=mode==GameMode.Mayhem?BuildParsers.ParseMayhem(page,query):BuildParsers.ParseLolalytics(page,query);
            True(d.Variants[0].Core.Count>=2);True(d.Variants.SelectMany(v=>v.Start.Concat(v.Core)).All(i=>catalog.Items.ContainsKey(i.Id)));
            True(SpellRules.Order(d.Variants[0].SummonerSpells,mode,true).All(id=>catalog.Spells.ContainsKey(id)));
            Console.WriteLine($"  {d.Source}, patch {d.Patch}, feitiços {string.Join(',',d.Variants[0].SummonerSpells!)}, core {string.Join(',',d.Variants[0].Core.Select(i=>i.Id))}");
        });
    }
    AsyncTest("LIVE Masters+ e função mid",async()=>{var query=new BuildQuery(103,"ahri",GameMode.Rift,"middle","master_plus",catalog!.Patch);var page=await remote.BuildHtmlAsync(query,CancellationToken.None);True(page.Contains("MASTER+",StringComparison.OrdinalIgnoreCase));var d=BuildParsers.ParseLolalytics(page,query);True(d.Variants[0].Core.Count>0);Console.WriteLine($"  Ahri {d.Role}, {d.Games} jogos");});
    using var game=new GameService();AsyncTest("LIVE detecção local sem alterar o jogo",async()=>{var snapshot=await game.PollAsync(catalog!);Console.WriteLine($"  {snapshot.Phase}, modo {snapshot.Mode}, campeão {snapshot.ChampionId}");});
}
Test("pós-jogo usa o participante e a equipe locais e separa proteção de autocura",()=>{
    var sample=System.Text.Json.Nodes.JsonNode.Parse("""
    {"gameId":42,"gameMode":"KIWI","gameLength":1245,"teams":[{"teamId":100,"isPlayerTeam":true,"isWinningTeam":false}],
    "localPlayer":{"isLocalPlayer":true,"puuid":"own","teamId":100,"championId":98,"items":[3084,3111,0],"stats":{
    "TOTAL_DAMAGE_DEALT_TO_CHAMPIONS":52694,"TOTAL_DAMAGE_TAKEN":106533,"TOTAL_HEAL":15247,
    "TOTAL_HEAL_ON_TEAMMATES":0,"TOTAL_DAMAGE_SHIELDED_ON_TEAMMATES":2865,"CHAMPIONS_KILLED":13,"NUM_DEATHS":12,"ASSISTS":37}}}
    """);
    var score=FinalScore.Parse(sample,"own",42)!;
    True(!score.Win && score.Duration==1245 && score.ChampionId==98 && score.AllyHealingAndShielding==2865 && score.ChampionDamage==52694);
    True(score.Items!.SequenceEqual(new[]{3084,3111}));
    True(FinalScore.Parse(sample,"other",42) is null && FinalScore.Parse(sample,"own",41) is null);
    sample!["localPlayer"]!["stats"]!.AsObject().Remove("TOTAL_HEAL_ON_TEAMMATES");
    True(FinalScore.Parse(sample,"own",42)!.AllyHealingAndShielding is null);
});
Test("placar associa jogadores por ID, conserva cartas escolhidas e bloqueia histórico de outra conta",()=>{
    var doc=System.Text.Json.Nodes.JsonNode.Parse("""
    {"gameId":10,"teams":[{"teamId":100,"win":"Fail"},{"teamId":200,"win":"Win"}],
    "participants":[{"participantId":2,"teamId":200,"championId":17,"stats":{"item0":3084,"kills":20}},
                    {"participantId":1,"teamId":100,"championId":98,"stats":{"playerAugment1":1053,"playerAugment2":1324,"playerAugment4":3134,"totalDamageDealtToChampions":52694}}],
    "participantIdentities":[{"participantId":1,"player":{"gameName":"Meu Shen","puuid":"self"}},{"participantId":2,"player":{"gameName":"Outro","puuid":"rival"}}]}
    """);
    var teams=MatchRoster.FromHistory(doc,"self",10);
    True(teams.Count==2 && teams[0].Win==false && teams[1].Win==true);
    var self=teams[0].Players.Single();True(self.Local && self.Name=="Meu Shen" && self.Damage==52694 && self.Cards.SequenceEqual(new[]{1053,1324,3134}));
    True(teams[1].Players.Single().Items.SequenceEqual(new[]{3084}));
    True(MatchRoster.FromHistory(doc,"other",10).Count==0 && MatchRoster.FromHistory(doc,"self",11).Count==0);
    var timeline=System.Text.Json.Nodes.JsonNode.Parse("""{"frames":[{"events":[{"timestamp":21000,"type":"CHAMPION_KILL","killerId":1,"victimId":2},{"timestamp":4000,"type":"BUILDING_KILL","buildingType":"TOWER_BUILDING"},{"type":"ITEM_SOLD","timestamp":100}]}]}""");
    var events=MatchTimeline.Read(timeline,doc,"self",10);
    True(events.Count==2 && events[0].Seconds==4 && events[1].KillerChampion==98 && events[1].VictimChampion==17);
    True(MatchTimeline.Read(timeline,doc,"other",10).Count==0);
    var end=System.Text.Json.Nodes.JsonNode.Parse("""
    {"teams":[{"teamId":100,"isWinningTeam":false,"players":[{"championId":98,"puuid":"self","items":[1,0,3],"stats":{"PLAYER_AUGMENT_4":3134,"NUM_DEATHS":12}}]}]}
    """)!;
    var player=MatchRoster.FromEnd(end,"self").Single().Players.Single();
    True(player.Local && player.Deaths==12 && player.Items.SequenceEqual(new[]{1,3}) && player.Cards.SequenceEqual(new[]{3134}));
});
Test("cartas traduzidas usam o mesmo ID e descartam títulos ambíguos",()=>{
    var names=CardTranslations.Pair("""[{"id":53,"nameTRA":"Mad Scientist"},{"id":40,"nameTRA":"Other"},{"id":41,"nameTRA":"Third"}]""",
        """[{"id":41,"nameTRA":"Igual"},{"id":53,"nameTRA":"Cientista Louco"},{"id":40,"nameTRA":"Igual"},{"id":100,"nameTRA":"Sem par"}]""");
    Equal(1,names.Count);Equal("Mad Scientist",names["Cientista Louco"]);
});
Console.WriteLine($"\n{passed} passed, {failed} failed");
Environment.ExitCode=failed>0?1:0;

sealed class FixedResponseHandler(HttpContent content) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=content});
}
sealed class WaitingStream : Stream
{
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
