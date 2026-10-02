using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using UpdateHub.Windows.Wmi;

namespace UpdateHub.Windows.Firmware;

/// <summary>
/// BIOS updates through HP Image Assistant (HPImageAssistant.exe), HP's official tool for analysing
/// and installing SoftPaqs. Only the BIOS category is requested.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HpImageAssistantStrategy : IOemFirmwareStrategy
{
    private const int ExitNoRecommendations = 256;
    private const int ExitNoUpdatesAvailable = 257;
    private const int ExitRebootRequired = 3010;

    private readonly IProcessRunner _runner;
    private readonly ISettingsService _settings;
    private readonly ILogger<HpImageAssistantStrategy> _logger;

    public HpImageAssistantStrategy(IProcessRunner runner, ISettingsService settings, ILogger<HpImageAssistantStrategy> logger)
    {
        _runner = runner;
        _settings = settings;
        _logger = logger;
    }

    public string OemKey => OemKeys.Hp;

    public FirmwareGuidance Describe(SystemInfo system) => new()
    {
        OemKey = OemKey,
        VendorName = "HP",
        ToolName = "HP Image Assistant",
        ToolPath = FindTool(),
        ToolWingetId = "HP.ImageAssistant",
        ToolDownloadUrl = "https://ftp.hp.com/pub/caps-softpaq/cmit/HPIA.html",
        SupportUrl = "https://support.hp.com/drivers",
        Notes = "HP Image Assistant, BIOS SoftPaq'larını HP'nin resmi sunucularından indirir. Ev tipi (Pavilion/Envy/OMEN) modellerde BIOS çoğunlukla Windows Update veya HP Support Assistant ile gelir; HPIA bu cihazlarda sonuç vermeyebilir.",
    };

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(string toolPath, SystemInfo system, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var reportDir = Path.Combine(AppPaths.TempDirectory, "hpia-report");
        if (Directory.Exists(reportDir))
        {
            Directory.Delete(reportDir, recursive: true);
        }

        Directory.CreateDirectory(reportDir);

        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            ["/Operation:Analyze", "/Category:BIOS", "/Selection:All", "/Action:List", "/Silent", $"/ReportFolder:{reportDir}"])
        {
            Timeout = TimeSpan.FromMinutes(15),
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode is ExitNoRecommendations or ExitNoUpdatesAvailable)
        {
            return Array.Empty<UpdateItem>();
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"HP Image Assistant analizi başarısız (çıkış kodu {result.ExitCode}).");
        }

        var items = new List<UpdateItem>();
        foreach (var json in Directory.GetFiles(reportDir, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(json, cancellationToken).ConfigureAwait(false));
                foreach (var rec in FindRecommendations(doc.RootElement))
                {
                    var name = Get(rec, "Name") ?? Get(rec, "Title") ?? "HP BIOS";
                    var target = Get(rec, "TargetVersion") ?? Get(rec, "Version") ?? string.Empty;
                    var softpaq = Get(rec, "SoftPaqId") ?? Get(rec, "Id") ?? name;
                    var releaseType = Get(rec, "ReleaseType") ?? string.Empty;

                    items.Add(new UpdateItem
                    {
                        Id = $"hp:{softpaq}",
                        ProviderKey = OemFirmwareProvider.ProviderKey,
                        Name = name,
                        Category = UpdateCategory.Firmware,
                        Source = "HP Image Assistant",
                        CurrentVersion = Get(rec, "InstalledVersion") ?? system.BiosVersion,
                        AvailableVersion = target,
                        Publisher = "HP",
                        Severity = releaseType.ToLowerInvariant() switch
                        {
                            "critical" => UpdateSeverity.Critical,
                            "recommended" => UpdateSeverity.Important,
                            "routine" => UpdateSeverity.Low,
                            _ => UpdateSeverity.Unknown,
                        },
                        RequiresReboot = true,
                        ReleaseDate = DateTimeOffset.TryParse(Get(rec, "ReleaseDate"), out var d) ? d : null,
                        InfoUrl = Get(rec, "SoftPaqUrl") ?? "https://support.hp.com/drivers",
                        Description = "HP Image Assistant tarafından önerilen resmi BIOS SoftPaq'ı.",
                    });
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not parse HPIA report {File}", json);
            }
        }

        return items.GroupBy(i => i.Id).Select(g => g.First()).ToList();
    }

    public async Task<InstallResult> InstallAsync(string toolPath, UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var downloadDir = Path.Combine(AppPaths.TempDirectory, "hpia-softpaqs");
        var reportDir = Path.Combine(AppPaths.LogDirectory, "hpia");
        Directory.CreateDirectory(downloadDir);
        Directory.CreateDirectory(reportDir);

        progress?.Report(new InstallProgress(item.Id, "HP Image Assistant BIOS SoftPaq'ını indiriyor ve kuruyor…"));
        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            [
                "/Operation:Analyze", "/Category:BIOS", "/Selection:All", "/Action:Install", "/Silent",
                $"/SoftpaqDownloadFolder:{downloadDir}", $"/ReportFolder:{reportDir}",
            ])
        {
            Timeout = TimeSpan.FromMinutes(45),
            OnOutputLine = line => progress?.Report(new InstallProgress(item.Id, line.Trim())),
        }, cancellationToken).ConfigureAwait(false);

        return result.ExitCode switch
        {
            0 => InstallResult.Reboot("BIOS güncellemesi hazırlandı; yeniden başlatmada uygulanacak."),
            ExitRebootRequired => InstallResult.Reboot(),
            ExitNoRecommendations or ExitNoUpdatesAvailable => InstallResult.UpToDate(),
            _ => InstallResult.Fail($"HP Image Assistant çıkış kodu {result.ExitCode}.", result.ExitCode),
        };
    }

    private string? FindTool() => FirmwarePaths.FirstExisting(
    [
        _settings.Current.HpImageAssistantPath,
        Path.Combine(FirmwarePaths.ProgramFiles, "HPIA", "HPImageAssistant.exe"),
        Path.Combine(FirmwarePaths.ProgramFiles, "HP", "HPIA", "HPImageAssistant.exe"),
        Path.Combine(FirmwarePaths.ProgramFilesX86, "HP", "HP Image Assistant", "HPImageAssistant.exe"),
        Path.Combine(FirmwarePaths.ProgramFiles, "HP", "HP Image Assistant", "HPImageAssistant.exe"),
        @"C:\SWSetup\HPIA\HPImageAssistant.exe",
        @"C:\HPIA\HPImageAssistant.exe",
    ]);

    private static IEnumerable<JsonElement> FindRecommendations(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Name.Equals("Recommendations", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rec in prop.Value.EnumerateArray())
                    {
                        yield return rec;
                    }
                }
                else
                {
                    foreach (var nested in FindRecommendations(prop.Value))
                    {
                        yield return nested;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var nested in FindRecommendations(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string? Get(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var prop in e.EnumerateObject())
        {
            if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }
}
