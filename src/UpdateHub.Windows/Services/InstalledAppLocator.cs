using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace UpdateHub.Windows.Services;

/// <summary>
/// Finds the executable of an installed desktop application through the Add/Remove Programs registry
/// entries (DisplayIcon / InstallLocation), so apps that ship their own updater can simply be started.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class InstalledAppLocator
{
    private static readonly (RegistryHive Hive, string Path)[] UninstallRoots =
    [
        (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphaNumRegex();

    public static string? FindExecutable(string displayName, string? packageId)
    {
        var wanted = Normalize(displayName);
        var wantedId = packageId is null ? null : Normalize(packageId.Contains('.') ? packageId[(packageId.IndexOf('.') + 1)..] : packageId);
        if (wanted.Length < 3)
        {
            return null;
        }

        string? best = null;
        var bestScore = 0;

        foreach (var (hive, path) in UninstallRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var root = baseKey.OpenSubKey(path);
                if (root is null)
                {
                    continue;
                }

                foreach (var subName in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(subName);
                    var name = key?.GetValue("DisplayName") as string;
                    if (key is null || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var normalized = Normalize(name);
                    var score = normalized == wanted ? 3
                        : normalized.StartsWith(wanted) || wanted.StartsWith(normalized) ? 2
                        : wantedId is not null && wantedId.Length >= 4 && normalized.Contains(wantedId) ? 1
                        : 0;
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    var exe = ExecutableFrom(key);
                    if (exe is not null)
                    {
                        best = exe;
                        bestScore = score;
                    }
                }
            }
            catch (Exception)
            {
                // Registry hives can be partially inaccessible; keep looking elsewhere.
            }
        }

        return best;
    }

    /// <summary>Starts the program through the shell as the interactive user, even when we run elevated.</summary>
    public static void Launch(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exePath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) });
        }
    }

    private static string? ExecutableFrom(RegistryKey key)
    {
        if (key.GetValue("DisplayIcon") is string icon && !string.IsNullOrWhiteSpace(icon))
        {
            var candidate = icon.Trim().Trim('"');
            var comma = candidate.LastIndexOf(',');
            if (comma > 0 && candidate[(comma + 1)..].Trim().All(c => char.IsDigit(c) || c == '-'))
            {
                candidate = candidate[..comma];
            }

            candidate = candidate.Trim().Trim('"');
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) &&
                !Path.GetFileName(candidate).Contains("unins", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        if (key.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
        {
            var dir = location.Trim().Trim('"');
            if (Directory.Exists(dir))
            {
                try
                {
                    var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                        .Where(f => !Path.GetFileName(f).Contains("unins", StringComparison.OrdinalIgnoreCase)
                                    && !Path.GetFileName(f).Contains("setup", StringComparison.OrdinalIgnoreCase)
                                    && !Path.GetFileName(f).Contains("crash", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .ToList();
                    if (exes.Count > 0)
                    {
                        return exes[0];
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        return null;
    }

    private static string Normalize(string s) => NonAlphaNumRegex().Replace(s.ToLowerInvariant(), string.Empty);
}
