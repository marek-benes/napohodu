using System.CommandLine;
using System.Globalization;
using System.Text;
using NaPohodu.Core.Filing;
using NaPohodu.Core.Mdb;
using NaPohodu.Core.Podklady;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu filing</c>: zatřídí soubory přijatých faktur ze zdrojového adresáře do Dokumentů jednotky.
/// Výchozí je náhled (nic se nemění); přesun až s <c>--apply</c>. Poznačení v databázi Pohody (DocDirs) zatím nedělá.
/// </summary>
internal static class FilingCommand
{
    public static Command Create(CliServices services)
    {
        var customerOption = new Option<string>("--customer", "-c")
        {
            Description = "Datový soubor účetní jednotky (sloupec „Datový soubor“ z příkazu customers).",
            Required = true,
        };
        var sourceOption = new Option<DirectoryInfo?>("--source", "-s")
        {
            Description = "Adresář se soubory faktur pojmenovanými číslem faktury (např. 251100001.pdf, 251100001_dodaci_list.pdf). " +
                "Výchozí je Ke zpracování zákazníka v Podkladech (Podklady:RootPath).",
        };
        var companyFolderOption = new Option<DirectoryInfo?>("--company-folder")
        {
            Description = "Složka firmy v Dokumentech, když ji nelze zjistit z Pohody ani podle názvu firmy.",
        };
        var applyOption = new Option<bool>("--apply")
        {
            Description = "Soubory opravdu přesunout. Bez této volby se jen vypíše plán.",
        };
        var conflictOption = new Option<ConflictMode>("--on-conflict")
        {
            Description = "Když v cíli je jiný soubor se stejným názvem: Skip = přeskočit, KeepBoth = přesunout pod názvem „soubor (2)“.",
            DefaultValueFactory = _ => ConflictMode.Skip,
        };

        var markDocsOption = new Option<bool>("--mark-docs")
        {
            Description = "Po přesunu poznačit podsložky v databázi Pohody (DocDirs). Jen s --apply; jednotka musí být v Pohodě zavřená.",
        };

        var command = new Command("filing", "Zatřídí soubory přijatých faktur do Dokumentů jednotky (výchozí je náhled).")
        {
            customerOption,
            sourceOption,
            companyFolderOption,
            applyOption,
            conflictOption,
            markDocsOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption);
            var companyFolderOverride = parseResult.GetValue(companyFolderOption);
            var error = parseResult.InvocationConfiguration.Error;
            var podklady = AppConfiguration.GetPodkladyOptions(services.Configuration);
            if (source is { Exists: false })
            {
                await error.WriteLineAsync($"Zdrojový adresář neexistuje: {source.FullName}").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            if (source is null && string.IsNullOrWhiteSpace(podklady.RootPath))
            {
                await error.WriteLineAsync("Zadejte zdroj volbou --source, nebo nastavte kořen Podkladů (Podklady:RootPath).").ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            if (companyFolderOverride is { Exists: false })
            {
                await error.WriteLineAsync($"Zadaná složka firmy neexistuje: {companyFolderOverride.FullName}").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var apply = parseResult.GetValue(applyOption);
            var markDocs = parseResult.GetValue(markDocsOption);
            if (markDocs && !apply)
            {
                await error.WriteLineAsync("--mark-docs má smysl jen spolu s --apply (náhled poznačení ukáže napohodu docdirs).").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            if (markDocs && DocDirsCli.CheckDataPath(options) is { } dataPathError)
            {
                await error.WriteLineAsync(dataPathError).ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            var failed = false;
            var sourceUnresolved = false;

            var exitCode = await PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                saveResponse: null,
                async (client, output, ct) =>
                {
                    await error.WriteLineAsync("Zjišťuji jednotku a načítám přijaté faktury…").ConfigureAwait(false);
                    var unit = await client.FindAccountingUnitAsync(parseResult.GetValue(customerOption)!, ct).ConfigureAwait(false);
                    var invoices = await client.GetReceivedInvoicesAsync(unit, cancellationToken: ct).ConfigureAwait(false);

                    var companyFolder = companyFolderOverride is not null
                        ? new CompanyFolderResolution(companyFolderOverride.FullName.TrimEnd('\\'), "zadáno volbou --company-folder", [])
                        : CompanyFolderResolver.Resolve(unit, invoices, options.DocumentsPath);

                    var sourcePath = source?.FullName;
                    if (sourcePath is null)
                    {
                        var inbox = CustomerInbox.Resolve(podklady.RootPath, companyFolder);
                        if (inbox.Path is null)
                        {
                            await error.WriteLineAsync($"Zdroj nelze odvodit z Podkladů: {inbox.Source}. Zadejte ho volbou --source.").ConfigureAwait(false);
                            sourceUnresolved = true;
                            return;
                        }

                        sourcePath = inbox.Path;
                        await error.WriteLineAsync($"Zdroj: {inbox.Source}.").ConfigureAwait(false);
                    }

                    var plan = FilingPlanner.Plan(sourcePath, invoices, companyFolder);
                    await output.WriteAsync(FormatPlan(plan, unit.DisplayName, invoices.Count)).ConfigureAwait(false);

                    if (!apply)
                    {
                        await output.WriteLineAsync("Náhled – nic se nepřesunulo. Pro provedení přidejte --apply.").ConfigureAwait(false);
                        return;
                    }

                    var results = FilingExecutor.Execute(plan, parseResult.GetValue(conflictOption));
                    await output.WriteAsync(FormatResults(results, docDirsHandled: markDocs)).ConfigureAwait(false);
                    failed = results.Any(r => r.Outcome == FilingOutcome.Failed);

                    if (markDocs)
                    {
                        failed |= !await MarkDocsAsync(output, results, unit.DataFile, options).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);

            return exitCode != PohodaCommandRunner.Ok ? exitCode
                : sourceUnresolved ? PohodaCommandRunner.InvalidArguments
                : failed ? PohodaCommandRunner.PohodaError
                : exitCode;
        });

        return command;
    }

    internal static string FormatPlan(FilingPlan plan, string unitName, int invoiceCount)
    {
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"Jednotka:     {unitName} ({invoiceCount} přijatých faktur)");
        output.AppendLine(CultureInfo.InvariantCulture, $"Zdroj:        {plan.SourceDirectory}");
        output.AppendLine(CultureInfo.InvariantCulture, $"Složka firmy: {plan.CompanyFolder.Path ?? "(neurčena)"} – {plan.CompanyFolder.Source}");
        foreach (var candidate in plan.CompanyFolder.Candidates)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"              kandidát: {candidate}");
        }

        output.AppendLine();

        var table = new TextTable(
            new("Soubor", MaxWidth: 40),
            new("Faktura"),
            new("Stav"),
            new("Podsložka / důvod", MaxWidth: 80));
        foreach (var item in plan.Items)
        {
            table.AddRow(
                item.FileName,
                item.InvoiceNumber,
                Describe(item.Status),
                item.Status == FilingStatus.Unresolved ? item.Reason : item.SubFolder);
        }

        if (table.RowCount > 0)
        {
            output.Append(table).AppendLine();
        }

        output.AppendLine(CultureInfo.InvariantCulture,
            $"Spárováno {plan.Items.Count}: připraveno {plan.Count(FilingStatus.Ready)}, už zatříděno {plan.Count(FilingStatus.AlreadyFiled)}, " +
            $"kolize {plan.Count(FilingStatus.Conflict)}, nelze určit {plan.Count(FilingStatus.Unresolved)}.");
        if (plan.Unmatched.Count > 0)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Nespárováno {plan.Unmatched.Count} (název neodpovídá faktuře ročníku): {string.Join(", ", plan.Unmatched)}");
        }

        return output.ToString();
    }

    /// <summary>Poznačí DocDirs pro zatříděné soubory. Při otevřené jednotce nic nepíše a poradí, jak dokončit.</summary>
    private static async Task<bool> MarkDocsAsync(TextWriter output, IReadOnlyList<FilingResultItem> results, string dataFile, Core.Configuration.PohodaOptions options)
    {
        var mdbPath = PohodaDataFile.PathFor(options.DataPath, dataFile);
        var plan = DocDirsPlanner.Plan(mdbPath, DocDirsSnapshot.Read(mdbPath), DocDirsTargets.FromFiling(results));
        await output.WriteLineAsync().ConfigureAwait(false);
        await output.WriteAsync(DocDirsCli.FormatPlan(plan)).ConfigureAwait(false);

        if (plan.Writes.Any() && PohodaDataFile.FindLock(mdbPath) is not null)
        {
            await output.WriteLineAsync(
                $"Jednotka je otevřená v Pohodě – soubory jsou přesunuté, ale DocDirs se nezapsaly. Zavřete ji a spusťte: napohodu docdirs -c {dataFile} --apply")
                .ConfigureAwait(false);
            return false;
        }

        await output.WriteAsync(DocDirsCli.Apply(plan, options)).ConfigureAwait(false);
        return true;
    }

    internal static string FormatResults(IReadOnlyList<FilingResultItem> results, bool docDirsHandled = false)
    {
        var output = new StringBuilder();
        output.AppendLine();
        foreach (var result in results.Where(r => r.Outcome is FilingOutcome.MovedRenamed or FilingOutcome.Failed))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"{Describe(result.Outcome)}: {result.Item.FileName} → {result.TargetPath} ({result.Message})");
        }

        output.AppendLine(CultureInfo.InvariantCulture,
            $"Přesunuto {results.Count(r => r.Outcome is FilingOutcome.Moved or FilingOutcome.MovedRenamed)}, " +
            $"přeskočeno {results.Count(r => r.Outcome == FilingOutcome.Skipped)}, chyb {results.Count(r => r.Outcome == FilingOutcome.Failed)}.");
        if (!docDirsHandled && results.Any(r => r.Outcome is FilingOutcome.Moved or FilingOutcome.MovedRenamed))
        {
            output.AppendLine("Pozor: nové podsložky Pohoda nezná, dokud je nepoznačíte v databázi: napohodu docdirs -c <datový soubor> --apply (nebo příště filing --apply --mark-docs).");
        }

        return output.ToString();
    }

    private static string Describe(FilingStatus status) =>
        status switch
        {
            FilingStatus.Ready => "připraveno",
            FilingStatus.AlreadyFiled => "už zatříděno",
            FilingStatus.Conflict => "kolize",
            FilingStatus.Unresolved => "nelze určit",
            _ => status.ToString(),
        };

    private static string Describe(FilingOutcome outcome) =>
        outcome switch
        {
            FilingOutcome.Moved => "Přesunuto",
            FilingOutcome.MovedRenamed => "Přesunuto pod jiným názvem",
            FilingOutcome.Skipped => "Přeskočeno",
            FilingOutcome.Failed => "Chyba",
            _ => outcome.ToString(),
        };
}
