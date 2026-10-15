using System.Globalization;
using System.Text;
using NaPohodu.Core.Models;

namespace NaPohodu.Core.Filing;

/// <summary>Výsledek hledání složky firmy v Dokumentech.</summary>
/// <param name="Path">Absolutní cesta ke složce firmy (bez koncového <c>\</c>), nebo <c>null</c>, když ji nelze určit.</param>
/// <param name="Source">Odkud se cesta vzala (pro výpis uživateli).</param>
/// <param name="Candidates">Při neúspěchu existující složky, které by mohly odpovídat.</param>
public sealed record CompanyFolderResolution(string? Path, string Source, IReadOnlyList<string> Candidates)
{
    public bool IsResolved => Path is not null;
}

/// <summary>
/// Určí složku firmy v Dokumentech. Pořadí: <c>companyDocumentsFolder</c> z exportu faktur
/// (Pohoda ho posílá jen u faktur s dokumenty) → existující složka pod Dokumenty odpovídající názvu
/// firmy. 1. generace odsekávala „právní formu“ bez oddělovače a ořezávala i běžná jména
/// („Adidas“ → „Adid“); tady se porovnávají celá slova a výsledek musí na disku existovat.
/// </summary>
public static class CompanyFolderResolver
{
    // Právní formy po normalizaci (bez teček a čárek, malá písmena), delší dřív.
    private static readonly string[] LegalForms =
    [
        "spol s r o", "s r o", "a s", "v o s", "k s", "z s", "z ú", "o p s", "o s", "s p", "se", "gmbh",
    ];

    public static CompanyFolderResolution Resolve(AccountingUnit unit, IEnumerable<Invoice> invoices, string documentsPath)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(invoices);

        var fromExport = invoices
            .Select(i => i.Documents?.CompanyDocumentsFolder)
            .OfType<string>()
            .Select(p => p.TrimEnd('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fromExport.Count == 1)
        {
            return new CompanyFolderResolution(fromExport[0], "export faktur (companyDocumentsFolder)", []);
        }

        if (fromExport.Count > 1)
        {
            return new CompanyFolderResolution(null, "export faktur vrátil více složek firmy", fromExport);
        }

        if (string.IsNullOrWhiteSpace(documentsPath) || !Directory.Exists(documentsPath))
        {
            return new CompanyFolderResolution(null, $"složka Dokumentů neexistuje ({documentsPath})", []);
        }

        var folders = Directory.EnumerateDirectories(documentsPath).Select(System.IO.Path.GetFileName).OfType<string>().ToList();
        var names = new[] { unit.Company, unit.PersonName }.OfType<string>().ToList();
        var matches = folders.Where(f => names.Any(n => Matches(f, n))).ToList();

        return matches.Count == 1
            ? new CompanyFolderResolution(System.IO.Path.Combine(documentsPath, matches[0]), "název firmy (složka nalezena pod Dokumenty)", [])
            : new CompanyFolderResolution(
                null,
                matches.Count == 0 ? "pod Dokumenty není složka odpovídající názvu firmy" : "názvu firmy odpovídá více složek",
                matches.Count > 0 ? [.. matches.Select(m => System.IO.Path.Combine(documentsPath, m))] : SimilarFolders(folders, names, documentsPath));
    }

    /// <summary>
    /// Složka odpovídá názvu firmy, když se po normalizaci (bez interpunkce, mezery sjednocené,
    /// bez ohledu na velikost písmen) shoduje celý název, nebo název bez koncové právní formy.
    /// Příklady z praxe: „R&amp;S Autopůjčovna s.r.o.“ → „R&amp;S Autopůjčovna“,
    /// „ROSTEME …, z. s.“ → „ROSTEME … z s“, „Mateřská škola, Kunovice, …“ → „Mateřská škola Kunovice …“.
    /// </summary>
    public static bool Matches(string folderName, string companyName)
    {
        var folder = Normalize(folderName);
        var company = Normalize(companyName);
        if (folder.Length == 0)
        {
            return false;
        }

        if (folder == company)
        {
            return true;
        }

        // Jen celé slovo: „adid“ není prefix „adidas“ (chyba 1. generace).
        return company.StartsWith(folder + " ", StringComparison.Ordinal)
            && LegalForms.Contains(company[(folder.Length + 1)..]);
    }

    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLower(CultureInfo.GetCultureInfo("cs-CZ")))
        {
            builder.Append(c is '.' or ',' ? ' ' : c);
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Kandidáti pro chybovou hlášku: složky, které sdílejí první slovo názvu.
    private static List<string> SimilarFolders(IEnumerable<string> folders, IEnumerable<string> names, string documentsPath)
    {
        var firstWords = names.Select(n => Normalize(n).Split(' ')[0]).Where(w => w.Length > 0).ToHashSet();
        return [.. folders.Where(f => firstWords.Contains(Normalize(f).Split(' ')[0])).Select(f => System.IO.Path.Combine(documentsPath, f))];
    }
}
