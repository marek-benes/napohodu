using System.Text;
using NaPohodu.Core.Text;

namespace NaPohodu.Tests.Text;

public class PohodaXmlDecodingTests
{
    private const string Body = "<a>Přijaté faktury – Žluťoučký kůň</a>";

    [Fact]
    public void Decode_UsesDeclaredWindows1250()
    {
        var xml = $"<?xml version=\"1.0\" encoding=\"Windows-1250\"?>{Body}";

        var decoded = PohodaXmlDecoding.Decode(PohodaEncoding.Windows1250.GetBytes(xml));

        Assert.Equal(xml, decoded);
    }

    [Fact]
    public void Decode_UsesDeclaredUtf8()
    {
        var xml = $"<?xml version='1.0' encoding='utf-8'?>{Body}";

        var decoded = PohodaXmlDecoding.Decode(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(xml, decoded);
    }

    [Fact]
    public void Decode_WithoutDeclaration_FallsBackToWindows1250()
    {
        var decoded = PohodaXmlDecoding.Decode(PohodaEncoding.Windows1250.GetBytes(Body));

        Assert.Equal(Body, decoded);
    }

    [Fact]
    public void Decode_UnknownDeclaredEncoding_FallsBackToWindows1250()
    {
        var xml = $"<?xml version=\"1.0\" encoding=\"x-neznamé\"?>{Body}";

        var decoded = PohodaXmlDecoding.Decode(PohodaEncoding.Windows1250.GetBytes(xml));

        Assert.Equal(xml, decoded);
    }

    [Fact]
    public void Decode_Utf8Bom_WinsOverDeclaration()
    {
        var xml = $"<?xml version=\"1.0\" encoding=\"Windows-1250\"?>{Body}";
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(xml)];

        var decoded = PohodaXmlDecoding.Decode(bytes);

        Assert.Equal(xml, decoded);
    }
}
