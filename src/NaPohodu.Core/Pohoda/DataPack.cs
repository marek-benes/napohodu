using System.Xml.Linq;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Obálka požadavku (<c>data-package.xsd</c>). <c>dat:dataPack</c> vyžaduje <c>version</c>, <c>id</c>,
/// <c>application</c> a <c>note</c>; <c>ico</c> je nepovinné (u seznamu jednotek se vynechává).
/// <c>dat:dataPackItem</c> vyžaduje <c>version</c> a <c>id</c>.
/// </summary>
public static class DataPack
{
    public const string Application = "NaPohodu";
    private const string Version = "2.0";

    public static XDocument Create(string id, string note, string? ico, XElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(content);

        var pack = new XElement(
            Dat + "dataPack",
            Declarations(),
            new XAttribute("version", Version),
            new XAttribute("id", id),
            string.IsNullOrEmpty(ico) ? null : new XAttribute("ico", ico),
            new XAttribute("application", Application),
            new XAttribute("note", note),
            new XElement(
                Dat + "dataPackItem",
                new XAttribute("version", Version),
                new XAttribute("id", id),
                content));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), pack);
    }
}
