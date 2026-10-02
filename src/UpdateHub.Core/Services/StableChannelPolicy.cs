using System.Text.RegularExpressions;
using UpdateHub.Core.Models;

namespace UpdateHub.Core.Services;

/// <summary>
/// Decides whether a package / version belongs to the stable release channel.
/// Anything that looks like a beta, preview, nightly, canary, insider, dev or RC build is treated as pre-release
/// and is excluded from "update everything" runs.
/// </summary>
public static partial class StableChannelPolicy
{
    [GeneratedRegex(@"(?<![a-z0-9])(beta|alpha|preview|nightly|canary|insider|dev|rc|prerelease|pre-release|experimental|snapshot|unstable|daily|edge\.dev|insiders)(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreReleaseNameRegex();

    [GeneratedRegex(@"[-+.]?(beta|alpha|preview|pre|rc|nightly|canary|dev|insider|snapshot)[-.]?\d*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreReleaseVersionRegex();

    /// <summary>Ids that contain a "dev" token but are in fact stable products.</summary>
    private static readonly HashSet<string> KnownStableIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.VisualStudioCode",
        "Microsoft.DevHome",
        "Microsoft.WindowsSDK",
        "Microsoft.AzureCLI",
        "Docker.DockerDesktop",
        "Google.AndroidStudio",
        "JetBrains.Toolbox",
    };

    public static UpdateChannel Classify(string? id, string? name, string? version)
    {
        if (!string.IsNullOrWhiteSpace(id) && KnownStableIds.Contains(id))
        {
            return UpdateChannel.Stable;
        }

        if (!string.IsNullOrWhiteSpace(id) && PreReleaseNameRegex().IsMatch(id.Replace('.', ' ')))
        {
            return UpdateChannel.PreRelease;
        }

        if (!string.IsNullOrWhiteSpace(name) && PreReleaseNameRegex().IsMatch(name))
        {
            return UpdateChannel.PreRelease;
        }

        if (!string.IsNullOrWhiteSpace(version) && PreReleaseVersionRegex().IsMatch(version))
        {
            return UpdateChannel.PreRelease;
        }

        return UpdateChannel.Stable;
    }

    public static bool IsStable(string? id, string? name, string? version) =>
        Classify(id, name, version) == UpdateChannel.Stable;
}
