using System.Xml.Linq;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Jmenné prostory Pohoda XML (version_2). Lokálně deklarovaný element patří do namespace
/// schématu, ve kterém je deklarovaný, ne schématu svého typu
/// (např. <c>lst:requestInvoice</c> → child <c>ftr:filter</c>).
/// </summary>
public static class PohodaNamespaces
{
    private const string Base = "http://www.stormware.cz/schema/version_2/";

    public static readonly XNamespace Dat = Base + "data.xsd";
    public static readonly XNamespace Rsp = Base + "response.xsd";
    public static readonly XNamespace Rdc = Base + "documentresponse.xsd";
    public static readonly XNamespace Typ = Base + "type.xsd";
    public static readonly XNamespace Lst = Base + "list.xsd";
    public static readonly XNamespace Ftr = Base + "filter.xsd";
    public static readonly XNamespace Acu = Base + "accountingunit.xsd";
    public static readonly XNamespace Inv = Base + "invoice.xsd";

    /// <summary>Prefixy pro čitelné vstupní XML (stejné jako v dokumentaci Stormware).</summary>
    internal static IEnumerable<XAttribute> Declarations() =>
    [
        new(XNamespace.Xmlns + "dat", Dat.NamespaceName),
        new(XNamespace.Xmlns + "typ", Typ.NamespaceName),
        new(XNamespace.Xmlns + "lst", Lst.NamespaceName),
        new(XNamespace.Xmlns + "ftr", Ftr.NamespaceName),
        new(XNamespace.Xmlns + "acu", Acu.NamespaceName),
        new(XNamespace.Xmlns + "inv", Inv.NamespaceName),
    ];
}
