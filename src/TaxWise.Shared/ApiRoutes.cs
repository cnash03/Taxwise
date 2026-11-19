namespace TaxWise.Shared;

/// <summary>API paths shared by the Api (routing, links it hands out) and the App (calls).</summary>
public static class ApiRoutes
{
    public const string Ask = "api/ask";
    /// <summary>The same question to the same model with no retrieval, for the "Compare with a plain chatbot" view.</summary>
    public const string AskBaseline = "api/ask/baseline";
    public const string KnowledgeBase = "api/knowledge-base";
    public const string ExplainForm = "api/forms/explain";
    /// <summary>The student's own W-2 or 1098-T, sent as the raw PDF body (<see cref="FormUploadLimits"/>).</summary>
    public const string UploadForm = "api/forms/upload";
    public const string Calculator = "api/calculator";
    public const string CompareYears = "api/calculator/compare";
    public const string Quiz = "api/quiz";

    /// <summary>A sample form and its boxes, e.g. <c>api/forms/w2</c>.</summary>
    public static string Form(string formId) => $"api/forms/{Uri.EscapeDataString(formId)}";

    /// <summary>Relative URL of a stored source PDF. The UI appends <c>#page=N</c>.</summary>
    public static string SourcePdf(string docId) => $"api/sources/{Uri.EscapeDataString(docId)}/pdf";
}
