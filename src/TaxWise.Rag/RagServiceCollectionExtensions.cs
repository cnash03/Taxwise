using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Qdrant.Client;
using TaxWise.Rag.VectorStore;
using TaxWise.Rag.Voyage;

namespace TaxWise.Rag;

public static class RagServiceCollectionExtensions
{
    /// <summary>Registers the Voyage client (with retry) and the Qdrant chunk store from configuration.</summary>
    public static IServiceCollection AddTaxWiseRag(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<VoyageOptions>(configuration.GetSection(VoyageOptions.SectionName));
        services.Configure<QdrantOptions>(configuration.GetSection(QdrantOptions.SectionName));

        services.AddVoyageClient();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(o.Host, o.Port, o.UseHttps, string.IsNullOrWhiteSpace(o.ApiKey) ? null : o.ApiKey);
        });
        services.AddSingleton<IChunkStore, QdrantChunkStore>();
        return services;
    }

    /// <summary>
    /// Typed Voyage HttpClient with a jittered exponential retry (3 tries) on 408/429/5xx and network
    /// errors, honouring Retry-After (BUILD_PLAN §4.2 step 6, §10). Returns the builder so tests can swap the handler.
    /// </summary>
    public static IHttpClientBuilder AddVoyageClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient<IVoyageClient, VoyageClient>((sp, http) =>
        {
            var o = sp.GetRequiredService<IOptions<VoyageOptions>>().Value;
            http.BaseAddress = new Uri(o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/");
            http.Timeout = TimeSpan.FromMinutes(2);
        });
        builder.AddResilienceHandler("voyage-retry", (pipeline, context) =>
        {
            var o = context.ServiceProvider.GetRequiredService<IOptions<VoyageOptions>>().Value;
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = o.RetryBaseDelay,
            });
        });
        return builder;
    }
}
