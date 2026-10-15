using System.CommandLine;
using System.Globalization;
using System.Text;
using NaPohodu.Core.Filing;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu audit-export</c>: zkopíruje soubory přijatých faktur za období (podle DUZP) do jedné
/// složky pro kontrolu. Nic v Dokumentech nemění; v cíli nikdy nepřepisuje.
/// </summary>
internal static class AuditExportCommand
{
    public static Command Create(CliServices services)
    {
        var customerOption = new Option<string>("--customer", "-c")
        {
            Description = "Datový soubor účetní jednotky (ročník).",
            Required = true,
        };
        var fromOption = CliOptions.Date("--from", "DUZP od");
        var toOption = CliOptions.Date("--to", "DUZP do");
        var destinationOption = new Option<DirectoryInfo>("--dest", "-d")
        {
            Description = "Cílová složka (vytvoří se). Existující soubory se nepřepisují.",
            Required = true,
        };
        var companyFolderOption = new Option<DirectoryInfo?>("--company-folder")
        {
            Description = "Složka firmy v Dokumentech, když ji nelze zjistit z Pohody ani podle názvu firmy.",
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Jen vypsat, co by se zkopírovalo.",
        };

        var command = new Command("audit-export", "Zkopíruje soubory přijatých faktur za období (podle DUZP) do složky pro kontrolu.")
        {
            customerOption,
            fromOption,
            toOption,
            destinationOption,
            companyFolderOption,
            dryRunOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var error = parseResult.InvocationConfiguration.Error;
            var (from, to) = (parseResult.GetValue(fromOption), parseResult.GetValue(toOption));
            if (from > to)
            {
                await error.WriteLineAsync("Datum --from je po datu --to.").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            var companyFolderOverride = parseResult.GetValue(companyFolderOption);
            if (companyFolderOverride is { Exists: false })
            {
                await error.WriteLineAsync($"Zadaná složka firmy neexistuje: {companyFolderOverride.FullName}").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            var documentsPath = AppConfiguration.GetPohodaOptions(services.Configuration).DocumentsPath;
            var failed = false;
            var exitCode = await PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                saveResponse: null,
                async (client, output, ct) =>
                {
                    await error.WriteLineAsync("Zjišťuji jednotku a načítám přijaté faktury…").ConfigureAwait(false);
                    var unit = await client.FindAccountingUnitAsync(parseResult.GetValue(customerOption)!, ct).ConfigureAwait(false);
                    var invoices = await client.GetReceivedInvoicesAsync(unit, cancellationToken: ct).ConfigureAwait(false);

                    var companyFolder = companyFolderOverride?.FullName.TrimEnd('\\')
                        ?? CompanyFolderResolver.Resolve(unit, invoices, documentsPath) switch
                        {
                            { Path: { } path } => path,
                            var r => throw new PohodaException($"Složku firmy nelze určit ({r.Source}). Zadejte ji volbou --company-folder."),
                        };

                    var plan = AuditExport.Plan(invoices, companyFolder, from, to);
                    var destination = parseResult.GetValue(destinationOption)!.FullName;
                    await output.WriteAsync(FormatPlan(plan, unit.DisplayName, companyFolder, destination, from, to)).ConfigureAwait(false);

                    if (parseResult.GetValue(dryRunOption))
                    {
                        await output.WriteLineAsync("Jen náhled (--dry-run), nic se nezkopírovalo.").ConfigureAwait(false);
                        return;
                    }

                    var results = AuditExport.Execute(plan, destination);
                    await output.WriteAsync(FormatResults(results)).ConfigureAwait(false);
                    failed = results.Any(r => r.Outcome == AuditExportOutcome.Failed);
                },
                cancellationToken).ConfigureAwait(false);

            return exitCode == PohodaCommandRunner.Ok && failed ? PohodaCommandRunner.PohodaError : exitCode;
        });

        return command;
    }

    internal static string FormatPlan(AuditExportPlan plan, string unitName, string companyFolder, string destination, DateOnly? from, DateOnly? to)
    {
        var czech = CultureInfo.GetCultureInfo("cs-CZ");
        var period = $"{from?.ToString("d.M.yyyy", czech) ?? "…"} – {to?.ToString("d.M.yyyy", czech) ?? "…"}";
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"Jednotka:     {unitName}");
        output.AppendLine(CultureInfo.InvariantCulture, $"Období (DUZP): {period}");
        output.AppendLine(CultureInfo.InvariantCulture, $"Složka firmy: {companyFolder}");
        output.AppendLine(CultureInfo.InvariantCulture, $"Cíl:          {destination}");
        output.AppendLine(CultureInfo.InvariantCulture,
            $"Faktur v období {plan.Invoices.Count}, souborů ke zkopírování {plan.Files.Count}, faktur bez dokumentů {plan.WithoutDocuments.Count}.");
        if (plan.WithoutDocuments.Count > 0)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Bez dokumentů: {string.Join(", ", plan.WithoutDocuments)}");
        }

        foreach (var missing in plan.MissingFiles)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Pohoda eviduje, ale na disku chybí: {missing}");
        }

        return output.ToString();
    }

    internal static string FormatResults(IReadOnlyList<AuditExportResultItem> results)
    {
        var output = new StringBuilder();
        foreach (var result in results.Where(r => r.Outcome is AuditExportOutcome.CopiedRenamed or AuditExportOutcome.Failed))
        {
            output.AppendLine(CultureInfo.InvariantCulture,
                $"{(result.Outcome == AuditExportOutcome.Failed ? "Chyba" : "Pod jiným názvem")}: {result.File.ExportName} → {result.TargetPath}{(result.Error is null ? "" : $" ({result.Error})")}");
        }

        output.AppendLine(CultureInfo.InvariantCulture,
            $"Zkopírováno {results.Count(r => r.Outcome is AuditExportOutcome.Copied or AuditExportOutcome.CopiedRenamed)}, " +
            $"už v cíli {results.Count(r => r.Outcome == AuditExportOutcome.AlreadyThere)}, chyb {results.Count(r => r.Outcome == AuditExportOutcome.Failed)}.");
        return output.ToString();
    }
}
