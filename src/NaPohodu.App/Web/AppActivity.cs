using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.JSInterop;

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

/// <summary>
/// Počítá otevřená okna podle připojení Blazor obvodů (zavřené okno = odpojení) a eviduje je
/// v <see cref="OpenWindows"/>, aby jim šlo při ukončení aplikace dát vědět.
/// </summary>
internal sealed class WindowTrackingCircuitHandler(AppActivity activity, OpenWindows windows, IJSRuntime js) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        windows.Add(circuit.Id, js);
        activity.WindowOpened();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        windows.Remove(circuit.Id);
        activity.WindowClosed();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Otevřená okna aplikace. Při ukončení serveru (ikona „Ukončit“, Ctrl+C, vypnutí) dostanou zprávu:
/// okno se pokusí samo zavřít a jinak místo marného opakování připojení ukáže „NaPohodu je ukončená“.
/// Jako hostovaná služba se zastavuje dřív než webový server, takže spojení ještě existují.
/// </summary>
internal sealed class OpenWindows : IHostedService
{
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<string, IJSRuntime> _windows = new(StringComparer.Ordinal);

    public void Add(string circuitId, IJSRuntime js) => _windows[circuitId] = js;

    public void Remove(string circuitId) => _windows.TryRemove(circuitId, out _);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => NotifyShutdownAsync();

    public async Task NotifyShutdownAsync()
    {
        using var timeout = new CancellationTokenSource(NotifyTimeout);
        await Task.WhenAll(_windows.Values.Select(async js =>
        {
            try
            {
                await js.InvokeVoidAsync("napohodu.shutdown", timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
            {
                // Okno už je pryč nebo neodpovídá – nevadí, ukončení pokračuje.
            }
        })).ConfigureAwait(false);
    }
}
