using NaPohodu.Core.Podklady;
using NaPohodu.Tests.Filing;

namespace NaPohodu.Tests.Podklady;

public sealed class PodkladyInitTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose() => _root.Dispose();

    private string Customer => Path.Combine(_root.Path, "Firma");

    [Fact]
    public void NewCustomer_CreatesEverythingAndIsIdempotent()
    {
        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, VatPeriod.Monthly);

        Assert.Null(plan.Error);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.All(plan.Actions, a => Assert.Equal(PodkladyActionKind.Create, a.Kind));
        // zákazník, inbox, Vráceno, Šanony, rok, 12 × (období + 5 agend), Mzdy + 12 měsíců + Mzdové listy, Závěrka, Dokumenty
        Assert.Equal(5 + (12 * 6) + 14 + 2, plan.Actions.Count);

        var results = PodkladyInit.Execute(plan);

        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.True(Directory.Exists(Path.Combine(Customer, "Ke zpracování", "Vráceno")));
        Assert.True(Directory.Exists(Path.Combine(Customer, "Šanony", "2026", "09", "Přijaté faktury")));
        Assert.True(Directory.Exists(Path.Combine(Customer, "Šanony", "2026", "Mzdy", "Mzdové listy")));
        Assert.Empty(PodkladyLayout.Nonconforming(Path.Combine(Customer, "Šanony")));

        var again = PodkladyInit.Plan(_root.Path, "Firma", 2026, null);
        Assert.Null(again.Error);
        Assert.Empty(again.Actions);
        Assert.Empty(again.Warnings);
        Assert.Equal("podle založeného roku 2026", again.PeriodSource);
    }

    // Tvar jako ADJG (2026-10-18): období pojmenovaná po svém a volné soubory.
    [Fact]
    public void OldBinders_AreRenamedToArchive()
    {
        _root.Dir("Firma", "Ke zpracování", "Vráceno");
        _root.File("x", "Firma", "Šanony", "2026", "PF 092026", "Kontrolní_hlášení.pdf");
        _root.File("x", "Firma", "Šanony", "2026", "potvrzeni.pdf");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, VatPeriod.Quarterly);
        PodkladyInit.Execute(plan);

        var archive = Assert.Single(plan.Actions, a => a.Kind == PodkladyActionKind.Archive);
        Assert.Equal(Path.Combine(Customer, "Archiv"), archive.Target);
        Assert.Empty(plan.Warnings);
        Assert.True(File.Exists(Path.Combine(Customer, "Archiv", "2026", "PF 092026", "Kontrolní_hlášení.pdf")));
        Assert.True(Directory.Exists(Path.Combine(Customer, "Šanony", "2026", "Q3", "DPH")));
        Assert.False(File.Exists(Path.Combine(Customer, "Šanony", "2026", "potvrzeni.pdf")));
    }

    [Fact]
    public void ExistingArchive_ForeignItemsAreOnlyReported()
    {
        _root.Dir("Firma", "Archiv");
        _root.Dir("Firma", "Šanony", "2026", "Q1", "DPH");
        _root.Dir("Firma", "Šanony", "2026", "Moje složka");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, null);

        Assert.Null(plan.Error);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == PodkladyActionKind.Archive);
        Assert.Contains(plan.Warnings, w => w.Contains(@"2026\Moje složka\", StringComparison.Ordinal));
        Assert.Equal(VatPeriod.Quarterly, plan.Period);
    }

    [Fact]
    public void PeriodUnknown_IsError()
    {
        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, null);

        Assert.True(plan.NeedsPeriod);
        Assert.Empty(plan.Actions);
        Assert.False(plan.CanApply);
        Assert.Throws<InvalidOperationException>(() => PodkladyInit.Execute(plan));
    }

    [Fact]
    public void Period_IsTakenFromPreviousYear()
    {
        _root.Dir("Firma", "Šanony", "2025", "Q4", "Banka");
        _root.Dir("Firma", "Šanony", "2027", "01");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, null);

        Assert.Equal(VatPeriod.Quarterly, plan.Period);
        Assert.Equal("podle roku 2025", plan.PeriodSource);
    }

    [Fact]
    public void Period_FromFilingsBeatsOtherYears()
    {
        _root.Dir("Firma", "Šanony", "2025", "Q4", "Banka");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, null, new(VatPeriod.Monthly, "podle přiznání X"));

        Assert.Equal(VatPeriod.Monthly, plan.Period);
        Assert.Equal("podle přiznání X", plan.PeriodSource);
    }

    [Fact]
    public void Period_RequestedOverridesFilingsWithWarning()
    {
        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, VatPeriod.Quarterly, new(VatPeriod.Monthly, "podle přiznání X"));

        Assert.Equal(VatPeriod.Quarterly, plan.Period);
        Assert.Contains(plan.Warnings, w => w == "Zadáno čtvrtletně, ale podle přiznání X je měsíčně.");
    }

    [Fact]
    public void Period_ExistingYearBeatsFilings()
    {
        _root.Dir("Firma", "Šanony", "2026", "Q1");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, null, new(VatPeriod.Monthly, "podle přiznání X"));

        Assert.Equal(VatPeriod.Quarterly, plan.Period);
    }

    [Fact]
    public void Period_CannotChangeExistingYear()
    {
        _root.Dir("Firma", "Šanony", "2026", "01");

        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, VatPeriod.Quarterly);

        Assert.Contains("měsíčně", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRoot_IsError()
    {
        var plan = PodkladyInit.Plan(Path.Combine(_root.Path, "neni"), "Firma", 2026, VatPeriod.Monthly);

        Assert.Contains("neexistuje", plan.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_StopsWhenArchiveAppeared()
    {
        _root.File("x", "Firma", "Šanony", "volný.pdf");
        var plan = PodkladyInit.Plan(_root.Path, "Firma", 2026, VatPeriod.Monthly);
        _root.Dir("Firma", "Archiv");

        var results = PodkladyInit.Execute(plan);

        Assert.False(results[^1].Succeeded);
        Assert.Equal(PodkladyActionKind.Archive, results[^1].Action.Kind);
        Assert.True(File.Exists(Path.Combine(Customer, "Šanony", "volný.pdf")));
    }

    [Fact]
    public void Nonconforming_AllowsContentOfLeafFolders()
    {
        var binders = _root.Dir("Šanony");
        _root.File("x", "Šanony", "2026", "03", "Přijaté faktury", "261100001.pdf");
        _root.File("x", "Šanony", "2026", "Mzdy", "09", "páska.pdf");
        _root.File("x", "Šanony", "2026", "Dokumenty", "Smlouvy", "smlouva.pdf");
        _root.File("x", "Šanony", "2026", "Závěrka", "přiznání.pdf");
        _root.File("x", "Šanony", "2026", "desktop.ini");

        Assert.Empty(PodkladyLayout.Nonconforming(binders));
    }

    [Fact]
    public void Nonconforming_ReportsForeignItems()
    {
        var binders = _root.Dir("Šanony");
        _root.File("x", "Šanony", "1.Q.zip");
        _root.Dir("Šanony", "2025", "01");
        _root.Dir("Šanony", "2025", "Q1");
        _root.Dir("Šanony", "2025", "Závěr 2025");
        _root.File("x", "Šanony", "2025", "01", "volný.pdf");
        _root.Dir("Šanony", "2025", "Mzdy", "2025 MZDY");

        var found = PodkladyLayout.Nonconforming(binders);

        Assert.Contains("1.Q.zip", found);
        Assert.Contains(@"2025\ (měsíce i čtvrtletí)", found);
        Assert.Contains(@"2025\Závěr 2025\", found);
        Assert.Contains(@"2025\01\volný.pdf", found);
        Assert.Contains(@"2025\Mzdy\2025 MZDY\", found);
    }
}
