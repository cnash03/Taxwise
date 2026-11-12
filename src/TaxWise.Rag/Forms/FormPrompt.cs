using System.Globalization;
using System.Text;
using System.Text.Json;
using TaxWise.Core;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Forms;

/// <summary>Builds the "Explain my form" prompt from the embedded templates in Prompts/ (BUILD_PLAN §6).</summary>
public static class FormPrompt
{
    private static readonly string SystemTemplate = AnswerPrompt.Resource("form.system.md");

    /// <summary>The form explanation JSON schema, as sent in <c>output_config.format</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnswerPrompt.Resource("form.schema.json"))!;

    /// <summary>Same text for every box with the same year and mode, so the prefix stays cacheable.</summary>
    public static string SystemPrompt(int taxYear, bool plainLanguage) =>
        SystemTemplate
            .Replace("{TaxYear}", taxYear.ToString(CultureInfo.InvariantCulture))
            .Replace("{WritingStyle}", AnswerPrompt.WritingStyle(plainLanguage))
            .TrimEnd();

    /// <summary>The passages as P1..Pn blocks, then the box. The box's sample value is left out on purpose.</summary>
    public static string UserMessage(FormSample form, FormBox box, IReadOnlyList<Chunk> passages)
    {
        var sb = new StringBuilder();
        AnswerPrompt.AppendPassages(sb, passages);
        sb.Append($"<box form=\"{AnswerPrompt.Attr(form.Form)}\" id=\"{AnswerPrompt.Attr(box.Id)}\" ")
          .Append($"label=\"{AnswerPrompt.Attr(box.Label)}\" goes_to=\"{AnswerPrompt.Attr(box.GoesTo)}\" />");
        return sb.ToString();
    }
}
