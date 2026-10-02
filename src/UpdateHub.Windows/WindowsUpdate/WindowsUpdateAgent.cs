using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Models;

namespace UpdateHub.Windows.WindowsUpdate;

/// <summary>One update returned by the Windows Update Agent, with the COM object kept for installation.</summary>
public sealed record WuaUpdate
{
    public required string UpdateId { get; init; }
    public required int Revision { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public bool IsDriver { get; init; }
    public bool IsFirmware { get; init; }
    public string DriverClass { get; init; } = string.Empty;
    public string DriverManufacturer { get; init; } = string.Empty;
    public string DriverModel { get; init; } = string.Empty;
    public string DriverProvider { get; init; } = string.Empty;
    public DateTimeOffset? DriverVersionDate { get; init; }
    public string DriverHardwareId { get; init; } = string.Empty;
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> KbArticleIds { get; init; } = Array.Empty<string>();
    public string MsrcSeverity { get; init; } = string.Empty;
    public string SupportUrl { get; init; } = string.Empty;
    public bool MayRequestReboot { get; init; }
    public DateTimeOffset? LastDeploymentChangeTime { get; init; }
    public required object ComUpdate { get; init; }

    public string Key => $"{UpdateId}:{Revision}";
}

/// <summary>
/// Thin wrapper over the Windows Update Agent COM API (WUApi). Uses late binding so the project
/// does not need a generated interop assembly. One search is shared between the software, driver and
/// firmware providers that run concurrently during a scan.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateAgent
{
    private const string Criteria = "IsInstalled=0 and IsHidden=0";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);

    private readonly ILogger<WindowsUpdateAgent> _logger;
    private readonly object _gate = new();
    private Task<IReadOnlyList<WuaUpdate>>? _inFlight;
    private IReadOnlyList<WuaUpdate>? _cached;
    private DateTimeOffset _cachedAt;

    public WindowsUpdateAgent(ILogger<WindowsUpdateAgent> logger)
    {
        _logger = logger;
    }

    public static bool IsSupported => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Microsoft.Update.Session") is not null;

    public Task<IReadOnlyList<WuaUpdate>> SearchAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_inFlight is not null)
            {
                return _inFlight;
            }

            if (_cached is not null && DateTimeOffset.Now - _cachedAt < CacheLifetime)
            {
                return Task.FromResult(_cached);
            }

            var task = Task.Run(() => SearchCore(cancellationToken), cancellationToken);
            _inFlight = task;
            task.ContinueWith(t =>
            {
                lock (_gate)
                {
                    _inFlight = null;
                    if (t.IsCompletedSuccessfully)
                    {
                        _cached = t.Result;
                        _cachedAt = DateTimeOffset.Now;
                    }
                }
            }, TaskScheduler.Default);
            return task;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private IReadOnlyList<WuaUpdate> SearchCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dynamic session = CreateSession();
        dynamic searcher = session.CreateUpdateSearcher();
        searcher.Online = true;
        searcher.IncludePotentiallySupersededUpdates = false;

        _logger.LogInformation("Windows Update: searching ({Criteria})", Criteria);
        dynamic result = searcher.Search(Criteria);
        dynamic updates = result.Updates;
        int count = updates.Count;

        var list = new List<WuaUpdate>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic u = updates.Item(i);
            try
            {
                list.Add(Map(u));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping an update that could not be read");
            }
        }

        _logger.LogInformation("Windows Update: {Count} updates ({Drivers} drivers)", list.Count, list.Count(u => u.IsDriver));
        return list;
    }

    private static WuaUpdate Map(dynamic u)
    {
        string title = u.Title ?? string.Empty;
        int type = u.Type; // 1 = Software, 2 = Driver
        var isDriver = type == 2;

        var categories = new List<string>();
        try
        {
            dynamic cats = u.Categories;
            int catCount = cats.Count;
            for (var i = 0; i < catCount; i++)
            {
                categories.Add((string)cats.Item(i).Name);
            }
        }
        catch (COMException)
        {
        }

        var kbs = new List<string>();
        try
        {
            dynamic kb = u.KBArticleIDs;
            int kbCount = kb.Count;
            for (var i = 0; i < kbCount; i++)
            {
                kbs.Add("KB" + (string)kb.Item(i));
            }
        }
        catch (COMException)
        {
        }

        string driverClass = string.Empty, driverManufacturer = string.Empty, driverModel = string.Empty, driverProvider = string.Empty, hwId = string.Empty;
        DateTimeOffset? driverDate = null;
        if (isDriver)
        {
            driverClass = SafeString(() => (string)u.DriverClass);
            driverManufacturer = SafeString(() => (string)u.DriverManufacturer);
            driverModel = SafeString(() => (string)u.DriverModel);
            driverProvider = SafeString(() => (string)u.DriverProvider);
            hwId = SafeString(() => (string)u.DriverHardwareID);
            try
            {
                DateTime d = u.DriverVerDate;
                driverDate = d == default ? null : new DateTimeOffset(d);
            }
            catch (Exception)
            {
            }
        }

        var isFirmware = isDriver && (
            driverClass.Equals("Firmware", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("firmware", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("UEFI", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("BIOS", StringComparison.OrdinalIgnoreCase) ||
            categories.Any(c => c.Contains("Firmware", StringComparison.OrdinalIgnoreCase)));

        bool mayReboot = false;
        try
        {
            int rebootBehavior = u.InstallationBehavior.RebootBehavior; // 0 never, 1 always, 2 can request
            mayReboot = rebootBehavior != 0;
        }
        catch (Exception)
        {
        }

        long? size = null;
        try
        {
            decimal max = u.MaxDownloadSize;
            size = (long)max;
        }
        catch (Exception)
        {
        }

        DateTimeOffset? deployed = null;
        try
        {
            DateTime d = u.LastDeploymentChangeTime;
            deployed = d == default ? null : new DateTimeOffset(d);
        }
        catch (Exception)
        {
        }

        return new WuaUpdate
        {
            UpdateId = (string)u.Identity.UpdateID,
            Revision = (int)u.Identity.RevisionNumber,
            Title = title,
            Description = SafeString(() => (string)u.Description),
            SizeBytes = size,
            IsDriver = isDriver,
            IsFirmware = isFirmware,
            DriverClass = driverClass,
            DriverManufacturer = driverManufacturer,
            DriverModel = driverModel,
            DriverProvider = driverProvider,
            DriverVersionDate = driverDate,
            DriverHardwareId = hwId,
            Categories = categories,
            KbArticleIds = kbs,
            MsrcSeverity = SafeString(() => (string)u.MsrcSeverity),
            SupportUrl = SafeString(() => (string)u.SupportUrl),
            MayRequestReboot = mayReboot,
            LastDeploymentChangeTime = deployed,
            ComUpdate = u,
        };
    }

    /// <summary>Downloads and installs a single update. Must not be called concurrently.</summary>
    public Task<InstallResult> InstallAsync(WuaUpdate update, IProgress<InstallProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            dynamic session = CreateSession();
            dynamic collection = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!)!;
            dynamic com = update.ComUpdate;

            try
            {
                if (!(bool)com.EulaAccepted)
                {
                    com.AcceptEula();
                }
            }
            catch (COMException ex)
            {
                _logger.LogWarning(ex, "AcceptEula failed for {Title}", update.Title);
            }

            collection.Add(com);

            if (!(bool)com.IsDownloaded)
            {
                progress?.Report(new InstallProgress(update.Key, "İndiriliyor…"));
                dynamic downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                dynamic downloadResult = downloader.Download();
                int downloadCode = downloadResult.ResultCode;
                if (downloadCode is not (2 or 3))
                {
                    var hr = TryGetHResult(downloadResult);
                    return InstallResult.Fail($"İndirme başarısız (kod {downloadCode}{hr}).", downloadCode);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new InstallProgress(update.Key, "Kuruluyor…"));
            dynamic installer = session.CreateUpdateInstaller();
            installer.Updates = collection;
            try
            {
                installer.ForceQuiet = true;
            }
            catch (Exception)
            {
                // Older agents do not expose IUpdateInstaller2.
            }

            dynamic installResult = installer.Install();
            int code = installResult.ResultCode; // 2 succeeded, 3 succeeded with errors, 4 failed, 5 aborted
            bool reboot = installResult.RebootRequired;

            return code switch
            {
                2 or 3 when reboot => InstallResult.Reboot(),
                2 => InstallResult.Ok(),
                3 => InstallResult.Ok("Kuruldu (bazı uyarılarla)."),
                5 => InstallResult.Cancelled(),
                _ => InstallResult.Fail($"Kurulum başarısız (kod {code}{TryGetHResult(installResult)}).", code),
            };
        }, cancellationToken);

    private static string TryGetHResult(dynamic result)
    {
        try
        {
            int hr = result.GetUpdateResult(0).HResult;
            return hr == 0 ? string.Empty : $", HRESULT 0x{hr:X8}";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static object CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new PlatformNotSupportedException("Windows Update Agent bulunamadı.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "UpdateHub";
        return session;
    }

    private static string SafeString(Func<string> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
