using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TaxWise.Rag.Voyage;

/// <summary>
/// Typed HttpClient for the Voyage AI REST API (no official .NET SDK). Shapes checked against
/// https://docs.voyageai.com/reference/embeddings-api and /reranker-api on 2026-11-10.
/// Retries on 429/5xx are added by the resilience handler registered in <see cref="RagServiceCollectionExtensions"/>.
/// </summary>
public sealed class VoyageClient(HttpClient http, IOptions<VoyageOptions> options) : IVoyageClient
{
    private readonly VoyageOptions _options = options.Value;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, VoyageInputType inputType, CancellationToken ct = default)
    {
        var result = new List<float[]>(texts.Count);
        var batchSize = Math.Max(1, _options.EmbedBatchSize);
        for (var start = 0; start < texts.Count; start += batchSize)
        {
            var batch = texts.Skip(start).Take(batchSize).ToList();
            var request = new EmbedRequest(batch, _options.EmbedModel, inputType == VoyageInputType.Query ? "query" : "document");
            var response = await PostAsync<EmbedRequest, EmbedResponse>("embeddings", request, ct);
            if (response.Data.Count != batch.Count)
                throw new VoyageApiException(200, $"Voyage returned {response.Data.Count} embeddings for {batch.Count} inputs.");
            result.AddRange(response.Data.OrderBy(d => d.Index).Select(d => d.Embedding));
        }
        return result;
    }

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IReadOnlyList<string> documents, int? topK = null, CancellationToken ct = default)
    {
        if (documents.Count == 0) return [];
        var request = new RerankRequest(query, documents, _options.RerankModel, topK);
        var response = await PostAsync<RerankRequest, RerankResponse>("rerank", request, ct);
        return response.Data
            .Select(d => new RerankResult(d.Index, d.RelevanceScore))
            .OrderByDescending(r => r.RelevanceScore)
            .ToList();
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Voyage:ApiKey is not configured. Set it with `dotnet user-secrets set Voyage:ApiKey <key>`.");

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // The error body never contains the key; safe to surface for debugging.
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new VoyageApiException((int)response.StatusCode, $"Voyage {path} failed ({(int)response.StatusCode}): {Truncate(detail, 300)}");
        }
        return await response.Content.ReadFromJsonAsync<TResponse>(ct)
               ?? throw new VoyageApiException((int)response.StatusCode, $"Voyage {path} returned an empty body.");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed record EmbedRequest(
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType);

    private sealed record EmbedResponse([property: JsonPropertyName("data")] List<EmbedData> Data);

    private sealed record EmbedData(
        [property: JsonPropertyName("embedding")] float[] Embedding,
        [property: JsonPropertyName("index")] int Index);

    private sealed record RerankRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("top_k"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TopK);

    private sealed record RerankResponse([property: JsonPropertyName("data")] List<RerankData> Data);

    private sealed record RerankData(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] double RelevanceScore);
}
