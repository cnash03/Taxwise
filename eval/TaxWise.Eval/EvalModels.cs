using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxWise.Rag.Answering;

namespace TaxWise.Eval;

public interface IJudge
{
    /// <param name="goldAnswer">Empty for an out-of-scope question: the judge then only tells "declined" from not.</param>
    Task<JudgeVerdict> JudgeAsync(string question, int taxYear, string goldAnswer, string candidate, CancellationToken ct = default);
}

public interface IBaselineAnswerer
{
    /// <summary>Same model, same question, no passages (BUILD_PLAN §8 "Baseline").</summary>
    Task<(string? Answer, bool Refused, ModelCallInfo Call)> AnswerAsync(string question, int taxYear, CancellationToken ct = default);
}

/// <summary>LLM judge with structured output <c>{verdict, reason}</c>. Its <see cref="ClaudeJsonClient"/> runs the judge model.</summary>
public sealed class ClaudeJudge(ClaudeJsonClient claude) : IJudge
{
    private sealed record Reply([property: JsonPropertyName("verdict")] string Verdict, [property: JsonPropertyName("reason")] string Reason);

    public async Task<JudgeVerdict> JudgeAsync(string question, int taxYear, string goldAnswer, string candidate,
        CancellationToken ct = default)
    {
        var user = new StringBuilder()
            .Append("<question tax_year=\"").Append(taxYear).Append("\">").Append(question.Trim()).Append("</question>\n\n")
            .Append("<reference>").Append(goldAnswer.Trim()).Append("</reference>\n\n")
            .Append("<candidate>").Append(candidate.Trim()).Append("</candidate>")
            .ToString();
        var result = await claude.CallAsync(EvalPrompts.JudgeSystem, user, EvalPrompts.JudgeSchema, ct);
        if (result.Refused) return new JudgeVerdict(Verdicts.Incorrect, "Judge model refused to grade.");
        var reply = claude.Parse<Reply>(result);
        return new JudgeVerdict(reply.Verdict, reply.Reason);
    }
}

/// <summary>A plain chatbot: the answer model with a one-line system prompt and no retrieval.</summary>
public sealed class ClaudeBaselineAnswerer(ClaudeJsonClient claude) : IBaselineAnswerer
{
    private sealed record Reply([property: JsonPropertyName("answer")] string Answer);

    public async Task<(string? Answer, bool Refused, ModelCallInfo Call)> AnswerAsync(string question, int taxYear,
        CancellationToken ct = default)
    {
        var result = await claude.CallAsync(EvalPrompts.BaselineSystem(taxYear), question.Trim(), EvalPrompts.BaselineSchema, ct);
        if (result.Refused) return (null, true, result.Call);
        return (claude.Parse<Reply>(result).Answer, false, result.Call);
    }
}

/// <summary>The eval's own prompts, embedded from Prompts/ like the app's.</summary>
public static class EvalPrompts
{
    public static string JudgeSystem { get; } = Resource("judge.system.md").TrimEnd();
    public static IReadOnlyDictionary<string, JsonElement> JudgeSchema { get; } = Schema("judge.schema.json");
    public static IReadOnlyDictionary<string, JsonElement> BaselineSchema { get; } = Schema("baseline.schema.json");
    private static readonly string BaselineTemplate = Resource("baseline.system.md").TrimEnd();

    public static string BaselineSystem(int taxYear) =>
        BaselineTemplate.Replace("{TaxYear}", taxYear.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static IReadOnlyDictionary<string, JsonElement> Schema(string name) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Resource(name))!;

    private static string Resource(string name)
    {
        using var stream = typeof(EvalPrompts).Assembly.GetManifestResourceStream($"TaxWise.Eval.Prompts.{name}")
                           ?? throw new InvalidOperationException($"Missing embedded prompt {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
}
