using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Pohoda;

public class InvoiceParserTests
{
    private static IReadOnlyList<Invoice> Invoices2025() =>
        InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2025));

    private static Invoice Invoice2025(int id) => Invoices2025().Single(i => i.Id == id);

    [Fact]
    public void Parse_ReadsAllInvoices()
    {
        var invoices = Invoices2025();

        Assert.Equal(53, invoices.Count);
        Assert.All(invoices, i => Assert.Equal("receivedInvoice", i.InvoiceType));
        Assert.Equal(36, invoices.Count(i => i.Documents is not null));
    }

    [Fact]
    public void Parse_ReadsHeader()
    {
        var invoice = Invoice2025(4451);

        Assert.Equal("251100002", invoice.Number);
        Assert.Equal("2511", invoice.NumberSeries);
        Assert.Equal("DOD00004", invoice.OriginalDocument);
        Assert.Equal(new DateOnly(2025, 1, 31), invoice.Date);
        Assert.Equal(new DateOnly(2025, 1, 31), invoice.DateTax);
        Assert.Equal(new DateOnly(2025, 2, 14), invoice.DateDue);
        Assert.Equal(new DateOnly(2025, 2, 14), invoice.LiquidationDate);
        Assert.Equal("Popis dokladu 4", invoice.Text);
        Assert.Equal("Firma Šťastná 026", invoice.PartnerDisplayName);
        Assert.Equal("90000022", invoice.PartnerIco);
        Assert.Equal(18939.6m, invoice.TotalHome);
        Assert.Null(invoice.TotalForeign);
    }

    [Fact]
    public void Parse_DocumentsWithPrefixLevelAndFilesNotNamedByNumber()
    {
        var documents = Invoice2025(4451).Documents;

        Assert.NotNull(documents);
        Assert.Equal(RealFixtures.CompanyDocumentsFolder, documents.CompanyDocumentsFolder);
        Assert.Equal(@"Fakturace\Přijaté faktury\2511\251100002", documents.SubFolder);
        Assert.Equal(["k251100002.pdf", "Sken 1.pdf"], documents.Files);
    }

    [Fact]
    public void Parse_DocumentsWithFlatSubFolder()
    {
        var invoice = Invoice2025(4450);

        Assert.Equal(@"Fakturace\Přijaté faktury\251100001", invoice.Documents?.SubFolder);
        Assert.Equal(["251100001.pdf"], invoice.Documents?.Files);
        Assert.Null(invoice.PartnerIco);
    }

    [Fact]
    public void Parse_InvoiceFromPreviousYearSeries_HasNoNumberSeries()
    {
        var invoice = Invoice2025(4019);

        Assert.Equal("241100209", invoice.Number);
        Assert.Null(invoice.NumberSeries);
        Assert.Null(invoice.Documents);
    }

    [Theory]
    [InlineData(4464, "800")]      // priceNone -0.01 + priceLowSum 50 + priceHighSum 750.01
    [InlineData(4474, "91.42")]    // priceHighSum 91.43 + priceRound -0.01
    [InlineData(4475, "472.28")]   // priceNone 472.27 + priceRound 0.01
    [InlineData(4535, "-204")]     // dobropisová částka
    public void Parse_SumsHomeCurrencyAcrossRatesAndRounding(int id, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Invoice2025(id).TotalHome);
    }

    [Fact]
    public void Parse_ForeignCurrency()
    {
        var invoice = Invoice2025(4019);

        Assert.Equal(new ForeignAmount("EUR", 423.5m), invoice.TotalForeign);
        Assert.Equal(10473.16m, invoice.TotalHome);
    }

    [Fact]
    public void Parse_UnitWithoutAnyDocuments_HasNoDocumentsTab()
    {
        var invoices = InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2026WithoutDocuments));

        Assert.Equal(16, invoices.Count);
        Assert.All(invoices, i => Assert.Null(i.Documents));
    }

    [Fact]
    public void Parse_RejectedPackage_ThrowsWithPohodaNote()
    {
        var ex = Assert.Throws<PohodaException>(
            () => InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ErrorNotForThisUnit)));

        Assert.Contains("Tento balíček není určen pro tuto jednotku.", ex.Message, StringComparison.Ordinal);
    }
}
