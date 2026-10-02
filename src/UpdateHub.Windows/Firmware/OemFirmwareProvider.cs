using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Windows.Wmi;

namespace UpdateHub.Windows.Firmware;

/// <summary>
/// BIOS / UEFI updates through the manufacturer's official tool. For vendors without a scriptable tool
/// (most desktop motherboards) the provider reports nothing and the UI points at the official support page.
/// Firmware is never part of "update everything" unless the user opts in.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OemFirmwareProvider : IUpdateProvider
{
    public const string ProviderKey = "oem-firmware";

    private readonly ISystemInfoProvider _systemInfo;
    private readonly IReadOnlyDictionary<string, IOemFirmwareStrategy> _strategies;
    private readonly ILogger<OemFirmwareProvider> _logger;

    public OemFirmwareProvider(ISystemInfoProvider systemInfo, IEnumerable<IOemFirmwareStrategy> strategies, ILogger<OemFirmwareProvider> logger)
    {
        _systemInfo = systemInfo;
        _strategies = strategies.ToDictionary(s => s.OemKey, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public string Key => ProviderKey;

    public string DisplayName => "BIOS / UEFI (üretici aracı)";

    public UpdateCategory Category => UpdateCategory.Firmware;

    public string? UnavailableReason { get; private set; }

    public async Task<FirmwareGuidance> GetGuidanceAsync(CancellationToken cancellationToken)
    {
        var system = await _systemInfo.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false);
        return _strategies.TryGetValue(system.OemKey, out var strategy)
            ? strategy.Describe(system)
            : VendorGuidance.Describe(system);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var guidance = await GetGuidanceAsync(cancellationToken).ConfigureAwait(false);
        if (!guidance.SupportsAutomation)
        {
            UnavailableReason = $"{guidance.VendorName} için komut satırından çalışan resmi bir BIOS aracı yok. Windows Update'teki donanım yazılımı güncellemeleri yine de taranır.";
            return false;
        }

        if (!guidance.ToolInstalled)
        {
            UnavailableReason = $"{guidance.ToolName} kurulu değil.";
            return false;
        }

        UnavailableReason = null;
        return true;
    }

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var system = await _systemInfo.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!_strategies.TryGetValue(system.OemKey, out var strategy))
        {
            return Array.Empty<UpdateItem>();
        }

        var guidance = strategy.Describe(system);
        if (guidance.ToolPath is null)
        {
            return Array.Empty<UpdateItem>();
        }

        progress?.Report(new ScanProgress(Key, $"{guidance.ToolName} BIOS kataloğunu sorguluyor…"));
        var items = await strategy.ScanAsync(guidance.ToolPath, system, progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("{Tool}: {Count} firmware update(s)", guidance.ToolName, items.Count);
        return items;
    }

    public async Task<InstallResult> InstallAsync(UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var system = await _systemInfo.GetSystemInfoAsync(cancellationToken).ConfigureAwait(false);
        if (!_strategies.TryGetValue(system.OemKey, out var strategy))
        {
            return InstallResult.Manual("Bu üretici için otomatik BIOS kurulumu desteklenmiyor.");
        }

        var guidance = strategy.Describe(system);
        if (guidance.ToolPath is null)
        {
            return InstallResult.Manual($"{guidance.ToolName} kurulu değil.");
        }

        return await strategy.InstallAsync(guidance.ToolPath, item, progress, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Official support pages for manufacturers that do not ship a scriptable BIOS updater.</summary>
public static class VendorGuidance
{
    public static FirmwareGuidance Describe(SystemInfo system)
    {
        var (vendor, url, notes) = system.OemKey switch
        {
            OemKeys.Asus => ("ASUS", "https://www.asus.com/support/download-center/",
                "BIOS güncellemeleri MyASUS / Armoury Crate uygulamalarıyla veya destek sayfasından indirilen dosyanın UEFI içindeki EZ Flash ile yüklenmesiyle yapılır."),
            OemKeys.Msi => ("MSI", "https://www.msi.com/support/download",
                "BIOS güncellemeleri MSI Center üzerinden veya destek sayfasından indirilen dosyanın M-Flash ile yüklenmesiyle yapılır."),
            OemKeys.Gigabyte => ("GIGABYTE", "https://www.gigabyte.com/Support",
                "BIOS güncellemeleri GIGABYTE Control Center veya UEFI içindeki Q-Flash ile yapılır."),
            OemKeys.AsRock => ("ASRock", "https://www.asrock.com/support/index.asp",
                "BIOS güncellemeleri destek sayfasından indirilen dosyanın Instant Flash ile yüklenmesiyle yapılır."),
            OemKeys.Acer => ("Acer", "https://www.acer.com/support",
                "BIOS güncellemeleri Acer Care Center veya destek sayfasındaki resmi yükleyici ile yapılır."),
            OemKeys.Microsoft => ("Microsoft Surface", "https://support.microsoft.com/surface",
                "Surface cihazlarında UEFI ve donanım yazılımı tamamen Windows Update ile dağıtılır; ayrı bir araç gerekmez."),
            OemKeys.Samsung => ("Samsung", "https://www.samsung.com/support/",
                "BIOS güncellemeleri Samsung Update uygulamasıyla yapılır."),
            OemKeys.Toshiba => ("Dynabook / Toshiba", "https://support.dynabook.com/",
                "BIOS güncellemeleri destek sayfasındaki resmi yükleyici ile yapılır."),
            OemKeys.Fujitsu => ("Fujitsu", "https://support.ts.fujitsu.com/",
                "BIOS güncellemeleri DeskUpdate veya destek sayfasındaki resmi yükleyici ile yapılır."),
            OemKeys.Huawei => ("Huawei", "https://consumer.huawei.com/en/support/",
                "BIOS güncellemeleri PC Manager uygulamasıyla yapılır."),
            OemKeys.Monster => ("Monster Notebook", "https://www.monsternotebook.com.tr/",
                "BIOS güncellemeleri Monster destek sayfasındaki resmi yükleyici ile yapılır."),
            OemKeys.Casper => ("Casper", "https://www.casper.com.tr/",
                "BIOS güncellemeleri Casper destek sayfasındaki resmi yükleyici ile yapılır."),
            _ => (string.IsNullOrWhiteSpace(system.Manufacturer) ? "Bilinmeyen üretici" : system.Manufacturer, "https://www.bing.com/search?q=" + Uri.EscapeDataString($"{system.Manufacturer} {system.Model} BIOS download"),
                "Bu üretici için otomatik BIOS güncellemesi desteklenmiyor. BIOS dosyasını yalnızca üreticinin resmi destek sayfasından indirin."),
        };

        return new FirmwareGuidance
        {
            OemKey = system.OemKey,
            VendorName = vendor,
            SupportUrl = url,
            Notes = notes,
        };
    }
}
