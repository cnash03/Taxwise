using System.Globalization;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxWise.Eval;
using TaxWise.Rag;
using TaxWise.Rag.Answering;
using TaxWise.Rag.Sources;

// Gold-set evaluation runner. See docs/BUILD_PLAN.md §8.
EvalArgs options;
try { options = EvalArgs.Parse(args); }
catch (Exception e) when (e is ArgumentException or FormatException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

// Same settings as the API, so the eval measures what the app runs.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddJsonFile(Path.Combine(RepoPaths.Root, "src", "TaxWise.Api", "appsettings.json"), optional: false);
builder.Configuration.AddUserSecrets<EvalRunner>(optional: true);
builder.Configuration.AddEnvironmentVariables();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning).AddFilter("Polly", LogLevel.Warning)
    .AddFilter("Microsoft", LogLevel.Warning).AddFilter("TaxWise.Rag", LogLevel.Warning);

builder.Services.AddTaxWiseRag(builder.Configuration).AddTaxWiseAnswering(builder.Configuration);
builder.Services.AddSingleton<IBaselineAnswerer, ClaudeBaselineAnswerer>();
builder.Services.AddSingleton<IJudge>(sp =>
{
    // The judge is a separate, cheaper model with its own client settings; the answer model is untouched.
    var answer = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    var judgeOptions = new AnthropicOptions { Model = options.JudgeModel, Effort = "low", MaxTokens = 4000, UseRefusalFallback = answer.UseRefusalFallback };
    return new ClaudeJudge(new ClaudeJsonClient(sp.GetRequiredService<AnthropicClient>(), Options.Create(judgeOptions),
        sp.GetRequiredService<ILogger<ClaudeJsonClient>>()));
});
builder.Services.AddSingleton<EvalRunner>();

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var gold = GoldSet.Load(options.GoldPath);
    var runner = host.Services.GetRequiredService<EvalRunner>();
    var answerOptions = host.Services.GetRequiredService<IOptions<AnthropicOptions>>().Value;
    var retrieval = host.Services.GetRequiredService<IOptions<RetrievalOptions>>().Value;

    EvalReport report;
    string basePath;
    if (options.Rejudge is not null)
    {
        var previous = EvalReport.Load(options.Rejudge);
        var byId = gold.ToDictionary(g => g.Id);
        var rejudged = new List<QuestionResult>();
        foreach (var r in previous.Results)
            // hit@4 is rescored too, so a corrected expectedDocs/expectedPages doesn't need a re-run.
            rejudged.Add(byId.TryGetValue(r.Id, out var g)
                ? await runner.JudgeAsync(r.Retrieved.Count > 0 ? r with { HitAt4 = Metrics.HitAt4(g, r.Retrieved) } : r, g, cts.Token)
                : r);
        Console.WriteLine($"Judged {rejudged.Count(r => r.Judge is not null) - previous.Results.Count(r => r.Judge is not null)} more question(s).");
        report = previous with { JudgeModel = options.JudgeModel, Results = rejudged };
        basePath = Path.ChangeExtension(options.Rejudge, null);
    }
    else
    {
        var items = options.Select(gold);
        Console.WriteLine($"Running {items.Count} question(s) in {options.Mode} mode against {answerOptions.Model} " +
                          $"(judge {options.JudgeModel}, concurrency {options.Concurrency})…");
        var done = 0;
        var progress = new Progress<QuestionResult>(r =>
            Console.WriteLine($"  [{Interlocked.Increment(ref done),2}/{items.Count}] {r.Id} {r.Outcome} " +
                              $"top={r.TopScore.ToString("0.000", CultureInfo.InvariantCulture)} verdict={r.Judge?.Verdict ?? "-"}"));
        var results = await runner.RunAsync(options.Mode, items, options.Concurrency, progress, cts.Token);
        var date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        report = new EvalReport(options.Mode, date, answerOptions.Model, options.JudgeModel, retrieval.MinRerankScore,
            results.OrderBy(r => r.Id, StringComparer.Ordinal).ToList());
        Directory.CreateDirectory(options.OutDir);
        basePath = Path.Combine(options.OutDir, $"{date}-{options.Mode}{(options.IsSubset ? "-smoke" : "")}");
    }

    await File.WriteAllTextAsync(basePath + ".json", report.ToJson(), cts.Token);
    var markdown = ReportWriter.ToMarkdown(report, gold);
    await File.WriteAllTextAsync(basePath + ".md", markdown, cts.Token);
    Console.WriteLine();
    Console.WriteLine(markdown[..markdown.IndexOf("## By persona", StringComparison.Ordinal)].TrimEnd());
    Console.WriteLine();
    Console.WriteLine($"Wrote {basePath}.md and .json");
    return 0;
}
catch (Exception e) when (e is FormatException or IOException or InvalidOperationException or Grpc.Core.RpcException)
{
    // Gold-set, file, Qdrant or config problems: print the reason, not a stack trace. Messages never contain keys.
    Console.Error.WriteLine($"Eval failed: {e.Message}");
    return 1;
}
