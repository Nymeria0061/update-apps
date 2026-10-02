using UpdateHub.Core.Models;

namespace UpdateHub.Core.Abstractions;

/// <summary>Official links published in a package's manifest.</summary>
public sealed record PackageLinks(string? Homepage, string? InstallerUrl, string? SupportUrl, string? PublisherUrl, string? ReleaseNotesUrl)
{
    public string? BestWebsite => Homepage ?? PublisherUrl ?? SupportUrl;
}

/// <summary>
/// Alternatives for packages the package manager cannot upgrade itself: where the official installer
/// lives, where the app is installed so its own updater can be started, and version skipping.
/// </summary>
public interface IPackageActionProvider
{
    bool Supports(UpdateItem item);

    Task<PackageLinks?> GetLinksAsync(UpdateItem item, CancellationToken cancellationToken);

    /// <summary>Path of the installed application's executable, if it can be located.</summary>
    string? FindExecutable(UpdateItem item);

    /// <summary>Starts the executable as the desktop user (not elevated).</summary>
    void LaunchExecutable(string path);

    /// <summary>Hides this exact available version on later scans.</summary>
    Task SkipVersionAsync(UpdateItem item, CancellationToken cancellationToken);
}
