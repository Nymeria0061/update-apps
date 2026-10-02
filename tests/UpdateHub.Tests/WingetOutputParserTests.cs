using UpdateHub.Windows.Winget;
using Xunit;

namespace UpdateHub.Tests;

public class WingetOutputParserTests
{
    private const string EnglishOutput = """
        Name                                     Id                                 Version        Available      Source
        ---------------------------------------------------------------------------------------------------------------
        7-Zip 23.01 (x64)                        7zip.7zip                          23.01          24.08          winget
        Google Chrome                            Google.Chrome                      128.0.6613.85  129.0.6668.59  winget
        Microsoft Teams                          XP8BT8DW290MPQ                     24215.1005.0   24231.512.0    msstore
        Some Tool                                Vendor.Tool                        Unknown        2.5.0          winget
        4 upgrades available.

        The following packages have an upgrade available, but require explicit targeting for upgrade:
        Name                 Id                     Version      Available    Source
        -----------------------------------------------------------------------------
        Pinned App           Pinned.App             1.0.0        2.0.0        winget
        """;

    private const string TurkishOutput = """
        Ad                                       Kimlik                             Sürüm          Kullanılabilir Kaynak
        ---------------------------------------------------------------------------------------------------------------
        Notepad++ (64-bit x64)                   Notepad++.Notepad++                8.6.9          8.7            winget
        1 yükseltme kullanılabilir.
        """;

    [Fact]
    public void Parses_english_table_including_explicit_targeting_section()
    {
        var rows = WingetOutputParser.ParseUpgradeTable(EnglishOutput);

        Assert.Equal(5, rows.Count);
        Assert.Contains(rows, r => r.Id == "7zip.7zip" && r.Version == "23.01" && r.Available == "24.08" && r.Source == "winget");
        Assert.Contains(rows, r => r.Id == "XP8BT8DW290MPQ" && r.Source == "msstore" && r.Name == "Microsoft Teams");
        Assert.Contains(rows, r => r.Id == "Pinned.App" && r.Available == "2.0.0");
    }

    [Fact]
    public void Unknown_version_is_flagged()
    {
        var rows = WingetOutputParser.ParseUpgradeTable(EnglishOutput);
        var tool = Assert.Single(rows, r => r.Id == "Vendor.Tool");
        Assert.True(tool.IsVersionUnknown);
    }

    [Fact]
    public void Parses_localized_headers()
    {
        var rows = WingetOutputParser.ParseUpgradeTable(TurkishOutput);
        var row = Assert.Single(rows);
        Assert.Equal("Notepad++.Notepad++", row.Id);
        Assert.Equal("Notepad++ (64-bit x64)", row.Name);
        Assert.Equal("8.7", row.Available);
    }

    [Fact]
    public void Ignores_spinner_and_progress_noise()
    {
        var noisy = "-\r\\\r|\r/\r" + "  ██████████████████████████████  100%\r\n" + EnglishOutput;
        var rows = WingetOutputParser.ParseUpgradeTable(noisy);
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void Falls_back_to_regex_when_wide_characters_break_alignment()
    {
        const string output = """
            Name                     Id                    Version    Available  Source
            ----------------------------------------------------------------------------
            微信 WeChat             Tencent.WeChat        3.9.10     3.9.12     winget
            """;
        var rows = WingetOutputParser.ParseUpgradeTable(output);
        var row = Assert.Single(rows);
        Assert.Equal("Tencent.WeChat", row.Id);
        Assert.Equal("3.9.12", row.Available);
        Assert.Equal("winget", row.Source);
    }

    [Fact]
    public void Empty_output_yields_no_rows()
    {
        Assert.Empty(WingetOutputParser.ParseUpgradeTable(string.Empty));
        Assert.Empty(WingetOutputParser.ParseUpgradeTable("No installed package found matching input criteria."));
    }

    [Fact]
    public void Reads_upgrade_count_from_summary_line()
    {
        Assert.Equal(4, WingetOutputParser.ParseUpgradeCount(EnglishOutput));
        Assert.Equal(1, WingetOutputParser.ParseUpgradeCount(TurkishOutput));
        Assert.Null(WingetOutputParser.ParseUpgradeCount("nothing here"));
    }
}
