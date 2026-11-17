using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TaxWise.Rag.Answering;
using TaxWise.Shared;

namespace TaxWise.Rag.Forms;

/// <summary>Claude's reading of an uploaded form, as the extraction JSON schema shapes it. Not yet trusted.</summary>
public sealed record FormExtraction(
    [property: JsonPropertyName("formType")] string FormType,
    [property: JsonPropertyName("taxYear")] int TaxYear,
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("boxes")] IReadOnlyList<ExtractedBox>? Boxes);

public sealed record ExtractedBox(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] string? Value);

/// <summary><see cref="Extraction"/> is null when <see cref="ModelRefused"/>.</summary>
public sealed record FormExtractorResult(FormExtraction? Extraction, bool ModelRefused, ModelCallInfo Call);

public interface IFormExtractor
{
    /// <param name="documentText">Text from the uploaded PDF. It holds personal data: never log it.</param>
    Task<FormExtractorResult> ExtractAsync(string documentText, CancellationToken ct = default);
}

/// <summary>Reads box values from the PDF text with the schema <c>{ formType, taxYear, issuer, boxes[] }</c>.</summary>
public sealed class ClaudeFormExtractor(ClaudeJsonClient claude, FormLibrary library) : IFormExtractor
{
    private readonly string _system = FormExtractPrompt.SystemPrompt(library.Forms);

    public async Task<FormExtractorResult> ExtractAsync(string documentText, CancellationToken ct = default)
    {
        var result = await claude.CallAsync(_system, FormExtractPrompt.UserMessage(documentText), FormExtractPrompt.Schema, ct);
        if (result.Refused)
            return new FormExtractorResult(null, true, result.Call);

        return new FormExtractorResult(claude.Parse<FormExtraction>(result), false, result.Call);
    }
}

/// <summary>Builds the upload extraction prompt from the embedded templates in Prompts/ and the reviewed sample boxes.</summary>
public static class FormExtractPrompt
{
    private static readonly string SystemTemplate = AnswerPrompt.Resource("form-extract.system.md");

    /// <summary>The extraction JSON schema, as sent in <c>output_config.format</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Schema { get; } =
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(AnswerPrompt.Resource("form-extract.schema.json"))!;

    /// <summary>Lists each sample form's boxes, so Claude only reads boxes TaxWise can explain. Same forms, same bytes.</summary>
    public static string SystemPrompt(IReadOnlyList<FormSample> forms)
    {
        var sb = new StringBuilder();
        foreach (var form in forms)
        {
            sb.Append($"<form id=\"{AnswerPrompt.Attr(form.FormId)}\" name=\"Form {AnswerPrompt.Attr(form.Form)}\">\n");
            foreach (var box in form.Boxes)
                sb.Append($"Box {box.Id}: {box.Label} ({KindName(FormUploadMapper.KindOf(box))})\n");
            sb.Append("</form>\n");
        }
        return SystemTemplate.Replace("{Forms}", sb.ToString().TrimEnd()).TrimEnd();
    }

    /// <summary>The PDF text in a <c>&lt;document&gt;</c> element. A closing tag inside the text can't end it early.</summary>
    public static string UserMessage(string documentText) =>
        $"<document>\n{documentText.Replace("</document>", "</ document>", StringComparison.OrdinalIgnoreCase)}\n</document>";

    private static string KindName(FormBoxKind kind) => kind switch
    {
        FormBoxKind.Amount => "amount",
        FormBoxKind.Checkbox => "checkbox",
        FormBoxKind.CodeAmount => "code",
        _ => "text",
    };
}
