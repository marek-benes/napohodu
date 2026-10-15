using NaPohodu.Core.Filing;

namespace NaPohodu.Core.Mdb;

/// <summary>Požadované vazby DocDirs ze dvou zdrojů: výsledek zatřídění, nebo sken složek na disku.</summary>
public static class DocDirsTargets
{
    /// <summary>
    /// Vazby pro soubory, které po zatřídění leží v podsložce dokladu (přesunuté i „už zatříděné“).
    /// </summary>
    public static IReadOnlyList<DocDirTarget> FromFiling(IEnumerable<FilingResultItem> results) =>
        [
            .. results
                .Where(r => r.Outcome is FilingOutcome.Moved or FilingOutcome.MovedRenamed
                    || r.Item.Status == FilingStatus.AlreadyFiled)
                .Where(r => r.Item is { InvoiceId: not null, SubFolder: not null })
                .Select(r => new DocDirTarget(r.Item.InvoiceId!.Value, r.Item.InvoiceNumber, r.Item.SubFolder!)),
        ];

    /// <summary>
    /// Vazby z adresářové struktury (rekonciliace jako v 1. generaci): složka pojmenovaná číslem faktury
    /// ↔ <c>FA.Cislo</c> v daném ročníku. Duplicitní čísla v MDB se přeskakují.
    /// </summary>
    /// <param name="notInDataFile">Čísla složek, která v ročníku nejsou (jiný rok nebo nejde o fakturu).</param>
    public static IReadOnlyList<DocDirTarget> FromDisk(DocumentFolderScan scan, DocDirsSnapshot snapshot, out IReadOnlyList<string> notInDataFile)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(snapshot);

        var byNumber = snapshot.Invoices
            .Where(i => i.Number.Length > 0)
            .GroupBy(i => i.Number, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.Ordinal);

        var missing = new List<string>();
        var targets = new List<DocDirTarget>();
        foreach (var (number, dir) in scan.Folders.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            if (byNumber.TryGetValue(number, out var id))
            {
                targets.Add(new DocDirTarget(id, number, dir));
            }
            else
            {
                missing.Add(number);
            }
        }

        notInDataFile = missing;
        return targets;
    }
}
