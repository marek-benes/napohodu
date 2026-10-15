using System.Xml.Linq;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Kontrola odpovědi (<c>response.xsd</c>, <c>documentresponse.xsd</c>). <c>rsp:responsePack</c>,
/// <c>rsp:responsePackItem</c> i exportní seznam (<c>rdc:listVersionType</c>) nesou atribut
/// <c>state</c> (<c>ok</c>/<c>error</c>). Chyba se nikdy nesmí tvářit jako prázdný seznam.
/// </summary>
public static class ResponsePack
{
    private const string ErrorState = "error";

    /// <summary>
    /// Vrátí exportní seznam (např. <c>lst:listInvoice</c>) z prvního <c>responsePackItem</c>,
    /// nebo vyhodí <see cref="PohodaException"/>, pokud odpověď hlásí chybu, seznam chybí
    /// nebo je export rozdělený do částí.
    /// </summary>
    public static XElement GetList(XDocument response, XName listName)
    {
        ArgumentNullException.ThrowIfNull(response);

        var pack = response.Root;
        if (pack is null || pack.Name != Rsp + "responsePack")
        {
            throw new PohodaException($"Neočekávaná odpověď Pohody: kořenový element {pack?.Name.LocalName ?? "(chybí)"}.");
        }

        ThrowIfError(pack, "Pohoda odmítla požadavek");

        // responsePackItem je maxOccurs="unbounded"; u exportních požadavků posíláme jeden.
        var item = pack.Element(Rsp + "responsePackItem")
            ?? throw new PohodaException("Odpověď Pohody neobsahuje žádnou položku (responsePackItem).");

        ThrowIfError(item, "Pohoda nezpracovala požadavek");

        var list = item.Element(listName)
            ?? throw new PohodaException($"Odpověď Pohody neobsahuje očekávaný seznam {listName.LocalName}.");

        if (IsError(list))
        {
            var details = ErrorDetails(list);
            throw new PohodaException(
                details.Count == 0
                    ? $"Export {listName.LocalName} skončil chybou."
                    : $"Export {listName.LocalName} skončil chybou: {string.Join("; ", details)}");
        }

        if (list.Element(Rdc + "parts") is not null)
        {
            throw new PohodaException(
                "Export Pohody byl rozdělen do více souborů (rdc:parts); zpracování dílčích souborů není podporováno.");
        }

        return list;
    }

    private static void ThrowIfError(XElement element, string prefix)
    {
        if (IsError(element))
        {
            var note = element.Attribute("note")?.Value;
            throw new PohodaException(string.IsNullOrWhiteSpace(note) ? $"{prefix}." : $"{prefix}: {note}");
        }
    }

    private static bool IsError(XElement element) =>
        string.Equals(element.Attribute("state")?.Value, ErrorState, StringComparison.OrdinalIgnoreCase);

    private static List<string> ErrorDetails(XElement list) =>
        [
            .. list.Elements(Rdc + "importDetails")
                .Elements(Rdc + "detail")
                .Where(d => string.Equals((string?)d.Element(Rdc + "state"), ErrorState, StringComparison.OrdinalIgnoreCase))
                .Select(d => FormatDetail((string?)d.Element(Rdc + "errno"), (string?)d.Element(Rdc + "note")))
                .Where(s => s.Length > 0),
        ];

    private static string FormatDetail(string? errno, string? note) =>
        (errno, note) switch
        {
            (null, null) => string.Empty,
            (null, _) => note!,
            (_, null) => $"chyba {errno}",
            _ => $"{note} (chyba {errno})",
        };
}
