using System.CommandLine;
using NaPohodu.App.Web;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu serve</c>: webové UI v konzoli (vývoj, ladění). Účetní spouští okenní aplikaci
/// <c>NaPohodu.Desktop</c> zástupcem na ploše; obě sdílejí hostitele (<see cref="AppHost"/>) i zámek jedné kopie.
/// </summary>
internal static class ServeCommand
{
    public static Command Create(CliServices services)
    {
        var portOption = new Option<int?>("--port", "-p")
        {
            Description = "Port na 127.0.0.1 (výchozí z konfigurace Instance:Port, produkce 5080).",
        };
        var noBrowserOption = new Option<bool>("--no-browser")
        {
            Description = "Neotvírat okno aplikace.",
        };

        var command = new Command("serve", "Spustí webové rozhraní NaPohodu v konzoli (jen na tomto počítači).")
        {
            portOption,
            noBrowserOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;
            var instance = AppConfiguration.GetInstanceOptions(services.Configuration);
            if (parseResult.GetValue(portOption) is { } port)
            {
                instance.Port = port;
            }

            var openWindow = !parseResult.GetValue(noBrowserOption);

            // Jen jedna kopie: druhé spuštění jen otevře okno běžící instance.
            using var singleInstance = new Mutex(initiallyOwned: true, AppHost.SingleInstanceName(instance), out var isFirst);
            if (!isFirst)
            {
                await output.WriteLineAsync($"{instance.Name} už běží na {instance.Url}{(openWindow ? ", otevírám okno" : "")}.").ConfigureAwait(false);
                if (openWindow)
                {
                    AppWindow.Open(instance.Url);
                }

                return PohodaCommandRunner.Ok;
            }

            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var errors = AppConfiguration.Validate(options);
            if (errors.Count > 0)
            {
                foreach (var message in errors)
                {
                    await error.WriteLineAsync(message).ConfigureAwait(false);
                }

                await error.WriteLineAsync($"Nastavení doplňte do {Path.Combine(AppContext.BaseDirectory, AppConfiguration.LocalFileName)}.").ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            await using var app = AppHost.Build(services, options, instance);
            try
            {
                await AppHost.StartAsync(app, instance, cancellationToken).ConfigureAwait(false);
            }
            catch (PohodaException ex)
            {
                await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            SetConsoleTitle($"{instance.Name}{(instance.IsProduction ? "" : " (VÝVOJ)")} – zavřením okna se aplikace ukončí");
            var idle = instance.IdleShutdown > TimeSpan.Zero ? $", po {instance.IdleShutdown.TotalMinutes:0} min bez okna se sama vypne" : "";
            await output.WriteLineAsync($"{instance.Name} běží na {instance.Url}{idle}. Ukončíte zavřením tohoto okna nebo Ctrl+C.").ConfigureAwait(false);
            if (openWindow)
            {
                AppWindow.Open(instance.Url);
            }

            await app.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            return PohodaCommandRunner.Ok;
        });

        return command;
    }

    private static void SetConsoleTitle(string title)
    {
        try
        {
            Console.Title = title;
        }
        catch (IOException)
        {
            // Bez konzole (přesměrovaný výstup) titulek nejde nastavit.
        }
    }
}
