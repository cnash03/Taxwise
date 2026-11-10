using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxWise.Ingestion;
using TaxWise.Rag;
using TaxWise.Rag.Voyage;

// Ingestion pipeline: download → parse → chunk → embed → upsert. See docs/BUILD_PLAN.md §4.
IngestionArgs options;
try { options = IngestionArgs.Parse(args); }
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddUserSecrets<IngestionPipeline>(optional: true);
builder.Configuration.AddEnvironmentVariables();
var overrides = new Dictionary<string, string?>();
if (options.QdrantHost is not null) overrides["Qdrant:Host"] = options.QdrantHost;
if (options.QdrantApiKey is not null)
{
    overrides["Qdrant:ApiKey"] = options.QdrantApiKey;
    overrides["Qdrant:UseHttps"] = "true";
}
builder.Configuration.AddInMemoryCollection(overrides);

builder.Services.AddTaxWiseRag(builder.Configuration);
builder.Services.AddHttpClient<SourceDownloader>(http =>
{
    http.DefaultRequestHeaders.UserAgent.ParseAdd("TaxWise-Ingestion/1.0 (student project)");
    http.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddTransient<IngestionPipeline>();
builder.Services.AddTransient<SpotCheck>();

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var exitCode = 0;
    if (!options.SpotCheckOnly)
        exitCode = await host.Services.GetRequiredService<IngestionPipeline>().RunAsync(options, cts.Token);
    if (exitCode == 0 && options.SpotCheck && !options.DryRun)
        exitCode = await host.Services.GetRequiredService<SpotCheck>().RunAsync(cts.Token);
    return exitCode;
}
catch (Exception e) when (e is VoyageApiException or InvalidOperationException or Grpc.Core.RpcException)
{
    // Voyage/Qdrant/config problems: print the reason, not a stack trace. Messages never contain keys.
    Console.Error.WriteLine($"Ingestion failed: {e.Message}");
    return 1;
}
