using System.Globalization;
using System.Text;
using NaPohodu.Core.Configuration;
using NaPohodu.Core.Mdb;

namespace NaPohodu.App.Cli;

/// <summary>Společný výpis a zápis poznačení DocDirs pro <c>docdirs</c> i <c>filing --mark-docs</c>.</summary>
internal static class DocDirsCli
{
    public static string FormatPlan(DocDirsPlan plan, IReadOnlyList<string>? notInDataFile = null, IReadOnlyList<string>? duplicates = null)
    {
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"Datový soubor: {plan.MdbPath}");

        var table = new TextTable(new("Faktura"), new("Akce"), new("Podsložka", MaxWidth: 70), new("Nyní v Pohodě", MaxWidth: 70));
        foreach (var item in plan.Items.Where(i => i.Action != DocDirAction.AlreadyMarked))
        {
            table.AddRow(item.Target.InvoiceNumber, Describe(item.Action), item.Target.Dir, item.CurrentDir);
        }

        if (table.RowCount > 0)
        {
            output.AppendLine().Append(table).AppendLine();
        }

        output.AppendLine(CultureInfo.InvariantCulture,
            $"DocDirs: vložit {plan.Count(DocDirAction.Insert)}, doplnit {plan.Count(DocDirAction.Update)}, už poznačeno {plan.Count(DocDirAction.AlreadyMarked)}, " +
            $"konflikt {plan.Count(DocDirAction.Conflict)} (nepřepisuje se), faktura v ročníku není {plan.Count(DocDirAction.MissingInvoice)}.");
        if (duplicates is { Count: > 0 })
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Číslo ve více složkách na disku (přeskočeno, vyřešte ručně): {string.Join(", ", duplicates)}");
        }

        if (notInDataFile is { Count: > 0 })
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"Složky bez faktury v tomto ročníku (jiný rok?): {notInDataFile.Count}");
        }

        return output.ToString();
    }

    /// <summary>Zapíše plán, nebo vysvětlí, proč ne (zámek). Vrací text pro výstup.</summary>
    public static string Apply(DocDirsPlan plan, PohodaOptions options)
    {
        if (!plan.Writes.Any())
        {
            return "DocDirs: není co zapsat." + Environment.NewLine;
        }

        var result = DocDirsWriter.Apply(plan, options.EffectiveBackupPath, options.BackupsToKeep);
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"DocDirs zapsáno: vloženo {result.Inserted}, doplněno {result.Updated}. Záloha: {result.BackupPath}");
        foreach (var change in result.Changed)
        {
            output.AppendLine(change);
        }

        output.AppendLine("Ověřte v Pohodě záložku Dokumenty u některé z dotčených faktur.");
        return output.ToString();
    }

    public static string? CheckDataPath(PohodaOptions options) =>
        string.IsNullOrWhiteSpace(options.DataPath) || !Directory.Exists(options.DataPath)
            ? $"Složka s daty Pohody není nastavena nebo neexistuje (Pohoda:DataPath = „{options.DataPath}“)."
            : null;

    private static string Describe(DocDirAction action) =>
        action switch
        {
            DocDirAction.Insert => "vložit",
            DocDirAction.Update => "doplnit",
            DocDirAction.AlreadyMarked => "už poznačeno",
            DocDirAction.Conflict => "konflikt",
            DocDirAction.MissingInvoice => "faktura není v ročníku",
            _ => action.ToString(),
        };
}
