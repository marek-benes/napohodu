using System.Xml.Linq;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Obálka požadavku (<c>data-package.xsd</c>). <c>dat:dataPack</c> vyžaduje <c>version</c>, <c>id</c>,
/// <c>application</c> a <c>note</c>. <c>ico</c>/<c>key</c> jsou podle XSD nepovinné, ale Pohoda
/// u požadavku nad jednotkou bez nich odmítne balíček („Tento balíček není určen pro tuto jednotku“,
/// ověřeno 2026-10-15); vynechávají se jen u seznamu jednotek.
/// <c>dat:dataPackItem</c> vyžaduje <c>version</c> a <c>id</c>.
/// </summary>
public static class DataPack
{
    public const string Application = "NaPohodu";
    private const string Version = "2.0";

    public static XDocument Create(string id, string note, UnitIdentity? unit, XElement content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(content);

        var pack = new XElement(
            Dat + "dataPack",
            Declarations(),
            new XAttribute("version", Version),
            new XAttribute("id", id),
            string.IsNullOrEmpty(unit?.Ico) ? null : new XAttribute("ico", unit.Ico),
            string.IsNullOrEmpty(unit?.Key) ? null : new XAttribute("key", unit.Key),
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

/// <summary>
/// Identita jednotky v <c>dat:dataPack</c>: IČ (má přednost), nebo <c>key</c> = jedinečný
/// identifikátor účetnictví (pro jednotky bez IČ). Aspoň jedno musí být vyplněno.
/// </summary>
public sealed record UnitIdentity
{
    public UnitIdentity(string? ico, string? key)
    {
        if (string.IsNullOrWhiteSpace(ico) && string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Identita jednotky potřebuje IČ nebo key.");
        }

        Ico = string.IsNullOrWhiteSpace(ico) ? null : ico.Trim();
        Key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public string? Ico { get; }

    public string? Key { get; }
}
