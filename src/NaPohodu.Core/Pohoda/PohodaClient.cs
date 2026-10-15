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

    /// <param name="dataFile">Datový soubor jednotky (<see cref="AccountingUnit.DataFile"/>).</param>
    /// <param name="ico">IČ jednotky; nepovinné, Pohoda s ním ověří, že jde o správnou firmu.</param>
    public async Task<IReadOnlyList<Invoice>> GetReceivedInvoicesAsync(
        string dataFile,
        InvoiceFilter? filter = null,
        string? ico = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFile);

        var response = await runner.RunAsync(PohodaRequests.ListReceivedInvoices(filter, ico), dataFile, cancellationToken)
            .ConfigureAwait(false);
        return InvoiceParser.Parse(response.Document);
    }
}
