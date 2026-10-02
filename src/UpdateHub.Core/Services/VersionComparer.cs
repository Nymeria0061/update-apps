using System.Text.RegularExpressions;

namespace UpdateHub.Core.Services;

/// <summary>
/// Lenient version comparison that copes with the mixed formats seen in the wild
/// ("1.2.3", "2024.10", "v3.1-rc1", "A12", "1.0.0.123 (build 5)").
/// Numeric segments are compared numerically, text segments ordinally.
/// </summary>
public static partial class VersionComparer
{
    [GeneratedRegex(@"\d+|[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    /// <summary>Returns &lt;0 if a &lt; b, 0 if equal, &gt;0 if a &gt; b. Null/empty sorts first.</summary>
    public static int Compare(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(a))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(b))
        {
            return 1;
        }

        var ta = Tokenize(a);
        var tb = Tokenize(b);
        var len = Math.Max(ta.Count, tb.Count);

        for (var i = 0; i < len; i++)
        {
            var x = i < ta.Count ? ta[i] : "0";
            var y = i < tb.Count ? tb[i] : "0";

            var xNum = ulong.TryParse(x, out var xn);
            var yNum = ulong.TryParse(y, out var yn);

            int c;
            if (xNum && yNum)
            {
                c = xn.CompareTo(yn);
            }
            else if (xNum)
            {
                // "1.2.0" > "1.2.rc" : a numeric segment beats a pre-release tag
                c = 1;
            }
            else if (yNum)
            {
                c = -1;
            }
            else
            {
                c = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            }

            if (c != 0)
            {
                return c;
            }
        }

        return 0;
    }

    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    private static List<string> Tokenize(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V'))
        {
            v = v[1..];
        }

        return TokenRegex().Matches(v).Select(m => m.Value).ToList();
    }
}
