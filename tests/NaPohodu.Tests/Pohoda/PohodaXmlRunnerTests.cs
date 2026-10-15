using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Pohoda;

/// <summary>
/// Runner proti falešnému „Pohoda.exe“ (dávkový soubor), který ověří argumenty a zkopíruje
/// fixture do <c>response_xml</c> z INI. Skutečnou Pohodu tyto testy nespouštějí.
/// </summary>
public sealed class PohodaXmlRunnerTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("napohodu-tests-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public async Task RunAsync_PassesArgumentsAndReadsResponse()
    {
        // Cesty bereme jen z argumentů (%~dp0, %~dp4): text dávky (OEM) ani INI (windows-1250)
        // nesmí nést diakritiku z cesty k temp adresáři. response.xml leží vedle INI.
        File.WriteAllBytes(Path.Combine(_dir.FullName, "fixture.xml"), FixtureFiles.ReadBytes("Synthetic/listAccountingUnit.response.xml"));
        var exe = FakePohoda(
            """
            if not "%~1"=="/XML" exit /b 90
            if not "%~2"=="uzivatel" exit /b 91
            if not "%~3"=="tajne heslo" exit /b 92
            findstr /b /c:"database=StwPh_1" "%~4" >nul || exit /b 93
            copy /y "%~dp0fixture.xml" "%~dp4response.xml" >nul || exit /b 94
            exit /b 0
            """);

        var response = await Runner(exe).RunAsync(PohodaRequests.ListAccountingUnits(), "StwPh_1", TestContext.Current.CancellationToken);

        Assert.Equal(FixtureFiles.ReadBytes("Synthetic/listAccountingUnit.response.xml"), response.RawBytes);
        Assert.Equal(2, AccountingUnitParser.Parse(response.Document).Count);
    }

    [Fact]
    public async Task RunAsync_ErrorExitCode_ThrowsWithoutPassword()
    {
        var exe = FakePohoda("exit /b 554");

        var ex = await Assert.ThrowsAsync<PohodaException>(
            () => Runner(exe).RunAsync(PohodaRequests.ListAccountingUnits(), null, TestContext.Current.CancellationToken));

        Assert.Equal(554, ex.ExitCode);
        Assert.Contains("554", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tajne heslo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoResponseFile_Throws()
    {
        var exe = FakePohoda("exit /b 0");

        var ex = await Assert.ThrowsAsync<PohodaException>(
            () => Runner(exe).RunAsync(PohodaRequests.ListAccountingUnits(), null, TestContext.Current.CancellationToken));

        Assert.Equal(0, ex.ExitCode);
    }

    [Fact]
    public async Task RunAsync_Hanging_IsKilledAfterTimeout()
    {
        var exe = FakePohoda("ping -n 30 127.0.0.1 >nul");

        await Assert.ThrowsAsync<PohodaTimeoutException>(
            () => Runner(exe, TimeSpan.FromSeconds(1)).RunAsync(PohodaRequests.ListAccountingUnits(), null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_MissingExe_Throws()
    {
        var ex = await Assert.ThrowsAsync<PohodaException>(
            () => Runner(Path.Combine(_dir.FullName, "Pohoda.exe")).RunAsync(PohodaRequests.ListAccountingUnits(), null, TestContext.Current.CancellationToken));

        Assert.Contains("nebyla nalezena", ex.Message, StringComparison.Ordinal);
    }

    private string FakePohoda(string body)
    {
        var path = Path.Combine(_dir.FullName, $"pohoda-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(path, "@echo off\r\n" + body.ReplaceLineEndings("\r\n") + "\r\n", Encoding.ASCII);
        return path;
    }

    private static PohodaXmlRunner Runner(string exePath, TimeSpan? timeout = null) =>
        new(
            new PohodaOptions
            {
                ExePath = exePath,
                Username = "uzivatel",
                Password = "tajne heslo",
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
            },
            NullLogger<PohodaXmlRunner>.Instance);
}
