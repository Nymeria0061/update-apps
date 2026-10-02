using System.Runtime.Versioning;
using UpdateHub.Core.Abstractions;

namespace UpdateHub.Windows.Winget;

/// <summary>
/// Finds winget.exe. The per-user app execution alias is not always visible to elevated processes,
/// so the packaged install directory is probed as a fallback.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WingetLocator
{
    private readonly ISettingsService _settings;
    private string? _resolved;

    public WingetLocator(ISettingsService settings)
    {
        _settings = settings;
    }

    public string? Resolve()
    {
        if (_resolved is not null && File.Exists(_resolved))
        {
            return _resolved;
        }

        var custom = _settings.Current.WingetPath;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
        {
            return _resolved = custom;
        }

        foreach (var candidate in Candidates())
        {
            if (File.Exists(candidate))
            {
                return _resolved = candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(dir.Trim(), "winget.exe");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var windowsApps = Path.Combine(programFiles, "WindowsApps");
        IEnumerable<string> packages;
        try
        {
            packages = Directory.Exists(windowsApps)
                ? Directory.GetDirectories(windowsApps, "Microsoft.DesktopAppInstaller_*_8wekyb3d8bbwe")
                : Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            packages = Array.Empty<string>();
        }
        catch (IOException)
        {
            packages = Array.Empty<string>();
        }

        // Newest version first; directory names look like Microsoft.DesktopAppInstaller_1.9.25200.0_x64__8wekyb3d8bbwe
        foreach (var pkg in packages.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(pkg, "winget.exe");
        }
    }
}
