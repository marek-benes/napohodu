using System.CommandLine;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using NaPohodu.App.Cli;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Cli;

public sealed class CliTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeRunner _runner = new();

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    [Fact]
    public async Task Customers_PrintsTable()
    {
        var exitCode = await Run("customers");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var text = _output.ToString();
        Assert.Contains(RealFixtures.UnitDataFile2025, text, StringComparison.Ordinal);
        Assert.Contains("Firma Šťastná 011", text, StringComparison.Ordinal);
        Assert.Contains("daňová evidence", text, StringComparison.Ordinal);
        Assert.Contains("podvojné (nezisková)", text, StringComparison.Ordinal);
        Assert.Contains("Celkem 83 jednotek.", text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(_runner.Calls).Database);
    }

    [Fact]
    public async Task Customers_FiltersByYearAndSearch()
    {
        var exitCode = await Run("customers", "--year", "2025", "--search", "šťastná 011");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var text = _output.ToString();
        Assert.Contains(RealFixtures.UnitDataFile2025, text, StringComparison.Ordinal);
        Assert.DoesNotContain("90000010_2026.mdb", text, StringComparison.Ordinal);
        Assert.Contains("Zobrazeno 1 z 83 jednotek.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_PrintsTableAndDocumentsSummary()
    {
        var exitCode = await Run("invoices", "--customer", RealFixtures.UnitDataFile2025, "--from", "2025-01-01", "--to", "2025-12-31");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var text = _output.ToString();
        Assert.Contains("251100002", text, StringComparison.Ordinal);
        Assert.Contains(@"Fakturace\Přijaté faktury\2511\251100002", text, StringComparison.Ordinal);
        Assert.Contains(@"Fakturace\Přijaté faktury\251100001", text, StringComparison.Ordinal);
        Assert.Contains("EUR", text, StringComparison.Ordinal);
        Assert.Contains("Celkem 53 faktur.", text, StringComparison.Ordinal);
        Assert.Contains($"Složka firmy v Dokumentech: {RealFixtures.CompanyDocumentsFolder}", text, StringComparison.Ordinal);
        Assert.Contains("Bez složky firmy: 17, bez podsložky dokladu: 17.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_WithoutIco_ResolvesUnitFirst()
    {
        // Bez přípony .mdb, jak ji uživatel nejspíš napíše.
        var exitCode = await Run("invoices", "-c", "90000010_2025");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.Collection(
            _runner.Calls,
            acu => Assert.Equal((null, null), (acu.Database, acu.Ico)),
            inv => Assert.Equal((RealFixtures.UnitDataFile2025, RealFixtures.UnitIco), (inv.Database, inv.Ico)));
    }

    [Fact]
    public async Task Invoices_WithIco_SkipsUnitLookup()
    {
        var exitCode = await Run("invoices", "-c", RealFixtures.UnitDataFile2025, "--ico", RealFixtures.UnitIco);

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal((RealFixtures.UnitDataFile2025, RealFixtures.UnitIco), (call.Database, call.Ico));
    }

    [Fact]
    public async Task Invoices_RejectedByPohoda_ReturnsErrorWithNote()
    {
        _runner.InvoiceFixture = RealFixtures.ErrorNotForThisUnit;

        var exitCode = await Run("invoices", "-c", RealFixtures.UnitDataFile2025, "--ico", RealFixtures.UnitIco);

        Assert.Equal(PohodaCommandRunner.PohodaError, exitCode);
        Assert.Contains("Tento balíček není určen pro tuto jednotku.", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_UnknownCustomer_ReturnsError()
    {
        var exitCode = await Run("invoices", "-c", "neexistuje");

        Assert.Equal(PohodaCommandRunner.PohodaError, exitCode);
        Assert.Contains("„neexistuje“", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_SaveResponse_WritesRawBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"napohodu-{Guid.NewGuid():N}.xml");
        try
        {
            var exitCode = await Run("invoices", "-c", "X", "--ico", RealFixtures.UnitIco, "--save-response", path);

            Assert.Equal(PohodaCommandRunner.Ok, exitCode);
            Assert.Equal(FixtureFiles.ReadBytes(RealFixtures.ReceivedInvoices2025), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Filing_PreviewDoesNotMoveAndApplyMoves()
    {
        using var temp = new Filing.TempDirectory();
        var source = temp.Dir("zdroj");
        var company = temp.Dir("Firma");
        temp.File("nová", "zdroj", "251100038.pdf");
        temp.File("x", "zdroj", "neznámý.pdf");
        string[] args = ["filing", "-c", RealFixtures.UnitDataFile2025, "--source", source, "--company-folder", company];

        var preview = await Run(args);

        Assert.Equal(PohodaCommandRunner.Ok, preview);
        Assert.True(File.Exists(Path.Combine(source, "251100038.pdf")));
        var text = _output.ToString();
        Assert.Contains(@"Fakturace\Přijaté faktury\2511\251100038", text, StringComparison.Ordinal);
        Assert.Contains("Spárováno 1: připraveno 1", text, StringComparison.Ordinal);
        Assert.Contains("Nespárováno 1", text, StringComparison.Ordinal);
        Assert.Contains("Náhled – nic se nepřesunulo.", text, StringComparison.Ordinal);

        var apply = await Run([.. args, "--apply"]);

        Assert.Equal(PohodaCommandRunner.Ok, apply);
        Assert.True(File.Exists(Path.Combine(company, "Fakturace", "Přijaté faktury", "2511", "251100038", "251100038.pdf")));
        Assert.False(File.Exists(Path.Combine(source, "251100038.pdf")));
        Assert.Contains("Přesunuto 1, přeskočeno 0, chyb 0.", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filing_UsesCompanyFolderFromExport()
    {
        using var temp = new Filing.TempDirectory();
        var source = temp.Dir("zdroj");

        var exitCode = await Run("filing", "-c", RealFixtures.UnitDataFile2025, "--source", source);

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.Contains($"Složka firmy: {RealFixtures.CompanyDocumentsFolder.TrimEnd('\\')} – export faktur", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocDirs_PreviewThenApply()
    {
        using var temp = new Filing.TempDirectory();
        var mdb = Mdb.TestMdb.Create(Path.Combine(temp.Path, "data", RealFixtures.UnitDataFile2025));
        Mdb.TestMdb.AddInvoice(mdb, 4451, "251100002");
        var company = temp.Dir("Firma");
        temp.File("x", "Firma", "Fakturace", "Přijaté faktury", "2511", "251100002", "k251100002.pdf");
        string[] args = ["docdirs", "-c", RealFixtures.UnitDataFile2025, "--company-folder", company];

        var preview = await Run(SettingsWithData(temp), args);

        Assert.Equal(PohodaCommandRunner.Ok, preview);
        Assert.Contains("DocDirs: vložit 1", _output.ToString(), StringComparison.Ordinal);
        Assert.Empty(Mdb.TestMdb.DocDirs(mdb));

        var apply = await Run(SettingsWithData(temp), [.. args, "--apply"]);

        Assert.Equal(PohodaCommandRunner.Ok, apply);
        Assert.Equal(@"Fakturace\Přijaté faktury\2511\251100002", Assert.Single(Mdb.TestMdb.DocDirs(mdb)).Dir);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(temp.Path, "zalohy")));
    }

    [Fact]
    public async Task Filing_ApplyWithMarkDocs_MovesAndMarks()
    {
        using var temp = new Filing.TempDirectory();
        var mdb = Mdb.TestMdb.Create(Path.Combine(temp.Path, "data", RealFixtures.UnitDataFile2025));
        Mdb.TestMdb.AddInvoice(mdb, 4532, "251100038");
        var source = temp.Dir("zdroj");
        var company = temp.Dir("Firma");
        temp.File("nová", "zdroj", "251100038.pdf");

        var exitCode = await Run(
            SettingsWithData(temp),
            "filing", "-c", RealFixtures.UnitDataFile2025, "--source", source, "--company-folder", company, "--apply", "--mark-docs");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.True(File.Exists(Path.Combine(company, "Fakturace", "Přijaté faktury", "2511", "251100038", "251100038.pdf")));
        Assert.Equal((4532, @"Fakturace\Přijaté faktury\2511\251100038"), Assert.Single(Mdb.TestMdb.DocDirs(mdb)) is var row ? (row.RelId, row.Dir) : default);
        Assert.Contains("DocDirs zapsáno: vloženo 1", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filing_MarkDocsWhileUnitIsOpen_MovesButDoesNotWrite()
    {
        using var temp = new Filing.TempDirectory();
        var mdb = Mdb.TestMdb.Create(Path.Combine(temp.Path, "data", RealFixtures.UnitDataFile2025));
        Mdb.TestMdb.AddInvoice(mdb, 4532, "251100038");
        var source = temp.Dir("zdroj");
        var company = temp.Dir("Firma");
        temp.File("nová", "zdroj", "251100038.pdf");

        // Pohoda drží MDB otevřenou: skutečný zámek Jet nesmaže, dokud je soubor otevřený.
        using var pohoda = new System.Data.OleDb.OleDbConnection($"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={mdb}");
        pohoda.Open();

        var exitCode = await Run(
            SettingsWithData(temp),
            "filing", "-c", RealFixtures.UnitDataFile2025, "--source", source, "--company-folder", company, "--apply", "--mark-docs");

        Assert.Equal(PohodaCommandRunner.PohodaError, exitCode);
        Assert.True(File.Exists(Path.Combine(company, "Fakturace", "Přijaté faktury", "2511", "251100038", "251100038.pdf")));
        Assert.Contains("napohodu docdirs -c 90000010_2025.mdb --apply", _output.ToString(), StringComparison.Ordinal);
        pohoda.Close();
        Assert.Empty(Mdb.TestMdb.DocDirs(mdb));
    }

    [Fact]
    public async Task Filing_WithoutSource_UsesCustomerInbox()
    {
        using var temp = new Filing.TempDirectory();
        var company = temp.Dir("Dokumenty", "Firma");
        temp.File("nová", "Podklady", "Firma", "Ke zpracování", "251100038.pdf");
        var settings = Settings();
        settings["Podklady:RootPath"] = Path.Combine(temp.Path, "Podklady");

        var exitCode = await Run(settings, "filing", "-c", RealFixtures.UnitDataFile2025, "--company-folder", company);

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.Contains("Zdroj: Ke zpracování zákazníka v Podkladech.", _error.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"Podklady\Firma\Ke zpracování", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Spárováno 1: připraveno 1", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filing_WithoutSource_InboxMissing_IsRejected()
    {
        using var temp = new Filing.TempDirectory();
        var company = temp.Dir("Dokumenty", "Firma");
        var settings = Settings();
        settings["Podklady:RootPath"] = temp.Dir("Podklady");

        var exitCode = await Run(settings, "filing", "-c", RealFixtures.UnitDataFile2025, "--company-folder", company);

        Assert.Equal(PohodaCommandRunner.InvalidArguments, exitCode);
        Assert.Contains("chybí složka zákazníka „Firma“", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filing_WithoutSourceAndPodklady_IsConfigurationError()
    {
        var exitCode = await Run("filing", "-c", RealFixtures.UnitDataFile2025);

        Assert.Equal(PohodaCommandRunner.ConfigurationError, exitCode);
        Assert.Contains("Podklady:RootPath", _error.ToString(), StringComparison.Ordinal);
        Assert.False(_runner.WasCalled);
    }

    [Fact]
    public async Task Filing_MarkDocsWithoutApply_IsRejected()
    {
        using var temp = new Filing.TempDirectory();

        var exitCode = await Run("filing", "-c", RealFixtures.UnitDataFile2025, "--source", temp.Path, "--mark-docs");

        Assert.Equal(PohodaCommandRunner.InvalidArguments, exitCode);
        Assert.False(_runner.WasCalled);
    }

    [Fact]
    public async Task Filing_MissingSource_IsRejectedWithoutRunningPohoda()
    {
        var exitCode = await Run("filing", "-c", RealFixtures.UnitDataFile2025, "--source", @"C:\neexistuje\nikde");

        Assert.Equal(PohodaCommandRunner.InvalidArguments, exitCode);
        Assert.False(_runner.WasCalled);
    }

    [Fact]
    public async Task Invoices_WithoutCustomer_IsParseError()
    {
        var exitCode = await Run("invoices");

        Assert.NotEqual(PohodaCommandRunner.Ok, exitCode);
        Assert.False(_runner.WasCalled);
    }

    [Theory]
    [InlineData("1.3.2026", "2026-03-31")]
    [InlineData("2026-04-01", "2026-03-31")]
    public async Task Invoices_InvalidDates_AreRejected(string from, string to)
    {
        var exitCode = await Run("invoices", "-c", "X", "--from", from, "--to", to);

        Assert.NotEqual(PohodaCommandRunner.Ok, exitCode);
        Assert.False(_runner.WasCalled);
    }

    [Fact]
    public async Task PohodaError_ReturnsOneWithMessage()
    {
        _runner.Error = new PohodaException("Pohoda.exe skončila s kódem 554.");

        var exitCode = await Run("customers");

        Assert.Equal(PohodaCommandRunner.PohodaError, exitCode);
        Assert.Contains("Chyba: Pohoda.exe skončila s kódem 554.", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingConfiguration_ReturnsTwoWithoutRunningPohoda()
    {
        var exitCode = await Run(new Dictionary<string, string?>(), "customers");

        Assert.Equal(PohodaCommandRunner.ConfigurationError, exitCode);
        Assert.Contains("Pohoda:Username", _error.ToString(), StringComparison.Ordinal);
        Assert.False(_runner.WasCalled);
    }

    [Fact]
    public async Task Config_NeverPrintsPassword()
    {
        var exitCode = await Run("config");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.Contains("(nastaveno)", _output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("tajne-heslo", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Podklady:            (nenastaveno)", _output.ToString(), StringComparison.Ordinal);
    }

    private Task<int> Run(params string[] args) => Run(Settings(), args);

    private static Dictionary<string, string?> Settings() =>
        new()
        {
            // Validace vyžaduje existující soubor; skutečně se nespouští (falešný runner).
            ["Pohoda:ExePath"] = typeof(CliTests).Assembly.Location,
            ["Pohoda:Username"] = "uzivatel",
            ["Pohoda:Password"] = "tajne-heslo",
        };

    private static Dictionary<string, string?> SettingsWithData(Filing.TempDirectory temp)
    {
        var settings = Settings();
        settings["Pohoda:DataPath"] = Path.Combine(temp.Path, "data");
        settings["Pohoda:BackupPath"] = Path.Combine(temp.Path, "zalohy");
        return settings;
    }

    private Task<int> Run(Dictionary<string, string?> settings, params string[] args)
    {
        var services = new CliServices
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            RunnerFactory = (_, _) => _runner,
        };

        return NapohoduCli.Create(services)
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = _output, Error = _error }, TestContext.Current.CancellationToken);
    }

    /// <summary>Vrací fixture podle typu požadavku, zaznamenává volání, umí simulovat chybu.</summary>
    private sealed class FakeRunner : IPohodaXmlRunner
    {
        public PohodaException? Error { get; set; }

        public string InvoiceFixture { get; set; } = RealFixtures.ReceivedInvoices2025;

        public List<(string? Database, string? Ico)> Calls { get; } = [];

        public bool WasCalled => Calls.Count > 0;

        public Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
        {
            Calls.Add((database, request.Root!.Attribute("ico")?.Value));
            if (Error is not null)
            {
                throw Error;
            }

            var isUnitList = request.Descendants(PohodaNamespaces.Acu + "listAccountingUnitRequest").Any();
            var bytes = FixtureFiles.ReadBytes(isUnitList ? RealFixtures.AccountingUnits : InvoiceFixture);
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
