using System.Text.Json;

static class BuildFixture
{
    public static string Html(int champion=26,string slug="zilean",bool determination=false)
    {
        var pool=new List<object?>();
        string Add(object? value){int n=pool.Count;pool.Add(value);string text="";do{text="0123456789abcdefghijklmnopqrstuvwxyz"[n%36]+text;n/=36;}while(n>0);return text;}
        Dictionary<string,object?> Obj(params object?[] values){var d=new Dictionary<string,object?>();for(int i=0;i<values.Length;i+=2)d[(string)values[i]!]=values[i+1];return d;}
        object? Encode(object? value){if(value is Dictionary<string,object?> d)return d.ToDictionary(x=>x.Key,x=>Add(Encode(x.Value)));if(value is object?[] a)return a.Select(x=>Add(Encode(x))).ToArray();return value;}
        var variant=Obj("sums",Obj("ids",new object?[]{4,7}),"items",Obj("start",Obj("set",new object?[]{2003,2003}),"core",Obj("set",new object?[]{6655,3158},"n",1000,"wr",53.0)),"skillpriority",Obj("id","QEW"),"runes",Obj("set",Obj("pri",determination?new object?[]{8465,8463,8473,8242}:new object?[]{8214,8226,8210,8232},"sec",new object?[]{8105,8126},"mod",new object?[]{5007,5008,5001})));
        var root=Obj("header",Obj("cid",champion,"queue",420,"patch","16.23","lane","support","wr",53.0,"n",10000),"summary",Obj("pick",variant,"win",variant));pool.Add(Encode(root));
        pool.Add(Encode(Obj("path","/lol/"+slug+"/build/","cid",champion,"champion",slug,"mode","ranked","patch","16.23","lane","support","tier","emerald_plus")));
        return "<script type=\"qwik/json\">"+JsonSerializer.Serialize(new{objs=pool})+"</script>";
    }
}
