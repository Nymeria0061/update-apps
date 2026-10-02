namespace UpdateHub.Core.Models;

public enum FirmwareType
{
    Unknown,
    LegacyBios,
    Uefi,
}

/// <summary>
/// Snapshot of the machine the app is running on. Shown on the dashboard and used to pick the right OEM firmware tool.
/// </summary>
public sealed record SystemInfo
{
    public string OsName { get; init; } = "Windows";
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string OsDisplayVersion { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;

    public string Manufacturer { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string SystemFamily { get; init; } = string.Empty;

    public string BaseBoardManufacturer { get; init; } = string.Empty;
    public string BaseBoardProduct { get; init; } = string.Empty;

    public string BiosVendor { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public DateTimeOffset? BiosReleaseDate { get; init; }
    public FirmwareType FirmwareType { get; init; }
    public bool? SecureBootEnabled { get; init; }

    public string Cpu { get; init; } = string.Empty;
    public long TotalMemoryBytes { get; init; }
    public IReadOnlyList<string> Gpus { get; init; } = Array.Empty<string>();

    public bool IsElevated { get; init; }

    /// <summary>Normalised OEM key (dell, lenovo, hp, asus, msi, gigabyte, acer, microsoft, ...).</summary>
    public string OemKey { get; init; } = "unknown";
}

public enum DeviceStatus
{
    Ok,
    Problem,
    Disabled,
    Unknown,
}

/// <summary>A PnP device with its currently installed driver.</summary>
public sealed record DeviceInfo
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public string DeviceClass { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string DriverProvider { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public DateTimeOffset? DriverDate { get; init; }
    public bool IsSigned { get; init; }
    public string Signer { get; init; } = string.Empty;
    public string InfName { get; init; } = string.Empty;
    public DeviceStatus Status { get; init; } = DeviceStatus.Unknown;
    public int ProblemCode { get; init; }
}
