using NaPohodu.Core.Filing;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Filing;

public sealed class AuditExportTests : IDisposable
{
    private const string Received = @"Fakturace\Přijaté faktury";

    private readonly TempDirectory _temp = new();
    private readonly IReadOnlyList<Invoice> _invoices = InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2025));
    private readonly string _company;
    private readonly string _destination;

    public AuditExportTests()
    {
        _company = _temp.Dir("Firma");
        _destination = Path.Combine(_temp.Path, "kontrola");

        // Soubory tak, jak je eviduje fixture (4450 plochá, 4451 se dvěma soubory, 4452 jeden chybí).
        _temp.File("a", "Firma", "Fakturace", "Přijaté faktury", "251100001", "251100001.pdf");
        _temp.File("b", "Firma", "Fakturace", "Přijaté faktury", "2511", "251100002", "k251100002.pdf");
        _temp.File("sken 2", "Firma", "Fakturace", "Přijaté faktury", "2511", "251100002", "Sken 1.pdf");
        _temp.File("c", "Firma", "Fakturace", "Přijaté faktury", "2511", "251100003", "K251100003.pdf");
        _temp.File("x", "Firma", "Fakturace", "Přijaté faktury", "2511", "251100003", "desktop.ini");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Plan_SelectsByTaxDateAndUsesSubFolderFromPohoda()
    {
        var plan = AuditExport.Plan(_invoices, _company, new DateOnly(2025, 1, 31), new DateOnly(2025, 1, 31));

        Assert.All(plan.Invoices, i => Assert.Equal(new DateOnly(2025, 1, 31), i.DateTax));
        Assert.Equal(
            ["251100001.pdf", "251100002_k251100002.pdf", "251100002_Sken 1.pdf", "251100003_K251100003.pdf"],
            plan.Files.Select(f => f.ExportName));
        Assert.Contains(@"251100003: Fakturace\Přijaté faktury\2511\251100003\Sken 1.pdf", plan.MissingFiles);
    }

    [Fact]
    public void Plan_InvoicesWithoutDocumentsAreListed()
    {
        var plan = AuditExport.Plan(_invoices, _company, new DateOnly(2025, 2, 1), new DateOnly(2025, 2, 28));

        Assert.Contains("251100038", plan.WithoutDocuments);
    }

    [Fact]
    public void Execute_CopiesAndNeverOverwrites()
    {
        var plan = AuditExport.Plan(_invoices, _company, new DateOnly(2025, 1, 31), new DateOnly(2025, 1, 31));
        _temp.File("jiný obsah", "kontrola", "251100002_k251100002.pdf");

        var first = AuditExport.Execute(plan, _destination);
        var second = AuditExport.Execute(plan, _destination);

        Assert.Equal("jiný obsah", File.ReadAllText(Path.Combine(_destination, "251100002_k251100002.pdf")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(_destination, "251100002_k251100002 (2).pdf")));
        Assert.Equal(AuditExportOutcome.CopiedRenamed, first.Single(r => r.File.ExportName == "251100002_k251100002.pdf").Outcome);
        // Druhý běh nic nekopíruje – ani soubor, který minule skončil jako „(2)“.
        Assert.All(second, r => Assert.Equal(AuditExportOutcome.AlreadyThere, r.Outcome));
        Assert.Equal(5, Directory.EnumerateFiles(_destination).Count());
        Assert.True(File.Exists(Path.Combine(_company, Received, "251100001", "251100001.pdf")));   // originál zůstal
    }

    [Theory]
    [InlineData("251100001.pdf", "251100001.pdf")]
    [InlineData("251100001_dodaci_list.pdf", "251100001_dodaci_list.pdf")]
    [InlineData("Sken 1.pdf", "251100001_Sken 1.pdf")]
    [InlineData("k251100001.pdf", "251100001_k251100001.pdf")]
    public void ExportName_PrefixesFilesNotNamedByNumber(string fileName, string expected)
    {
        Assert.Equal(expected, AuditExport.ExportName("251100001", fileName));
    }
}
