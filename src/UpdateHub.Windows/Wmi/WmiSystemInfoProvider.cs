using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;

namespace UpdateHub.Windows.Wmi;

[SupportedOSPlatform("windows")]
public sealed class WmiSystemInfoProvider : ISystemInfoProvider
{
    private readonly ILogger<WmiSystemInfoProvider> _logger;
    private SystemInfo? _cached;

    public WmiSystemInfoProvider(ILogger<WmiSystemInfoProvider> logger)
    {
        _logger = logger;
    }

    public Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return Task.FromResult(_cached);
        }

        return Task.Run(() =>
        {
            _cached = Collect();
            return _cached;
        }, cancellationToken);
    }

    private SystemInfo Collect()
    {
        var info = new SystemInfo
        {
            Architecture = RuntimeInformation.OSArchitecture.ToString(),
            IsElevated = IsElevated(),
        };

        try
        {
            var os = WmiQuery.Select("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
            if (os is not null)
            {
                info = info with
                {
                    OsName = os.Str("Caption").Replace("Microsoft ", string.Empty),
                    OsVersion = os.Str("Version"),
                    OsBuild = os.Str("BuildNumber"),
                };
            }

            info = info with { OsDisplayVersion = ReadDisplayVersion() };

            var cs = WmiQuery.Select("SELECT Manufacturer, Model, SystemFamily, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs is not null)
            {
                info = info with
                {
                    Manufacturer = cs.Str("Manufacturer"),
                    Model = cs.Str("Model"),
                    SystemFamily = cs.Str("SystemFamily"),
                    TotalMemoryBytes = cs.Long("TotalPhysicalMemory"),
                };
            }

            var board = WmiQuery.Select("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
            if (board is not null)
            {
                info = info with
                {
                    BaseBoardManufacturer = board.Str("Manufacturer"),
                    BaseBoardProduct = board.Str("Product"),
                };
            }

            var bios = WmiQuery.Select("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
            if (bios is not null)
            {
                info = info with
                {
                    BiosVendor = bios.Str("Manufacturer"),
                    BiosVersion = bios.Str("SMBIOSBIOSVersion"),
                    BiosReleaseDate = bios.CimDate("ReleaseDate"),
                };
            }

            var cpu = WmiQuery.Select("SELECT Name FROM Win32_Processor").FirstOrDefault();
            if (cpu is not null)
            {
                info = info with { Cpu = cpu.Str("Name") };
            }

            var gpus = WmiQuery.Select("SELECT Name, DriverVersion FROM Win32_VideoController")
                .Select(g => $"{g.Str("Name")} ({g.Str("DriverVersion")})")
                .ToList();
            info = info with { Gpus = gpus };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI system information is incomplete");
        }

        info = info with
        {
            FirmwareType = ReadFirmwareType(),
            SecureBootEnabled = ReadSecureBoot(),
        };

        info = info with { OemKey = OemKeys.Normalize(info.Manufacturer, info.BaseBoardManufacturer) };
        return info;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string ReadDisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var display = key?.GetValue("DisplayVersion") as string;
            var ubr = key?.GetValue("UBR") as int?;
            var build = key?.GetValue("CurrentBuild") as string;
            var full = ubr is null ? build : $"{build}.{ubr}";
            return string.IsNullOrEmpty(display) ? full ?? string.Empty : $"{display} ({full})";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static FirmwareType ReadFirmwareType()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            return (key?.GetValue("PEFirmwareType") as int?) switch
            {
                1 => FirmwareType.LegacyBios,
                2 => FirmwareType.Uefi,
                _ => FirmwareType.Unknown,
            };
        }
        catch
        {
            return FirmwareType.Unknown;
        }
    }

    private static bool? ReadSecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") as int? == 1;
        }
        catch
        {
            return null;
        }
    }
}

public static class OemKeys
{
    public const string Dell = "dell";
    public const string Lenovo = "lenovo";
    public const string Hp = "hp";
    public const string Asus = "asus";
    public const string Msi = "msi";
    public const string Gigabyte = "gigabyte";
    public const string Acer = "acer";
    public const string Microsoft = "microsoft";
    public const string AsRock = "asrock";
    public const string Samsung = "samsung";
    public const string Toshiba = "toshiba";
    public const string Fujitsu = "fujitsu";
    public const string Huawei = "huawei";
    public const string Monster = "monster";
    public const string Casper = "casper";
    public const string Unknown = "unknown";

    public static string Normalize(string? manufacturer, string? boardManufacturer)
    {
        var m = (manufacturer ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(m) || m.Contains("to be filled") || m.Contains("system manufacturer") || m == "default string")
        {
            m = (boardManufacturer ?? string.Empty).Trim().ToLowerInvariant();
        }

        if (m.StartsWith("dell"))
        {
            return Dell;
        }

        if (m.StartsWith("lenovo"))
        {
            return Lenovo;
        }

        if (m.StartsWith("hp") || m.StartsWith("hewlett"))
        {
            return Hp;
        }

        if (m.StartsWith("asus"))
        {
            return Asus;
        }

        if (m.StartsWith("micro-star") || m.StartsWith("msi"))
        {
            return Msi;
        }

        if (m.StartsWith("gigabyte") || m.StartsWith("giga-byte"))
        {
            return Gigabyte;
        }

        if (m.StartsWith("acer"))
        {
            return Acer;
        }

        if (m.StartsWith("microsoft"))
        {
            return Microsoft;
        }

        if (m.StartsWith("asrock"))
        {
            return AsRock;
        }

        if (m.StartsWith("samsung"))
        {
            return Samsung;
        }

        if (m.StartsWith("toshiba") || m.StartsWith("dynabook"))
        {
            return Toshiba;
        }

        if (m.StartsWith("fujitsu"))
        {
            return Fujitsu;
        }

        if (m.StartsWith("huawei"))
        {
            return Huawei;
        }

        if (m.StartsWith("monster"))
        {
            return Monster;
        }

        if (m.StartsWith("casper"))
        {
            return Casper;
        }

        return Unknown;
    }
}
