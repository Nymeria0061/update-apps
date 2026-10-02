using System.Collections.Concurrent;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;

namespace UpdateHub.Core.Services;

public sealed record ProviderScanResult(IUpdateProvider Provider, IReadOnlyList<UpdateItem> Items, Exception? Error, bool Skipped, string? SkipReason);

public sealed record ScanSummary(IReadOnlyList<ProviderScanResult> Results, DateTimeOffset CompletedAt)
{
    public IReadOnlyList<UpdateItem> AllItems => Results.SelectMany(r => r.Items).ToList();
}

public sealed record BatchItemResult(UpdateItem Item, InstallResult Result);

public sealed record BatchInstallSummary(IReadOnlyList<BatchItemResult> Results)
{
    public int Succeeded => Results.Count(r => r.Result.IsSuccess);
    public int Failed => Results.Count(r => r.Result.Outcome == InstallOutcome.Failed);
    public int Manual => Results.Count(r => r.Result.Outcome == InstallOutcome.ManualActionRequired);
    public bool RebootRequired => Results.Any(r => r.Result.RebootRequired || r.Item.RequiresReboot && r.Result.IsSuccess);
}

public sealed record BatchProgress(UpdateItem? CurrentItem, int Completed, int Total, string Message, double? ItemPercent);

/// <summary>
/// Coordinates all registered providers: scans them concurrently and installs updates one at a time
/// (installers do not like running in parallel). Applies the stable-only / exclusion / source policies.
/// </summary>
public sealed class UpdateOrchestrator
{
    private readonly IReadOnlyList<IUpdateProvider> _providers;
    private readonly ISettingsService _settings;

    public UpdateOrchestrator(IEnumerable<IUpdateProvider> providers, ISettingsService settings)
    {
        _providers = providers.ToList();
        _settings = settings;
    }

    public IReadOnlyList<IUpdateProvider> Providers => _providers;

    public async Task<ScanSummary> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var results = new ConcurrentBag<ProviderScanResult>();

        var tasks = _providers.Select(async provider =>
        {
            if (!IsCategoryEnabled(provider.Category, settings))
            {
                results.Add(new ProviderScanResult(provider, Array.Empty<UpdateItem>(), null, true, "Ayarlardan kapatıldı"));
                return;
            }

            try
            {
                if (!await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    results.Add(new ProviderScanResult(provider, Array.Empty<UpdateItem>(), null, true, provider.UnavailableReason));
                    return;
                }

                progress?.Report(new ScanProgress(provider.Key, $"{provider.DisplayName} taranıyor…"));
                var items = await provider.ScanAsync(progress, cancellationToken).ConfigureAwait(false);
                var filtered = items
                    .Where(i => !settings.ExcludedIds.Contains(i.Id, StringComparer.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.Severity)
                    .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                results.Add(new ProviderScanResult(provider, filtered, null, false, null));
                progress?.Report(new ScanProgress(provider.Key, $"{provider.DisplayName}: {filtered.Count} güncelleme", 100));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new ProviderScanResult(provider, Array.Empty<UpdateItem>(), ex, false, null));
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var ordered = _providers
            .Select(p => results.First(r => r.Provider == p))
            .ToList();

        return new ScanSummary(ordered, DateTimeOffset.Now);
    }

    /// <summary>
    /// Items that "update everything" would install under the current policy:
    /// stable channel (if enabled), not excluded, installable without manual steps,
    /// and firmware only when the user explicitly allowed it.
    /// </summary>
    public IReadOnlyList<UpdateItem> SelectForBulkInstall(IEnumerable<UpdateItem> items)
    {
        var settings = _settings.Current;
        return items.Where(i =>
                i.CanInstallAutomatically &&
                (!settings.StableOnly || i.IsStable) &&
                !settings.ExcludedIds.Contains(i.Id, StringComparer.OrdinalIgnoreCase) &&
                (!i.IsInstalledVersionUnknown || settings.IncludeUnknownVersionsInBulk) &&
                (i.Category != UpdateCategory.Firmware || settings.AllowFirmwareInBulkUpdate))
            .ToList();
    }

    public async Task<BatchInstallSummary> InstallAsync(
        IReadOnlyList<UpdateItem> items,
        IProgress<BatchProgress>? progress,
        Func<UpdateItem, InstallResult, Task>? onItemCompleted,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchItemResult>(items.Count);

        // Firmware last: a BIOS flash usually wants a reboot and must never be interrupted by another installer.
        var ordered = items
            .OrderBy(i => i.Category == UpdateCategory.Firmware ? 1 : 0)
            .ThenBy(i => i.Category)
            .ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = ordered[index];
            var provider = _providers.FirstOrDefault(p => p.Key == item.ProviderKey);

            progress?.Report(new BatchProgress(item, index, ordered.Count, $"{item.Name} kuruluyor…", null));

            InstallResult result;
            if (provider is null)
            {
                result = InstallResult.Fail($"Sağlayıcı bulunamadı: {item.ProviderKey}");
            }
            else
            {
                var itemProgress = new Progress<InstallProgress>(p =>
                    progress?.Report(new BatchProgress(item, index, ordered.Count, p.Message, p.Percent)));
                try
                {
                    result = await provider.InstallAsync(item, itemProgress, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result = InstallResult.Cancelled();
                    results.Add(new BatchItemResult(item, result));
                    break;
                }
                catch (Exception ex)
                {
                    result = InstallResult.Fail(ex.Message);
                }
            }

            results.Add(new BatchItemResult(item, result));
            if (onItemCompleted is not null)
            {
                await onItemCompleted(item, result).ConfigureAwait(false);
            }
        }

        progress?.Report(new BatchProgress(null, results.Count, ordered.Count, "Tamamlandı", 100));
        return new BatchInstallSummary(results);
    }

    private static bool IsCategoryEnabled(UpdateCategory category, AppSettings settings) => category switch
    {
        UpdateCategory.Application => settings.IncludeApplications,
        UpdateCategory.WindowsUpdate => settings.IncludeWindowsUpdates,
        UpdateCategory.Driver => settings.IncludeDrivers,
        UpdateCategory.Firmware => settings.IncludeFirmware,
        _ => true,
    };
}
