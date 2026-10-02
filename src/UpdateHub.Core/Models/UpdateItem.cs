namespace UpdateHub.Core.Models;

/// <summary>
/// High-level grouping used by the UI and the orchestrator.
/// </summary>
public enum UpdateCategory
{
    /// <summary>Desktop / Store applications (winget, msstore).</summary>
    Application,

    /// <summary>Windows quality, security and feature updates.</summary>
    WindowsUpdate,

    /// <summary>Device drivers delivered through Windows Update (WHQL signed).</summary>
    Driver,

    /// <summary>BIOS / UEFI and other system firmware.</summary>
    Firmware,
}

/// <summary>
/// Release channel of an update. Only <see cref="Stable"/> items are installed automatically.
/// </summary>
public enum UpdateChannel
{
    Stable,
    PreRelease,
    Unknown,
}

/// <summary>
/// Severity hint used for sorting and badges.
/// </summary>
public enum UpdateSeverity
{
    Unknown,
    Low,
    Moderate,
    Important,
    Critical,
}

/// <summary>
/// One installable (or at least detectable) update, independent of where it came from.
/// </summary>
public sealed record UpdateItem
{
    /// <summary>Stable identifier inside the provider (winget id, WUA UpdateID, OEM package id...).</summary>
    public required string Id { get; init; }

    /// <summary>Key of the <c>IUpdateProvider</c> that produced this item and can install it.</summary>
    public required string ProviderKey { get; init; }

    public required string Name { get; init; }

    public required UpdateCategory Category { get; init; }

    /// <summary>Human readable source, e.g. "winget", "msstore", "Windows Update", "Dell Command | Update".</summary>
    public required string Source { get; init; }

    public string? CurrentVersion { get; init; }

    public string? AvailableVersion { get; init; }

    /// <summary>
    /// The package manager could not read the installed version, so it cannot be sure the item is
    /// actually outdated (winget "Unknown"). Such items are listed but not bulk-installed by default.
    /// </summary>
    public bool IsInstalledVersionUnknown { get; init; }

    public string? Publisher { get; init; }

    public string? Description { get; init; }

    public UpdateChannel Channel { get; init; } = UpdateChannel.Stable;

    public UpdateSeverity Severity { get; init; } = UpdateSeverity.Unknown;

    public long? SizeBytes { get; init; }

    public bool RequiresReboot { get; init; }

    /// <summary>
    /// False when the provider can only detect the update and the user must finish it with an official tool
    /// (e.g. motherboard BIOS from the vendor's support page). <see cref="ManualInstructions"/> explains what to do.
    /// </summary>
    public bool CanInstallAutomatically { get; init; } = true;

    public string? ManualInstructions { get; init; }

    /// <summary>Official information / download page, if known.</summary>
    public string? InfoUrl { get; init; }

    public DateTimeOffset? ReleaseDate { get; init; }

    /// <summary>Opaque provider data (e.g. the COM update object) needed to install the item.</summary>
    public object? ProviderData { get; init; }

    public bool IsStable => Channel == UpdateChannel.Stable;

    public string DisplayVersionChange =>
        string.IsNullOrWhiteSpace(CurrentVersion)
            ? AvailableVersion ?? string.Empty
            : $"{CurrentVersion} → {AvailableVersion}";
}
