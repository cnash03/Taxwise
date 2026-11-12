using System.Text;
using System.Text.Json;
using TaxWise.Core;
using TaxWise.Core.Guardrails;
using TaxWise.Rag.Answering;

namespace TaxWise.Rag.Quiz;

/// <summary>Builds the quiz question prompt from the embedded templates in Prompts/ (BUILD_PLAN §7, Phase 6).</summary>
public static class QuizPrompt
{
    private static readonly string SystemTemplate = AnswerPrompt.Resource("quiz.system.md");

    /// <summary>The question JSON schema <c>{ question, options[4], correctIndex, explanation, quote }</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnswerPrompt.Resource("quiz.schema.json"))!;

    /// <summary>Same text for every question in the same mode, so the prefix stays cacheable across a quiz's parallel calls.</summary>
    public static string SystemPrompt(bool plainLanguage) =>
        SystemTemplate.Replace("{WritingStyle}", AnswerPrompt.WritingStyle(plainLanguage)).TrimEnd();

    /// <summary>The one passage as P1, then the topic. On the retry, <paramref name="failedCheck"/> says what was wrong.</summary>
    public static string UserMessage(string topic, Chunk passage, string? failedCheck = null)
    {
        var sb = new StringBuilder();
        AnswerPrompt.AppendPassages(sb, [passage]);
        sb.Append("<topic>").Append(AnswerPrompt.Body(topic.Trim()).Replace("</topic>", "</ topic>")).Append("</topic>");
        if (failedCheck is not null)
        {
            sb.Append("\n\n<retry>Your previous question failed a check: ").Append(RetryHint(failedCheck))
              .Append(" Write a new question that follows every rule.</retry>");
        }
        return sb.ToString();
    }

    private static string RetryHint(string outcome) => outcome switch
    {
        QuizCheck.WrongOptionCount => "it must have exactly four non-empty options.",
        QuizCheck.DuplicateOptions => "two of its options were the same; all four must be different.",
        QuizCheck.IndexOutOfRange => "correctIndex must be 0, 1, 2 or 3.",
        QuizCheck.QuoteNotFound => "its quote is not in P1. Copy one sentence from P1 exactly, character for character.",
        _ => "the question and the explanation must not be empty.",
    };
}
