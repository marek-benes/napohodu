using NaPohodu.Core.Filing;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Filing;

public sealed class FilingPlannerAndExecutorTests : IDisposable
{
    private const string Received = @"Fakturace\Přijaté faktury";

    private readonly TempDirectory _temp = new();
    private readonly IReadOnlyList<Invoice> _invoices = InvoiceParser.Parse(FixtureFiles.LoadResponse(RealFixtures.ReceivedInvoices2025));
    private readonly string _source;
    private readonly string _company;

    public FilingPlannerAndExecutorTests()
    {
        _source = _temp.Dir("zdroj");
        _company = _temp.Dir("Dokumenty", "Firma");
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Plan_ClassifiesFiles()
    {
        _temp.File("nová", "zdroj", "251100038.pdf");                       // bez dokumentů → prefix řady
        _temp.File("příloha", "zdroj", "251100038_dodaci_list.pdf");         // příloha téže faktury
        _temp.File("jiný obsah", "zdroj", "k251100002.pdf");                 // nepáruje se (prefix „k“)
        _temp.File("stejné", "zdroj", "251100002.pdf");
        _temp.File("stejné", "Dokumenty", "Firma", Received, "2511", "251100002", "251100002.pdf");
        _temp.File("nové", "zdroj", "251100001.pdf");
        _temp.File("staré", "Dokumenty", "Firma", Received, "251100001", "251100001.pdf");
        _temp.File("x", "zdroj", "241100209.pdf");                           // řada minulého roku, prefix neznámý
        _temp.File("x", "zdroj", "desktop.ini");

        var plan = Plan();

        Assert.Equal(_company, plan.CompanyFolder.Path);
        Assert.Collection(
            plan.Items,
            i => Assert.Equal(("241100209.pdf", FilingStatus.Unresolved), (i.FileName, i.Status)),
            i => Assert.Equal(("251100001.pdf", FilingStatus.Conflict, $@"{Received}\251100001"), (i.FileName, i.Status, i.SubFolder)),
            i => Assert.Equal(("251100002.pdf", FilingStatus.AlreadyFiled, 4451), (i.FileName, i.Status, i.InvoiceId!.Value)),
            i => Assert.Equal(("251100038.pdf", FilingStatus.Ready, $@"{Received}\2511\251100038"), (i.FileName, i.Status, i.SubFolder)),
            i => Assert.Equal(("251100038_dodaci_list.pdf", FilingStatus.Ready, "251100038"), (i.FileName, i.Status, i.InvoiceNumber)));
        Assert.Equal(["k251100002.pdf"], plan.Unmatched);
    }

    [Fact]
    public void Plan_WithoutCompanyFolder_MarksMatchesUnresolved()
    {
        _temp.File("x", "zdroj", "251100038.pdf");

        var plan = FilingPlanner.Plan(_source, _invoices, new CompanyFolderResolution(null, "nenalezena", []));

        Assert.Equal(FilingStatus.Unresolved, Assert.Single(plan.Items).Status);
    }

    [Fact]
    public void Execute_MovesReadyAndSkipsConflictsAndKeepsSource()
    {
        _temp.File("nová", "zdroj", "251100038.pdf");
        _temp.File("nové", "zdroj", "251100001.pdf");
        var conflictTarget = _temp.File("staré", "Dokumenty", "Firma", Received, "251100001", "251100001.pdf");

        var results = FilingExecutor.Execute(Plan());

        var target = Path.Combine(_company, Received, "2511", "251100038", "251100038.pdf");
        Assert.Equal("nová", File.ReadAllText(target));
        Assert.False(File.Exists(Path.Combine(_source, "251100038.pdf")));
        Assert.Equal("staré", File.ReadAllText(conflictTarget));
        Assert.True(File.Exists(Path.Combine(_source, "251100001.pdf")));
        Assert.Equal([FilingOutcome.Skipped, FilingOutcome.Moved], results.Select(r => r.Outcome));
    }

    [Fact]
    public void Execute_KeepBoth_RenamesInsteadOfOverwriting()
    {
        _temp.File("nové", "zdroj", "251100001.pdf");
        var existing = _temp.File("staré", "Dokumenty", "Firma", Received, "251100001", "251100001.pdf");
        _temp.File("ještě starší", "Dokumenty", "Firma", Received, "251100001", "251100001 (2).pdf");

        var result = Assert.Single(FilingExecutor.Execute(Plan(), ConflictMode.KeepBoth));

        Assert.Equal(FilingOutcome.MovedRenamed, result.Outcome);
        Assert.Equal(Path.Combine(_company, Received, "251100001", "251100001 (3).pdf"), result.TargetPath);
        Assert.Equal("nové", File.ReadAllText(result.TargetPath!));
        Assert.Equal("staré", File.ReadAllText(existing));
    }

    [Fact]
    public void Execute_IsSafeToRepeat()
    {
        _temp.File("nová", "zdroj", "251100038.pdf");
        FilingExecutor.Execute(Plan());

        var second = Plan();

        Assert.Empty(second.Items);
        Assert.Empty(FilingExecutor.Execute(second));
    }

    [Fact]
    public void Execute_TargetAppearedAfterPlanning_IsNotOverwritten()
    {
        _temp.File("nová", "zdroj", "251100038.pdf");
        var plan = Plan();
        var target = _temp.File("mezitím", "Dokumenty", "Firma", Received, "2511", "251100038", "251100038.pdf");

        var result = Assert.Single(FilingExecutor.Execute(plan));

        Assert.Equal(FilingOutcome.Failed, result.Outcome);
        Assert.Equal("mezitím", File.ReadAllText(target));
    }

    private FilingPlan Plan() =>
        FilingPlanner.Plan(_source, _invoices, new CompanyFolderResolution(_company, "test", []));
}
