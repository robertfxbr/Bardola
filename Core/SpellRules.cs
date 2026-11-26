namespace NexoLoL.Core;

public static class SpellRules
{
    static readonly Dictionary<string,int> Keys=new(){["SummonerBoost"]=1,["SummonerExhaust"]=3,["SummonerFlash"]=4,["SummonerHaste"]=6,["SummonerHeal"]=7,["SummonerSmite"]=11,["SummonerTeleport"]=12,["SummonerMana"]=13,["SummonerDot"]=14,["SummonerBarrier"]=21,["SummonerSnowball"]=32};
    public static int Id(string key)=>Keys.GetValueOrDefault(key);
    public static int[] Order(IReadOnlyList<int>? ids,GameMode mode,bool flashOnLeft){
        if(ids is not {Count:2}||ids[0]==ids[1]||ids.Any(id=>!Keys.ContainsValue(id))||
           (mode==GameMode.Rift&&ids.Any(id=>id is 13 or 32))||(mode!=GameMode.Rift&&ids.Any(id=>id is 11 or 12)))
            throw new InvalidDataException("A fonte não forneceu dois feitiços válidos para este modo. Seus feitiços foram preservados.");
        int[] pair=[..ids];if(pair.Contains(4)&&pair[flashOnLeft?0:1]!=4)Array.Reverse(pair);return pair;
    }
    public static string Name(int id)=>id switch{1=>"Purificar",3=>"Exaustão",4=>"Flash",6=>"Fantasma",7=>"Curar",11=>"Golpear",12=>"Teleporte",13=>"Clareza",14=>"Incendiar",21=>"Barreira",32=>"Marca",_=>"Feitiço "+id};
}
