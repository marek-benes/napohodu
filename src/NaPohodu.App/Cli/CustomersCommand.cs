using System.CommandLine;
using System.Globalization;
using NaPohodu.Core.Models;

namespace NaPohodu.App.Cli;

/// <summary><c>napohodu customers</c>: seznam účetních jednotek (zákazníků) z Pohody.</summary>
internal static class CustomersCommand
{
    public static Command Create(CliServices services)
    {
        var yearOption = new Option<int?>("--year", "-y") { Description = "Jen jednotky daného účetního roku." };
        var searchOption = new Option<string?>("--search", "-s") { Description = "Hledat v názvu, IČ nebo datovém souboru." };
        var saveResponseOption = CliOptions.SaveResponse();

        var command = new Command("customers", "Vypíše účetní jednotky (zákazníky) z Pohody.")
        {
            yearOption,
            searchOption,
            saveResponseOption,
        };

        command.SetAction((parseResult, cancellationToken) =>
            PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                parseResult.GetValue(saveResponseOption),
                async (client, output, ct) =>
                {
                    var units = await client.GetAccountingUnitsAsync(ct).ConfigureAwait(false);
                    var filtered = Filter(units, parseResult.GetValue(yearOption), parseResult.GetValue(searchOption));
                    await output.WriteAsync(Format(filtered, units.Count)).ConfigureAwait(false);
                },
                cancellationToken));

        return command;
    }

    internal static IReadOnlyList<AccountingUnit> Filter(IEnumerable<AccountingUnit> units, int? year, string? search) =>
        [
            .. units
                .Where(u => year is null || u.Year == year)
                .Where(u => string.IsNullOrWhiteSpace(search)
                    || Contains(u.DisplayName, search)
                    || Contains(u.Ico, search)
                    || Contains(u.DataFile, search))
                .OrderBy(u => u.DisplayName, StringComparer.Create(CultureInfo.GetCultureInfo("cs-CZ"), ignoreCase: true))
                .ThenByDescending(u => u.Year),
        ];

    internal static string Format(IReadOnlyList<AccountingUnit> units, int total)
    {
        var table = new TextTable(
            new("Datový soubor"),
            new("Rok", AlignRight: true),
            new("Název", MaxWidth: 50),
            new("IČ"),
            new("Účetnictví"));

        foreach (var unit in units)
        {
            table.AddRow(
                unit.DataFile,
                unit.Year?.ToString(CultureInfo.InvariantCulture),
                unit.DisplayName,
                unit.Ico,
                DescribeUnitType(unit.UnitType));
        }

        var summary = units.Count == total
            ? $"Celkem {total} jednotek."
            : $"Zobrazeno {units.Count} z {total} jednotek.";
        return table + Environment.NewLine + summary + Environment.NewLine;
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase) == true;

    private static string? DescribeUnitType(string? unitType) =>
        unitType switch
        {
            "singleEntry" => "daňová evidence",
            "doubleEntry" => "podvojné",
            "doubleEntryNonProfit" => "podvojné (nezisková)",
            "doubleEntryAllowance" => "podvojné (příspěvková)",
            _ => unitType,
        };
}
