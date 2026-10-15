using System.CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>
/// Společný běh příkazů, které volají Pohodu: konfigurace, logování, runner, ošetření chyb.
/// Návratové kódy: 0 = OK, 1 = chyba Pohody (i chybné volby při parsování), 2 = chybná konfigurace,
/// 64 = nesmyslná kombinace voleb, 130 = přerušeno.
/// </summary>
internal static class PohodaCommandRunner
{
    public const int Ok = 0;
    public const int PohodaError = 1;
    public const int ConfigurationError = 2;
    public const int InvalidArguments = 64;
    public const int Cancelled = 130;

    public static async Task<int> RunAsync(
        ParseResult parseResult,
        CliServices services,
        FileInfo? saveResponse,
        Func<PohodaClient, TextWriter, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var output = parseResult.InvocationConfiguration.Output;
        var error = parseResult.InvocationConfiguration.Error;

        var options = AppConfiguration.GetPohodaOptions(services.Configuration);
        var errors = AppConfiguration.Validate(options);
        if (errors.Count > 0)
        {
            foreach (var message in errors)
            {
                await error.WriteLineAsync(message).ConfigureAwait(false);
            }

            await error.WriteLineAsync($"Nastavení doplňte do {AppConfiguration.LocalFileName} vedle napohodu.exe (vzor appsettings.Local.example.json).")
                .ConfigureAwait(false);
            return ConfigurationError;
        }

        using var loggerFactory = CreateLoggerFactory(parseResult.GetValue(CliOptions.Verbose));
        var runner = services.RunnerFactory(options, loggerFactory);
        if (saveResponse is not null)
        {
            runner = new RecordingRunner(runner, saveResponse.FullName);
        }

        try
        {
            await error.WriteLineAsync("Spouštím Pohodu…").ConfigureAwait(false);
            await action(new PohodaClient(runner), output, cancellationToken).ConfigureAwait(false);
            if (saveResponse is not null)
            {
                await error.WriteLineAsync($"Odpověď Pohody uložena do {saveResponse.FullName} (obsahuje osobní údaje).").ConfigureAwait(false);
            }

            return Ok;
        }
        catch (PohodaException ex)
        {
            await error.WriteLineAsync($"Chyba: {ex.Message}").ConfigureAwait(false);
            return PohodaError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Přerušeno.").ConfigureAwait(false);
            return Cancelled;
        }
    }

    private static ILoggerFactory CreateLoggerFactory(bool verbose) =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            // Logy patří na stderr, stdout je vyhrazený pro tabulky (přesměrování do souboru).
            builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        });
}
