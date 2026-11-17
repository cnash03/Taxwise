using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TaxWise.Rag.Answering;
using TaxWise.Shared;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TaxWise.Rag.Forms;

/// <summary>The upload can't become a form. <see cref="Exception.Message"/> is safe to show users.</summary>
/// <param name="outcome">Short code for the log line, e.g. <c>no_text</c>.</param>
public sealed class FormUploadException(string outcome, string message) : Exception(message)
{
    public string Outcome { get; } = outcome;
}

/// <summary>
/// "Upload mine": PDF text (PdfPig) → Claude structured extraction → values copied onto the reviewed sample form.
/// Labels, <c>goesTo</c> and retrieval queries all come from the sample, and the result keeps the sample's
/// <c>formId</c>, so <c>/api/forms/explain</c> works unchanged (the explainer never sees box values). The PDF is only
/// held in memory, and box values, names and PDF text are never logged.
/// </summary>
public sealed class FormUploadService(FormLibrary library, IFormExtractor extractor, ILogger<FormUploadService> logger)
{
    public async Task<FormSample> ParseAsync(byte[] pdf, CancellationToken ct = default)
    {
        ModelCallInfo? call = null;
        UploadedPdf.Text? text = null;
        try
        {
            text = UploadedPdf.ReadText(pdf);
            var result = await extractor.ExtractAsync(text.Content, ct);
            call = result.Call;
            if (result.ModelRefused || result.Extraction is null)
                throw new FormUploadException("model_refusal", "TaxWise couldn't read this form. Please try a different PDF.");

            var form = FormUploadMapper.Map(result.Extraction, library);
            Log("ok", call, text, form.FormId, form.Boxes.Count);
            return form;
        }
        catch (FormUploadException e)
        {
            Log(e.Outcome, call, text, null, 0);
            throw;
        }
    }

    // One line per upload. Counts and the form type only: no values, names or text (they're the student's PII).
    private void Log(string outcome, ModelCallInfo? call, UploadedPdf.Text? text, string? form, int boxes) =>
        logger.LogInformation(
            "endpoint=forms.upload outcome={Outcome} model={Model} latencyMs={LatencyMs} inputTokens={InputTokens} " +
            "outputTokens={OutputTokens} stopReason={StopReason} pages={Pages} textChars={TextChars} form={Form} boxes={Boxes}",
            outcome, call?.Model ?? "none", call?.LatencyMs ?? 0, call?.InputTokens ?? 0, call?.OutputTokens ?? 0,
            call?.StopReason ?? "none", text?.Pages ?? 0, text?.Content.Length ?? 0, form ?? "none", boxes);
}

/// <summary>Reads the text layer of an uploaded PDF in memory. There's no OCR, so scans and photos are turned away.</summary>
public static class UploadedPdf
{
    /// <summary>A W-2 PDF is usually one page per copy; more pages than this aren't a single form.</summary>
    public const int MaxPages = 6;
    public const int MaxChars = 30_000;

    /// <summary>Fewer letters and digits than this means there's no real text layer.</summary>
    private const int MinTextChars = 40;

    public sealed record Text(string Content, int Pages);

    public static Text ReadText(byte[] pdf)
    {
        if (pdf.Length == 0 || pdf.Length > FormUploadLimits.MaxBytes)
            throw new FormUploadException("bad_size", $"Upload a PDF of up to {FormUploadLimits.MaxBytes / (1024 * 1024)} MB.");
        if (!HasPdfHeader(pdf))
            throw new FormUploadException("not_pdf", "That file isn't a PDF. Upload your W-2 or 1098-T as a PDF.");

        var sb = new StringBuilder();
        int pages;
        try
        {
            using var document = PdfDocument.Open(pdf);
            pages = document.NumberOfPages;
            foreach (var page in document.GetPages().Take(MaxPages))
            {
                sb.Append($"--- page {page.Number} ---\n");
                sb.Append(ContentOrderTextExtractor.GetText(page).Trim()).Append('\n');
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Encrypted or damaged. The type is enough to debug; the message can quote file contents.
            throw new FormUploadException("unreadable_pdf",
                $"TaxWise couldn't open that PDF ({e.GetType().Name}). If it has a password, save a copy without one and try again.");
        }

        var content = sb.Length > MaxChars ? sb.ToString(0, MaxChars) : sb.ToString();
        if (content.Count(char.IsLetterOrDigit) - CountPageMarkers(pages) < MinTextChars)
            throw new FormUploadException("no_text",
                "This PDF has no text TaxWise can read. It looks like a scan or a photo. Download the PDF from your " +
                "employer's or school's website and upload that instead.");
        return new Text(content, pages);
    }

    // The header may follow a few junk bytes; PDF readers accept it anywhere in the first 1 KB.
    private static bool HasPdfHeader(byte[] pdf) =>
        pdf.AsSpan(0, Math.Min(1024, pdf.Length)).IndexOf("%PDF-"u8) >= 0;

    // "--- page 1 ---" adds letters and digits of its own; don't count them as text.
    private static int CountPageMarkers(int pages) =>
        Enumerable.Range(1, Math.Min(pages, MaxPages)).Sum(n => 4 + n.ToString(CultureInfo.InvariantCulture).Length);
}

public enum FormBoxKind { Amount, Checkbox, CodeAmount, Text }

/// <summary>
/// Turns Claude's extraction into a <see cref="FormSample"/> on the reviewed sample's boxes. Values are normalised to the
/// sample's display format, anything that doesn't fit is dropped, and SSN-shaped text is masked as a backstop (the
/// prompt never asks for the SSN).
/// </summary>
public static partial class FormUploadMapper
{
    public const string IssuerNotFound = "Not found on your form";
    private const int MaxTextLength = 60;
    private const int MaxIssuerLength = 100;
    private const decimal MaxAmount = 100_000_000m;

    /// <summary>The reviewed sample value shows what kind of box it is: "$28,000.00", "Checked", "DD $4,800.00", "CO".</summary>
    public static FormBoxKind KindOf(FormBox box) =>
        box.Value.StartsWith('$') ? FormBoxKind.Amount
        : box.Value is "Checked" or "Not checked" ? FormBoxKind.Checkbox
        : CodeAmountRegex().IsMatch(box.Value) ? FormBoxKind.CodeAmount
        : FormBoxKind.Text;

    public static FormSample Map(FormExtraction extraction, FormLibrary library)
    {
        if (library.Find(extraction.FormType ?? "") is not { } sample)
            throw new FormUploadException("not_a_form",
                "This doesn't look like a Form W-2 or 1098-T. Explain my form can read only those two forms.");
        if (extraction.TaxYear != 0 && extraction.TaxYear != sample.TaxYear)
            throw new FormUploadException("wrong_year",
                $"This looks like a {extraction.TaxYear} Form {sample.Form}. Explain my form covers tax year {sample.TaxYear} " +
                "only for now, because box and line numbers can change between years.");

        var extracted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in extraction.Boxes ?? [])
        {
            if (box.Value is null) continue;
            extracted.TryAdd(NormalizeId(box.Id), box.Value);    // a repeated copy of the form keeps the first value
        }

        var boxes = sample.Boxes
            .Select(b => extracted.TryGetValue(b.Id, out var raw) && NormalizeValue(b, raw) is { } value ? b with { Value = value } : null)
            .OfType<FormBox>()
            .ToList();
        if (boxes.Count == 0)
            throw new FormUploadException("no_boxes",
                $"TaxWise couldn't find any box values on this Form {sample.Form}. If it's a fillable PDF, print it to a new " +
                "PDF first, then upload that.");

        var issuer = Clean(extraction.Issuer, MaxIssuerLength);
        return sample with { Issuer = issuer.Length > 0 ? issuer : IssuerNotFound, Boxes = boxes };
    }

    /// <summary>The value in the sample's display format, or null when it doesn't fit the box.</summary>
    public static string? NormalizeValue(FormBox box, string raw) => KindOf(box) switch
    {
        FormBoxKind.Amount => Amount(raw),
        FormBoxKind.Checkbox => Checkbox(raw),
        FormBoxKind.CodeAmount => CodeAmount(box, raw),
        _ => Clean(raw, MaxTextLength) is { Length: > 0 } text ? text : null,
    };

    /// <summary>"123-45-6789" or "123456789" → "XXX-XX-6789". Amounts are reformatted from numbers, so they never match.</summary>
    public static string MaskSsn(string text) => SsnRegex().Replace(text, "XXX-XX-$1");

    private static string? Amount(string raw)
    {
        var s = raw.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount <= MaxAmount
            ? amount.ToString("$#,##0.00", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? Checkbox(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "checked" or "x" or "yes" or "true" or "✓" or "✔" => "Checked",
        "not checked" or "unchecked" or "no" or "false" or "" => "Not checked",
        _ => null,
    };

    // Only the code the sample box is about (DD for the W-2's box 12); the label and goesTo are specific to it.
    private static string? CodeAmount(FormBox box, string raw)
    {
        var code = CodeAmountRegex().Match(box.Value).Groups[1].Value;
        var m = CodeAmountRegex().Match(raw.Trim());
        return m.Success && string.Equals(m.Groups[1].Value, code, StringComparison.OrdinalIgnoreCase)
               && Amount(raw.Trim()[m.Groups[1].Length..]) is { } amount
            ? $"{code} {amount}"
            : null;
    }

    private static string Clean(string? text, int maxLength)
    {
        var s = MaskSsn(WhitespaceRegex().Replace(text ?? "", " ").Trim());
        return s.Length > maxLength ? s[..maxLength].TrimEnd() : s;
    }

    private static string NormalizeId(string id) => BoxPrefixRegex().Replace(id.Trim(), "");

    [GeneratedRegex(@"^([A-Za-z]{1,2})\s+\$?\s*\d")]
    private static partial Regex CodeAmountRegex();

    [GeneratedRegex(@"\b\d{3}[- ]?\d{2}[- ]?(\d{4})\b")]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^box\s*", RegexOptions.IgnoreCase)]
    private static partial Regex BoxPrefixRegex();
}
