namespace UpdateHub.Core.Models;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences persisted as JSON under %LOCALAPPDATA%\UpdateHub.</summary>
public sealed class AppSettings
{
    /// <summary>Only install releases from the stable channel. Pre-release items are listed but not auto-installed.</summary>
    public bool StableOnly { get; set; } = true;

    /// <summary>Only accept packages from these winget sources (official community repo + Microsoft Store).</summary>
    public List<string> AllowedWingetSources { get; set; } = ["winget", "msstore"];

    /// <summary>Package / update ids the user never wants to see in "update all".</summary>
    public List<string> ExcludedIds { get; set; } = [];

    /// <summary>
    /// winget packages that reported "cannot be upgraded using WinGet" (the publisher ships its own
    /// updater). They are shown as manual-only on later scans instead of failing again.
    /// </summary>
    public List<string> WingetManualIds { get; set; } = [];

    /// <summary>Include packages whose installed version winget cannot read in "update all".</summary>
    public bool IncludeUnknownVersionsInBulk { get; set; } = false;

    /// <summary>
    /// Package id → available version the user (or the post-install check) decided to skip, so a package
    /// whose installer never changes the reported version does not reappear on every scan.
    /// </summary>
    public Dictionary<string, string> SkippedVersions { get; set; } = new();

    public bool ScanOnStartup { get; set; } = true;

    public bool IncludeApplications { get; set; } = true;

    public bool IncludeWindowsUpdates { get; set; } = true;

    public bool IncludeDrivers { get; set; } = true;

    public bool IncludeFirmware { get; set; } = true;

    /// <summary>Firmware (BIOS/UEFI) is never flashed in bulk unless the user opts in; it is always shown.</summary>
    public bool AllowFirmwareInBulkUpdate { get; set; } = false;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Optional explicit path to HP Image Assistant (HPImageAssistant.exe).</summary>
    public string? HpImageAssistantPath { get; set; }

    /// <summary>Optional explicit path to winget.exe when auto-detection fails (e.g. elevated sessions).</summary>
    public string? WingetPath { get; set; }

    public int MaxLogEntries { get; set; } = 2000;
}
