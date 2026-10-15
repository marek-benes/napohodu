using NaPohodu.Core.Pohoda;

namespace NaPohodu.Tests.Pohoda;

public class PohodaIniFileAndExitCodeTests
{
    [Fact]
    public void Ini_WithoutDatabase_OmitsDatabaseKey()
    {
        var ini = PohodaIniFile.Build(@"C:\t\in.xml", @"C:\t\out.xml", database: null);

        Assert.Equal("[XML]\r\ninput_xml=C:\\t\\in.xml\r\nresponse_xml=C:\\t\\out.xml\r\nformat_output=1\r\n", ini);
    }

    [Fact]
    public void Ini_WithDatabase_IncludesIt()
    {
        var ini = PohodaIniFile.Build(@"C:\t\in.xml", @"C:\t\out.xml", "StwPh_12345678_2026");

        Assert.Contains("\r\ndatabase=StwPh_12345678_2026\r\n", ini, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(105, true, false)]
    [InlineData(554, true, false)]
    [InlineData(601, false, true)]
    [InlineData(1, true, false)]
    public void ExitCodes_Classification(int code, bool isError, bool isWarning)
    {
        Assert.Equal(isError, PohodaExitCodes.IsError(code));
        Assert.Equal(isWarning, PohodaExitCodes.IsWarning(code));
    }
}
