using System.Text.RegularExpressions;

namespace UpdateHub.Windows.Winget;

/// <summary>
/// Turns winget's console output into a short Turkish explanation of why an upgrade did not happen,
/// keeping the original line as detail so the user can search for it.
/// </summary>
public static partial class WingetFailureInterpreter
{
    private static readonly (string Needle, string Explanation)[] KnownMessages =
    [
        ("hash does not match", "Yükleyici dosyasının karması manifestteki değerle uyuşmuyor; güvenlik nedeniyle kurulum reddedildi. Yayıncı dosyayı değiştirmiş olabilir, bir süre sonra tekrar deneyin."),
        ("cannot be upgraded using WinGet", "Bu uygulama winget ile yükseltilemiyor; yayıncı kendi güncelleyicisini kullanıyor. Uygulamayı açıp kendi içinden güncelleyin."),
        ("method provided by the publisher", "Bu uygulama winget ile yükseltilemiyor; yayıncı kendi güncelleyicisini kullanıyor. Uygulamayı açıp kendi içinden güncelleyin."),
        ("cannot be run from an administrator context", "Bu uygulamanın yükleyicisi yönetici olarak çalıştırılamıyor (kullanıcı bazlı kurulum). Uygulamayı kendi içinden güncelleyin."),
        ("is currently running", "Uygulama şu anda açık. Kapatıp tekrar deneyin."),
        ("application is in use", "Uygulama şu anda kullanımda. Kapatıp tekrar deneyin."),
        ("Another installation is already in progress", "Başka bir kurulum devam ediyor. Bitmesini bekleyip tekrar deneyin."),
        ("blocked by policy", "Kurulum grup ilkesi tarafından engellendi."),
        ("No applicable installer", "Bu sistem (mimari/sürüm) için uygun bir yükleyici bulunamadı."),
        ("No package found matching input criteria", "Paket kaynakta bulunamadı; kaynak listesi güncellenmiş olabilir."),
        ("No available upgrade found", "winget bu paket için yeni bir sürüm görmüyor; uygulama zaten güncel olabilir."),
        ("No newer package versions are available", "winget bu paket için yeni bir sürüm görmüyor; uygulama zaten güncel olabilir."),
        ("requires explicit targeting", "Bu paket açıkça hedeflenmeden yükseltilemiyor; ayrı olarak tekrar deneyin."),
        ("Restart your PC", "Kurulum tamamlandı ancak bitmesi için yeniden başlatma gerekiyor."),
        ("Insufficient disk space", "Diskte yeterli boş alan yok."),
        ("Failed when downloading", "İndirme başarısız oldu (ağ ya da yayıncı sunucusu). Tekrar deneyin."),
        ("Download failed", "İndirme başarısız oldu (ağ ya da yayıncı sunucusu). Tekrar deneyin."),
        ("Installer failed with exit code", "Uygulamanın kendi yükleyicisi hata verdi."),
        ("Installation abandoned", "Kurulum kullanıcı tarafından iptal edildi."),
        ("The installer failed", "Uygulamanın kendi yükleyicisi hata verdi."),
    ];

    [GeneratedRegex(@"exit code:?\s*(-?\d+|0x[0-9A-Fa-f]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InstallerExitCodeRegex();

    private static readonly char[] ProgressGlyphs = ['█', '▒', '░', '▓'];

    public static string Describe(string output, int exitCode)
    {
        var lines = output.Replace("\r", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.IndexOfAny(ProgressGlyphs) < 0 && l is not ("-" or "\\" or "|" or "/") && !l.EndsWith('%'))
            .ToList();

        var explanation = KnownMessages
            .Where(k => lines.Any(l => l.Contains(k.Needle, StringComparison.OrdinalIgnoreCase)))
            .Select(k => k.Explanation)
            .FirstOrDefault();

        var installerCode = lines.Select(l => InstallerExitCodeRegex().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        if (installerCode is not null && explanation is not null)
        {
            explanation = explanation.TrimEnd('.') + $" (yükleyici kodu {installerCode})";
        }

        // Most informative raw line: the last one that is not a generic banner.
        var detail = lines.LastOrDefault(l =>
            !l.StartsWith("Found ", StringComparison.OrdinalIgnoreCase) &&
            !l.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase) &&
            !l.StartsWith("Starting package install", StringComparison.OrdinalIgnoreCase) &&
            !l.StartsWith("Successfully verified", StringComparison.OrdinalIgnoreCase) &&
            !l.Contains("agreements", StringComparison.OrdinalIgnoreCase));

        var code = $"winget kodu 0x{exitCode:X8}";
        return (explanation, detail) switch
        {
            (null, null) => $"Kurulum başarısız ({code}).",
            (null, _) => $"{detail} ({code})",
            (_, null) => $"{explanation} ({code})",
            _ => $"{explanation} — winget: “{detail}” ({code})",
        };
    }
}
