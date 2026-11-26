using NexoLoL.Core;

static class SpellTests
{
    public static async Task Run(Func<string,Func<Task>,Task> test,BuildDocument source,Func<ClientImporter> importer)
    {
        void Check(bool ok){if(!ok)throw new Exception("condição não satisfeita");}
        var doc=source with{Variants=[source.Variants[0] with{SummonerSpells=[4,7]}]};
        await test("feitiços da build são confirmados com Flash no D",async()=>{using var api=new FakeApi();var r=await importer().ApplyAsync(api,doc,0,new(Items:false,Runes:false,Spells:true));Check(r.Success&&api.Spell1==4&&api.Spell2==7&&r.Spells.Contains("confirmados"));Check(api.Writes.Single().Method==HttpMethod.Patch);});
        await test("Flash no F inverte somente a posição do par recomendado",async()=>{using var api=new FakeApi();var r=await importer().ApplyAsync(api,doc,0,new(Items:false,Runes:false,Spells:true,FlashOnLeft:false));Check(r.Success&&api.Spell1==7&&api.Spell2==4);});
        await test("build sem Flash preserva os dois feitiços recomendados",async()=>{using var api=new FakeApi();var d=doc with{Variants=[doc.Variants[0] with{SummonerSpells=[6,14]}]};Check((await importer().ApplyAsync(api,d,0,new(Items:false,Runes:false,Spells:true))).Success);Check(api.Spell1==6&&api.Spell2==14);});
        await test("recomendação ausente ou incompatível não altera feitiços",async()=>{foreach(var pair in new List<int>?[]{null,[4,4],[4,32],[4,99999]}){using var api=new FakeApi();var d=doc with{Variants=[doc.Variants[0] with{SummonerSpells=pair}]};Check(!(await importer().ApplyAsync(api,d,0,new(Items:false,Runes:false,Spells:true))).Success);Check(api.Writes.Count==0);}});
        await test("troca de conta durante preparo de feitiços cancela escrita",async()=>{using var api=new FakeApi();int reads=0;api.OnRead=p=>{if(p=="/lol-summoner/v1/current-summoner"&&++reads==2)api.Owner="other";};Check(!(await importer().ApplyAsync(api,doc,0,new(Items:false,Runes:false,Spells:true))).Success);Check(api.Writes.Count==0);});
        await test("sem confirmação de feitiços o resultado não é sucesso",async()=>{using var api=new FakeApi{IgnoreWrites=true};Check(!(await importer().ApplyAsync(api,doc,0,new(Items:false,Runes:false,Spells:true))).Success);});
        await test("cronômetros nativos preservam as outras preferências",async()=>{using var api=new FakeApi();await NativeTimerSettings.ConfigureAsync(api,true);Check(api.NativeTimers);Check(api.MinimapScale==1.65);Check(api.Writes.Single().Body!.AsObject().Count==1);});
        await test("cronômetros já ativados não repetem escrita",async()=>{using var api=new FakeApi{NativeTimers=true};await NativeTimerSettings.ConfigureAsync(api,true);Check(api.Writes.Count==0);});
        await test("cronômetros esperam fim de partida antes de mudar configuração",async()=>{using var api=new FakeApi{Phase="InProgress"};bool wait=false;try{await NativeTimerSettings.ConfigureAsync(api,true);}catch(InvalidDataException){wait=true;}Check(wait&&api.Writes.Count==0);});
        await test("falha de confirmação de cronômetros é informada",async()=>{using var api=new FakeApi{IgnoreWrites=true};bool fail=false;try{await NativeTimerSettings.ConfigureAsync(api,true);}catch(InvalidDataException){fail=true;}Check(fail);});
    }
}
