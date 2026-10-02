using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Windows.Wmi;

namespace UpdateHub.Windows.Firmware;

/// <summary>
/// BIOS / firmware updates through Lenovo System Update (tvsu.exe). The tool downloads packages from
/// Lenovo's official package repository. Results are read back through the WMI export it supports.
/// Package type 3 = BIOS, 4 = firmware.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LenovoSystemUpdateStrategy : IOemFirmwareStrategy
{
    private readonly IProcessRunner _runner;
    private readonly ILogger<LenovoSystemUpdateStrategy> _logger;

    public LenovoSystemUpdateStrategy(IProcessRunner runner, ILogger<LenovoSystemUpdateStrategy> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public string OemKey => OemKeys.Lenovo;

    public FirmwareGuidance Describe(SystemInfo system) => new()
    {
        OemKey = OemKey,
        VendorName = "Lenovo",
        ToolName = "Lenovo System Update",
        ToolPath = FindTool(),
        ToolWingetId = "Lenovo.SystemUpdate",
        ToolDownloadUrl = "https://support.lenovo.com/solutions/ht003029",
        SupportUrl = "https://pcsupport.lenovo.com/",
        Notes = "Lenovo System Update, BIOS ve donanım yazılımı paketlerini Lenovo'nun resmi deposundan indirir. Kurulumda tüm uygulanabilir BIOS/firmware paketleri birlikte kurulur ve yeniden başlatma gerekir.",
    };

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(string toolPath, SystemInfo system, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            ["/CM", "-search", "A", "-action", "LIST", "-packagetypes", "3,4", "-noicon", "-nolicense", "-exporttowmi"])
        {
            Timeout = TimeSpan.FromMinutes(15),
        }, cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            throw new TimeoutException("Lenovo System Update taraması zaman aşımına uğradı.");
        }

        var items = new List<UpdateItem>();
        List<Dictionary<string, object?>> rows;
        try
        {
            rows = WmiQuery.Select("SELECT * FROM Lenovo_Updates", @"root\Lenovo");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lenovo_Updates WMI class is not available; falling back to console output");
            rows = [];
        }

        foreach (var row in rows)
        {
            var title = row.Str("Title");
            var packageId = row.Str("PackageID");
            var version = row.Str("Version");
            var status = row.Str("Status");
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(packageId))
            {
                continue;
            }

            if (status.Contains("installed", StringComparison.OrdinalIgnoreCase) && !status.Contains("not", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isFirmware = title.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
                             title.Contains("UEFI", StringComparison.OrdinalIgnoreCase) ||
                             title.Contains("firmware", StringComparison.OrdinalIgnoreCase);
            if (!isFirmware)
            {
                continue;
            }

            items.Add(new UpdateItem
            {
                Id = $"lenovo:{(string.IsNullOrEmpty(packageId) ? title : packageId)}",
                ProviderKey = OemFirmwareProvider.ProviderKey,
                Name = string.IsNullOrEmpty(title) ? packageId : title,
                Category = UpdateCategory.Firmware,
                Source = "Lenovo System Update",
                CurrentVersion = system.BiosVersion,
                AvailableVersion = version,
                Publisher = "Lenovo",
                Severity = row.Str("Severity").ToLowerInvariant() switch
                {
                    "critical" => UpdateSeverity.Critical,
                    "recommended" => UpdateSeverity.Important,
                    "optional" => UpdateSeverity.Low,
                    _ => UpdateSeverity.Unknown,
                },
                RequiresReboot = true,
                InfoUrl = "https://pcsupport.lenovo.com/",
                Description = "Lenovo System Update deposundan resmi BIOS/firmware paketi.",
            });
        }

        if (rows.Count == 0 && result.StandardOutput.Contains("BIOS", StringComparison.OrdinalIgnoreCase))
        {
            // WMI export unavailable but the tool reported something: surface a generic entry.
            items.Add(new UpdateItem
            {
                Id = "lenovo:bios",
                ProviderKey = OemFirmwareProvider.ProviderKey,
                Name = "Lenovo BIOS güncellemesi",
                Category = UpdateCategory.Firmware,
                Source = "Lenovo System Update",
                CurrentVersion = system.BiosVersion,
                Publisher = "Lenovo",
                RequiresReboot = true,
                InfoUrl = "https://pcsupport.lenovo.com/",
            });
        }

        return items;
    }

    public async Task<InstallResult> InstallAsync(string toolPath, UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new InstallProgress(item.Id, "Lenovo System Update BIOS/firmware paketlerini indiriyor…"));
        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            [
                "/CM", "-search", "A", "-action", "INSTALL",
                "-packagetypes", "3,4",
                "-includerebootpackages", "1,3,4",
                "-noicon", "-nolicense", "-noreboot",
            ])
        {
            Timeout = TimeSpan.FromMinutes(45),
            OnOutputLine = line => progress?.Report(new InstallProgress(item.Id, line.Trim())),
        }, cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return InstallResult.Fail("Lenovo System Update zaman aşımına uğradı.");
        }

        return result.ExitCode == 0
            ? InstallResult.Reboot("BIOS/firmware paketi kuruldu; tamamlanması için yeniden başlatın.")
            : InstallResult.Fail($"Lenovo System Update çıkış kodu {result.ExitCode}.", result.ExitCode);
    }

    private static string? FindTool() => FirmwarePaths.FirstExisting(
    [
        Path.Combine(FirmwarePaths.ProgramFilesX86, "Lenovo", "System Update", "tvsu.exe"),
        Path.Combine(FirmwarePaths.ProgramFiles, "Lenovo", "System Update", "tvsu.exe"),
    ]);
}
