using Microsoft.Extensions.Options;
using TaxWise.Api;
using TaxWise.Rag;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Forms;
using TaxWise.Rag.Sources;
using TaxWise.Rag.Voyage;
using TaxWise.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TaxYearOptions>(builder.Configuration.GetSection(TaxYearOptions.SectionName));
builder.Services.Configure<SourceOptions>(builder.Configuration.GetSection(SourceOptions.SectionName));
builder.Services.AddTaxWiseRag(builder.Configuration);
builder.Services.AddTaxWiseAnswering(builder.Configuration);
builder.Services.AddSingleton<SourceLibrary>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.MapGet("/health", () => new HealthResponse("ok", typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0"));

app.MapPost("/" + ApiRoutes.Ask, async (AskRequest? request, AskService ask, IOptions<TaxYearOptions> years,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Question))
        return Results.Problem("Ask a question.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Question.Length > AskLimits.MaxQuestionLength)
        return Results.Problem($"Questions can be at most {AskLimits.MaxQuestionLength} characters.", statusCode: StatusCodes.Status400BadRequest);
    if (!years.Value.Supported.Contains(request.TaxYear))
        return Results.Problem($"Tax year {request.TaxYear} isn't supported. Choose {string.Join(", ", years.Value.Supported)}.",
            statusCode: StatusCodes.Status400BadRequest);

    return await Guarded(logger, async () =>
    {
        var response = await ask.AskAsync(request, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
});

app.MapGet("/api/forms/{formId}", (string formId, FormLibrary forms) =>
    forms.Find(formId) is { } form ? Results.Ok(form) : Results.NotFound());

app.MapPost("/" + ApiRoutes.ExplainForm, async (ExplainFormRequest? request, FormLibrary forms, FormExplainService explain,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Form) || string.IsNullOrWhiteSpace(request.BoxId))
        return Results.Problem("Choose a form and a box.", statusCode: StatusCodes.Status400BadRequest);
    if (forms.Find(request.Form) is not { } form)
        return Results.Problem($"There's no sample form called '{request.Form}'.", statusCode: StatusCodes.Status404NotFound);
    if (FormLibrary.FindBox(form, request.BoxId) is not { } box)
        return Results.Problem($"Form {form.Form} has no box '{request.BoxId}'.", statusCode: StatusCodes.Status404NotFound);

    return await Guarded(logger, async () =>
    {
        var response = await explain.ExplainAsync(form, box, request.PlainLanguage, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
});

app.MapGet("/api/sources/{docId}/pdf", (string docId, SourceLibrary library) =>
    // Only manifest doc IDs resolve to a file, so the route can't be used to read anything else.
    library.FindPdf(docId) is { } path
        ? Results.File(path, "application/pdf", enableRangeProcessing: true)
        : Results.NotFound());

app.MapGet("/" + ApiRoutes.KnowledgeBase, (SourceLibrary library, IOptions<TaxYearOptions> years, HttpContext http) =>
    new KnowledgeBaseResponse(
        library.Documents
            .Select(d => new KnowledgeBaseDocument(d.DocId, d.Title, d.Kind, d.TaxYears, Absolute(http, ApiRoutes.SourcePdf(d.DocId))))
            .ToList(),
        years.Value.Supported,
        years.Value.FullKnowledgeBase));

app.Run();

// Maps answerer and retrieval failures to safe ProblemDetails (429 / 502), shared by /api/ask and /api/forms/explain.
static async Task<IResult> Guarded(ILogger logger, Func<Task<IResult>> run)
{
    try
    {
        return await run();
    }
    catch (AnswererException e) when (e.Failure == AnswererFailure.RateLimited)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (AnswererException e)
    {
        return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception e) when (e is VoyageApiException or Grpc.Core.RpcException)
    {
        logger.LogError("Retrieval failed: {Type}: {Message}", e.GetType().Name, e.Message);
        return Results.Problem("The search service is unavailable. Please try again.", statusCode: StatusCodes.Status502BadGateway);
    }
}

static List<SourcePassage> AbsoluteSources(HttpContext http, IEnumerable<SourcePassage> sources) =>
    sources.Select(s => s with { PdfUrl = Absolute(http, s.PdfUrl) }).ToList();

static string Absolute(HttpContext http, string relative) =>
    $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/{relative}";

// Exposed so TaxWise.Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
