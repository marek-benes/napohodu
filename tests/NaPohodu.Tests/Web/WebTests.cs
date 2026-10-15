using System.Net;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NaPohodu.App.Cli;
using NaPohodu.App.Web;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Web;

public class WebTests
{
    [Fact]
    public async Task Session_CachesUntilRefreshOrInvalidate()
    {
        var runner = new CountingRunner();
        using var session = new PohodaSession(new PohodaClient(runner), TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;

        var unit = await session.GetUnitAsync("90000010_2025", ct);
        await session.GetUnitsAsync(cancellationToken: ct);
        await session.GetInvoicesAsync(unit, cancellationToken: ct);
        await session.GetInvoicesAsync(unit, cancellationToken: ct);
        Assert.Equal((1, 1), (runner.UnitCalls, runner.InvoiceCalls));

        await session.GetInvoicesAsync(unit, refresh: true, cancellationToken: ct);
        session.Invalidate(unit);
        await session.GetInvoicesAsync(unit, cancellationToken: ct);
        await session.GetUnitsAsync(refresh: true, cancellationToken: ct);

        Assert.Equal((2, 3), (runner.UnitCalls, runner.InvoiceCalls));
    }

    [Fact]
    public async Task Session_UnknownDataFile_Throws()
    {
        using var session = new PohodaSession(new PohodaClient(new CountingRunner()), TimeProvider.System);

        await Assert.ThrowsAsync<PohodaException>(() => session.GetUnitAsync("neexistuje", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Serve_RendersPagesOnLocalhostOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new CliServices
        {
            Configuration = new ConfigurationBuilder().Build(),
            RunnerFactory = (_, _) => new CountingRunner(),
        };
        await using var app = ServeCommand.Build(services, new PohodaOptions(), "http://127.0.0.1:0");
        await app.StartAsync(ct);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        // Razor kóduje diakritiku jako HTML entity; porovnává se dekódovaný text.
        var customers = WebUtility.HtmlDecode(await http.GetStringAsync("/", ct));
        var invoices = WebUtility.HtmlDecode(await http.GetStringAsync("/z/90000010_2025.mdb/faktury", ct));
        var missing = await http.GetAsync("/neexistuje", ct);

        Assert.StartsWith("http://127.0.0.1:", address, StringComparison.Ordinal);
        Assert.Contains("Výběr zákazníka", customers, StringComparison.Ordinal);
        Assert.Contains("Firma Šťastná 011", customers, StringComparison.Ordinal);
        Assert.Contains("53 faktur", invoices, StringComparison.Ordinal);
        Assert.Contains(@"Fakturace\Přijaté faktury\2511\251100002", invoices, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await app.StopAsync(ct);
    }

    private sealed class CountingRunner : IPohodaXmlRunner
    {
        public int UnitCalls { get; private set; }

        public int InvoiceCalls { get; private set; }

        public Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
        {
            var isUnitList = request.Descendants(PohodaNamespaces.Acu + "listAccountingUnitRequest").Any();
            _ = isUnitList ? UnitCalls++ : InvoiceCalls++;
            var bytes = FixtureFiles.ReadBytes(isUnitList ? RealFixtures.AccountingUnits : RealFixtures.ReceivedInvoices2025);
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
