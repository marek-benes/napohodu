using NaPohodu.Core.Filing;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Filing;

public sealed class InvoiceSubFoldersTests : IDisposable
{
    private readonly TempDirectory _company = new();
    private readonly IReadOnlyList<Invoice> _invoices = InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2025));

    public void Dispose() => _company.Dispose();

    private Invoice ById(int id) => _invoices.Single(i => i.Id == id);

    [Theory]
    [InlineData(4451, @"Fakturace\Přijaté faktury\2511\251100002")]
    [InlineData(4450, @"Fakturace\Přijaté faktury\251100001")]   // plochá podsložka z Pohody se zachová
    public void ExistingSubFolderFromExportWins(int id, string expected)
    {
        var result = new InvoiceSubFolders(_invoices, _company.Path).Resolve(ById(id));

        Assert.Equal(expected, result.SubFolder);
    }

    [Fact]
    public void NewDocument_UsesSeriesPrefix()
    {
        var result = new InvoiceSubFolders(_invoices, _company.Path).Resolve(ById(4532));

        Assert.Equal(@"Fakturace\Přijaté faktury\2511\251100038", result.SubFolder);
    }

    [Fact]
    public void MissingIds_UsesSeriesOfAnotherInvoice()
    {
        var invoice = new Invoice { Id = 1, InvoiceType = "receivedInvoice", Number = "251100999" };

        var result = new InvoiceSubFolders([.. _invoices, invoice], _company.Path).Resolve(invoice);

        Assert.Equal(@"Fakturace\Přijaté faktury\2511\251100999", result.SubFolder);
    }

    [Fact]
    public void MissingIds_UsesSingleSeriesFolderOnDisk()
    {
        _company.Dir("Fakturace", "Přijaté faktury", "2411", "241100001");
        _company.Dir("Fakturace", "Přijaté faktury", "2311");

        var result = new InvoiceSubFolders(_invoices, _company.Path).Resolve(ById(4019));

        Assert.Equal(@"Fakturace\Přijaté faktury\2411\241100209", result.SubFolder);
    }

    [Fact]
    public void MissingIds_NoSeriesKnown_IsUnresolvedInsteadOfGuessing()
    {
        var result = new InvoiceSubFolders(_invoices, _company.Path).Resolve(ById(4019));

        Assert.False(result.IsResolved);
    }

    [Fact]
    public void MissingIds_AmbiguousFoldersOnDisk_IsUnresolved()
    {
        _company.Dir("Fakturace", "Přijaté faktury", "24");
        _company.Dir("Fakturace", "Přijaté faktury", "2411");

        var result = new InvoiceSubFolders(_invoices, _company.Path).Resolve(ById(4019));

        Assert.False(result.IsResolved);
    }

    [Theory]
    [InlineData(@"..\..\Windows")]
    [InlineData(@"C:\Windows")]
    public void UnsafeSubFolderFromExport_IsUnresolved(string subFolder)
    {
        var invoice = new Invoice
        {
            Id = 1,
            InvoiceType = "receivedInvoice",
            Number = "251100999",
            Documents = new InvoiceDocuments { SubFolder = subFolder },
        };

        Assert.False(new InvoiceSubFolders([invoice], _company.Path).Resolve(invoice).IsResolved);
    }
}
