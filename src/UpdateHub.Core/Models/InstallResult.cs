namespace UpdateHub.Core.Models;

public enum InstallOutcome
{
    Success,
    AlreadyUpToDate,
    RebootRequired,
    ManualActionRequired,
    Cancelled,
    Failed,
}

public sealed record InstallResult(InstallOutcome Outcome, string? Message = null, int? ExitCode = null)
{
    public bool IsSuccess => Outcome is InstallOutcome.Success or InstallOutcome.AlreadyUpToDate or InstallOutcome.RebootRequired;

    public bool RebootRequired => Outcome == InstallOutcome.RebootRequired;

    public static InstallResult Ok(string? message = null) => new(InstallOutcome.Success, message);

    public static InstallResult Reboot(string? message = null) => new(InstallOutcome.RebootRequired, message);

    public static InstallResult Fail(string message, int? exitCode = null) => new(InstallOutcome.Failed, message, exitCode);

    public static InstallResult Manual(string message) => new(InstallOutcome.ManualActionRequired, message);

    public static InstallResult UpToDate() => new(InstallOutcome.AlreadyUpToDate);

    public static InstallResult Cancelled() => new(InstallOutcome.Cancelled);
}

/// <summary>Progress reported while scanning a single provider.</summary>
public sealed record ScanProgress(string ProviderKey, string Message, double? Percent = null);

/// <summary>Progress reported while installing a single item.</summary>
public sealed record InstallProgress(string ItemId, string Message, double? Percent = null);
