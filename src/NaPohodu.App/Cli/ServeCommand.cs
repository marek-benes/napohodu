using System.CommandLine;
using System.Diagnostics;
using NaPohodu.App.Web;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu serve</c>: webové UI (Blazor Server) jen na <c>127.0.0.1</c>, bez přihlašování –
/// aplikace běží na PC účetní a sahá na lokální soubory a databázi Pohody.
/// </summary>
internal static class ServeCommand
{
    public const int DefaultPort = 5080;

    public static Command Create(CliServices services)
    {
        var portOption = new Option<int>("--port", "-p")
        {
            Description = "Port na 127.0.0.1.",
            DefaultValueFactory = _ => DefaultPort,
        };
        var noBrowserOption = new Option<bool>("--no-browser")
        {
            Description = "Neotvírat prohlížeč.",
        };

        var command = new Command("serve", "Spustí webové rozhraní NaPohodu v prohlížeči (jen na tomto počítači).")
        {
            portOption,
            noBrowserOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var errors = AppConfiguration.Validate(options);
            if (errors.Count > 0)
            {
                foreach (var message in errors)
                {
                    await parseResult.InvocationConfiguration.Error.WriteLineAsync(message).ConfigureAwait(false);
                }

                return PohodaCommandRunner.ConfigurationError;
            }

            var url = $"http://127.0.0.1:{parseResult.GetValue(portOption)}";
            var app = Build(services, options, url);
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            await parseResult.InvocationConfiguration.Output.WriteLineAsync($"NaPohodu běží na {url} (ukončíte Ctrl+C).").ConfigureAwait(false);
            if (!parseResult.GetValue(noBrowserOption))
            {
                OpenBrowser(url);
            }

            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return PohodaCommandRunner.Ok;
        });

        return command;
    }

    internal static WebApplication Build(CliServices services, PohodaOptions options, string url)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Podle názvu aplikace se hledá manifest statických souborů (napohodu.staticwebassets.*.json).
            ApplicationName = typeof(ServeCommand).Assembly.GetName().Name,
            // Obsah (wwwroot) vedle exe, ne v aktuální složce (dotnet run ji nastavuje jinam).
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        // Stejná konfigurace jako CLI. Kopie hodnot, ne řetězení: hostitel do konfigurace zapisuje
        // (např. adresa z UseUrls) a řetězený zdroj nad prázdnou konfigurací zápis neunese.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(services.Configuration.AsEnumerable());
        builder.WebHost.UseUrls(url);
        // Statické soubory z buildu (dotnet run) leží v projektu a balíčcích; manifest to ASP.NET Core
        // sám načte jen v prostředí Development. V publikované verzi manifest není a volání nic nedělá.
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => services.RunnerFactory(options, sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<PohodaClient>();
        builder.Services.AddSingleton<PohodaSession>();

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();
        return app;
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Bez prohlížeče: adresa je vypsaná v konzoli.
        }
    }
}
