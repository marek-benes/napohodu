using System.CommandLine;
using NaPohodu.Core.Configuration;

namespace NaPohodu.App.Cli;

/// <summary><c>napohodu config</c>: zobrazí účinnou konfiguraci (bez hesla) a zkontroluje ji.</summary>
internal static class ConfigCommand
{
    public static Command Create(CliServices services)
    {
        var command = new Command("config", "Zobrazí účinnou konfiguraci (bez hesla) a zkontroluje ji.");

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var output = parseResult.InvocationConfiguration.Output;
            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var instance = AppConfiguration.GetInstanceOptions(services.Configuration);
            await output.WriteLineAsync($"Instance:            {instance.Name}{(instance.IsProduction ? " (produkce)" : " (VÝVOJ)")}, {instance.Url}").ConfigureAwait(false);
            await output.WriteLineAsync($"Program:             {AppContext.BaseDirectory}").ConfigureAwait(false);
            await output.WriteAsync(Format(options)).ConfigureAwait(false);

            var errors = AppConfiguration.Validate(options);
            foreach (var error in errors)
            {
                await parseResult.InvocationConfiguration.Error.WriteLineAsync(error).ConfigureAwait(false);
            }

            return errors.Count == 0 ? PohodaCommandRunner.Ok : PohodaCommandRunner.ConfigurationError;
        });

        return command;
    }

    internal static string Format(PohodaOptions options)
    {
        var localFile = Path.Combine(AppContext.BaseDirectory, AppConfiguration.LocalFileName);
        return string.Join(
            Environment.NewLine,
            $"Lokální konfigurace: {localFile} ({(File.Exists(localFile) ? "nalezena" : "chybí")})",
            $"Pohoda.exe:          {Show(options.ExePath)}",
            $"Data:                {Show(options.DataPath)}",
            $"Dokumenty:           {Show(options.DocumentsPath)}",
            $"Uživatel:            {Show(options.Username)}",
            $"Heslo:               {(string.IsNullOrEmpty(options.Password) ? "(nenastaveno)" : "(nastaveno)")}",
            $"Timeout:             {options.Timeout}",
            $"Zálohy MDB:          {options.EffectiveBackupPath} (ponechat {options.BackupsToKeep})",
            string.Empty);
    }

    private static string Show(string value) => string.IsNullOrWhiteSpace(value) ? "(nenastaveno)" : value;
}
