using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;

namespace UpdateHub.Windows.Wmi;

/// <summary>
/// Lists every PnP device together with the driver currently installed for it
/// (Win32_PnPSignedDriver) and the device state (Win32_PnPEntity.ConfigManagerErrorCode).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiDeviceInventory : IDeviceInventory
{
    private readonly ILogger<WmiDeviceInventory> _logger;

    public WmiDeviceInventory(ILogger<WmiDeviceInventory> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<DeviceInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var status = new Dictionary<string, (int Code, DeviceStatus Status)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var row in WmiQuery.Select("SELECT DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity"))
                {
                    var code = (int)row.Long("ConfigManagerErrorCode");
                    var st = code switch
                    {
                        0 => DeviceStatus.Ok,
                        22 => DeviceStatus.Disabled,
                        _ => DeviceStatus.Problem,
                    };
                    status[row.Str("DeviceID")] = (code, st);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Win32_PnPEntity query failed");
            }

            var devices = new List<DeviceInfo>();
            try
            {
                foreach (var row in WmiQuery.Select(
                             "SELECT DeviceID, DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, IsSigned, Signer, InfName FROM Win32_PnPSignedDriver"))
                {
                    var id = row.Str("DeviceID");
                    var name = row.Str("DeviceName");
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    status.TryGetValue(id, out var st);
                    devices.Add(new DeviceInfo
                    {
                        DeviceId = id,
                        Name = name,
                        DeviceClass = row.Str("DeviceClass"),
                        Manufacturer = row.Str("Manufacturer"),
                        DriverProvider = row.Str("DriverProviderName"),
                        DriverVersion = row.Str("DriverVersion"),
                        DriverDate = row.CimDate("DriverDate"),
                        IsSigned = row.Bool("IsSigned"),
                        Signer = row.Str("Signer"),
                        InfName = row.Str("InfName"),
                        Status = status.ContainsKey(id) ? st.Status : DeviceStatus.Unknown,
                        ProblemCode = st.Code,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Win32_PnPSignedDriver query failed");
            }

            return devices
                .OrderBy(d => d.Status == DeviceStatus.Problem ? 0 : 1)
                .ThenBy(d => d.DeviceClass)
                .ThenBy(d => d.Name)
                .ToList();
        }, cancellationToken);
}
