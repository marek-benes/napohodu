using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Pohoda;

public class InvoiceParserTests
{
    private static IReadOnlyList<Invoice> Invoices() =>
        InvoiceParser.Parse(FixtureFiles.LoadResponse("Synthetic/listInvoice.received.response.xml"));

    [Fact]
    public void Parse_ReadsHeader()
    {
        var invoice = Invoices()[0];

        Assert.Equal(101, invoice.Id);
        Assert.Equal("receivedInvoice", invoice.InvoiceType);
        Assert.Equal("26110009", invoice.Number);
        Assert.Equal("2611", invoice.NumberSeries);
        Assert.Equal("FV-2026-042", invoice.OriginalDocument);
        Assert.Equal("20260042", invoice.SymVar);
        Assert.Equal(new DateOnly(2026, 3, 2), invoice.Date);
        Assert.Equal(new DateOnly(2026, 3, 1), invoice.DateTax);
        Assert.Equal(new DateOnly(2026, 3, 16), invoice.DateDue);
        Assert.Equal("Kancelářské potřeby", invoice.Text);
        Assert.Equal("Papírnictví Šťastný a.s.", invoice.PartnerDisplayName);
        Assert.Equal("11223344", invoice.PartnerIco);
        Assert.Null(invoice.LiquidationDate);
    }

    [Fact]
    public void Parse_SumsHomeCurrencyAcrossRatesAndRounding()
    {
        // priceNone 10.5 + priceLowSum 112 + priceHighSum 1210 + price3Sum 0 + priceRound -0.5
        Assert.Equal(1332m, Invoices()[0].TotalHome);
        Assert.Null(Invoices()[0].TotalForeign);
    }

    [Fact]
    public void Parse_ReadsDocumentsTab()
    {
        var documents = Invoices()[0].Documents;

        Assert.NotNull(documents);
        Assert.Equal(@"C:\ProgramData\STORMWARE\POHODA\Dokumenty\Žluťoučký kůň\", documents.CompanyDocumentsFolder);
        Assert.Equal(@"Fakturace\Přijaté faktury\2611\26110009", documents.SubFolder);
        Assert.Equal(["26110009.pdf", "26110009_dodaci_list.pdf"], documents.Files);
    }

    [Fact]
    public void Parse_ForeignCurrencyLiquidatedWithoutDocuments()
    {
        var invoice = Invoices()[1];

        Assert.Equal(102, invoice.Id);
        Assert.Null(invoice.NumberSeries);
        Assert.Equal("Hans Müller", invoice.PartnerDisplayName);
        Assert.Equal(2500m, invoice.TotalHome);
        Assert.Equal(new ForeignAmount("EUR", 100m), invoice.TotalForeign);
        Assert.Equal(new DateOnly(2026, 3, 10), invoice.LiquidationDate);
        Assert.Null(invoice.Documents);
    }
}
