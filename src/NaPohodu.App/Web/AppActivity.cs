using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NaPohodu.App.Web;

/// <summary>
/// Co se v aplikaci právě děje: počet otevřených oken (připojení Blazoru) a rozběhnutých operací
/// (přesun souborů, zápis do MDB, volání Pohody). Podle toho se aplikace vypíná po nečinnosti
/// a „Ukončit“ počká na dokončení operací. Události mohou přijít z libovolného vlákna.
/// </summary>
internal sealed class AppActivity
{
    private int _windows;
    private int _operations;

    /// <summary>Změna počtu oken nebo operací.</summary>
    public event Action? Changed;

    public int Windows => Volatile.Read(ref _windows);

    public int Operations => Volatile.Read(ref _operations);

    public bool IsIdle => Windows == 0 && Operations == 0;

    public void WindowOpened() => Update(ref _windows, +1);

    public void WindowClosed() => Update(ref _windows, -1);

    /// <summary>Označí běžící operaci; konec operace = <see cref="IDisposable.Dispose"/>.</summary>
    public IDisposable BeginOperation()
    {
        Update(ref _operations, +1);
        return new Operation(this);
    }

    /// <summary>Počká, až doběhnou rozběhnuté operace (nejdéle <paramref name="timeout"/>).</summary>
    public async Task<bool> WaitForOperationsAsync(TimeSpan timeout, TimeProvider time, CancellationToken cancellationToken = default)
    {
        var deadline = time.GetUtcNow() + timeout;
        while (Operations > 0)
        {
            if (time.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), time, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private void Update(ref int counter, int delta)
    {
        if (Interlocked.Add(ref counter, delta) < 0)
        {
            Interlocked.Exchange(ref counter, 0);
        }

        Changed?.Invoke();
    }

    private sealed class Operation(AppActivity activity) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                activity.Update(ref activity._operations, -1);
            }
        }
    }
}

/// <summary>Počítá otevřená okna podle připojení Blazor obvodů (zavřené okno = odpojení).</summary>
internal sealed class WindowTrackingCircuitHandler(AppActivity activity) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        activity.WindowOpened();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        activity.WindowClosed();
        return Task.CompletedTask;
    }
}
