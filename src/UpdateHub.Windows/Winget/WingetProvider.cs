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
    private const int InstallUpgradeNotSupported = unchecked((int)0x8A150114);

    public const string ManualUpgradeMessage =
        "Bu uygulama winget ile yükseltilemiyor; yayıncı kendi güncelleyicisini kullanıyor. Uygulamayı açıp kendi içinden güncelleyin (ya da ⋯ menüsünden yoksayın).";

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
        var skippedCount = 0;

        foreach (var row in rows)
        {
            if (allowedSources.Count > 0 && !allowedSources.Contains(row.Source, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping {Id}: source {Source} is not in the allowed list", row.Id, row.Source);
                continue;
            }

            if (IsVersionSkipped(row.Id, row.Available))
            {
                skippedCount++;
                _logger.LogInformation("{Id} {Version} is on the skipped-version list; hidden", row.Id, row.Available);
                continue;
            }

            var channel = StableChannelPolicy.Classify(row.Id, row.Name, row.Available);
            var manualOnly = _settings.Current.WingetManualIds.Contains(row.Id, StringComparer.OrdinalIgnoreCase);
            items.Add(new UpdateItem
            {
                Id = row.Id,
                ProviderKey = Key,
                Name = row.Name,
                Category = UpdateCategory.Application,
                Source = row.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase) ? "Microsoft Store" : "winget",
                CurrentVersion = row.IsVersionUnknown ? null : row.Version,
                AvailableVersion = row.Available,
                IsInstalledVersionUnknown = row.IsVersionUnknown,
                CanInstallAutomatically = !manualOnly,
                ManualInstructions = manualOnly ? ManualUpgradeMessage : null,
                Channel = channel,
                Publisher = row.Id.Contains('.') ? row.Id[..row.Id.IndexOf('.')] : null,
                InfoUrl = row.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase)
                    ? $"https://apps.microsoft.com/detail/{row.Id}"
                    : $"https://github.com/microsoft/winget-pkgs/tree/master/manifests/{char.ToLowerInvariant(row.Id[0])}/{row.Id.Replace('.', '/')}",
                ProviderData = row,
            });
        }

        progress?.Report(new ScanProgress(Key, $"{items.Count} uygulama güncellemesi bulundu" + (skippedCount > 0 ? $" ({skippedCount} atlanan sürüm gizlendi)" : string.Empty), 100));
        return items;
    }

    private bool IsVersionSkipped(string id, string version) =>
        _settings.Current.SkippedVersions.Any(kv =>
            kv.Key.Equals(id, StringComparison.OrdinalIgnoreCase) && kv.Value.Equals(version, StringComparison.OrdinalIgnoreCase));

    /// <summary>Hides <paramref name="version"/> of <paramref name="id"/> on later scans; a newer version shows up again.</summary>
    public async Task SkipVersionAsync(string id, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var skipped = _settings.Current.SkippedVersions;
        var existing = skipped.Keys.FirstOrDefault(k => k.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            skipped.Remove(existing);
        }

        skipped[id] = version;
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist skipped version for {Id}", id);
        }
    }

    /// <summary>Official URLs from the package manifest (homepage, installer, support).</summary>
    public async Task<PackageLinks?> GetLinksAsync(string id, string? source, CancellationToken cancellationToken)
    {
        var exe = _locator.Resolve();
        if (exe is null)
        {
            return null;
        }

        var args = new List<string> { "show", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity" };
        if (!string.IsNullOrEmpty(source))
        {
            args.AddRange(["--source", source]);
        }

        var result = await _runner.RunAsync(new ProcessRequest(exe, args) { Timeout = TimeSpan.FromSeconds(60) }, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        return WingetShowParser.ParseLinks(result.StandardOutput);
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

        if (result.ExitCode == InstallUpgradeNotSupported || IsUpgradeNotSupportedMessage(result.StandardOutput + result.StandardError))
        {
            await RememberManualOnlyAsync(item.Id).ConfigureAwait(false);
            return InstallResult.Manual(ManualUpgradeMessage);
        }

        var output = result.StandardOutput + result.StandardError;
        _logger.LogWarning("winget upgrade {Id} ended with 0x{Code:X8}: {Tail}", item.Id, result.ExitCode, Tail(output));

        return result.ExitCode switch
        {
            UpdateNotApplicable or InstallAlreadyInstalled => InstallResult.UpToDate(),
            InstallRebootRequiredToFinish or InstallRebootRequiredForInstall or InstallRebootInitiated =>
                InstallResult.Reboot("Kurulumun tamamlanması için yeniden başlatma gerekiyor."),
            InstallCancelledByUser => InstallResult.Cancelled(),
            InstallPackageInUse => InstallResult.Fail("Uygulama şu anda kullanımda. Kapatıp tekrar deneyin.", result.ExitCode),
            InstallInProgress => InstallResult.Fail("Başka bir kurulum devam ediyor. Bitmesini bekleyip tekrar deneyin.", result.ExitCode),
            InstallBlockedByPolicy => InstallResult.Fail("Kurulum grup ilkesi tarafından engellendi.", result.ExitCode),
            NoApplicableInstaller => InstallResult.Fail("Bu sistem (mimari/sürüm) için uygun bir yükleyici yok.", result.ExitCode),
            NoApplicationsFound => InstallResult.Fail("Paket kaynakta bulunamadı.", result.ExitCode),
            _ => InstallResult.Fail(WingetFailureInterpreter.Describe(output, result.ExitCode), result.ExitCode),
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

            if (installed is null)
            {
                return InstallResult.Ok("Kurulum tamamlandı (winget sürümü doğrulayamadı).");
            }

            if (installed.IsVersionUnknown)
            {
                // winget will list "Unknown → X" again on every scan; remember X so the loop stops until a newer release.
                await SkipVersionAsync(item.Id, item.AvailableVersion).ConfigureAwait(false);
                return InstallResult.Warning(
                    $"Yükleyici hatasız tamamlandı, ancak winget bu uygulamanın yüklü sürümünü okuyamıyor (uygulama sürümünü Windows'a bildirmiyor). " +
                    $"Bu yüzden her taramada yeniden görünüyordu; {item.AvailableVersion} sürümü artık atlanacak, yeni bir sürüm çıkınca tekrar listelenir. " +
                    "Ayarlar › Atlanan sürümler'den geri alabilirsiniz.");
            }

            if (string.IsNullOrWhiteSpace(item.AvailableVersion) || VersionComparer.Compare(installed.Version, item.AvailableVersion) >= 0)
            {
                return InstallResult.Ok($"Doğrulandı: yüklü sürüm {installed.Version}");
            }

            _logger.LogWarning("{Id}: winget reported success but installed version is still {Installed}, expected {Expected}", item.Id, installed.Version, item.AvailableVersion);
            await SkipVersionAsync(item.Id, item.AvailableVersion).ConfigureAwait(false);
            return InstallResult.Warning(
                $"Yükleyici hatasız bitti ama yüklü sürüm değişmedi ({installed.Version}). Uygulamanın Windows'a bildirdiği sürüm winget kataloğundaki {item.AvailableVersion} ile uyuşmuyor; " +
                "büyük olasılıkla uygulama zaten güncel ya da kendi güncelleyicisini kullanıyor. Bu sürüm artık taramalarda atlanacak (Ayarlar › Atlanan sürümler'den geri alınabilir). " +
                "Emin olmak için uygulamayı açıp kendi 'güncelleme' seçeneğine bakın.");
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

    private static bool IsUpgradeNotSupportedMessage(string output) =>
        output.Contains("cannot be upgraded using WinGet", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("method provided by the publisher", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("WinGet kullanılarak yükseltilemez", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("yayıncı tarafından sağlanan yöntemi", StringComparison.OrdinalIgnoreCase);

    private async Task RememberManualOnlyAsync(string id)
    {
        var list = _settings.Current.WingetManualIds;
        if (list.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        list.Add(id);
        try
        {
            await _settings.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist manual-only package {Id}", id);
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
