using System.Text;
using System.Text.RegularExpressions;

namespace TaxWise.Core.Chunking;

public static partial class TextUtil
{
    public static int CountWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Token estimate used for chunk sizing: words × 1.33 (BUILD_PLAN §4.2 step 4).</summary>
    public static int EstimateTokens(string text) => (int)Math.Ceiling(CountWords(text) * 1.33);

    /// <summary>Joins wrapped lines with spaces; "re-" + "search" becomes "research".</summary>
    public static string JoinLines(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (sb.Length > 0)
            {
                var hyphenated = sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && char.IsLower(line[0]);
                if (hyphenated) sb.Length--;
                else sb.Append(' ');
            }
            sb.Append(line);
        }
        return sb.ToString();
    }

    /// <summary>Last <paramref name="tokens"/> tokens' worth of words from <paramref name="text"/>.</summary>
    public static string TailTokens(string text, int tokens)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var take = Math.Min(words.Length, (int)Math.Floor(tokens / 1.33));
        return string.Join(' ', words[^take..]);
    }

    /// <summary>Splits text into word windows of at most <paramref name="maxTokens"/> tokens each.</summary>
    public static IEnumerable<string> SplitByTokens(string text, int maxTokens)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var perPiece = Math.Max(1, (int)Math.Floor(maxTokens / 1.33));
        for (var i = 0; i < words.Length; i += perPiece)
            yield return string.Join(' ', words.Skip(i).Take(perPiece));
    }

    /// <summary>Lowercase, digits collapsed to '#', whitespace collapsed — for spotting running headers.</summary>
    public static string Fingerprint(string text) =>
        Whitespace().Replace(Digits().Replace(text.ToLowerInvariant(), "#"), " ").Trim();

    [GeneratedRegex(@"\d+")] private static partial Regex Digits();
    [GeneratedRegex(@"\s+")] private static partial Regex Whitespace();
}
