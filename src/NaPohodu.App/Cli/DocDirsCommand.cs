using System.CommandLine;
using NaPohodu.Core.Filing;
using NaPohodu.Core.Mdb;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu docdirs</c>: srovná poznačení dokumentů přijatých faktur v databázi Pohody (<c>DocDirs</c>)
/// se složkami na disku. Výchozí náhled; zápis s <c>--apply</c> (vyžaduje zavřenou jednotku, dělá zálohu).
/// </summary>
internal static class DocDirsCommand
{
    public static Command Create(CliServices services)
    {
        var customerOption = new Option<string>("--customer", "-c")
        {
            Description = "Datový soubor účetní jednotky (ročník). Dokumenty jsou společné pro všechny roky, poznačuje se každý ročník zvlášť.",
            Required = true,
        };
        var companyFolderOption = new Option<DirectoryInfo?>("--company-folder")
        {
            Description = "Složka firmy v Dokumentech, když ji nelze zjistit z Pohody ani podle názvu firmy.",
        };
        var applyOption = new Option<bool>("--apply")
        {
            Description = "Změny opravdu zapsat do MDB (jednotka musí být v Pohodě zavřená). Bez této volby jen náhled.",
        };

        var command = new Command("docdirs", "Srovná poznačení dokumentů přijatých faktur v Pohodě (DocDirs) se složkami na disku.")
        {
            customerOption,
            companyFolderOption,
            applyOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var error = parseResult.InvocationConfiguration.Error;
            if (DocDirsCli.CheckDataPath(options) is { } dataPathError)
            {
                await error.WriteLineAsync(dataPathError).ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            var companyFolderOverride = parseResult.GetValue(companyFolderOption);
            if (companyFolderOverride is { Exists: false })
            {
                await error.WriteLineAsync($"Zadaná složka firmy neexistuje: {companyFolderOverride.FullName}").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            return await PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                saveResponse: null,
                async (client, output, ct) =>
                {
                    await error.WriteLineAsync("Zjišťuji jednotku a složku firmy…").ConfigureAwait(false);
                    var unit = await client.FindAccountingUnitAsync(parseResult.GetValue(customerOption)!, ct).ConfigureAwait(false);

                    string companyFolder;
                    if (companyFolderOverride is not null)
                    {
                        companyFolder = companyFolderOverride.FullName.TrimEnd('\\');
                    }
                    else
                    {
                        var invoices = await client.GetReceivedInvoicesAsync(unit, cancellationToken: ct).ConfigureAwait(false);
                        var resolution = CompanyFolderResolver.Resolve(unit, invoices, options.DocumentsPath);
                        if (resolution.Path is null)
                        {
                            throw new Core.Pohoda.PohodaException(
                                $"Složku firmy nelze určit ({resolution.Source}). Zadejte ji volbou --company-folder."
                                + (resolution.Candidates.Count > 0 ? $" Kandidáti: {string.Join(", ", resolution.Candidates)}" : string.Empty));
                        }

                        companyFolder = resolution.Path;
                    }

                    var mdbPath = PohodaDataFile.PathFor(options.DataPath, unit.DataFile);
                    var snapshot = DocDirsSnapshot.Read(mdbPath);
                    var scan = DocumentFolderScanner.Scan(companyFolder);
                    var targets = DocDirsTargets.FromDisk(scan, snapshot, out var notInDataFile);
                    var plan = DocDirsPlanner.Plan(mdbPath, snapshot, targets);

                    await output.WriteLineAsync($"Jednotka:      {unit.DisplayName}").ConfigureAwait(false);
                    await output.WriteLineAsync($"Složka firmy:  {companyFolder}").ConfigureAwait(false);
                    await output.WriteAsync(DocDirsCli.FormatPlan(plan, notInDataFile, scan.Duplicates)).ConfigureAwait(false);

                    if (!parseResult.GetValue(applyOption))
                    {
                        await output.WriteLineAsync("Náhled – nic se nezapsalo. Pro zápis zavřete jednotku v Pohodě a přidejte --apply.").ConfigureAwait(false);
                        return;
                    }

                    await output.WriteAsync(DocDirsCli.Apply(plan, options)).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }
}
