using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;

namespace UpdateHub.Windows.Winget;

/// <summary>
/// Application updates through the Windows Package Manager. Manifests in the official <c>winget</c>
/// repository point at the vendors' own download URLs (hash verified), and <c>msstore</c> is the Microsoft Store,
/// so every item here comes from an official distribution channel.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WingetProvider : IUpdateProvider
{
    public const string ProviderKey = "winget";

    // HRESULTs documented at https://github.com/microsoft/winget-cli/blob/master/doc/windows/package-manager/winget/returnCodes.md
    private const int NoApplicationsFound = unchecked((int)0x8A150010);
    private const int NoApplicableInstaller = unchecked((int)0x8A150011);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int InstallPackageInUse = unchecked((int)0x8A150101);
    private const int InstallInProgress = unchecked((int)0x8A150102);
    private const int InstallRebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int InstallRebootRequiredForInstall = unchecked((int)0x8A15010A);
    private const int InstallRebootInitiated = unchecked((int)0x8A15010B);
    private const int InstallCancelledByUser = unchecked((int)0x8A15010C);
    private const int InstallAlreadyInstalled = unchecked((int)0x8A15010D);
    private const int InstallBlockedByPolicy = unchecked((int)0x8A15010F);

    private readonly WingetLocator _locator;
    private readonly IProcessRunner _runner;
    private readonly ISettingsService _settings;
    private readonly ILogger<WingetProvider> _logger;

    public WingetProvider(WingetLocator locator, IProcessRunner runner, ISettingsService settings, ILogger<WingetProvider> logger)
    {
        _locator = locator;
        _runner = runner;
        _settings = settings;
        _logger = logger;
    }

    public string Key => ProviderKey;

    public string DisplayName => "Uygulamalar (winget / Microsoft Store)";

    public UpdateCategory Category => UpdateCategory.Application;

    public string? UnavailableReason { get; private set; }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var exe = _locator.Resolve();
        if (exe is null)
        {
            UnavailableReason = "winget bulunamadı. Microsoft Store'dan \"Uygulama Yükleyici\" (App Installer) paketini kurun.";
            return false;
        }

        var result = await _runner.RunAsync(new ProcessRequest(exe, ["--version"]) { Timeout = TimeSpan.FromSeconds(30) }, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            UnavailableReason = $"winget çalıştırılamadı (çıkış kodu {result.ExitCode}).";
            return false;
        }

        _logger.LogInformation("winget {Version} at {Path}", result.StandardOutput.Trim(), exe);
        UnavailableReason = null;
        return true;
    }

    public async Task<IReadOnlyList<UpdateItem>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var exe = _locator.Resolve() ?? throw new InvalidOperationException("winget bulunamadı.");

        var result = await _runner.RunAsync(new ProcessRequest(exe,
            [
                "upgrade",
                "--include-unknown",
                "--accept-source-agreements",
                "--disable-interactivity",
            ])
        {
            Timeout = TimeSpan.FromMinutes(5),
        }, cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            throw new TimeoutException("winget upgrade zaman aşımına uğradı.");
        }

        // winget returns NO_APPLICATIONS_FOUND when nothing needs an upgrade; that is not an error.
        if (result.ExitCode != 0 && result.ExitCode != NoApplicationsFound)
        {
            _logger.LogWarning("winget upgrade exited with {Code}: {Err}", result.ExitCode, result.StandardError);
        }

        var allowedSources = _settings.Current.AllowedWingetSources;
        var rows = WingetOutputParser.ParseUpgradeTable(result.StandardOutput);
        var items = new List<UpdateItem>();

        foreach (var row in rows)
        {
            if (allowedSources.Count > 0 && !allowedSources.Contains(row.Source, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping {Id}: source {Source} is not in the allowed list", row.Id, row.Source);
                continue;
            }

            var channel = StableChannelPolicy.Classify(row.Id, row.Name, row.Available);
            items.Add(new UpdateItem
            {
                Id = row.Id,
                ProviderKey = Key,
                Name = row.Name,
                Category = UpdateCategory.Application,
                Source = row.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase) ? "Microsoft Store" : "winget",
                CurrentVersion = row.IsVersionUnknown ? null : row.Version,
                AvailableVersion = row.Available,
                Channel = channel,
                Publisher = row.Id.Contains('.') ? row.Id[..row.Id.IndexOf('.')] : null,
                InfoUrl = row.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase)
                    ? $"https://apps.microsoft.com/detail/{row.Id}"
                    : $"https://github.com/microsoft/winget-pkgs/tree/master/manifests/{char.ToLowerInvariant(row.Id[0])}/{row.Id.Replace('.', '/')}",
                ProviderData = row,
            });
        }

        progress?.Report(new ScanProgress(Key, $"{items.Count} uygulama güncellemesi bulundu", 100));
        return items;
    }

    public async Task<InstallResult> InstallAsync(UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var exe = _locator.Resolve() ?? throw new InvalidOperationException("winget bulunamadı.");
        var row = item.ProviderData as WingetUpgradeRow;

        var args = new List<string>
        {
            "upgrade",
            "--id", item.Id,
            "--exact",
            "--silent",
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--disable-interactivity",
        };

        if (row is not null)
        {
            args.AddRange(["--source", row.Source]);
            if (row.IsVersionUnknown)
            {
                args.Add("--include-unknown");
            }
        }

        var lastLine = string.Empty;
        var result = await _runner.RunAsync(new ProcessRequest(exe, args)
        {
            Timeout = TimeSpan.FromMinutes(45),
            OnOutputLine = line =>
            {
                var clean = line.Trim();
                if (clean.Length > 0 && clean != lastLine)
                {
                    lastLine = clean;
                    progress?.Report(new InstallProgress(item.Id, clean));
                }
            },
        }, cancellationToken).ConfigureAwait(false);

        if (result.TimedOut)
        {
            return InstallResult.Fail("Kurulum zaman aşımına uğradı.", result.ExitCode);
        }

        if (result.ExitCode == 0)
        {
            return await VerifyInstalledVersionAsync(exe, item, row, cancellationToken).ConfigureAwait(false);
        }

        return result.ExitCode switch
        {
            UpdateNotApplicable or InstallAlreadyInstalled => InstallResult.UpToDate(),
            InstallRebootRequiredToFinish or InstallRebootRequiredForInstall or InstallRebootInitiated =>
                InstallResult.Reboot("Kurulumun tamamlanması için yeniden başlatma gerekiyor."),
            InstallCancelledByUser => InstallResult.Cancelled(),
            InstallPackageInUse => InstallResult.Fail("Uygulama şu anda kullanımda. Kapatıp tekrar deneyin.", result.ExitCode),
            InstallInProgress => InstallResult.Fail("Başka bir kurulum devam ediyor.", result.ExitCode),
            InstallBlockedByPolicy => InstallResult.Fail("Kurulum grup ilkesi tarafından engellendi.", result.ExitCode),
            NoApplicableInstaller => InstallResult.Fail("Bu sistem için uygun bir yükleyici yok.", result.ExitCode),
            NoApplicationsFound => InstallResult.Fail("Paket bulunamadı.", result.ExitCode),
            _ => InstallResult.Fail(Tail(result.StandardOutput + result.StandardError) ?? $"winget çıkış kodu 0x{result.ExitCode:X8}", result.ExitCode),
        };
    }

    /// <summary>
    /// winget exit code 0 only says the installer ran; ask winget what is installed now so the user
    /// sees "verified: version X" instead of having to trust the exit code.
    /// </summary>
    private async Task<InstallResult> VerifyInstalledVersionAsync(string exe, UpdateItem item, WingetUpgradeRow? row, CancellationToken cancellationToken)
    {
        try
        {
            var args = new List<string> { "list", "--id", item.Id, "--exact", "--accept-source-agreements", "--disable-interactivity" };
            if (row is not null)
            {
                args.AddRange(["--source", row.Source]);
            }

            var list = await _runner.RunAsync(new ProcessRequest(exe, args) { Timeout = TimeSpan.FromSeconds(90) }, cancellationToken).ConfigureAwait(false);
            var installed = WingetOutputParser.ParseListTable(list.StandardOutput)
                .FirstOrDefault(r => r.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));

            if (installed is null || string.IsNullOrWhiteSpace(installed.Version))
            {
                return InstallResult.Ok("Kurulum tamamlandı (winget sürümü doğrulayamadı).");
            }

            if (string.IsNullOrWhiteSpace(item.AvailableVersion) || VersionComparer.Compare(installed.Version, item.AvailableVersion) >= 0)
            {
                return InstallResult.Ok($"Doğrulandı: yüklü sürüm {installed.Version}");
            }

            _logger.LogWarning("{Id}: winget reported success but installed version is {Installed}, expected {Expected}", item.Id, installed.Version, item.AvailableVersion);
            return InstallResult.Ok($"Yükleyici tamamlandı ancak winget hâlâ {installed.Version} görüyor; uygulama bir sonraki açılışta güncellenebilir.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Post-install verification failed for {Id}", item.Id);
            return InstallResult.Ok("Kurulum tamamlandı (sürüm doğrulaması yapılamadı).");
        }
    }

    /// <summary>Installs a package that is not yet present (used for the OEM firmware tools).</summary>
    public async Task<InstallResult> InstallPackageAsync(string packageId, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var exe = _locator.Resolve() ?? throw new InvalidOperationException("winget bulunamadı.");
        var result = await _runner.RunAsync(new ProcessRequest(exe,
            [
                "install",
                "--id", packageId,
                "--exact",
                "--silent",
                "--source", "winget",
                "--accept-package-agreements",
                "--accept-source-agreements",
                "--disable-interactivity",
            ])
        {
            Timeout = TimeSpan.FromMinutes(20),
            OnOutputLine = line => progress?.Report(new InstallProgress(packageId, line.Trim())),
        }, cancellationToken).ConfigureAwait(false);

        return result.ExitCode switch
        {
            0 => InstallResult.Ok(),
            InstallAlreadyInstalled => InstallResult.UpToDate(),
            InstallRebootRequiredToFinish or InstallRebootRequiredForInstall => InstallResult.Reboot(),
            _ => InstallResult.Fail(Tail(result.StandardOutput + result.StandardError) ?? $"winget çıkış kodu 0x{result.ExitCode:X8}", result.ExitCode),
        };
    }

    private static string? Tail(string text)
    {
        var lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.IndexOfAny(['█', '▒', '░']) < 0)
            .ToList();
        return lines.Count == 0 ? null : string.Join(" ", lines.TakeLast(2));
    }
}
