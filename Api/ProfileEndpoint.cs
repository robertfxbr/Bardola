using System.Globalization;
using System.Net;
using System.Text.Json;
using NexoLoL.Core;

namespace Bardo.Api;

public static class ProfileEndpoint
{
    public static async Task<IResult> Lookup(HttpContext context,RiotProfiles riot,IConfiguration config,string? riotId)
    {
        if(!RiotId.TrySplit(riotId,out var name,out var tag))
            return Results.BadRequest(new{error="Use o formato Nome#TAG."});
        var key=config["RIOT_API_KEY"];
        if(string.IsNullOrWhiteSpace(key))return Results.StatusCode(503);
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(16));
        try{return Results.Ok(await riot.Lookup(name,tag,key,budget.Token));}
        catch(RiotCooldown e){
            context.Response.Headers.RetryAfter=e.Seconds.ToString(CultureInfo.InvariantCulture);
            return Results.StatusCode(429);
        }
        catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.NotFound){return Results.NotFound();}
        catch(HttpRequestException e) when(e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized){return Results.StatusCode(503);}
        catch(OperationCanceledException) when(!context.RequestAborted.IsCancellationRequested){return Results.StatusCode(504);}
        catch(Exception e) when(e is HttpRequestException or InvalidDataException or JsonException){return Results.StatusCode(502);}
    }
}
