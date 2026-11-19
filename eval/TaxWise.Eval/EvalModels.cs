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

/// <summary>
/// The eval's own prompts (the judge), embedded from Prompts/ like the app's. The baseline prompt lives in
/// TaxWise.Rag (<see cref="BaselinePrompt"/>), shared with <c>POST /api/ask/baseline</c>.
/// </summary>
public static class EvalPrompts
{
    public static string JudgeSystem { get; } = Resource("judge.system.md").TrimEnd();
    public static IReadOnlyDictionary<string, JsonElement> JudgeSchema { get; } = Schema("judge.schema.json");

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
