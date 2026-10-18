using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NaPohodu.App;
using NaPohodu.App.Cli;
using NaPohodu.App.Web;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.Desktop;

/// <summary>
/// Zástupce na ploše → <c>NaPohodu.Desktop.exe</c>: bez konzole spustí webový server, ikonu u hodin
/// a okno aplikace. Když už běží, jen otevře okno. Zavřením okna se aplikace po nečinnosti
/// (<c>Instance:IdleShutdown</c>) sama vypne; ikona u hodin umí „Ukončit“ hned.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var services = CliServices.CreateDefault();
        var instance = AppConfiguration.GetInstanceOptions(services.Configuration);
        var openWindow = !args.Contains("--no-window", StringComparer.OrdinalIgnoreCase);

        using var singleInstance = new Mutex(initiallyOwned: true, AppHost.SingleInstanceName(instance), out var isFirst);
        if (!isFirst)
        {
            if (openWindow)
            {
                AppWindow.Open(instance.Url);
            }

            return 0;
        }

        var options = AppConfiguration.GetPohodaOptions(services.Configuration);
        var errors = AppConfiguration.Validate(options);
        if (errors.Count > 0)
        {
            ShowError(
                string.Join(Environment.NewLine, errors) + Environment.NewLine + Environment.NewLine
                + $"Nastavení doplňte do {Path.Combine(AppContext.BaseDirectory, AppConfiguration.LocalFileName)}.");
            return 2;
        }

        using var tray = new TrayApplication(instance);
        var app = AppHost.Build(services, options, instance, s => s.AddSingleton<IFolderPicker>(tray.FolderPicker));
        try
        {
            // Start mimo vlákno UI (to ještě nemá smyčku zpráv), chyby (obsazený port) jako okno se zprávou.
            Task.Run(() => AppHost.StartAsync(app, instance, CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (PohodaException ex)
        {
            ShowError(ex.Message);
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return 2;
        }

        tray.Attach(app);
        if (openWindow)
        {
            AppWindow.Open(instance.Url);
        }

        Application.Run(tray);

        // Vypnutí po nečinnosti, nebo „Ukončit“ z ikony: dojet server a uklidit ikonu.
        Task.Run(async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }).GetAwaiter().GetResult();
        return 0;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "NaPohodu", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
