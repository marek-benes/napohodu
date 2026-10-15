using Microsoft.AspNetCore.Components;
using NaPohodu.App.Web;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Components.Common;

/// <summary>
/// Základ stránek konkrétního zákazníka (<c>/z/{DataFile}/…</c>): načte jednotku z cache seznamu
/// jednotek a chyby Pohody převede na text pro uživatele. Logika zůstává v Core; stránka jen volá a zobrazuje.
/// </summary>
public abstract class UnitPageBase : ComponentBase
{
    [Parameter]
    public string DataFile { get; set; } = string.Empty;

    [Inject]
    internal PohodaSession Session { get; set; } = default!;

    [Inject]
    internal PohodaOptions Options { get; set; } = default!;

    protected AccountingUnit? Unit { get; private set; }

    protected string? Error { get; set; }

    /// <summary>Text probíhající operace (zobrazí se se spinnerem), nebo <c>null</c>.</summary>
    protected string? Busy { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (Unit is not null && Unit.MatchesDataFile(DataFile))
        {
            return;
        }

        Unit = null;
        Error = null;
        await RunAsync("Načítám seznam jednotek z Pohody…", async () =>
        {
            Unit = await Session.GetUnitAsync(DataFile).ConfigureAwait(true);
            await OnUnitLoadedAsync(Unit).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    protected virtual Task OnUnitLoadedAsync(AccountingUnit unit) => Task.CompletedTask;

    /// <summary>
    /// Spustí operaci se spinnerem; chyby Pohody, MDB a souborů zobrazí jako text místo pádu obvodu.
    /// Pomalé synchronní části (MDB, disk) volejte uvnitř přes <see cref="Task.Run(Action)"/>.
    /// </summary>
    protected async Task RunAsync(string busyText, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Busy = busyText;
        Error = null;
        StateHasChanged();
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is PohodaException or IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = null;
            StateHasChanged();
        }
    }
}
