using System.Net.Http;
using System.Text.Json.Nodes;
using NexoLoL.Core;

sealed class FakeApi : ILeagueApi
{
    public string Phase="ChampSelect",Mode="CLASSIC",Owner="own";public int Champion=26,Queue=420,CurrentId=1;public bool CanAdd=true;
    public bool IgnoreWrites {get;set;} public bool IgnoreRuneName {get;set;}
    public Action<string>? OnRead {get;set;}
    public Action<HttpMethod,string,JsonNode?>? OnWrite {get;set;}
    public JsonNode Sets=JsonNode.Parse("""{"accountId":42,"timestamp":123,"itemSets":[{"uid":"personal","title":"Meu conjunto","blocks":[]},{"uid":"blitz-keep","title":"Blitz Ahri","blocks":[]}]}""")!;
    public JsonArray Pages=JsonNode.Parse("""[{"id":1,"name":"Minha página","isEditable":true,"current":true,"primaryStyleId":8000,"subStyleId":8200,"selectedPerkIds":[8005,9111,9104,8014,8226,8210,5005,5008,5001]}]""")!.AsArray();
    public List<(HttpMethod Method,string Path,JsonNode? Body)> Writes=[];
    public Task<JsonNode?> GetAsync(string path,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();OnRead?.Invoke(path);
        JsonNode? value=path switch {
            "/lol-gameflow/v1/gameflow-phase"=>JsonValue.Create(Phase),
            "/lol-summoner/v1/current-summoner"=>new JsonObject{["puuid"]=Owner,["summonerId"]=42},
            "/lol-champ-select/v1/session"=>new JsonObject{["gameId"]=123,["localPlayerCellId"]=0,["myTeam"]=new JsonArray(new JsonObject{["cellId"]=0,["championId"]=Champion,["assignedPosition"]="utility"})},
            "/lol-gameflow/v1/session"=>new JsonObject{["gameData"]=new JsonObject{["gameMode"]=Mode,["queue"]=new JsonObject{["id"]=Queue}}},
            "/lol-perks/v1/pages"=>Pages,
            "/lol-perks/v1/currentpage"=>Pages.FirstOrDefault(p=>p?["id"]?.GetValue<int>()==CurrentId),
            "/lol-perks/v1/inventory"=>new JsonObject{["canAddCustomPage"]=CanAdd},
            _ when path.StartsWith("/lol-item-sets/v1/item-sets/")=>Sets,
            _=>throw new InvalidOperationException("GET inesperado "+path)
        };return Task.FromResult(value is null?null:JsonNode.Parse(value.ToJsonString()));
    }
    public Task<JsonNode?> SendAsync(HttpMethod method,string path,JsonNode? body,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();body=body is null?null:JsonNode.Parse(body.ToJsonString());Writes.Add((method,path,body?.DeepClone()));if(IgnoreWrites)return Task.FromResult<JsonNode?>(null);
        JsonNode? result=null;
        if(path.StartsWith("/lol-item-sets/"))Sets=body!.DeepClone();
        else if(path=="/lol-perks/v1/pages" && method==HttpMethod.Post){var p=body!.DeepClone();p["id"]=100;p["isEditable"]=true;Pages.Add(p);result=p;}
        else if(path=="/lol-perks/v1/currentpage")CurrentId=body!.GetValue<int>();
        else if(path.StartsWith("/lol-perks/v1/pages/") && method==HttpMethod.Put){int id=int.Parse(path.Split('/').Last());var old=Pages.First(p=>p!["id"]!.GetValue<int>()==id)!;var index=Pages.IndexOf(old);var p=body!.DeepClone();p["id"]=id;p["isEditable"]=true;if(IgnoreRuneName)p["name"]=old["name"]!.DeepClone();Pages[index]=p;result=p;}
        else throw new InvalidOperationException("WRITE inesperado "+path);
        OnWrite?.Invoke(method,path,body);return Task.FromResult(result?.DeepClone());
    }
    public void Dispose(){}
}
