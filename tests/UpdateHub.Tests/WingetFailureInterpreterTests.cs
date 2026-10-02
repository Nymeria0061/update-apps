using UpdateHub.Windows.Winget;
using Xunit;

namespace UpdateHub.Tests;

public class WingetFailureInterpreterTests
{
    [Fact]
    public void Explains_publisher_updater_message_in_turkish()
    {
        const string output = """
            Found Parsec [Parsec.Parsec] Version 150.104.1.0
            The package cannot be upgraded using WinGet. Please use the method provided by the publisher for upgrading this package.
            """;
        var text = WingetFailureInterpreter.Describe(output, unchecked((int)0x8A150114));

        Assert.Contains("kendi güncelleyicisini", text);
        Assert.Contains("0x8A150114", text);
        Assert.Contains("cannot be upgraded", text);
    }

    [Fact]
    public void Explains_installer_exit_code()
    {
        const string output = """
            Found Foo [Foo.Foo] Version 2.0
            Downloading https://example.com/foo.exe
              ██████████████████████████████  10.0 MB / 10.0 MB
            Successfully verified installer hash
            Starting package install...
            Installer failed with exit code: 1603
            """;
        var text = WingetFailureInterpreter.Describe(output, -1978334975);

        Assert.StartsWith("Uygulamanın kendi yükleyicisi hata verdi (yükleyici kodu 1603)", text);
        Assert.DoesNotContain("█", text);
    }

    [Fact]
    public void Falls_back_to_last_meaningful_line()
    {
        var text = WingetFailureInterpreter.Describe("Something unusual happened\r\n", 7);
        Assert.Equal("Something unusual happened (winget kodu 0x00000007)", text);
    }

    [Fact]
    public void Handles_empty_output()
    {
        Assert.Equal("Kurulum başarısız (winget kodu 0x00000007).", WingetFailureInterpreter.Describe(string.Empty, 7));
    }
}
