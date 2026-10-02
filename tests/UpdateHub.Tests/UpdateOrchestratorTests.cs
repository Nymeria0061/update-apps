using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using Xunit;

namespace UpdateHub.Tests;

public class UpdateOrchestratorTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider : IUpdateProvider
    {
        private readonly List<UpdateItem> _items;

        public FakeProvider(string key, UpdateCategory category, IEnumerable<UpdateItem> items, bool available = true, Exception? scanError = null)
        {
            Key = key;
            Category = category;
            _items = items.ToList();
            Available = available;
            ScanError = scanError;
        }

        public string Key { get; }

        public string DisplayName => Key;

        public UpdateCategory Category { get; }

        public bool Available { get; }

        public Exception? ScanError { get; }

        public string? UnavailableReason => Available ? null : "not installed";

        public List<string> Installed { get; } = new();

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(Available);

        public Task<IReadOnlyList<UpdateItem>> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
        {
            if (ScanError is not null)
            {
                throw ScanError;
            }

            return Task.FromResult<IReadOnlyList<UpdateItem>>(_items);
        }

        public Task<InstallResult> InstallAsync(UpdateItem item, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
        {
            Installed.Add(item.Id);
            return Task.FromResult(item.RequiresReboot ? InstallResult.Reboot() : InstallResult.Ok());
        }
    }

    private static UpdateItem Item(string id, string provider, UpdateCategory category, UpdateChannel channel = UpdateChannel.Stable, bool auto = true, bool reboot = false) =>
        new()
        {
            Id = id,
            ProviderKey = provider,
            Name = id,
            Category = category,
            Source = provider,
            Channel = channel,
            CanInstallAutomatically = auto,
            RequiresReboot = reboot,
        };

    [Fact]
    public async Task Scan_collects_from_all_providers_and_isolates_failures()
    {
        var apps = new FakeProvider("apps", UpdateCategory.Application, [Item("a1", "apps", UpdateCategory.Application)]);
        var broken = new FakeProvider("broken", UpdateCategory.Driver, [], scanError: new InvalidOperationException("boom"));
        var missing = new FakeProvider("missing", UpdateCategory.Firmware, [], available: false);
        var orchestrator = new UpdateOrchestrator([apps, broken, missing], new FakeSettings());

        var summary = await orchestrator.ScanAsync(null, CancellationToken.None);

        Assert.Equal(3, summary.Results.Count);
        Assert.Single(summary.AllItems);
        Assert.NotNull(summary.Results[1].Error);
        Assert.True(summary.Results[2].Skipped);
        Assert.Equal("not installed", summary.Results[2].SkipReason);
    }

    [Fact]
    public async Task Scan_respects_category_toggles_and_exclusions()
    {
        var settings = new FakeSettings();
        settings.Current.IncludeDrivers = false;
        settings.Current.ExcludedIds.Add("a2");
        var apps = new FakeProvider("apps", UpdateCategory.Application, [Item("a1", "apps", UpdateCategory.Application), Item("a2", "apps", UpdateCategory.Application)]);
        var drivers = new FakeProvider("drv", UpdateCategory.Driver, [Item("d1", "drv", UpdateCategory.Driver)]);
        var orchestrator = new UpdateOrchestrator([apps, drivers], settings);

        var summary = await orchestrator.ScanAsync(null, CancellationToken.None);

        Assert.Equal(["a1"], summary.AllItems.Select(i => i.Id));
        Assert.True(summary.Results[1].Skipped);
    }

    [Fact]
    public void Bulk_selection_excludes_prerelease_manual_unknown_and_firmware_by_default()
    {
        var settings = new FakeSettings();
        var orchestrator = new UpdateOrchestrator([], settings);
        var items = new[]
        {
            Item("stable", "p", UpdateCategory.Application),
            Item("beta", "p", UpdateCategory.Application, UpdateChannel.PreRelease),
            Item("manual", "p", UpdateCategory.Application, auto: false),
            Item("unknown", "p", UpdateCategory.Application) with { IsInstalledVersionUnknown = true },
            Item("bios", "p", UpdateCategory.Firmware),
        };

        Assert.Equal(["stable"], orchestrator.SelectForBulkInstall(items).Select(i => i.Id));

        settings.Current.StableOnly = false;
        settings.Current.AllowFirmwareInBulkUpdate = true;
        settings.Current.IncludeUnknownVersionsInBulk = true;
        Assert.Equal(["stable", "beta", "unknown", "bios"], orchestrator.SelectForBulkInstall(items).Select(i => i.Id));
    }

    [Fact]
    public async Task Install_runs_firmware_last_and_reports_reboot()
    {
        var apps = new FakeProvider("apps", UpdateCategory.Application, []);
        var fw = new FakeProvider("fw", UpdateCategory.Firmware, []);
        var orchestrator = new UpdateOrchestrator([apps, fw], new FakeSettings());
        var order = new List<string>();

        var summary = await orchestrator.InstallAsync(
            [Item("bios", "fw", UpdateCategory.Firmware, reboot: true), Item("app", "apps", UpdateCategory.Application)],
            null,
            (item, _) =>
            {
                order.Add(item.Id);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(["app", "bios"], order);
        Assert.Equal(2, summary.Succeeded);
        Assert.True(summary.RebootRequired);
    }

    [Fact]
    public async Task Install_reports_missing_provider_as_failure()
    {
        var orchestrator = new UpdateOrchestrator([], new FakeSettings());
        var summary = await orchestrator.InstallAsync([Item("x", "ghost", UpdateCategory.Application)], null, null, CancellationToken.None);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(InstallOutcome.Failed, summary.Results[0].Result.Outcome);
    }
}
