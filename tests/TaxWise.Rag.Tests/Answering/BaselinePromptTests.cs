using TaxWise.Rag.Answering;

namespace TaxWise.Rag.Tests.Answering;

public sealed class BaselinePromptTests
{
    // The eval's 2026-11-12 baseline run used exactly this prompt; the in-app comparison must show the same chatbot.
    [Fact]
    public void SystemPrompt_IsTheEvalBaselinePrompt_AndByteStable()
    {
        var prompt = BaselinePrompt.SystemPrompt(2025);

        Assert.Equal("You are a helpful assistant. Answer the user's question about U.S. federal taxes for tax year 2025.", prompt);
        Assert.Equal(prompt, BaselinePrompt.SystemPrompt(2025));
        Assert.Contains("for tax year 2027.", BaselinePrompt.SystemPrompt(2027));
    }

    [Fact]
    public void Schema_HasOnlyAnAnswerString()
    {
        Assert.Equal("object", BaselinePrompt.Schema["type"].GetString());
        Assert.Equal(["answer"], BaselinePrompt.Schema["required"].EnumerateArray().Select(e => e.GetString()));
        Assert.False(BaselinePrompt.Schema["additionalProperties"].GetBoolean());
    }
}
