using TaxWise.Core.Guardrails;
using TaxWise.Rag.Quiz;
using TaxWise.Rag.Tests.Answering;

namespace TaxWise.Rag.Tests.Quiz;

public sealed class QuizPromptTests
{
    [Fact]
    public void SystemPrompt_IsByteStablePerMode_AndLimitsQuestionsToThePassage()
    {
        Assert.Equal(QuizPrompt.SystemPrompt(false), QuizPrompt.SystemPrompt(false));
        Assert.NotEqual(QuizPrompt.SystemPrompt(false), QuizPrompt.SystemPrompt(true));
        Assert.Contains("ONLY from P1", QuizPrompt.SystemPrompt(false));
        Assert.DoesNotContain("{", QuizPrompt.SystemPrompt(true));
    }

    [Fact]
    public void UserMessage_HasOnePassageThenTheTopic_AndARetryNoteOnlyOnRetry()
    {
        var message = QuizPrompt.UserMessage("Scholarships", TestChunks.Make(3));

        Assert.Contains("<passage id=\"P1\" source=\"Pub 970 · Ch. 1 · p. 3 · TY 2025\">", message);
        Assert.DoesNotContain("P2", message);
        Assert.EndsWith("<topic>Scholarships</topic>", message);

        var retry = QuizPrompt.UserMessage("Scholarships", TestChunks.Make(3), QuizCheck.QuoteNotFound);
        Assert.Contains("<retry>Your previous question failed a check: its quote is not in P1.", retry);
    }

    [Fact]
    public void Schema_RequiresEveryField()
    {
        var required = QuizPrompt.Schema["required"].EnumerateArray().Select(e => e.GetString());

        Assert.Equal(["question", "options", "correctIndex", "explanation", "quote"], required);
    }
}
