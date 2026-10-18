using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NaPohodu.App;
using NaPohodu.App.Web;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Web;

public class ActivityTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task IdleShutdown_StopsAfterIdleTimeWithoutWindows()
    {
        var (activity, lifetime, time, _) = await StartIdleShutdown();

        time.Advance(Idle - TimeSpan.FromSeconds(1));
        Assert.False(lifetime.StopRequested);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task IdleShutdown_OpenWindowPostponesAndClosingRestartsCountdown()
    {
        var (activity, lifetime, time, _) = await StartIdleShutdown();

        time.Advance(TimeSpan.FromMinutes(10));
        activity.WindowOpened();
        time.Advance(TimeSpan.FromHours(3));
        Assert.False(lifetime.StopRequested);

        activity.WindowClosed();
        time.Advance(Idle - TimeSpan.FromMinutes(1));
        Assert.False(lifetime.StopRequested);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task IdleShutdown_WaitsForRunningOperationEvenWithoutWindow()
    {
        var (activity, lifetime, time, _) = await StartIdleShutdown();

        var operation = activity.BeginOperation();   // např. přesun souborů, okno mezitím zavřené
        time.Advance(TimeSpan.FromHours(1));
        Assert.False(lifetime.StopRequested);

        operation.Dispose();
        operation.Dispose();                          // dvojí ukončení nesmí počet podtéct
        Assert.Equal(0, activity.Operations);
        time.Advance(Idle);
        Assert.True(lifetime.StopRequested);
    }

    [Fact]
    public async Task IdleShutdown_ZeroMeansNever()
    {
        var (_, lifetime, time, _) = await StartIdleShutdown(TimeSpan.Zero);

        time.Advance(TimeSpan.FromDays(30));

        Assert.False(lifetime.StopRequested);
    }

    [Fact]
    public async Task Activity_WaitForOperations_ReturnsWhenDoneOrTimesOut()
    {
        var activity = new AppActivity();
        var operation = activity.BeginOperation();
        var waiting = activity.WaitForOperationsAsync(TimeSpan.FromSeconds(30), TimeProvider.System, TestContext.Current.CancellationToken);

        operation.Dispose();

        Assert.True(await waiting);
        using (activity.BeginOperation())
        {
            Assert.False(await activity.WaitForOperationsAsync(TimeSpan.FromMilliseconds(300), TimeProvider.System, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Session_ReloadsDataOlderThanCacheMaxAge()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-18T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var runner = new CountingRunner();
        var activity = new AppActivity();
        using var session = new PohodaSession(new PohodaClient(runner), time, new InstanceOptions { CacheMaxAge = TimeSpan.FromHours(4) }, activity);
        var ct = TestContext.Current.CancellationToken;

        await session.GetUnitsAsync(cancellationToken: ct);
        time.Advance(TimeSpan.FromHours(3));
        await session.GetUnitsAsync(cancellationToken: ct);
        Assert.Equal(1, runner.Calls);

        time.Advance(TimeSpan.FromHours(2));
        await session.GetUnitsAsync(cancellationToken: ct);
        Assert.Equal(2, runner.Calls);
        Assert.Equal(0, activity.Operations);   // volání Pohody se počítá jako operace jen během běhu
    }

    private static async Task<(AppActivity Activity, FakeLifetime Lifetime, FakeTimeProvider Time, IdleShutdown Shutdown)> StartIdleShutdown(TimeSpan? idle = null)
    {
        var activity = new AppActivity();
        var lifetime = new FakeLifetime();
        var time = new FakeTimeProvider();
        var shutdown = new IdleShutdown(activity, lifetime, time, new InstanceOptions { IdleShutdown = idle ?? Idle });
        await shutdown.StartAsync(TestContext.Current.CancellationToken);
        return (activity, lifetime, time, shutdown);
    }

    [Fact]
    public async Task OpenWindows_StoppingNotifiesOpenWindowsAndIgnoresGoneOnes()
    {
        var windows = new OpenWindows();
        var open = new RecordingJs();
        var gone = new RecordingJs { Fail = true };
        var closed = new RecordingJs();
        windows.Add("a", open);
        windows.Add("b", gone);
        windows.Add("c", closed);
        windows.Remove("c");

        await windows.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["napohodu.shutdown"], open.Calls);
        Assert.Empty(closed.Calls);
    }

    private sealed class RecordingJs : Microsoft.JSInterop.IJSRuntime
    {
        public bool Fail { get; init; }

        public List<string> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (Fail)
            {
                throw new Microsoft.JSInterop.JSDisconnectedException("okno je pryč");
            }

            Calls.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }

    private sealed class CountingRunner : IPohodaXmlRunner
    {
        public int Calls { get; private set; }

        public Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
        {
            Calls++;
            var bytes = FixtureFiles.ReadBytes(RealFixtures.AccountingUnits);
            return Task.FromResult(new PohodaXmlResponse(PohodaXmlRunner.ParseResponse(bytes), bytes));
        }
    }
}
