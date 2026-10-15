using System.Xml.Linq;
using NaPohodu.Core.Models;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>Parser odpovědi na <c>lst:listInvoiceRequest</c>.</summary>
public static class InvoiceParser
{
    public static readonly XName ListName = Lst + "listInvoice";

    public static IReadOnlyList<Invoice> Parse(XDocument response) =>
        [.. ResponsePack.GetList(response, ListName).Elements(Lst + "invoice").Select(ParseInvoice)];

    private static Invoice ParseInvoice(XElement invoice)
    {
        var header = invoice.Element(Inv + "invoiceHeader")
            ?? throw new PohodaException("Faktura v odpovědi Pohody nemá hlavičku (invoiceHeader).");
        var number = header.Element(Inv + "number");
        var partner = header.Element(Inv + "partnerIdentity")?.Element(Typ + "address");
        var summary = invoice.Element(Inv + "invoiceSummary");

        return new Invoice
        {
            Id = header.Int(Inv + "id")
                ?? throw new PohodaException("Faktura v odpovědi Pohody nemá ID (invoiceHeader/id)."),
            InvoiceType = header.Required(Inv + "invoiceType"),
            Number = number.Text(Typ + "numberRequested") ?? string.Empty,
            NumberSeries = number.Text(Typ + "ids"),
            OriginalDocument = header.Text(Inv + "originalDocument"),
            SymVar = header.Text(Inv + "symVar"),
            Date = header.Date(Inv + "date"),
            DateTax = header.Date(Inv + "dateTax"),
            DateDue = header.Date(Inv + "dateDue"),
            Text = header.Text(Inv + "text"),
            PartnerCompany = partner.Text(Typ + "company"),
            PartnerName = partner.Text(Typ + "name"),
            PartnerIco = partner.Text(Typ + "ico"),
            TotalHome = SumHome(summary?.Element(Inv + "homeCurrency")),
            TotalForeign = ParseForeign(summary?.Element(Inv + "foreignCurrency")),
            LiquidationDate = header.Element(Inv + "liquidation").Date(Typ + "date"),
            Documents = ParseDocuments(invoice.Element(Inv + "attachments")),
        };
    }

    /// <summary>Součet <c>typ:typeCurrencyHome</c>: všechny sazby včetně DPH + nulová sazba + zaokrouhlení.</summary>
    internal static decimal SumHome(XElement? home)
    {
        if (home is null)
        {
            return 0m;
        }

        return (home.Decimal(Typ + "priceNone") ?? 0m)
            + (home.Decimal(Typ + "priceLowSum") ?? 0m)
            + (home.Decimal(Typ + "priceHighSum") ?? 0m)
            + (home.Decimal(Typ + "price3Sum") ?? 0m)
            + (home.Element(Typ + "round").Decimal(Typ + "priceRound") ?? 0m);
    }

    private static ForeignAmount? ParseForeign(XElement? foreign)
    {
        var currency = foreign?.Element(Typ + "currency").Text(Typ + "ids");
        var total = foreign.Decimal(Typ + "priceSum");

        // Pohoda může element poslat i u tuzemské faktury; bez kódu měny nebo s nulou ho ignorujeme.
        return currency is null || total is null or 0m ? null : new ForeignAmount(currency, total.Value);
    }

    private static InvoiceDocuments? ParseDocuments(XElement? attachments)
    {
        var files = attachments?.Element(Typ + "files");
        if (files is null)
        {
            return null;
        }

        return new InvoiceDocuments
        {
            CompanyDocumentsFolder = files.Text(Typ + "companyDocumentsFolder"),
            SubFolder = files.Text(Typ + "subFolder"),
            Files = [.. files.Elements(Typ + "file").Select(f => f.Text(Typ + "name")).OfType<string>()],
        };
    }
}
