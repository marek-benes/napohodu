using System.Text;
using System.Text.RegularExpressions;

namespace NaPohodu.Core.Text;

/// <summary>
/// Dekódování XML odpovědí Pohody. Pohoda zapisuje odpověď zpravidla ve windows-1250;
/// řídíme se BOM, pak kódováním z XML deklarace, a když chybí nebo je neznámé, windows-1250.
/// </summary>
public static partial class PohodaXmlDecoding
{
    // Deklarace je ASCII, proto ji lze bezpečně hledat v prvních bajtech přečtených jako Latin1.
    private const int DeclarationProbeLength = 256;

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            return Encoding.UTF8.GetString(bytes[Encoding.UTF8.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            return Encoding.Unicode.GetString(bytes[Encoding.Unicode.Preamble.Length..]);
        }

        return DetectDeclaredEncoding(bytes).GetString(bytes);
    }

    internal static Encoding DetectDeclaredEncoding(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, DeclarationProbeLength)]);
        var match = EncodingDeclaration().Match(head);
        if (!match.Success)
        {
            return PohodaEncoding.Windows1250;
        }

        try
        {
            // Registrace code pages proběhne při prvním přístupu k Windows1250.
            _ = PohodaEncoding.Windows1250;
            return Encoding.GetEncoding(match.Groups["name"].Value);
        }
        catch (ArgumentException)
        {
            return PohodaEncoding.Windows1250;
        }
    }

    [GeneratedRegex("""^\s*<\?xml[^>]*\bencoding\s*=\s*["'](?<name>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex EncodingDeclaration();
}
