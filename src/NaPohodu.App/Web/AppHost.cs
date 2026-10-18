using Microsoft.AspNetCore.Components.Server.Circuits;
using NaPohodu.App.Cli;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Web;

/// <summary>
/// Webový hostitel UI (Blazor Server na 127.0.0.1) společný pro <c>napohodu serve</c> (konzole, vývoj)
/// a okenní aplikaci <c>NaPohodu.Desktop</c> (zástupce na ploše, ikona u hodin).
/// </summary>
public static class AppHost
{
    /// <summary>Název zámku „jen jedna kopie“; sdílí ho serve i okenní aplikace téže instance.</summary>
    internal static string SingleInstanceName(InstanceOptions instance) => $@"Local\{instance.Name}-serve";

    /// <param name="configureServices">Doplňkové služby hostitele (okenní aplikace přidá výběr složky).</param>
    internal static WebApplication Build(
        CliServices services,
        PohodaOptions options,
        InstanceOptions instance,
        Action<IServiceCollection>? configureServices = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Podle názvu aplikace se hledá manifest statických souborů (napohodu.staticwebassets.*.json).
            ApplicationName = typeof(AppHost).Assembly.GetName().Name,
            // Obsah (wwwroot) vedle exe, ne v aktuální složce (dotnet run ji nastavuje jinam).
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        // Stejná konfigurace jako CLI. Kopie hodnot, ne řetězení: hostitel do konfigurace zapisuje
        // (např. adresa z UseUrls) a řetězený zdroj nad prázdnou konfigurací zápis neunese.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(services.Configuration.AsEnumerable());
        builder.WebHost.UseUrls(instance.Url);
        // Statické soubory z buildu (dotnet run) leží v projektu a balíčcích; manifest to ASP.NET Core
        // sám načte jen v prostředí Development. V publikované verzi manifest není a volání nic nedělá.
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(instance);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(sp => services.RunnerFactory(options, sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<PohodaClient>();
        builder.Services.AddSingleton<PohodaSession>();
        builder.Services.AddSingleton<AppActivity>();
        builder.Services.AddScoped<CircuitHandler, WindowTrackingCircuitHandler>();
        builder.Services.AddHostedService<IdleShutdown>();
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();
        return app;
    }

    /// <summary>
    /// Spustí hostitele. Obsazený port se hlásí srozumitelně (jiný program, nebo jiná instance s týmž portem).
    /// </summary>
    internal static async Task StartAsync(WebApplication app, InstanceOptions instance, CancellationToken cancellationToken)
    {
        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new PohodaException($"Port {instance.Port} je obsazený jiným programem ({ex.Message}). Nastavte jiný v Instance:Port.", ex);
        }
    }
}
