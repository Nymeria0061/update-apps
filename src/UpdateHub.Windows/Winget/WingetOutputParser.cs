using System.Text.RegularExpressions;

namespace UpdateHub.Windows.Winget;

public sealed record WingetUpgradeRow(string Name, string Id, string Version, string Available, string Source)
{
    public bool IsVersionUnknown =>
        string.IsNullOrWhiteSpace(Version) ||
        Version.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
        Version.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase) ||
        Version.StartsWith('<');
}

public sealed record WingetListRow(string Name, string Id, string Version, string Source);

/// <summary>
/// Parses the table printed by <c>winget upgrade</c>. The parser is locale independent:
/// it locates the header by the dashed separator line and derives column offsets from the header words.
/// A regex fallback handles rows whose alignment is off (names with wide characters).
/// </summary>
public static partial class WingetOutputParser
{
    [GeneratedRegex(@"^-{10,}\s*$")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s{2,}(?<id>\S+)\s+(?<ver>(?:<\s*)?\S+)\s+(?<avail>\S+)\s+(?<src>\S+)\s*$")]
    private static partial Regex RowFallbackRegex();

    [GeneratedRegex(@"\S+")]
    private static partial Regex WordRegex();

    private static readonly char[] ProgressGlyphs = ['█', '▒', '░', '▓', '█', '▓', '▒', '░'];

    public static IReadOnlyList<WingetUpgradeRow> ParseUpgradeTable(string output)
    {
        var lines = Normalize(output);
        var rows = new List<WingetUpgradeRow>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (!SeparatorRegex().IsMatch(lines[i]))
            {
                continue;
            }

            var header = lines[i - 1];
            var columns = WordRegex().Matches(header).Select(m => m.Index).ToList();
            if (columns.Count < 4)
            {
                continue;
            }

            // Walk rows until a blank line, the summary ("N upgrades available.") or the next header.
            for (var j = i + 1; j < lines.Count; j++)
            {
                var line = lines[j];
                if (string.IsNullOrWhiteSpace(line))
                {
                    break;
                }

                if (j + 1 < lines.Count && SeparatorRegex().IsMatch(lines[j + 1]))
                {
                    break;
                }

                if (SeparatorRegex().IsMatch(line))
                {
                    break;
                }

                var row = ParseRow(line, columns);
                if (row is not null)
                {
                    rows.Add(row);
                }
                else
                {
                    // Summary line ("3 upgrades available.") ends the table.
                    break;
                }
            }
        }

        // winget can list the same package twice (explicit targeting table); keep the first occurrence.
        return rows
            .GroupBy(r => (r.Id, r.Source), StringTupleComparer.Instance)
            .Select(g => g.First())
            .ToList();
    }

    private static WingetUpgradeRow? ParseRow(string line, List<int> columns)
    {
        // Column based slicing (fast path).
        if (columns.Count >= 5 && line.Length > columns[4])
        {
            var name = Slice(line, columns[0], columns[1]);
            var id = Slice(line, columns[1], columns[2]);
            var version = Slice(line, columns[2], columns[3]);
            var available = Slice(line, columns[3], columns[4]);
            var source = Slice(line, columns[4], null);

            if (LooksLikeId(id) && !source.Contains(' ') && !string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(name) && !available.Contains(' '))
            {
                return new WingetUpgradeRow(name, id, version, available, source);
            }
        }

        var m = RowFallbackRegex().Match(line);
        if (m.Success && LooksLikeId(m.Groups["id"].Value))
        {
            return new WingetUpgradeRow(
                m.Groups["name"].Value.Trim(),
                m.Groups["id"].Value,
                m.Groups["ver"].Value,
                m.Groups["avail"].Value,
                m.Groups["src"].Value);
        }

        return null;
    }

    private static bool LooksLikeId(string id) =>
        !string.IsNullOrWhiteSpace(id) && !id.Contains(' ') && id.Length >= 3;

    private static string Slice(string line, int start, int? end)
    {
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var length = end is null ? line.Length - start : Math.Min(end.Value, line.Length) - start;
        return length <= 0 ? string.Empty : line.Substring(start, length).Trim();
    }

    /// <summary>Removes spinner / progress-bar noise and carriage-return overwrites.</summary>
    private static List<string> Normalize(string output)
    {
        var result = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n").Split('\n'))
        {
            // A '\r' means the terminal would have overwritten the line: keep the final segment only.
            var line = raw.Contains('\r') ? raw[(raw.LastIndexOf('\r') + 1)..] : raw;
            line = line.TrimEnd();
            if (line.Length == 0)
            {
                result.Add(string.Empty);
                continue;
            }

            if (line.IndexOfAny(ProgressGlyphs) >= 0)
            {
                continue;
            }

            if (line.Trim() is "-" or "\\" or "|" or "/")
            {
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    [GeneratedRegex(@"^(?<name>.+?)\s{2,}(?<id>\S+)\s+(?<ver>\S+)(?:\s+(?<avail>\S+))?\s+(?<src>\S+)\s*$")]
    private static partial Regex ListRowFallbackRegex();

    /// <summary>
    /// Parses the table printed by <c>winget list</c>. The table has either four columns
    /// (Name, Id, Version, Source) or five when an upgrade is available (…, Available, Source).
    /// </summary>
    public static IReadOnlyList<WingetListRow> ParseListTable(string output)
    {
        var lines = Normalize(output);
        var rows = new List<WingetListRow>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (!SeparatorRegex().IsMatch(lines[i]))
            {
                continue;
            }

            var columns = WordRegex().Matches(lines[i - 1]).Select(m => m.Index).ToList();
            if (columns.Count < 4)
            {
                continue;
            }

            for (var j = i + 1; j < lines.Count; j++)
            {
                var line = lines[j];
                if (string.IsNullOrWhiteSpace(line) || SeparatorRegex().IsMatch(line))
                {
                    break;
                }

                WingetListRow? row = null;
                if (line.Length > columns[^1])
                {
                    var name = Slice(line, columns[0], columns[1]);
                    var id = Slice(line, columns[1], columns[2]);
                    var version = Slice(line, columns[2], columns[3]);
                    var source = Slice(line, columns[^1], null);
                    if (LooksLikeId(id) && !version.Contains(' ') && !source.Contains(' ') && name.Length > 0)
                    {
                        row = new WingetListRow(name, id, version, source);
                    }
                }

                if (row is null)
                {
                    var m = ListRowFallbackRegex().Match(line);
                    if (m.Success && LooksLikeId(m.Groups["id"].Value))
                    {
                        row = new WingetListRow(m.Groups["name"].Value.Trim(), m.Groups["id"].Value, m.Groups["ver"].Value, m.Groups["src"].Value);
                    }
                }

                if (row is null)
                {
                    break;
                }

                rows.Add(row);
            }
        }

        return rows;
    }

    /// <summary>Extracts the "Available" count from the summary line; null when not found.</summary>
    public static int? ParseUpgradeCount(string output)
    {
        var m = Regex.Match(output, @"(\d+)\s+(upgrades?|yükseltme|güncelleme)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string, string)>
    {
        public static readonly StringTupleComparer Instance = new();

        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(obj.Item1.ToUpperInvariant(), obj.Item2.ToUpperInvariant());
    }
}
