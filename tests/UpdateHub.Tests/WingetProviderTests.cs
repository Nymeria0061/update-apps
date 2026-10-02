using Microsoft.Extensions.Logging.Abstractions;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Windows.Winget;
using Xunit;

namespace UpdateHub.Tests;

public class WingetProviderTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler? Changed;

        public int Saves { get; private set; }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLocator : WingetLocator
    {
        public FakeLocator(ISettingsService settings) : base(settings)
        {
        }

        public override string? Resolve() => "winget";
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = new();

        public Func<ProcessRequest, ProcessResult> Handler { get; set; } = _ => new ProcessResult(0, string.Empty, string.Empty, false);

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Handler(request));
        }
    }

    private const string UpgradeOutput = """
        Name                 Id                     Version      Available      Source
        ----------------------------------------------------------------------------------
        Google Chrome        Google.Chrome          128.0.1      129.0.2        winget
        OpenAL               CreativeTechnology.OpenAL Unknown   1.1            winget
        Parsec               Parsec.Parsec          Unknown      150.104.1.0    winget
        Some Tool            Vendor.Tool            1.0          2.0            customsrc
        4 upgrades available.
        """;

    private static (WingetProvider Provider, FakeRunner Runner, FakeSettings Settings) Create()
    {
        var settings = new FakeSettings();
        var runner = new FakeRunner();
        var provider = new WingetProvider(new FakeLocator(settings), runner, settings, NullLogger<WingetProvider>.Instance);
        return (provider, runner, settings);
    }

    [Fact]
    public async Task Scan_flags_unknown_versions_manual_only_and_hides_skipped_and_foreign_sources()
    {
        var (provider, runner, settings) = Create();
        settings.Current.WingetManualIds.Add("Parsec.Parsec");
        settings.Current.SkippedVersions["Google.Chrome"] = "129.0.2";
        runner.Handler = _ => new ProcessResult(0, UpgradeOutput, string.Empty, false);

        var items = await provider.ScanAsync(null, CancellationToken.None);

        Assert.Equal(["CreativeTechnology.OpenAL", "Parsec.Parsec"], items.Select(i => i.Id));
        var openAl = items[0];
        Assert.True(openAl.IsInstalledVersionUnknown);
        Assert.Null(openAl.CurrentVersion);
        Assert.True(openAl.CanInstallAutomatically);
        var parsec = items[1];
        Assert.False(parsec.CanInstallAutomatically);
        Assert.Equal(WingetProvider.ManualUpgradeMessage, parsec.ManualInstructions);
    }

    [Fact]
    public async Task Install_verifies_version_and_reports_it()
    {
        var (provider, runner, _) = Create();
        runner.Handler = req => req.Arguments[0] switch
        {
            "upgrade" => new ProcessResult(0, "Successfully installed", string.Empty, false),
            "list" => new ProcessResult(0, """
                Name            Id              Version   Source
                -----------------------------------------------------
                Google Chrome   Google.Chrome   129.0.2   winget
                """, string.Empty, false),
            _ => throw new InvalidOperationException(req.Arguments[0]),
        };

        var item = new UpdateItem { Id = "Google.Chrome", ProviderKey = "winget", Name = "Google Chrome", Category = UpdateCategory.Application, Source = "winget", CurrentVersion = "128.0.1", AvailableVersion = "129.0.2" };
        var result = await provider.InstallAsync(item, null, CancellationToken.None);

        Assert.Equal(InstallOutcome.Success, result.Outcome);
        Assert.Contains("129.0.2", result.Message);
    }

    [Fact]
    public async Task Install_with_unchanged_version_warns_and_skips_that_version()
    {
        var (provider, runner, settings) = Create();
        runner.Handler = req => req.Arguments[0] switch
        {
            "upgrade" => new ProcessResult(0, "Successfully installed", string.Empty, false),
            "list" => new ProcessResult(0, """
                Name               Id                 Version      Available      Source
                -----------------------------------------------------------------------------
                Google Play Games  Google.PlayGames   26.9.555.0   156.0.8067.0   winget
                """, string.Empty, false),
            _ => throw new InvalidOperationException(req.Arguments[0]),
        };

        var item = new UpdateItem { Id = "Google.PlayGames", ProviderKey = "winget", Name = "Google Play Games", Category = UpdateCategory.Application, Source = "winget", CurrentVersion = "26.9.555.0", AvailableVersion = "156.0.8067.0" };
        var result = await provider.InstallAsync(item, null, CancellationToken.None);

        Assert.Equal(InstallOutcome.SuccessWithWarning, result.Outcome);
        Assert.Contains("değişmedi", result.Message);
        Assert.Equal("156.0.8067.0", settings.Current.SkippedVersions["Google.PlayGames"]);
        Assert.Equal(1, settings.Saves);
    }

    [Fact]
    public async Task Install_refused_by_publisher_becomes_manual_and_is_remembered()
    {
        var (provider, runner, settings) = Create();
        runner.Handler = _ => new ProcessResult(unchecked((int)0x8A150114),
            "The package cannot be upgraded using WinGet. Please use the method provided by the publisher for upgrading this package.", string.Empty, false);

        var item = new UpdateItem { Id = "Parsec.Parsec", ProviderKey = "winget", Name = "Parsec", Category = UpdateCategory.Application, Source = "winget", AvailableVersion = "150.104.1.0" };
        var result = await provider.InstallAsync(item, null, CancellationToken.None);

        Assert.Equal(InstallOutcome.ManualActionRequired, result.Outcome);
        Assert.Contains("Parsec.Parsec", settings.Current.WingetManualIds);
    }
}

public class WingetShowParserTests
{
    [Fact]
    public void Extracts_official_links()
    {
        const string output = """
            Found Parsec [Parsec.Parsec]
            Version: 150.104.1.0
            Publisher: Parsec Cloud, Inc.
            Publisher Url: https://parsec.app/
            Publisher Support Url: https://support.parsec.app/
            Homepage: https://parsec.app/
            License Url: https://parsec.app/terms
            Installer:
              Installer Type: exe
              Installer Url: https://builds.parsec.app/package/parsec-windows.exe
              Installer SHA256: abc
            """;
        var links = WingetShowParser.ParseLinks(output);

        Assert.Equal("https://parsec.app/", links.Homepage);
        Assert.Equal("https://builds.parsec.app/package/parsec-windows.exe", links.InstallerUrl);
        Assert.Equal("https://support.parsec.app/", links.SupportUrl);
        Assert.Equal("https://parsec.app/", links.PublisherUrl);
    }

    [Fact]
    public void Handles_output_without_links()
    {
        var links = WingetShowParser.ParseLinks("No package found matching input criteria.");
        Assert.Null(links.Homepage);
        Assert.Null(links.InstallerUrl);
        Assert.Null(links.BestWebsite);
    }
}
