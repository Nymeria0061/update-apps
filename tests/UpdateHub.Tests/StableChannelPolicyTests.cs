using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using Xunit;

namespace UpdateHub.Tests;

public class StableChannelPolicyTests
{
    [Theory]
    [InlineData("Google.Chrome", "Google Chrome", "129.0.6668.59")]
    [InlineData("Microsoft.VisualStudioCode", "Microsoft Visual Studio Code", "1.93.1")]
    [InlineData("Microsoft.DevHome", "Dev Home", "0.1800.566.0")]
    [InlineData("7zip.7zip", "7-Zip 23.01 (x64)", "24.08")]
    [InlineData("Docker.DockerDesktop", "Docker Desktop", "4.34.2")]
    [InlineData(null, "2024-09 Cumulative Update for Windows 11 (KB5043076)", null)]
    [InlineData("Dell.CommandUpdate", "Dell Command | Update", "5.3.0")]
    public void Stable_items_are_classified_as_stable(string? id, string name, string? version)
    {
        Assert.Equal(UpdateChannel.Stable, StableChannelPolicy.Classify(id, name, version));
    }

    [Theory]
    [InlineData("Google.Chrome.Beta", "Google Chrome Beta", "130.0.6723.19")]
    [InlineData("Google.Chrome.Dev", "Google Chrome Dev", "131.0.6744.0")]
    [InlineData("Google.Chrome.Canary", "Google Chrome Canary", "131.0.6750.0")]
    [InlineData("Microsoft.VisualStudioCode.Insiders", "Visual Studio Code Insiders", "1.94.0-insider")]
    [InlineData("Mozilla.Firefox.Nightly", "Firefox Nightly", "132.0a1")]
    [InlineData("Discord.Discord.PTB", "Discord PTB", "1.0.0-rc1")]
    [InlineData("Some.App", "Some App", "2.0.0-beta.3")]
    [InlineData("Some.App", "Some App", "3.0.0-preview1")]
    [InlineData(null, "2024-09 Cumulative Update Preview for Windows 11 (KB5043145)", null)]
    [InlineData("Vendor.App", "Vendor App (Alpha)", "0.9")]
    public void Prerelease_items_are_classified_as_prerelease(string? id, string name, string? version)
    {
        Assert.Equal(UpdateChannel.PreRelease, StableChannelPolicy.Classify(id, name, version));
    }

    [Fact]
    public void Dev_inside_a_word_is_not_a_prerelease_marker()
    {
        // "Developer" / "Device" must not trigger the dev token.
        Assert.True(StableChannelPolicy.IsStable("Vendor.DeveloperTools", "Developer Tools", "1.0"));
        Assert.True(StableChannelPolicy.IsStable("Vendor.DeviceManager", "Device Manager Pro", "1.0"));
    }
}
