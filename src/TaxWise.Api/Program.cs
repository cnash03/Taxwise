using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TaxWise.Api;
using TaxWise.Core.Calculator;
using TaxWise.Rag;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Calculator;
using TaxWise.Rag.Forms;
using TaxWise.Rag.Quiz;
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
builder.Services.Configure<UsageLimitOptions>(builder.Configuration.GetSection(UsageLimitOptions.SectionName));
builder.Services.AddRateLimiter(o =>
{
    // Options are read per request (not from builder.Configuration) so test overrides apply. Behind Azure the client IP
    // comes from X-Forwarded-For (ASPNETCORE_FORWARDEDHEADERS_ENABLED, docs/DEPLOY.md).
    o.AddPolicy(UsageLimitOptions.PolicyName, http =>
        http.RequestServices.GetRequiredService<IOptions<UsageLimitOptions>>().Value is { PermitLimit: > 0 } limit
            ? RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limit.PermitLimit, Window = TimeSpan.FromMinutes(limit.WindowMinutes) })
            : RateLimitPartition.GetNoLimiter("off"));
    o.OnRejected = (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return new ValueTask(Results.Problem("You've reached the TaxWise question limit for now. Please try again later.",
            statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(context.HttpContext));
    };
});

var app = builder.Build();

app.UseRateLimiter();

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
}).RequireRateLimiting(UsageLimitOptions.PolicyName);

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
}).RequireRateLimiting(UsageLimitOptions.PolicyName);

// The PDF is the raw request body, read into memory and never written to disk. (Multipart form files over 64 KB are
// buffered to a temp file by ASP.NET, which is why this isn't an IFormFile.)
app.MapPost("/" + ApiRoutes.UploadForm, async (HttpRequest request, FormUploadService upload,
    ILogger<Program> logger, CancellationToken ct) =>
{
    if (!string.Equals(request.GetTypedHeaders().ContentType?.MediaType.Value, FormUploadLimits.ContentType, StringComparison.OrdinalIgnoreCase))
        return Results.Problem("Upload your W-2 or 1098-T as a PDF.", statusCode: StatusCodes.Status415UnsupportedMediaType);
    if (request.ContentLength > FormUploadLimits.MaxBytes || await ReadUpToAsync(request.Body, FormUploadLimits.MaxBytes, ct) is not { } pdf)
        return Results.Problem($"PDFs can be at most {FormUploadLimits.MaxBytes / (1024 * 1024)} MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    return await Guarded(logger, async () =>
    {
        try
        {
            return Results.Ok(await upload.ParseAsync(pdf, ct));
        }
        catch (FormUploadException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    });
}).RequireRateLimiting(UsageLimitOptions.PolicyName);

app.MapPost("/" + ApiRoutes.Calculator,async (CalcRequest? request, CalculatorService calculator,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (CalcProblem(request) is { } problem) return problem;
    if (TaxYearRegistry.Get(request!.TaxYear) is not { } rules)
        return Results.Problem($"Tax year {request.TaxYear} isn't supported. Choose {string.Join(", ", TaxYearRegistry.Supported)}.",
            statusCode: StatusCodes.Status400BadRequest);

    return await Guarded(logger, async () =>
    {
        var response = await calculator.CalculateAsync(rules, request, ct);
        return Results.Ok(response with { Sources = AbsoluteSources(http, response.Sources) });
    });
}).RequireRateLimiting(UsageLimitOptions.PolicyName);

// Numbers only, for every supported year; TaxYear in the body is ignored.
app.MapPost("/" + ApiRoutes.CompareYears, (CalcRequest? request) =>
    CalcProblem(request) ?? Results.Ok(CalculatorService.Compare(request!)));

app.MapPost("/" + ApiRoutes.Quiz, async (QuizRequest? request, QuizService quiz, IOptions<TaxYearOptions> years,
    HttpContext http, ILogger<Program> logger, CancellationToken ct) =>
{
    if (request is null || string.IsNullOrWhiteSpace(request.Topic))
        return Results.Problem("Choose a topic to be quizzed on.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Topic.Length > QuizLimits.MaxTopicLength)
        return Results.Problem($"Topics can be at most {QuizLimits.MaxTopicLength} characters.", statusCode: StatusCodes.Status400BadRequest);
    if (request.ChunkIds is null || request.ChunkIds.Count > QuizLimits.MaxChunkIds)
        return Results.Problem($"Send at most {QuizLimits.MaxChunkIds} passage IDs.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Count is < 1 or > QuizLimits.MaxCount)
        return Results.Problem($"A quiz can have 1 to {QuizLimits.MaxCount} questions.", statusCode: StatusCodes.Status400BadRequest);
    if (!years.Value.Supported.Contains(request.TaxYear))
        return Results.Problem($"Tax year {request.TaxYear} isn't supported. Choose {string.Join(", ", years.Value.Supported)}.",
            statusCode: StatusCodes.Status400BadRequest);

    return await Guarded(logger, async () =>
    {
        var response = await quiz.CreateAsync(request, ct);
        return Results.Ok(response with
        {
            Items = response.Items.Select(i => i with { Source = AbsoluteSources(http, [i.Source])[0] }).ToList(),
        });
    });
}).RequireRateLimiting(UsageLimitOptions.PolicyName);

app.MapGet("/api/sources/{docId}/pdf", (string docId, SourceLibrary library) =>
    // Only manifest doc IDs resolve, so the route can't be used to read anything else. The deployed API ships
    // without the PDFs and redirects to irs.gov instead (BUILD_PLAN §8, Phase 8).
    library.FindPdf(docId) is { } path
        ? Results.File(path, "application/pdf", enableRangeProcessing: true)
        : library.FindRemoteUrl(docId) is { } url
            ? Results.Redirect(url)
            : Results.NotFound());

app.MapGet("/" + ApiRoutes.KnowledgeBase, (SourceLibrary library, IOptions<TaxYearOptions> years, HttpContext http) =>
    new KnowledgeBaseResponse(
        library.Documents
            .Select(d => new KnowledgeBaseDocument(d.DocId, d.Title, d.Kind, d.TaxYears, Absolute(http, ApiRoutes.SourcePdf(d.DocId))))
            .ToList(),
        years.Value.Supported,
        years.Value.FullKnowledgeBase));

app.Run();

// Maps answerer and retrieval failures to safe ProblemDetails (429 / 502), shared by /api/ask, /api/forms/explain,
// /api/forms/upload, /api/calculator and /api/quiz.
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

// Shared input checks for the calculator endpoints; null when the request is fine.
static IResult? CalcProblem(CalcRequest? request)
{
    if (request is null)
        return Results.Problem("Enter your wages and withholding.", statusCode: StatusCodes.Status400BadRequest);
    if (!Enum.IsDefined(request.FilingStatus))
        return Results.Problem("Choose a filing status.", statusCode: StatusCodes.Status400BadRequest);
    if (request.Wages is < 0 or > CalcLimits.MaxAmount || request.FederalWithholding is < 0 or > CalcLimits.MaxAmount)
        return Results.Problem($"Wages and withholding must be between $0 and ${CalcLimits.MaxAmount:N0}.",
            statusCode: StatusCodes.Status400BadRequest);
    return null;
}

// The whole body, or null when it's longer than maxBytes (Content-Length can be missing or wrong).
static async Task<byte[]?> ReadUpToAsync(Stream body, long maxBytes, CancellationToken ct)
{
    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await body.ReadAsync(chunk, ct)) > 0)
    {
        if (buffer.Length + read > maxBytes) return null;
        buffer.Write(chunk, 0, read);
    }
    return buffer.ToArray();
}

static List<SourcePassage> AbsoluteSources(HttpContext http, IEnumerable<SourcePassage> sources) =>
    sources.Select(s => s with { PdfUrl = Absolute(http, s.PdfUrl) }).ToList();

static string Absolute(HttpContext http, string relative) =>
    $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}/{relative}";

// Exposed so TaxWise.Api.Tests can use WebApplicationFactory<Program>.
public partial class Program;
