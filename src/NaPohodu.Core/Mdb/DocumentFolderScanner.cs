using NaPohodu.Core.Filing;

namespace NaPohodu.Core.Mdb;

/// <summary>Výsledek skenu složek dokladů přijatých faktur na disku.</summary>
/// <param name="Folders">Číslo faktury → podsložka relativně ke složce firmy.</param>
/// <param name="Duplicates">Čísla nalezená ve více složkách (přeskakují se).</param>
public sealed record DocumentFolderScan(IReadOnlyDictionary<string, string> Folders, IReadOnlyList<string> Duplicates);

/// <summary>
/// Najde podsložky dokladů pod <c>&lt;složka firmy&gt;\Fakturace\Přijaté faktury</c> v obou tvarech:
/// plochý <c>…\&lt;číslo&gt;</c> (složka přímo obsahuje soubory) i <c>…\&lt;prefix&gt;\&lt;číslo&gt;</c>.
/// Prázdné složky se ignorují. Co je opravdu faktura, rozhodne až párování s <c>FA.Cislo</c>.
/// </summary>
public static class DocumentFolderScanner
{
    public static DocumentFolderScan Scan(string companyFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companyFolder);

        var root = Path.Combine(companyFolder, InvoiceSubFolders.ReceivedInvoicesFolder);
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return new DocumentFolderScan(new Dictionary<string, string>(), []);
        }

        foreach (var level1 in Directory.EnumerateDirectories(root))
        {
            var name1 = Path.GetFileName(level1);
            if (HasDocuments(level1, recursive: false))
            {
                Add(found, name1, $@"{InvoiceSubFolders.ReceivedInvoicesFolder}\{name1}");
            }

            foreach (var level2 in Directory.EnumerateDirectories(level1))
            {
                if (HasDocuments(level2, recursive: true))
                {
                    var name2 = Path.GetFileName(level2);
                    Add(found, name2, $@"{InvoiceSubFolders.ReceivedInvoicesFolder}\{name1}\{name2}");
                }
            }
        }

        var duplicates = found.Where(f => f.Value.Count > 1).Select(f => f.Key).Order(StringComparer.Ordinal).ToList();
        var folders = found.Where(f => f.Value.Count == 1).ToDictionary(f => f.Key, f => f.Value[0], StringComparer.Ordinal);
        return new DocumentFolderScan(folders, duplicates);
    }

    private static void Add(Dictionary<string, List<string>> found, string number, string relative)
    {
        if (!found.TryGetValue(number, out var list))
        {
            found[number] = list = [];
        }

        list.Add(relative);
    }

    private static bool HasDocuments(string directory, bool recursive) =>
        Directory.EnumerateFiles(directory, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Any(f => !DocumentFileNames.IsHiddenOrSystem(Path.GetFileName(f)));
}
