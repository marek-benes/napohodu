using System.CommandLine;
using System.Globalization;
using System.Text;
using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary><c>napohodu invoices</c>: přijaté faktury jedné účetní jednotky včetně záložky Dokumenty.</summary>
internal static class InvoicesCommand
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static Command Create(CliServices services)
    {
        var customerOption = new Option<string>("--customer", "-c")
        {
            Description = "Datový soubor účetní jednotky (sloupec „Datový soubor“ z příkazu customers).",
            Required = true,
        };
        var fromOption = CliOptions.Date("--from", "Datum od");
        var toOption = CliOptions.Date("--to", "Datum do");
        var icoOption = new Option<string?>("--ico") { Description = "IČ jednotky; Pohoda jím ověří, že jde o správnou firmu." };
        var saveResponseOption = CliOptions.SaveResponse();

        var command = new Command("invoices", "Vypíše přijaté faktury účetní jednotky včetně záložky Dokumenty.")
        {
            customerOption,
            fromOption,
            toOption,
            icoOption,
            saveResponseOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            // Až v akci: validátor příkazu by četl hodnoty i z voleb, které neprošly parsováním.
            var filter = new InvoiceFilter(parseResult.GetValue(fromOption), parseResult.GetValue(toOption));
            if (filter.DateFrom > filter.DateTill)
            {
                await parseResult.InvocationConfiguration.Error.WriteLineAsync("Datum --from je po datu --to.").ConfigureAwait(false);
                return PohodaCommandRunner.InvalidArguments;
            }

            return await PohodaCommandRunner.RunAsync(
                parseResult,
                services,
                parseResult.GetValue(saveResponseOption),
                async (client, output, ct) =>
                {
                    var invoices = await client.GetReceivedInvoicesAsync(
                        parseResult.GetValue(customerOption)!,
                        filter,
                        parseResult.GetValue(icoOption),
                        ct).ConfigureAwait(false);
                    await output.WriteAsync(Format(invoices)).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    internal static string Format(IReadOnlyList<Invoice> invoices)
    {
        var table = new TextTable(
            new("ID", AlignRight: true),
            new("Číslo"),
            new("Řada"),
            new("Datum", AlignRight: true),
            new("Splatnost", AlignRight: true),
            new("Dodavatel", MaxWidth: 30),
            new("IČ"),
            new("Celkem", AlignRight: true),
            new("Měna"),
            new("Uhrazeno", AlignRight: true),
            new("Podsložka Dokumentů", MaxWidth: 60),
            new("Soubory", AlignRight: true));

        foreach (var invoice in invoices.OrderBy(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal))
        {
            var (total, currency) = invoice.TotalForeign is { } foreign
                ? (foreign.Total, foreign.Currency)
                : (invoice.TotalHome, "CZK");

            table.AddRow(
                invoice.Id.ToString(CultureInfo.InvariantCulture),
                invoice.Number,
                invoice.NumberSeries,
                FormatDate(invoice.Date),
                FormatDate(invoice.DateDue),
                invoice.PartnerDisplayName,
                invoice.PartnerIco,
                total.ToString("N2", Czech),
                currency,
                FormatDate(invoice.LiquidationDate),
                invoice.Documents?.SubFolder,
                invoice.Documents is { } documents ? documents.Files.Count.ToString(CultureInfo.InvariantCulture) : null);
        }

        var output = new StringBuilder();
        output.Append(table).AppendLine();
        output.Append(CultureInfo.InvariantCulture, $"Celkem {invoices.Count} faktur.").AppendLine();
        AppendDocumentsSummary(output, invoices);
        return output.ToString();
    }

    /// <summary>Souhrn záložky Dokumenty: podklad pro otevřenou otázku, zda Pohoda vrací složku firmy u všech faktur.</summary>
    private static void AppendDocumentsSummary(StringBuilder output, IReadOnlyList<Invoice> invoices)
    {
        var folders = invoices
            .Select(i => i.Documents?.CompanyDocumentsFolder)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var withoutFolder = invoices.Count(i => i.Documents?.CompanyDocumentsFolder is null);
        var withoutSubFolder = invoices.Count(i => i.Documents?.SubFolder is null);

        output.Append("Složka firmy v Dokumentech: ")
            .Append(folders.Count == 0 ? "(Pohoda nevrátila)" : string.Join(", ", folders))
            .AppendLine();
        if (invoices.Count > 0)
        {
            output.Append(CultureInfo.InvariantCulture, $"Bez složky firmy: {withoutFolder}, bez podsložky dokladu: {withoutSubFolder}.").AppendLine();
        }
    }

    private static string? FormatDate(DateOnly? date) => date?.ToString("d.M.yyyy", Czech);
}
