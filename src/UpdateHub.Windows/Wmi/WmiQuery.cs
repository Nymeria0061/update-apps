using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace UpdateHub.Windows.Wmi;

/// <summary>Small helpers around System.Management so providers stay readable.</summary>
[SupportedOSPlatform("windows")]
internal static class WmiQuery
{
    public static List<Dictionary<string, object?>> Select(string query, string scope = @"root\cimv2")
    {
        var rows = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query));
        using var results = searcher.Get();
        foreach (var obj in results)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obj.Properties)
            {
                row[prop.Name] = prop.Value;
            }

            rows.Add(row);
            obj.Dispose();
        }

        return rows;
    }

    public static string Str(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null ? v.ToString()?.Trim() ?? string.Empty : string.Empty;

    public static long Long(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is not null && long.TryParse(v.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0;

    public static bool Bool(this Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var v) && v is bool b && b;

    /// <summary>Parses a CIM_DATETIME value ("20240115000000.000000+000") into a DateTimeOffset.</summary>
    public static DateTimeOffset? CimDate(this Dictionary<string, object?> row, string key)
    {
        var s = row.Str(key);
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(s));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
