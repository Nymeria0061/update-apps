using System.Runtime.Versioning;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Windows.Services;

namespace UpdateHub.Windows.Winget;

/// <summary>Alternatives for winget packages: official links, launching the app's own updater, skipping a version.</summary>
[SupportedOSPlatform("windows")]
public sealed class WingetPackageActions : IPackageActionProvider
{
    private readonly WingetProvider _winget;

    public WingetPackageActions(WingetProvider winget)
    {
        _winget = winget;
    }

    public bool Supports(UpdateItem item) => item.ProviderKey == WingetProvider.ProviderKey;

    public Task<PackageLinks?> GetLinksAsync(UpdateItem item, CancellationToken cancellationToken) =>
        _winget.GetLinksAsync(item.Id, (item.ProviderData as WingetUpgradeRow)?.Source, cancellationToken);

    public string? FindExecutable(UpdateItem item) => InstalledAppLocator.FindExecutable(item.Name, item.Id);

    public void LaunchExecutable(string path) => InstalledAppLocator.Launch(path);

    public Task SkipVersionAsync(UpdateItem item, CancellationToken cancellationToken) =>
        _winget.SkipVersionAsync(item.Id, item.AvailableVersion);
}
