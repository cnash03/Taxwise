using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

public static partial class TableHeuristics
{
    /// <summary>
    /// A block is a table when it has at least two lines and at least half of its lines carry
    /// three or more numeric columns ("3 or more numeric columns", BUILD_PLAN §4.2 step 4).
    /// Tables are never split across chunks.
    /// </summary>
    public static bool IsTable(IReadOnlyList<string> lines)
    {
        if (lines.Count < 2) return false;
        var numericRows = lines.Count(l => NumericColumns(l) >= 3);
        return numericRows * 2 >= lines.Count;
    }

    public static int NumericColumns(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(t => Numeric().IsMatch(t));

    [GeneratedRegex(@"^[\$\(]*-?\d[\d,]*(\.\d+)?%?\)?[,;.]?$")]
    private static partial Regex Numeric();
}
