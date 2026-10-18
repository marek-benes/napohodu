using System.CommandLine;
using System.Globalization;
using System.Text;
using NaPohodu.Core.Filing;
using NaPohodu.Core.Pohoda;
using NaPohodu.Core.Podklady;

namespace NaPohodu.App.Cli;

/// <summary>
/// <c>napohodu podklady init</c>: založí strukturu zákazníka v Podkladech pro ročník jednotky a stávající
/// Šanony mimo strukturu přejmenuje na Archiv. Výchozí je náhled; změny až s <c>--apply</c>.
/// </summary>
internal static class PodkladyCommand
{
    public static Command Create(CliServices services)
    {
        var customerOption = new Option<string>("--customer", "-c")
        {
            Description = "Datový soubor účetní jednotky; rok struktury = ročník jednotky.",
            Required = true,
        };
        var periodOption = new Option<string?>("--period")
        {
            Description = "Periodicita DPH: mesicne nebo ctvrtletne. Bez volby se převezme ze založených roků.",
        };
        periodOption.AcceptOnlyFromAmong("mesicne", "ctvrtletne");
        var companyFolderOption = new Option<DirectoryInfo?>("--company-folder")
        {
            Description = "Složka firmy v Dokumentech (podle jejího názvu se hledá složka zákazníka v Podkladech), když ji nelze zjistit z Pohody.",
        };
        var applyOption = new Option<bool>("--apply")
        {
            Description = "Složky opravdu založit a přejmenovat. Bez této volby se jen vypíše plán.",
        };

        var init = new Command("init", "Založí strukturu zákazníka v Podkladech (Ke zpracování, Šanony roku) a staré Šanony přejmenuje na Archiv.")
        {
            customerOption,
            periodOption,
            companyFolderOption,
            applyOption,
        };

        init.SetAction(async (parseResult, cancellationToken) =>
        {
            var error = parseResult.InvocationConfiguration.Error;
            var podklady = AppConfiguration.GetPodkladyOptions(services.Configuration);
            if (string.IsNullOrWhiteSpace(podklady.RootPath))
            {
                await error.WriteLineAsync("Není nastaven kořen Podkladů (Podklady:RootPath).").ConfigureAwait(false);
                return PohodaCommandRunner.ConfigurationError;
            }

            var companyFolderOverride = parseResult.GetValue(companyFolderOption);
            if (companyFolderOverride is { Exists: false })
            {
                await error.WriteLineAsync($"Zadaná složka firmy neexistuje: {companyFolderOverride.FullName}").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            var period = parseResult.GetValue(periodOption) switch
            {
                "mesicne" => VatPeriod.Monthly,
                "ctvrtletne" => VatPeriod.Quarterly,
                _ => (VatPeriod?)null,
            };
            var options = AppConfiguration.GetPohodaOptions(services.Configuration);
            var failed = false;

            var exitCode = await PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                saveResponse: null,
                async (client, output, ct) =>
                {
                    await error.WriteLineAsync("Zjišťuji jednotku a složku firmy…").ConfigureAwait(false);
                    var unit = await client.FindAccountingUnitAsync(parseResult.GetValue(customerOption)!, ct).ConfigureAwait(false);
                    if (unit.Year is not { } year)
                    {
                        throw new PohodaException($"Pohoda u jednotky {unit.DataFile} nevrátila rok.");
                    }

                    var companyFolder = companyFolderOverride is not null
                        ? new CompanyFolderResolution(companyFolderOverride.FullName.TrimEnd('\\'), "zadáno volbou --company-folder", [])
                        : CompanyFolderResolver.Resolve(unit, await client.GetReceivedInvoicesAsync(unit, cancellationToken: ct).ConfigureAwait(false), options.DocumentsPath);
                    if (companyFolder.Path is null)
                    {
                        throw new PohodaException($"Složku firmy nelze určit ({companyFolder.Source}). Zadejte ji volbou --company-folder.");
                    }

                    var plan = PodkladyInit.Plan(podklady.RootPath, Path.GetFileName(companyFolder.Path), year, period);
                    await output.WriteAsync(FormatPlan(plan, unit.DisplayName)).ConfigureAwait(false);
                    if (plan.Error is not null)
                    {
                        failed = true;
                        return;
                    }

                    if (!parseResult.GetValue(applyOption) || plan.Actions.Count == 0)
                    {
                        await output.WriteLineAsync(plan.Actions.Count == 0 ? "Struktura je kompletní." : "Náhled – nic se nezměnilo. Pro provedení přidejte --apply.").ConfigureAwait(false);
                        return;
                    }

                    var results = PodkladyInit.Execute(plan);
                    foreach (var result in results.Where(r => !r.Succeeded))
                    {
                        await output.WriteLineAsync($"Chyba: {result.Action.Path}: {result.Message}").ConfigureAwait(false);
                    }

                    failed = results.Any(r => !r.Succeeded);
                    await output.WriteLineAsync(
                        $"Provedeno {results.Count(r => r.Succeeded)} z {plan.Actions.Count} kroků." + (failed ? " Po opravě spusťte znovu, hotové kroky se nezopakují." : string.Empty))
                        .ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return exitCode == PohodaCommandRunner.Ok && failed ? PohodaCommandRunner.PohodaError : exitCode;
        });

        return new Command("podklady", "Složky zákazníků na sdíleném disku Podklady.") { init };
    }

    internal static string FormatPlan(PodkladyInitPlan plan, string unitName)
    {
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"Jednotka:     {unitName}, rok {plan.Year}");
        output.AppendLine(CultureInfo.InvariantCulture, $"Zákazník:     {plan.CustomerFolder}");
        if (plan.Error is not null)
        {
            output.AppendLine("Nelze připravit: " + plan.Error + (plan.NeedsPeriod ? " (--period mesicne nebo ctvrtletne)." : "."));
            return output.ToString();
        }

        output.AppendLine(CultureInfo.InvariantCulture, $"Periodicita:  {PodkladyInit.Describe(plan.Period!.Value)} ({plan.PeriodSource})");
        output.AppendLine();

        var creates = plan.Actions.Where(a => a.Kind == PodkladyActionKind.Create).ToList();
        foreach (var archive in plan.Actions.Where(a => a.Kind == PodkladyActionKind.Archive))
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Přejmenovat:  {Relative(plan, archive.Path)} → {Relative(plan, archive.Target!)} (stávající Šanony mimo strukturu)");
        }

        // Kostra roku má desítky složek: vypisuje se jen do úrovně roku, zbytek souhrnem.
        var listed = creates.Where(a => Depth(plan, a.Path) <= 2).ToList();
        foreach (var create in listed)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Založit:      {Relative(plan, create.Path)}");
        }

        if (creates.Count > listed.Count)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"              … a {creates.Count - listed.Count} podsložek roku (období {(plan.Period == VatPeriod.Monthly ? "01–12" : "Q1–Q4")} s agendami, Mzdy, Závěrka, Dokumenty)");
        }

        foreach (var warning in plan.Warnings)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Pozor: {warning}");
        }

        output.AppendLine(CultureInfo.InvariantCulture, $"Celkem kroků: {plan.Actions.Count}.");
        return output.ToString();
    }

    private static string Relative(PodkladyInitPlan plan, string path)
    {
        var relative = Path.GetRelativePath(plan.CustomerFolder, path);
        return relative == "." ? "(složka zákazníka)" : relative;
    }

    // Hloubka pod složkou zákazníka: Šanony=1, rok=2, období/Mzdy=3.
    private static int Depth(PodkladyInitPlan plan, string path)
    {
        var relative = Path.GetRelativePath(plan.CustomerFolder, path);
        return relative == "." ? 0 : relative.Split(Path.DirectorySeparatorChar).Length;
    }
}
