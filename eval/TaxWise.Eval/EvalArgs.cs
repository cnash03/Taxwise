using System.Globalization;

namespace TaxWise.Eval;

/// <summary>
/// <c>--mode taxwise|baseline</c> runs the gold set; <c>--rejudge results.json</c> re-reads a run, judges questions whose
/// gold answers were written since, rescores hit@4 against the current gold set, and rewrites its reports. <c>--only</c>/<c>--limit</c> run a subset (a smoke run),
/// which is written with a <c>-smoke</c> suffix so it never overwrites a full run.
/// </summary>
public sealed record EvalArgs(
    string Mode,
    string GoldPath,
    string OutDir,
    IReadOnlySet<string>? Only,
    int? Limit,
    int Concurrency,
    string JudgeModel,
    string? Rejudge)
{
    public const string DefaultJudgeModel = "claude-sonnet-5-5";

    public bool IsSubset => Only is not null || Limit is not null;

    public const string Usage =
        "Usage: dotnet run --project eval/TaxWise.Eval -- --mode taxwise|baseline [--only q01,q02] [--limit N] " +
        "[--concurrency N] [--judge-model M] [--gold path] [--out dir]\n" +
        "       dotnet run --project eval/TaxWise.Eval -- --rejudge eval/results/YYYY-MM-DD-taxwise.json";

    public static EvalArgs Parse(IReadOnlyList<string> args)
    {
        string? mode = null, rejudge = null, gold = null, outDir = null;
        string judge = DefaultJudgeModel;
        HashSet<string>? only = null;
        int? limit = null;
        var concurrency = 3;

        for (var i = 0; i < args.Count; i++)
        {
            string Next() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.\n{Usage}");
            switch (args[i])
            {
                case "--mode": mode = Next().ToLowerInvariant(); break;
                case "--rejudge": rejudge = Next(); break;
                case "--gold": gold = Next(); break;
                case "--out": outDir = Next(); break;
                case "--judge-model": judge = Next(); break;
                case "--only": only = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(); break;
                case "--limit": limit = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--concurrency": concurrency = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown argument {args[i]}.\n{Usage}");
            }
        }

        if (rejudge is null && mode is not (EvalModes.TaxWise or EvalModes.Baseline))
            throw new ArgumentException($"--mode must be taxwise or baseline.\n{Usage}");
        if (concurrency < 1) throw new ArgumentException("--concurrency must be at least 1.");

        var root = Rag.Sources.RepoPaths.Root;
        return new EvalArgs(mode ?? "", gold ?? GoldSet.DefaultPath, outDir ?? Path.Combine(root, "eval", "results"),
            only, limit, concurrency, judge, rejudge);
    }

    /// <summary>The gold items this run covers, in file order.</summary>
    public IReadOnlyList<GoldItem> Select(IReadOnlyList<GoldItem> gold)
    {
        IEnumerable<GoldItem> items = gold;
        if (Only is not null) items = items.Where(g => Only.Contains(g.Id));
        if (Limit is not null) items = items.Take(Limit.Value);
        return items.ToList();
    }
}
