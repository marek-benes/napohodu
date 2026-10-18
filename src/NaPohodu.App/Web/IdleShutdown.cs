namespace NaPohodu.App.Web;

/// <summary>
/// Vypne aplikaci po nečinnosti: žádné otevřené okno a žádná rozběhnutá operace po dobu
/// <see cref="InstanceOptions.IdleShutdown"/> (výchozí 15 minut; nula = nikdy). Zavřít a za chvíli
/// otevřít je tak okamžité i s načtenými daty, druhý den se začíná načisto.
/// </summary>
internal sealed class IdleShutdown : IHostedService, IDisposable
{
    private readonly AppActivity _activity;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly TimeProvider _time;
    private readonly TimeSpan _idle;
    private readonly Lock _lock = new();
    private ITimer? _timer;

    public IdleShutdown(AppActivity activity, IHostApplicationLifetime lifetime, TimeProvider time, InstanceOptions instance)
    {
        _activity = activity;
        _lifetime = lifetime;
        _time = time;
        _idle = instance.IdleShutdown;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_idle > TimeSpan.Zero)
        {
            _activity.Changed += Reschedule;
            Reschedule();   // i když se okno vůbec neotevře, aplikace nezůstane viset
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _activity.Changed -= Reschedule;
        Cancel();
        return Task.CompletedTask;
    }

    public void Dispose() => Cancel();

    private void Reschedule()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = _activity.IsIdle ? _time.CreateTimer(_ => Expired(), null, _idle, Timeout.InfiniteTimeSpan) : null;
        }
    }

    private void Expired()
    {
        // Mezitím se mohlo otevřít okno; vypnout jen při trvající nečinnosti.
        if (_activity.IsIdle)
        {
            _lifetime.StopApplication();
        }
    }

    private void Cancel()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
