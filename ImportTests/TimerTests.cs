using NexoLoL.Core;

static class TimerTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test){
        void Check(bool ok){if(!ok)throw new Exception("condição não satisfeita");}
        await test("contador de cura só inicia com marcação do usuário",()=>{var t=new ManualRelicTimers();Check(t.Remaining(0,100) is null);t.Mark(0,100,90);Check(t.Remaining(0,130)==60);Check(t.Remaining(1,130) is null);Check(t.Remaining(0,200)==0);return Task.CompletedTask;});
        await test("contador usa tempo da partida e pode reiniciar",()=>{var t=new ManualRelicTimers();t.Mark(0,100,90);Check(t.Remaining(0,100)==90);t.Mark(0,110,120);Check(t.Remaining(0,120)==110);t.Reset();Check(t.Remaining(0,120) is null);return Task.CompletedTask;});
        await test("configuração de cronômetros recusa troca de conta antes da escrita",async()=>{using var api=new FakeApi();api.OnRead=p=>{if(p=="/lol-game-settings/v1/game-settings")api.Owner="other";};bool failed=false;try{await NativeTimerSettings.ConfigureAsync(api,true);}catch(InvalidDataException){failed=true;}Check(failed&&api.Writes.Count==0);});
        await test("configuração não escreve se partida começar durante a consulta",async()=>{using var api=new FakeApi();api.OnRead=p=>{if(p=="/lol-game-settings/v1/game-settings")api.Phase="InProgress";};bool failed=false;try{await NativeTimerSettings.ConfigureAsync(api,true);}catch(InvalidDataException){failed=true;}Check(failed&&api.Writes.Count==0);});
        await test("parser extrai par observado no campo sums.ids",()=>{var q=new BuildQuery(26,"zilean",GameMode.Rift,"support",Patch:"16.23");var d=BuildParsers.ParseLolalytics(BuildFixture.Html(),q);Check(d.Variants[0].SummonerSpells!.SequenceEqual(new[]{4,7}));return Task.CompletedTask;});
        await test("cache legado é renovado quando a importação exige feitiços",async()=>{
            string dir=Path.Combine(Path.GetTempPath(),"bardo-spell-cache-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var q=new BuildQuery(26,"zilean",GameMode.Rift,"support",Patch:"16.23");var doc=BuildParsers.ParseLolalytics(BuildFixture.Html(),q);doc=doc with{Variants=doc.Variants.Select(v=>v with{SummonerSpells=null}).ToList()};
                string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("schema2:"+q.CacheKey)));string path=Path.Combine(dir,hash+".json");File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(doc));int calls=0;var service=new BuildService(dir,(_,_)=>{calls++;return Task.FromResult(BuildFixture.Html());});
                Check((await service.GetAsync(q)).FromCache&&calls==0);var loaded=await service.GetAsync(q,requireSpells:true);Check(!loaded.FromCache&&calls==1&&loaded.Document.Variants[0].SummonerSpells!.Count==2);
                File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(doc));var offline=new BuildService(dir,(_,_)=>throw new HttpRequestException("offline"));var fallback=await offline.GetAsync(q,requireSpells:true);Check(fallback.Stale&&fallback.Document.Variants[0].Core.Count>0&&fallback.Document.Variants[0].SummonerSpells is null);
            }finally{Directory.Delete(dir,true);}
        });
        await test("Desordem extrai somente primeiro par da seção de feitiços",()=>{string html=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/mayhem-zilean-factual.html"))+"<h3>Summoner spells</h3><ul><li><img src='/img/spell/SummonerFlash.png'><img src='/img/spell/SummonerMana.png'></li><li><img src='/img/spell/SummonerFlash.png'><img src='/img/spell/SummonerHaste.png'></li></ul><h3>Skill order</h3>";var d=BuildParsers.ParseMayhem(html,new(26,"zilean",GameMode.Mayhem,Patch:"16.23"));Check(d.Variants[0].SummonerSpells!.SequenceEqual(new[]{4,13}));return Task.CompletedTask;});
    }
}
