using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using Xunit;

namespace UpdateHub.Tests;

public class JsonSettingsServiceTests
{
    [Fact]
    public async Task Round_trips_settings_and_survives_corrupt_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "updatehub-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new JsonSettingsService(path);

        await service.LoadAsync();
        Assert.True(service.Current.StableOnly);

        service.Current.StableOnly = false;
        service.Current.Theme = AppTheme.Dark;
        service.Current.ExcludedIds.Add("Vendor.App");
        await service.SaveAsync();

        var reloaded = new JsonSettingsService(path);
        await reloaded.LoadAsync();
        Assert.False(reloaded.Current.StableOnly);
        Assert.Equal(AppTheme.Dark, reloaded.Current.Theme);
        Assert.Equal(["Vendor.App"], reloaded.Current.ExcludedIds);

        await File.WriteAllTextAsync(path, "{ not json");
        var corrupt = new JsonSettingsService(path);
        await corrupt.LoadAsync();
        Assert.True(corrupt.Current.StableOnly);
    }
}
