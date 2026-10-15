using System.Text;

namespace NaPohodu.Core.Text;

/// <summary>
/// Kódování používané Pohodou. XML soubory (vstup, odpovědi, XSD) jsou zpravidla windows-1250,
/// které .NET bez registrace <see cref="CodePagesEncodingProvider"/> nezná.
/// </summary>
public static class PohodaEncoding
{
    private static readonly Lazy<Encoding> _windows1250 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250);
    });

    public static Encoding Windows1250 => _windows1250.Value;
}
