using System.Net.Http.Headers;

namespace NexoLoL.Core;

public sealed class RemoteData : IDisposable
{
    readonly HttpClient http;
    readonly TimeSpan deadline;
    public RemoteData(HttpMessageHandler? handler=null,TimeSpan? timeout=null) {http=new(handler??new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan};deadline=timeout??TimeSpan.FromSeconds(18);http.DefaultRequestHeaders.UserAgent.ParseAdd("Bardola/0.3 (desktop companion)");}
    public async Task<byte[]> BytesAsync(string url,int max=4_000_000,CancellationToken ct=default)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(deadline);
        var token=budget.Token;
        try {
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>max)throw new InvalidDataException("Resposta da fonte excedeu o limite.");
        await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();byte[] buffer=new byte[16384];int n;
        while((n=await input.ReadAsync(buffer,token))>0) {if(output.Length+n>max)throw new InvalidDataException("Resposta da fonte excedeu o limite.");await output.WriteAsync(buffer.AsMemory(0,n),token);}
        return output.ToArray();
        }catch(OperationCanceledException e) when(!ct.IsCancellationRequested){throw new TimeoutException("A fonte demorou mais que o limite de consulta.",e);}
    }
    public async Task<string> TextAsync(string url,CancellationToken ct=default)=>System.Text.Encoding.UTF8.GetString(await BytesAsync(url,4_000_000,ct));
    public Task<string> BuildHtmlAsync(BuildQuery query,CancellationToken ct)=>TextAsync(BuildParsers.Url(query),ct);
    public void Dispose()=>http.Dispose();
}
