using Bardo.Api;

if (args is ["--import-local"])
{
    var builder = WebApplication.CreateBuilder();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await LocalHistoryImport.Run(new RegionalSamples(builder.Configuration), timeout.Token);
    return;
}
ApiHost.Build(WebApplication.CreateBuilder(args)).Run();
