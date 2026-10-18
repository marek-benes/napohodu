using NaPohodu.Core.Filing;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Web;

/// <summary>Data načtená z Pohody a čas načtení (pro „Načteno v …“ a ruční obnovení).</summary>
internal sealed record Loaded<T>(T Value, DateTimeOffset LoadedAt);

/// <summary>
/// Data z Pohody pro webové UI s cache: seznam jednotek a přijaté faktury per jednotka se načtou
/// jednou a obnovují se ručně, nebo samy, když jsou starší než <see cref="InstanceOptions.CacheMaxAge"/>
/// (aplikace může běžet dlouho a účetní mezitím pracuje v Pohodě). 1. generace exportovala při
/// každém otevření obrazovky. Volání Pohody se řadí za sebe: každé spouští Pohoda.exe a souběh nemá smysl.
/// </summary>
internal sealed class PohodaSession(PohodaClient client, TimeProvider time, InstanceOptions instance, AppActivity activity) : IDisposable
{
    private readonly SemaphoreSlim _pohoda = new(1, 1);
    private readonly Dictionary<string, Loaded<IReadOnlyList<Invoice>>> _invoices = new(StringComparer.OrdinalIgnoreCase);
    private Loaded<IReadOnlyList<AccountingUnit>>? _units;

    public async Task<Loaded<IReadOnlyList<AccountingUnit>>> GetUnitsAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _units is { } cached && IsFresh(cached))
        {
            return cached;
        }

        var units = await RunAsync(ct => client.GetAccountingUnitsAsync(ct), cancellationToken).ConfigureAwait(false);
        return _units = new(units, time.GetUtcNow());
    }

    /// <summary>Jednotka podle datového souboru (s příponou i bez ní) z cache seznamu jednotek.</summary>
    public async Task<AccountingUnit> GetUnitAsync(string dataFile, CancellationToken cancellationToken = default)
    {
        var units = await GetUnitsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return units.Value.FirstOrDefault(u => u.MatchesDataFile(dataFile))
            ?? throw new PohodaException($"Účetní jednotka s datovým souborem „{dataFile}“ v Pohodě není.");
    }

    public async Task<Loaded<IReadOnlyList<Invoice>>> GetInvoicesAsync(AccountingUnit unit, bool refresh = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        lock (_invoices)
        {
            if (!refresh && _invoices.TryGetValue(unit.DataFile, out var cached) && IsFresh(cached))
            {
                return cached;
            }
        }

        var invoices = await RunAsync(ct => client.GetReceivedInvoicesAsync(unit, cancellationToken: ct), cancellationToken).ConfigureAwait(false);
        var loaded = new Loaded<IReadOnlyList<Invoice>>(invoices, time.GetUtcNow());
        lock (_invoices)
        {
            _invoices[unit.DataFile] = loaded;
        }

        return loaded;
    }

    /// <summary>Po přesunu souborů neodpovídá cache faktur záložce Dokumenty; příště se načte znovu.</summary>
    public void Invalidate(AccountingUnit unit)
    {
        lock (_invoices)
        {
            _invoices.Remove(unit.DataFile);
        }
    }

    public static CompanyFolderResolution ResolveCompanyFolder(AccountingUnit unit, IReadOnlyList<Invoice> invoices, string documentsPath, string? manualFolder) =>
        string.IsNullOrWhiteSpace(manualFolder)
            ? CompanyFolderResolver.Resolve(unit, invoices, documentsPath)
            : Directory.Exists(manualFolder)
                ? new CompanyFolderResolution(manualFolder.TrimEnd('\\'), "zadáno ručně", [])
                : new CompanyFolderResolution(null, $"zadaná složka neexistuje: {manualFolder}", []);

    public void Dispose() => _pohoda.Dispose();

    private bool IsFresh<T>(Loaded<T> loaded) =>
        instance.CacheMaxAge <= TimeSpan.Zero || time.GetUtcNow() - loaded.LoadedAt < instance.CacheMaxAge;

    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        // Volání Pohody je operace: „Ukončit“ ani vypnutí po nečinnosti ho nepřeruší.
        using var operation = activity.BeginOperation();
        await _pohoda.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pohoda.Release();
        }
    }
}
