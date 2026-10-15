using System.Xml.Linq;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Tests.Pohoda;

public class PohodaClientTests
{
    [Fact]
    public async Task GetAccountingUnits_RunsWithoutDatabase()
    {
        var runner = new FakeRunner();

        var units = await new PohodaClient(runner).GetAccountingUnitsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(83, units.Count);
        var call = Assert.Single(runner.Calls);
        Assert.Null(call.Database);
        Assert.Equal(Acu + "listAccountingUnitRequest", call.ContentName);
        Assert.Null(call.Ico);
    }

    [Fact]
    public async Task GetReceivedInvoices_RunsAgainstDataFileWithIco()
    {
        var runner = new FakeRunner();

        var invoices = await new PohodaClient(runner).GetReceivedInvoicesAsync(
            RealFixtures.UnitDataFile2025,
            new UnitIdentity(RealFixtures.UnitIco, key: null),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(53, invoices.Count);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(RealFixtures.UnitDataFile2025, call.Database);
        Assert.Equal(Lst + "listInvoiceRequest", call.ContentName);
        Assert.Equal(RealFixtures.UnitIco, call.Ico);
    }

    [Theory]
    [InlineData("90000010_2025")]
    [InlineData(" 90000010_2025.MDB ")]
    public async Task FindAccountingUnit_MatchesDataFileWithOrWithoutExtension(string requested)
    {
        var runner = new FakeRunner();

        var unit = await new PohodaClient(runner).FindAccountingUnitAsync(requested, TestContext.Current.CancellationToken);

        Assert.Equal(RealFixtures.UnitIco, unit.Ico);
        Assert.Equal(2025, unit.Year);
    }

    [Fact]
    public async Task FindAccountingUnit_Unknown_Throws()
    {
        var runner = new FakeRunner();

        await Assert.ThrowsAsync<PohodaException>(
            () => new PohodaClient(runner).FindAccountingUnitAsync("neexistuje", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReceivedInvoices_UnitWithoutIcoOrKey_ThrowsWithoutRunningPohoda()
    {
        var runner = new FakeRunner();

        await Assert.ThrowsAsync<PohodaException>(
            () => new PohodaClient(runner).GetReceivedInvoicesAsync(
                new AccountingUnit { DataFile = "X" },
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void AccountingUnit_MatchesDataFile()
    {
        var unit = new AccountingUnit { DataFile = "08215839_2026.mdb" };

        Assert.True(unit.MatchesDataFile("08215839_2026"));
        Assert.True(unit.MatchesDataFile(" 08215839_2026.MDB "));
        Assert.False(unit.MatchesDataFile("08215839_2025"));
    }

    /// <summary>Vrací fixture podle typu požadavku a zaznamenává volání.</summary>
    internal sealed class FakeRunner : IPohodaXmlRunner
    {
        public List<(string? Database, XName ContentName, string? Ico)> Calls { get; } = [];

        public string InvoiceFixture { get; init; } = RealFixtures.ReceivedInvoices2025;

        public Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
        {
            var contentName = request.Root!.Element(Dat + "dataPackItem")!.Elements().Single().Name;
            Calls.Add((database, contentName, request.Root.Attribute("ico")?.Value));

            var fixture = contentName == Acu + "listAccountingUnitRequest"
                ? RealFixtures.AccountingUnits
                : InvoiceFixture;
            var bytes = FixtureFiles.ReadBytes(fixture);
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
