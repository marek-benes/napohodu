using NaPohodu.Core.Filing;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Filing;

public sealed class CompanyFolderResolverTests : IDisposable
{
    private readonly TempDirectory _documents = new();

    public void Dispose() => _documents.Dispose();

    // Tvary ověřené proti skutečným složkám Pohody (2026-10-15), jména firem vymyšlená.
    [Theory]
    [InlineData("Rybník", "Rybník s.r.o.", true)]
    [InlineData("R&S Půjčovna", "R&S Půjčovna s.r.o.", true)]
    [InlineData("Rosteme spolu z s", "Rosteme spolu, z. s.", true)]
    [InlineData("Mateřská škola Lhota Polní 12 příspěvková organizace", "Mateřská škola, Lhota, Polní 12, příspěvková organizace", true)]
    [InlineData("Jana Nováková", "Jana Nováková", true)]
    [InlineData("STAVBY", "Stavby, spol. s r.o.", true)]
    [InlineData("Kovo", "Kovo a.s.", true)]
    [InlineData("Adid", "Adidas", false)]                 // chyba 1. generace: uříznutí uprostřed slova
    [InlineData("Kovo", "Kovo Morava a.s.", false)]       // zbytek není právní forma
    [InlineData("Rybník s r o", "Rybník s.r.o.", true)]
    [InlineData("", "Rybník s.r.o.", false)]
    public void Matches(string folder, string company, bool expected)
    {
        Assert.Equal(expected, CompanyFolderResolver.Matches(folder, company));
    }

    [Fact]
    public void Resolve_PrefersExport()
    {
        var invoices = InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2025));

        var result = CompanyFolderResolver.Resolve(Unit("Cokoli"), invoices, _documents.Path);

        Assert.Equal(RealFixtures.CompanyDocumentsFolder.TrimEnd('\\'), result.Path);
    }

    [Fact]
    public void Resolve_ByNameWhenExportHasNoFolder()
    {
        _documents.Dir("Rybník");
        _documents.Dir("Jiná firma");

        var result = CompanyFolderResolver.Resolve(Unit("Rybník s.r.o."), [], _documents.Path);

        Assert.Equal(Path.Combine(_documents.Path, "Rybník"), result.Path);
    }

    [Fact]
    public void Resolve_NoFolder_ReturnsSimilarCandidates()
    {
        _documents.Dir("Rybník Holding");
        _documents.Dir("Jiná firma");

        var result = CompanyFolderResolver.Resolve(Unit("Rybník s.r.o."), [], _documents.Path);

        Assert.False(result.IsResolved);
        Assert.Equal([Path.Combine(_documents.Path, "Rybník Holding")], result.Candidates);
    }

    [Fact]
    public void Resolve_Ambiguous_IsNotResolved()
    {
        _documents.Dir("Rybník");
        _documents.Dir("Rybník s r o");

        var result = CompanyFolderResolver.Resolve(Unit("Rybník s.r.o."), [], _documents.Path);

        Assert.False(result.IsResolved);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Resolve_ExportWithTwoDifferentFolders_IsNotResolved()
    {
        Invoice[] invoices =
        [
            InvoiceWithFolder(1, @"C:\Dokumenty\A\"),
            InvoiceWithFolder(2, @"C:\Dokumenty\B\"),
        ];

        var result = CompanyFolderResolver.Resolve(Unit("A"), invoices, _documents.Path);

        Assert.False(result.IsResolved);
    }

    private static AccountingUnit Unit(string company) => new() { DataFile = "x.mdb", Company = company };

    private static Invoice InvoiceWithFolder(int id, string folder) =>
        new()
        {
            Id = id,
            InvoiceType = "receivedInvoice",
            Number = $"25110000{id}",
            Documents = new InvoiceDocuments { CompanyDocumentsFolder = folder, SubFolder = $@"Fakturace\Přijaté faktury\25110000{id}" },
        };
}
