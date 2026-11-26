using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace NexoLoL.Core;

// Credenciais efêmeras da sessão local. Nunca serializar esta classe.
public sealed class LeagueClient : ILeagueApi
{
    readonly HttpClient client;
    readonly string authorization;
    readonly Uri origin;
    LeagueClient(int port,string secret)
    {
        origin=new Uri($"https://127.0.0.1:{port}");authorization=Convert.ToBase64String(Encoding.UTF8.GetBytes("riot:"+secret));
        client=new(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,
            ServerCertificateCustomValidationCallback=(request,_,_,_)=>request.RequestUri is {IsLoopback:true,Scheme:"https"} uri && uri.Port==port})
            {Timeout=TimeSpan.FromSeconds(4),MaxResponseContentBufferSize=8_000_000};
    }
    public static LeagueClient Open()
    {
        foreach(string folder in new[]{@"C:\Riot Games\League of Legends",@"D:\Riot Games\League of Legends",@"E:\Riot Games\League of Legends"}) {
            try {
                using var stream=new FileStream(Path.Combine(folder,"lockfile"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
                using var reader=new StreamReader(stream);var fields=reader.ReadToEnd().Split(':');
                if(fields.Length==5 && int.TryParse(fields[2],out int port) && port is >0 and <=65535 && fields[3].Length>0 && fields[4].Trim()=="https")return new(port,fields[3]);
            }catch(Exception e) when(e is IOException or UnauthorizedAccessException) {}
        }
        throw new InvalidDataException("Abra o cliente do League of Legends, entre na sua conta e tente conectar novamente.");
    }
    public Task<JsonNode?> GetAsync(string path,CancellationToken ct=default)=>SendAsync(HttpMethod.Get,path,null,ct);
    public async Task<JsonNode?> SendAsync(HttpMethod method,string path,JsonNode? body,CancellationToken ct=default)
    {
        if(!path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\'))throw new ArgumentException("Endpoint local inválido.");
        var uri=new Uri(origin,path);if(uri.Host!=origin.Host || uri.Port!=origin.Port || uri.Scheme!="https")throw new ArgumentException("A conexão deve permanecer local.");
        using var message=new HttpRequestMessage(method,uri);message.Headers.Authorization=new AuthenticationHeaderValue("Basic",authorization);
        if(body is not null)message.Content=new StringContent(body.ToJsonString(),Encoding.UTF8,"application/json");
        using var response=await client.SendAsync(message,ct);response.EnsureSuccessStatusCode();string content=await response.Content.ReadAsStringAsync(ct);return string.IsNullOrWhiteSpace(content)?null:JsonNode.Parse(content);
    }
    public void Dispose()=>client.Dispose();
}
