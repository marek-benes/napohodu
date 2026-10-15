namespace NaPohodu.Core.Filing;

/// <summary>Pravidla pro názvy souborů s fakturami.</summary>
public static class DocumentFileNames
{
    private static readonly HashSet<string> SystemFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "thumbs.db",
        "ehthumbs.db",
        ".ds_store",
    };

    /// <summary>Skryté (název začíná tečkou) a systémové soubory se nezatřiďují.</summary>
    public static bool IsHiddenOrSystem(string fileName) =>
        fileName.StartsWith('.') || SystemFileNames.Contains(fileName);

    /// <summary>
    /// Najde číslo faktury, ke kterému soubor patří, podle názvu bez přípony: přesná shoda
    /// (<c>251100001.pdf</c>), nebo příloha <c>&lt;číslo&gt;_&lt;popis&gt;</c>. Číslo samo může obsahovat
    /// podtržítko, proto se zkouší každý prefix končící před podtržítkem (nejkratší první).
    /// </summary>
    public static string? MatchInvoiceNumber(string fileName, IReadOnlySet<string> invoiceNumbers)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(invoiceNumbers);

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (invoiceNumbers.Contains(baseName))
        {
            return baseName;
        }

        for (var index = baseName.IndexOf('_', StringComparison.Ordinal); index > 0; index = baseName.IndexOf('_', index + 1))
        {
            var candidate = baseName[..index];
            if (invoiceNumbers.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
