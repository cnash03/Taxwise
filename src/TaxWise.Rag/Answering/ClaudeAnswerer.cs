using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxWise.Core;
using TaxWise.Core.Guardrails;

namespace TaxWise.Rag.Answering;

/// <summary>
/// Calls Claude with structured JSON output (not native citations: we validate our own passage IDs, and native
/// citations can't be combined with <c>output_config.format</c>). BUILD_PLAN §5.1 "Claude call".
/// </summary>
public sealed class ClaudeAnswerer(AnthropicClient client, IOptions<AnthropicOptions> options, ILogger<ClaudeAnswerer> logger)
    : IAnswerer
{
    private const string FallbackBeta = "server-side-fallback-2026-07-01";
    private readonly AnthropicOptions _options = options.Value;

    public async Task<AnswererResult> AnswerAsync(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage,
        CancellationToken ct = default)
    {
        var parameters = BuildParams(question, passages, taxYear, plainLanguage);
        var sw = Stopwatch.StartNew();
        var response = await CreateWithOneRetryAsync(parameters, ct);
        sw.Stop();

        var stopReason = response.StopReason?.Raw() ?? "unknown";
        var call = new ModelCallInfo(response.Model.Raw(), sw.ElapsedMilliseconds, response.Usage.InputTokens,
            response.Usage.OutputTokens, response.Usage.CacheReadInputTokens ?? 0, stopReason);

        // Check the stop reason before reading content: a refusal carries no usable answer.
        if (stopReason == "refusal")
            return new AnswererResult(null, true, call);

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text));
        try
        {
            var draft = JsonSerializer.Deserialize<DraftAnswer>(json)
                        ?? throw new JsonException("empty");
            return new AnswererResult(draft with { Paragraphs = draft.Paragraphs ?? [] }, false, call);
        }
        catch (JsonException e)
        {
            // Usually stop_reason max_tokens cut the JSON off. Don't log the body: it can echo the question.
            logger.LogWarning("Claude returned unparseable JSON (stop={StopReason}, outputTokens={OutputTokens}): {Error}",
                stopReason, call.OutputTokens, e.Message);
            throw new AnswererException(AnswererFailure.Unavailable, "The answer service returned an unreadable response. Please try again.", e);
        }
    }

    private MessageCreateParams BuildParams(string question, IReadOnlyList<Chunk> passages, int taxYear, bool plainLanguage)
    {
        var p = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            // Byte-stable per (year, mode) with a cache breakpoint after it (BUILD_PLAN §10).
            System = new List<BetaTextBlockParam>
            {
                new() { Text = AnswerPrompt.SystemPrompt(taxYear, plainLanguage), CacheControl = new BetaCacheControlEphemeral() },
            },
            Messages = [new BetaUserMessageParam { Content = AnswerPrompt.UserMessage(question, passages) }],
            // No Thinking config: claude-opus-5-5 (prod) and claude-sonnet-5-5 (dev) both run adaptive when it's omitted.
            OutputConfig = new BetaOutputConfig
            {
                Effort = ParseEffort(_options.Effort),
                Format = new BetaJsonOutputFormat { Schema = AnswerPrompt.Schema.ToDictionary() },
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
