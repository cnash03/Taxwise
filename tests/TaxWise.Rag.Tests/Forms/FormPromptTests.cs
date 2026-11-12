using System.Text.Json;
using TaxWise.Rag.Forms;
using TaxWise.Rag.Tests.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Tests.Forms;

public sealed class FormPromptTests
{
    [Fact]
    public void SystemPrompt_FillsYearAndStyle_AndIsByteStable()
    {
        var a = FormPrompt.SystemPrompt(2025, plainLanguage: false);

        Assert.Contains("for tax year 2025.", a);
        Assert.Contains("Use the passage's terminology.", a);
        Assert.Contains("8th-grade reading level", FormPrompt.SystemPrompt(2025, plainLanguage: true));
        Assert.DoesNotContain("{", a);
        Assert.Equal(a, FormPrompt.SystemPrompt(2025, plainLanguage: false));
    }

    [Fact]
    public void UserMessage_IsPassagesThenTheBox_WithoutTheSampleValue()
    {
        var form = new FormSample("w2", "W-2", "Wage and Tax Statement", 2025, "c · Employer name", "Sample Employer Inc.", []);
        var box = new FormBox("12", "Code DD · Cost of \"health\" coverage", "DD $4,800.00", "s", "Not entered on Form 1040", "q", ["iw2"]);

        var msg = FormPrompt.UserMessage(form, box, [TestChunks.Make(3, "Three.")]);

        Assert.StartsWith("<passage id=\"P1\" source=\"Pub 970 · Ch. 1 · p. 3 · TY 2025\">\nThree.\n</passage>", msg);
        Assert.EndsWith("<box form=\"W-2\" id=\"12\" label=\"Code DD · Cost of 'health' coverage\" goes_to=\"Not entered on Form 1040\" />", msg);
        Assert.DoesNotContain("4,800", msg);
    }

    [Fact]
    public void Schema_RequiresStatusExplanationAndCitations()
    {
        Assert.Equal("object", FormPrompt.Schema["type"].GetString());
        Assert.Equal(["status", "explanation", "citations"],
            FormPrompt.Schema["required"].EnumerateArray().Select(e => e.GetString()));
        Assert.False(FormPrompt.Schema["additionalProperties"].GetBoolean());
        Assert.Equal(JsonValueKind.Object, FormPrompt.Schema["properties"].GetProperty("citations").GetProperty("items").ValueKind);
    }
}
