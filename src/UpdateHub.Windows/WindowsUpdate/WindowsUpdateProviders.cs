using System.Runtime.Versioning;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;

namespace UpdateHub.Windows.WindowsUpdate;

/// <summary>Shared mapping/installation logic for the three Windows Update backed providers.</summary>
[SupportedOSPlatform("windows")]
public abstract class WindowsUpdateProviderBase : IUpdateProvider
{
    private readonly WindowsUpdateAgent _agent;

    protected WindowsUpdateProviderBase(WindowsUpdateAgent agent)
    {
        _agent = agent;
    }

    public abstract string Key { get; }

    public abstract string DisplayName { get; }

    public abstract UpdateCategory Category { get; }

    public string? UnavailableReason { get; private set; }

    protected abstract bool Accept(WuaUpdate update);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (!WindowsUpdateAgent.IsSupported)
        {
            UnavailableReason = "Windows Update Agent bu sistemde kullanılamıyor.";
            return Task.FromResult(false);
        }

        UnavailableReason = null;
        return Task.FromResult(true);
    }

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var updates = await _agent.SearchAsync(cancellationToken).ConfigureAwait(false);
        return updates.Where(Accept).Select(Map).ToList();
    }

    public async Task<InstallResult> InstallAsync(UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        if (item.ProviderData is not WuaUpdate update)
        {
            return InstallResult.Fail("Güncelleme verisi eksik; lütfen yeniden tarayın.");
        }

        var result = await _agent.InstallAsync(update, progress, cancellationToken).ConfigureAwait(false);
        _agent.Invalidate();
        return result;
    }

    private UpdateItem Map(WuaUpdate u)
    {
        var severity = u.MsrcSeverity.ToLowerInvariant() switch
        {
            "critical" => UpdateSeverity.Critical,
            "important" => UpdateSeverity.Important,
            "moderate" => UpdateSeverity.Moderate,
            "low" => UpdateSeverity.Low,
            _ => u.Categories.Any(c => c.Contains("Security", StringComparison.OrdinalIgnoreCase)) ? UpdateSeverity.Important : UpdateSeverity.Unknown,
        };

        var name = u.Title;
        if (u.IsDriver && !string.IsNullOrEmpty(u.DriverModel) && !name.Contains(u.DriverModel, StringComparison.OrdinalIgnoreCase))
        {
            name = $"{u.Title} – {u.DriverModel}";
        }

        var description = u.Description;
        if (u.IsDriver)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(u.DriverClass))
            {
                parts.Add($"Sınıf: {u.DriverClass}");
            }

            if (!string.IsNullOrEmpty(u.DriverHardwareId))
            {
                parts.Add($"Donanım kimliği: {u.DriverHardwareId}");
            }

            if (parts.Count > 0)
            {
                description = string.Join(" · ", parts) + (string.IsNullOrEmpty(description) ? string.Empty : "\n" + description);
            }
        }

        return new UpdateItem
        {
            Id = u.Key,
            ProviderKey = Key,
            Name = name,
            Category = Category,
            Source = "Windows Update",
            CurrentVersion = null,
            AvailableVersion = u.IsDriver ? u.DriverVersionDate?.ToString("yyyy-MM-dd") : (u.KbArticleIds.FirstOrDefault() ?? string.Empty),
            Publisher = u.IsDriver ? (string.IsNullOrEmpty(u.DriverProvider) ? u.DriverManufacturer : u.DriverProvider) : "Microsoft",
            Description = description,
            Channel = StableChannelPolicy.Classify(null, u.Title, null),
            Severity = severity,
            SizeBytes = u.SizeBytes,
            RequiresReboot = u.MayRequestReboot,
            InfoUrl = string.IsNullOrEmpty(u.SupportUrl) ? null : u.SupportUrl,
            ReleaseDate = u.LastDeploymentChangeTime,
            ProviderData = u,
        };
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSoftwareUpdateProvider : WindowsUpdateProviderBase
{
    public const string ProviderKey = "windows-update";

    public WindowsSoftwareUpdateProvider(WindowsUpdateAgent agent) : base(agent)
    {
    }

    public override string Key => ProviderKey;

    public override string DisplayName => "Windows Update";

    public override UpdateCategory Category => UpdateCategory.WindowsUpdate;

    protected override bool Accept(WuaUpdate update) => !update.IsDriver;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsDriverUpdateProvider : WindowsUpdateProviderBase
{
    public const string ProviderKey = "windows-driver";

    public WindowsDriverUpdateProvider(WindowsUpdateAgent agent) : base(agent)
    {
    }

    public override string Key => ProviderKey;

    public override string DisplayName => "Sürücüler (Windows Update)";

    public override UpdateCategory Category => UpdateCategory.Driver;

    protected override bool Accept(WuaUpdate update) => update.IsDriver && !update.IsFirmware;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsFirmwareUpdateProvider : WindowsUpdateProviderBase
{
    public const string ProviderKey = "windows-firmware";

    public WindowsFirmwareUpdateProvider(WindowsUpdateAgent agent) : base(agent)
    {
    }

    public override string Key => ProviderKey;

    public override string DisplayName => "Donanım yazılımı (Windows Update)";

    public override UpdateCategory Category => UpdateCategory.Firmware;

    protected override bool Accept(WuaUpdate update) => update.IsFirmware;
}
