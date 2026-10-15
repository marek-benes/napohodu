using System.Globalization;
using System.Xml.Linq;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>Filtr exportu faktur (<c>ftr:filterDocsType</c>). Data jsou včetně.</summary>
public sealed record InvoiceFilter(DateOnly? DateFrom = null, DateOnly? DateTill = null);

/// <summary>Požadavky na export dat z Pohody.</summary>
public static class PohodaRequests
{
    /// <summary>Seznam účetních jednotek (<c>acu:listAccountingUnitRequest</c>), bez <c>ico</c> a bez databáze.</summary>
    public static XDocument ListAccountingUnits() =>
        DataPack.Create(
            id: "acu",
            note: "Seznam účetních jednotek",
            unit: null,
            new XElement(Acu + "listAccountingUnitRequest", new XAttribute("version", "1.6")));

    /// <summary>
    /// Export přijatých faktur (<c>lst:listInvoiceRequest</c>) včetně záložky Dokumenty
    /// (<c>restrictionData/attachments=true</c> → <c>companyDocumentsFolder</c>, <c>subFolder</c>).
    /// <c>limit</c> neposíláme, takže export se nedělí na části (<c>rdc:parts</c>).
    /// </summary>
    public static XDocument ListReceivedInvoices(UnitIdentity unit, InvoiceFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(unit);
        filter ??= new InvoiceFilter();

        var request = new XElement(
            Lst + "listInvoiceRequest",
            new XAttribute("version", "2.0"),
            new XAttribute("invoiceType", "receivedInvoice"),
            new XAttribute("invoiceVersion", "2.0"),
            new XElement(
                Lst + "requestInvoice",
                new XElement(
                    Ftr + "filter",
                    filter.DateFrom is { } from ? new XElement(Ftr + "dateFrom", FormatDate(from)) : null,
                    filter.DateTill is { } till ? new XElement(Ftr + "dateTill", FormatDate(till)) : null)),
            new XElement(
                Lst + "restrictionData",
                new XElement(Lst + "attachments", "true")));

        return DataPack.Create(id: "inv", note: "Export přijatých faktur", unit, request);
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
