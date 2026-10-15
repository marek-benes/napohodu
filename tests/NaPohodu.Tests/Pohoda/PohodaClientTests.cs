using System.Xml.Linq;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Tests.Pohoda;

public class PohodaClientTests
{
    [Fact]
    public async Task GetAccountingUnits_RunsWithoutDatabase()
    {
        var runner = new FakeRunner("Synthetic/listAccountingUnit.response.xml");

        var units = await new PohodaClient(runner).GetAccountingUnitsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, units.Count);
        Assert.Null(runner.Database);
        Assert.Equal(Acu + "listAccountingUnitRequest", runner.ContentName);
    }

    [Fact]
    public async Task GetReceivedInvoices_RunsAgainstDataFile()
    {
        var runner = new FakeRunner("Synthetic/listInvoice.received.response.xml");

        var invoices = await new PohodaClient(runner).GetReceivedInvoicesAsync(
            "StwPh_12345678_2026",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, invoices.Count);
        Assert.Equal("StwPh_12345678_2026", runner.Database);
        Assert.Equal(Lst + "listInvoiceRequest", runner.ContentName);
    }

    private sealed class FakeRunner(string fixture) : IPohodaXmlRunner
    {
        public string? Database { get; private set; }

        public XName? ContentName { get; private set; }

        public Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
        {
            Database = database;
            ContentName = request.Root!.Element(Dat + "dataPackItem")!.Elements().Single().Name;
            var bytes = FixtureFiles.ReadBytes(fixture);
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
