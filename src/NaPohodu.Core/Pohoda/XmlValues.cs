using System.Globalization;
using System.Xml.Linq;

namespace NaPohodu.Core.Pohoda;

/// <summary>Čtení hodnot z odpovědí Pohody. Prázdný element = chybějící hodnota.</summary>
internal static class XmlValues
{
    public static string? Text(this XElement? parent, XName name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public static int? Int(this XElement? parent, XName name) =>
        parent.Text(name) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static decimal? Decimal(this XElement? parent, XName name) =>
        parent.Text(name) is { } text && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static DateOnly? Date(this XElement? parent, XName name) =>
        parent.Text(name) is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;

    /// <summary>Povinná hodnota; chybějící znamená poškozenou nebo neočekávanou odpověď.</summary>
    public static string Required(this XElement parent, XName name) =>
        parent.Text(name) ?? throw new PohodaException($"V odpovědi Pohody chybí povinný element {name.LocalName} v {parent.Name.LocalName}.");
}
