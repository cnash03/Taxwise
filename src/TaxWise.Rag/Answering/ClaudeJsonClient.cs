using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxWise.Rag.Answering;

/// <summary><see cref="Json"/> is null when <see cref="Refused"/> (stop_reason "refusal": no content is read).</summary>
public sealed record ClaudeJsonResult(string? Json, bool Refused, ModelCallInfo Call);

/// <summary>
/// One Claude call with structured JSON output (not native citations: we validate our own passage IDs, and native
/// citations can't be combined with <c>output_config.format</c>). Shared by the chat answerer and the form explainer.
/// BUILD_PLAN §5.1 "Claude call".
/// </summary>
public sealed class ClaudeJsonClient(AnthropicClient client, IOptions<AnthropicOptions> options, ILogger<ClaudeJsonClient> logger)
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    private readonly AnthropicOptions _options = options.Value;

    /// <param name="system">Byte-stable per (year, mode); a cache breakpoint goes after it (BUILD_PLAN §10).</param>
    public async Task<ClaudeJsonResult> CallAsync(string system, string user, IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken ct = default)
    {
        var parameters = BuildParams(system, user, schema);
        var sw = Stopwatch.StartNew();
        var response = await CreateWithOneRetryAsync(parameters, ct);
        sw.Stop();

        var stopReason = response.StopReason?.Raw() ?? "unknown";
        var call = new ModelCallInfo(response.Model.Raw(), sw.ElapsedMilliseconds, response.Usage.InputTokens,
            response.Usage.OutputTokens, response.Usage.CacheReadInputTokens ?? 0, stopReason);

        // Check the stop reason before reading content: a refusal carries no usable answer.
        if (stopReason == "refusal")
            return new ClaudeJsonResult(null, true, call);

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        return new ClaudeJsonResult(json, false, call);
    }

    /// <summary>Deserializes the JSON text or throws a user-safe <see cref="AnswererException"/>.</summary>
    public T Parse<T>(ClaudeJsonResult result) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(result.Json ?? "") ?? throw new JsonException("empty");
        }
        catch (JsonException e)
        {
            // Usually stop_reason max_tokens cut the JSON off. Don't log the body: it can echo the question.
            logger.LogWarning("Claude returned unparseable JSON (stop={StopReason}, outputTokens={OutputTokens}): {Error}",
                result.Call.StopReason, result.Call.OutputTokens, e.Message);
            throw new AnswererException(AnswererFailure.Unavailable, "The answer service returned an unreadable response. Please try again.", e);
        }
    }

    private MessageCreateParams BuildParams(string system, string user, IReadOnlyDictionary<string, JsonElement> schema)
    {
        var p = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = new List<BetaTextBlockParam>
            {
                new() { Text = system, CacheControl = new BetaCacheControlEphemeral() },
            },
            Messages = [new BetaUserMessageParam { Content = user }],
            // No Thinking config: claude-opus-5-5 (prod) and claude-sonnet-5-5 (dev) both run adaptive when it's omitted.
            OutputConfig = new BetaOutputConfig
            {
                Effort = ParseEffort(_options.Effort),
                Format = new BetaJsonOutputFormat { Schema = schema.ToDictionary() },
            },
        };
        if (_options.UseRefusalFallback)
        {
            // A policy decline is re-served by a fallback model chosen by refusal category, inside the same call.
            p = p with { Betas = [FallbackBeta], Fallbacks = new Default() };
        }
        return p;
    }

    /// <summary>Rate limit → fail fast; 5xx/connection → retry once; any other API error → fail with a safe message.</summary>
    private async Task<BetaMessage> CreateWithOneRetryAsync(MessageCreateParams parameters, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.Beta.Messages.Create(parameters, ct);
            }
            catch (AnthropicRateLimitException e)
            {
                logger.LogWarning("Claude rate limited (429)");
                throw new AnswererException(AnswererFailure.RateLimited, "TaxWise is busy right now. Please try again in a minute.", e);
            }
            catch (Exception e) when (e is Anthropic5xxException or AnthropicIOException && attempt == 1)
            {
                logger.LogWarning("Claude call failed ({Error}); retrying once", e.GetType().Name);
            }
            catch (Exception e) when (e is Anthropic5xxException or AnthropicIOException)
            {
                logger.LogError("Claude call failed again ({Error})", e.GetType().Name);
                throw new AnswererException(AnswererFailure.Unavailable, "The answer service is unavailable. Please try again.", e);
            }
            catch (AnthropicApiException e)
            {
                // The SDK's message is the API's error text; it never contains the key.
                logger.LogError("Claude API error {Type}: {Message}", e.GetType().Name, e.Message);
                throw new AnswererException(AnswererFailure.Unavailable, "The answer service couldn't handle that request.", e);
            }
        }
    }

    private static Effort ParseEffort(string effort) => effort.Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => throw new InvalidOperationException($"Anthropic:Effort must be low, medium, high, xhigh or max (got '{effort}')."),
    };
}
