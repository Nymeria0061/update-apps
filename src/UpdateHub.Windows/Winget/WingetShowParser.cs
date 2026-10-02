using UpdateHub.Core.Abstractions;

namespace UpdateHub.Windows.Winget;

/// <summary>Extracts the official URLs from <c>winget show</c> output (locale independent: label text is only used as a hint).</summary>
public static class WingetShowParser
{
    public static PackageLinks ParseLinks(string output)
    {
        string? homepage = null, installer = null, support = null, publisher = null, releaseNotes = null, first = null;

        foreach (var raw in output.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var label = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            first ??= value;
            if (label.Contains("installer url") || label.Contains("yükleyici url") || label.Contains("yükleyici adresi"))
            {
                installer ??= value;
            }
            else if (label.Contains("homepage") || label.Contains("giriş sayfası") || label.Contains("ana sayfa") || label.Contains("web sitesi"))
            {
                homepage ??= value;
            }
            else if (label.Contains("support") || label.Contains("destek"))
            {
                support ??= value;
            }
            else if (label.Contains("release notes") || label.Contains("sürüm notları"))
            {
                releaseNotes ??= value;
            }
            else if (label.Contains("publisher url") || label.Contains("yayıncı url") || label.Contains("yayımcı url") || label.Contains("yayıncı adresi"))
            {
                publisher ??= value;
            }
        }

        return new PackageLinks(homepage ?? (installer is null && support is null ? first : null), installer, support, publisher, releaseNotes);
    }
}
