using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using UpdateHub.Windows.Wmi;

namespace UpdateHub.Windows.Firmware;

/// <summary>
/// BIOS updates through Dell Command | Update (dcu-cli.exe). Packages come from Dell's own catalog
/// and are signed by Dell, so they are always official stable releases.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DellCommandUpdateStrategy : IOemFirmwareStrategy
{
    private const int ExitNoUpdates = 500;
    private const int ExitRebootRequired = 1;

    private readonly IProcessRunner _runner;
    private readonly ILogger<DellCommandUpdateStrategy> _logger;

    public DellCommandUpdateStrategy(IProcessRunner runner, ILogger<DellCommandUpdateStrategy> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public string OemKey => OemKeys.Dell;

    public FirmwareGuidance Describe(SystemInfo system) => new()
    {
        OemKey = OemKey,
        VendorName = "Dell",
        ToolName = "Dell Command | Update",
        ToolPath = FindTool(),
        ToolWingetId = "Dell.CommandUpdate",
        ToolDownloadUrl = "https://www.dell.com/support/kbdoc/000177325/dell-command-update",
        SupportUrl = "https://www.dell.com/support/home/",
        Notes = "BIOS güncellemeleri Dell Command | Update kataloğundan alınır ve bir sonraki yeniden başlatmada uygulanır. BitLocker otomatik olarak askıya alınır; dizüstü bilgisayarı güç adaptörüne takılı tutun.",
    };

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(string toolPath, SystemInfo system, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var reportDir = Path.Combine(AppPaths.TempDirectory, "dcu-report");
        Directory.CreateDirectory(reportDir);
        foreach (var old in Directory.GetFiles(reportDir, "*.xml"))
        {
            File.Delete(old);
        }

        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            ["/scan", "-updateType=bios,firmware", $"-report={reportDir}", "-silent"])
        {
            Timeout = TimeSpan.FromMinutes(10),
        }, cancellationToken).ConfigureAwait(false);

        if (result.ExitCode == ExitNoUpdates)
        {
            return Array.Empty<UpdateItem>();
        }

        if (result.ExitCode != 0 && result.ExitCode != ExitRebootRequired)
        {
            throw new InvalidOperationException($"dcu-cli /scan başarısız (çıkış kodu {result.ExitCode}). {Tail(result)}");
        }

        var reportFile = Path.Combine(reportDir, "DCUApplicableUpdates.xml");
        if (!File.Exists(reportFile))
        {
            _logger.LogWarning("Dell Command | Update did not write {Report}", reportFile);
            return Array.Empty<UpdateItem>();
        }

        var items = new List<UpdateItem>();
        var doc = XDocument.Load(reportFile);
        foreach (var update in doc.Descendants().Where(e => e.Name.LocalName.Equals("update", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Attr(update, "name") ?? "Dell BIOS";
            var version = Attr(update, "version") ?? string.Empty;
            var type = Attr(update, "type") ?? string.Empty;
            var urgency = Attr(update, "urgency") ?? string.Empty;
            var date = Attr(update, "date");
            var release = Attr(update, "release") ?? name;

            // -updateType already filters, but be defensive about non-BIOS entries in the report.
            var isBios = type.Contains("bios", StringComparison.OrdinalIgnoreCase) ||
                         type.Contains("firmware", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("bios", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("firmware", StringComparison.OrdinalIgnoreCase);
            if (!isBios)
            {
                continue;
            }

            items.Add(new UpdateItem
            {
                Id = $"dell:{release}",
                ProviderKey = OemFirmwareProvider.ProviderKey,
                Name = name,
                Category = UpdateCategory.Firmware,
                Source = "Dell Command | Update",
                CurrentVersion = system.BiosVersion,
                AvailableVersion = version,
                Publisher = "Dell",
                Severity = urgency.ToLowerInvariant() switch
                {
                    "urgent" or "critical" => UpdateSeverity.Critical,
                    "recommended" => UpdateSeverity.Important,
                    "optional" => UpdateSeverity.Low,
                    _ => UpdateSeverity.Unknown,
                },
                RequiresReboot = true,
                ReleaseDate = DateTimeOffset.TryParse(date, out var d) ? d : null,
                InfoUrl = "https://www.dell.com/support/home/",
                Description = "Dell Command | Update kataloğundan resmi BIOS/firmware paketi.",
            });
        }

        return items;
    }

    public async Task<InstallResult> InstallAsync(string toolPath, UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var logDir = Path.Combine(AppPaths.LogDirectory, "dcu");
        Directory.CreateDirectory(logDir);
        progress?.Report(new InstallProgress(item.Id, "Dell Command | Update BIOS paketini indiriyor ve hazırlıyor…"));

        var result = await _runner.RunAsync(new ProcessRequest(toolPath,
            [
                "/applyUpdates",
                "-updateType=bios,firmware",
                "-reboot=disable",
                "-autoSuspendBitLocker=enable",
                $"-outputLog={Path.Combine(logDir, "dcu-apply.log")}",
                "-silent",
            ])
        {
            Timeout = TimeSpan.FromMinutes(40),
            OnOutputLine = line => progress?.Report(new InstallProgress(item.Id, line.Trim())),
        }, cancellationToken).ConfigureAwait(false);

        return result.ExitCode switch
        {
            0 or ExitRebootRequired => InstallResult.Reboot("BIOS güncellemesi hazırlandı; yeniden başlatmada uygulanacak."),
            ExitNoUpdates => InstallResult.UpToDate(),
            _ => InstallResult.Fail($"dcu-cli /applyUpdates başarısız (çıkış kodu {result.ExitCode}). {Tail(result)}", result.ExitCode),
        };
    }

    private static string? FindTool() => FirmwarePaths.FirstExisting(
    [
        Path.Combine(FirmwarePaths.ProgramFiles, "Dell", "CommandUpdate", "dcu-cli.exe"),
        Path.Combine(FirmwarePaths.ProgramFilesX86, "Dell", "CommandUpdate", "dcu-cli.exe"),
    ]);

    private static string? Attr(XElement e, string name) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value
        ?? e.Elements().FirstOrDefault(c => c.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string Tail(ProcessResult r) =>
        string.Join(" ", (r.StandardOutput + r.StandardError)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(2));
}
