using UpdateHub.Core.Models;

namespace UpdateHub.Core.Abstractions;

/// <summary>
/// A source of updates (winget, Windows Update, an OEM firmware tool...).
/// Providers must only ever return releases published through official channels.
/// </summary>
public interface IUpdateProvider
{
    /// <summary>Stable key stored in <see cref="UpdateItem.ProviderKey"/>.</summary>
    string Key { get; }

    string DisplayName { get; }

    UpdateCategory Category { get; }

    /// <summary>
    /// Whether the provider can work on this machine (e.g. winget installed, OEM tool present).
    /// A provider that is not available is skipped silently; <see cref="UnavailableReason"/> explains why.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    string? UnavailableReason { get; }

    Task<IReadOnlyList<UpdateItem>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken);

    Task<InstallResult> InstallAsync(UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken);
}

public interface ISystemInfoProvider
{
    Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken);
}

public interface IDeviceInventory
{
    Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken);
}

/// <summary>Runs a console tool and captures its output. Abstracted so providers can be unit tested.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Called for every line of stdout/stderr as it arrives (for live progress).</summary>
    public Action<string>? OnOutputLine { get; init; }
}

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
