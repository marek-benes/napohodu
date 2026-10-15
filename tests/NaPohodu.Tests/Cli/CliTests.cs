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
        Assert.Contains("StwPh_12345678_2026", text, StringComparison.Ordinal);
        Assert.Contains("Žluťoučký kůň s.r.o.", text, StringComparison.Ordinal);
        Assert.Contains("daňová evidence", text, StringComparison.Ordinal);
        Assert.Contains("Celkem 2 jednotek.", text, StringComparison.Ordinal);
        Assert.Null(Assert.Single(_runner.Calls).Database);
    }

    [Fact]
    public async Task Customers_FiltersByYearAndSearch()
    {
        var exitCode = await Run("customers", "--year", "2025", "--search", "dvořák");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var text = _output.ToString();
        Assert.Contains("StwPh_87654321_2025", text, StringComparison.Ordinal);
        Assert.DoesNotContain("StwPh_12345678_2026", text, StringComparison.Ordinal);
        Assert.Contains("Zobrazeno 1 z 2 jednotek.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_PrintsTableAndDocumentsSummary()
    {
        var exitCode = await Run("invoices", "--customer", "StwPh_12345678_2026", "--from", "2026-01-01", "--to", "2026-03-31");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var text = _output.ToString();
        Assert.Contains("26110009", text, StringComparison.Ordinal);
        Assert.Contains(@"Fakturace\Přijaté faktury\2611\26110009", text, StringComparison.Ordinal);
        Assert.Contains("EUR", text, StringComparison.Ordinal);
        Assert.Contains("Celkem 2 faktur.", text, StringComparison.Ordinal);
        Assert.Contains(@"Složka firmy v Dokumentech: C:\ProgramData\STORMWARE\POHODA\Dokumenty\Žluťoučký kůň\", text, StringComparison.Ordinal);
        Assert.Contains("Bez složky firmy: 1, bez podsložky dokladu: 1.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invoices_WithoutIco_ResolvesUnitFirst()
    {
        var exitCode = await Run("invoices", "-c", "StwPh_12345678_2026");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        Assert.Collection(
            _runner.Calls,
            acu => Assert.Equal((null, null), (acu.Database, acu.Ico)),
            inv => Assert.Equal(("StwPh_12345678_2026", "12345678"), (inv.Database, inv.Ico)));
    }

    [Fact]
    public async Task Invoices_WithIco_SkipsUnitLookup()
    {
        var exitCode = await Run("invoices", "-c", "StwPh_12345678_2026", "--ico", "12345678");

        Assert.Equal(PohodaCommandRunner.Ok, exitCode);
        var call = Assert.Single(_runner.Calls);
        Assert.Equal(("StwPh_12345678_2026", "12345678"), (call.Database, call.Ico));
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
            var exitCode = await Run("invoices", "-c", "X", "--ico", "12345678", "--save-response", path);

            Assert.Equal(PohodaCommandRunner.Ok, exitCode);
            Assert.Equal(FixtureFiles.ReadBytes("Synthetic/listInvoice.received.response.xml"), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
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
    }

    private Task<int> Run(params string[] args) =>
        Run(
            new Dictionary<string, string?>
            {
                // Validace vyžaduje existující soubor; skutečně se nespouští (falešný runner).
                ["Pohoda:ExePath"] = typeof(CliTests).Assembly.Location,
                ["Pohoda:Username"] = "uzivatel",
                ["Pohoda:Password"] = "tajne-heslo",
            },
            args);

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
            var bytes = FixtureFiles.ReadBytes(isUnitList
                ? "Synthetic/listAccountingUnit.response.xml"
                : "Synthetic/listInvoice.received.response.xml");
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
