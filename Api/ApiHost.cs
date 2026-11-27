using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;


namespace Bardo.Api;

public static class ApiHost
{
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        if(string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
            builder.WebHost.UseUrls("http://127.0.0.1:5219");
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
        builder.Services.TryAddSingleton(_=>new HttpClient(new HttpClientHandler{AllowAutoRedirect=false})
            {Timeout=TimeSpan.FromSeconds(7),MaxResponseContentBufferSize=4_000_000});
        builder.Services.AddSingleton<RiotProfiles>();
        builder.Services.AddSingleton<RegionalSamples>();
        builder.Services.AddOutputCache(o=>o.AddPolicy("profiles",p=>p.Expire(TimeSpan.FromMinutes(3)).SetVaryByQuery("riotId")));
        builder.Services.AddRateLimiter(o=>{
            o.RejectionStatusCode=429;
            o.AddFixedWindowLimiter("lookups",policy=>{
                policy.PermitLimit=8;
                policy.Window=TimeSpan.FromMinutes(1);
                policy.QueueLimit=0;
            });
        });
        var app=builder.Build();
        app.UseRateLimiter();
        app.UseOutputCache();
        app.MapGet("/health",()=>Results.Ok(new{service="Bardola"}));
        app.MapGet("/v1/comparisons/{region}/{gameId:long}/{team:int}/{champion:int}",RegionalSamples.Lookup).RequireRateLimiting("lookups");
        app.MapGet("/v1/br1/profile",ProfileEndpoint.Lookup).RequireRateLimiting("lookups").CacheOutput("profiles");
        return app;
    }
}
