using NaPohodu.Core.Models;

namespace NaPohodu.Core.Pohoda;

/// <summary>Exporty z Pohody přes XML API. Každé volání spouští Pohodu (sekundy), výsledky si cachujte.</summary>
public sealed class PohodaClient(IPohodaXmlRunner runner)
{
    public async Task<IReadOnlyList<AccountingUnit>> GetAccountingUnitsAsync(CancellationToken cancellationToken = default)
    {
        var response = await runner.RunAsync(PohodaRequests.ListAccountingUnits(), database: null, cancellationToken)
            .ConfigureAwait(false);
        return AccountingUnitParser.Parse(response.Document);
    }

    /// <summary>
    /// Najde jednotku podle datového souboru (s příponou <c>.mdb</c> i bez ní). Stojí jedno spuštění Pohody.
    /// </summary>
    public async Task<AccountingUnit> FindAccountingUnitAsync(string dataFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFile);

        var units = await GetAccountingUnitsAsync(cancellationToken).ConfigureAwait(false);
        return units.FirstOrDefault(u => u.MatchesDataFile(dataFile))
            ?? throw new PohodaException($"Účetní jednotka s datovým souborem „{dataFile}“ v Pohodě není (viz napohodu customers).");
    }

    public Task<IReadOnlyList<Invoice>> GetReceivedInvoicesAsync(
        AccountingUnit unit,
        InvoiceFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        var identity = unit.Identity
            ?? throw new PohodaException($"Účetní jednotka {unit.DataFile} nemá v Pohodě IČ ani key; balíček pro ni nelze adresovat.");
        return GetReceivedInvoicesAsync(unit.DataFile, identity, filter, cancellationToken);
    }

    /// <param name="dataFile">Datový soubor jednotky (<see cref="AccountingUnit.DataFile"/>).</param>
    /// <param name="unit">IČ nebo key jednotky; bez nich Pohoda balíček odmítne.</param>
    public async Task<IReadOnlyList<Invoice>> GetReceivedInvoicesAsync(
        string dataFile,
        UnitIdentity unit,
        InvoiceFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFile);
        ArgumentNullException.ThrowIfNull(unit);

        var response = await runner.RunAsync(PohodaRequests.ListReceivedInvoices(unit, filter), dataFile, cancellationToken)
            .ConfigureAwait(false);
        return InvoiceParser.Parse(response.Document);
    }
}
