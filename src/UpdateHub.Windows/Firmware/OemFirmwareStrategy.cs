using UpdateHub.Core.Models;

namespace UpdateHub.Windows.Firmware;

/// <summary>What the UI shows on the firmware page for the detected manufacturer.</summary>
public sealed record FirmwareGuidance
{
    public required string OemKey { get; init; }
    public required string VendorName { get; init; }

    /// <summary>Official vendor tool that can scan and flash BIOS from the command line, if one exists.</summary>
    public string? ToolName { get; init; }
    public string? ToolPath { get; init; }
    public string? ToolWingetId { get; init; }
    public string? ToolDownloadUrl { get; init; }

    /// <summary>Official support / download page for this vendor.</summary>
    public required string SupportUrl { get; init; }

    public required string Notes { get; init; }

    public bool ToolInstalled => ToolPath is not null;

    public bool SupportsAutomation => ToolName is not null;
}

/// <summary>Vendor specific BIOS/UEFI automation using the manufacturer's own official tool.</summary>
public interface IOemFirmwareStrategy
{
    string OemKey { get; }

    FirmwareGuidance Describe(SystemInfo system);

    Task<IReadOnlyList<UpdateItem>> ScanAsync(string toolPath, SystemInfo system, IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    Task<InstallResult> InstallAsync(string toolPath, UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken);
}

internal static class FirmwarePaths
{
    public static string? FirstExisting(IEnumerable<string?> candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));

    public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
}
