using NaPohodu.Core.Text;

namespace NaPohodu.Tests.Text;

public class PohodaEncodingTests
{
    [Fact]
    public void Windows1250_RoundTripsCzechCharacters()
    {
        const string text = "Přijaté faktury – Účetní jednotka, žluťoučký kůň";

        var bytes = PohodaEncoding.Windows1250.GetBytes(text);

        Assert.Equal(1250, PohodaEncoding.Windows1250.CodePage);
        Assert.Equal(text.Length, bytes.Length);
        Assert.Equal(text, PohodaEncoding.Windows1250.GetString(bytes));
    }

    [Fact]
    public void Process_RunsAsX86()
    {
        // Jet 4.0 OLEDB je jen 32bit; testy i aplikace musí běžet jako x86.
        Assert.False(Environment.Is64BitProcess);
    }
}
