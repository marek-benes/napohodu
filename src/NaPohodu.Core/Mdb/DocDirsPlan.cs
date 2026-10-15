namespace NaPohodu.Core.Mdb;

/// <summary>Požadovaná vazba faktury na podsložku dokladu (relativně ke složce firmy).</summary>
public sealed record DocDirTarget(int InvoiceId, string InvoiceNumber, string Dir);

public enum DocDirAction
{
    /// <summary>Faktura nemá řádek v DocDirs → INSERT.</summary>
    Insert,

    /// <summary>Řádek existuje s prázdným Dir → UPDATE.</summary>
    Update,

    /// <summary>Řádek už má stejný Dir (bez ohledu na velikost písmen).</summary>
    AlreadyMarked,

    /// <summary>Řádek má jiný Dir → nikdy nepřepisovat, jen nahlásit.</summary>
    Conflict,

    /// <summary>Faktura v datovém souboru není (jiný ročník, smazaná, jiný typ dokladu).</summary>
    MissingInvoice,
}

public sealed record DocDirsPlanItem(DocDirTarget Target, DocDirAction Action, int? DocDirId, string? CurrentDir);

public sealed record DocDirsPlan(string MdbPath, IReadOnlyList<DocDirsPlanItem> Items)
{
    public IEnumerable<DocDirsPlanItem> Writes => Items.Where(i => i.Action is DocDirAction.Insert or DocDirAction.Update);

    public int Count(DocDirAction action) => Items.Count(i => i.Action == action);
}

/// <summary>
/// Porovná požadované vazby se stavem <c>DocDirs</c>. Pravidla: chybějící řádek → INSERT,
/// prázdný Dir → UPDATE, stejný Dir → nic, jiný Dir → konflikt (nikdy nepřepisovat).
/// </summary>
public static class DocDirsPlanner
{
    public static DocDirsPlan Plan(string mdbPath, DocDirsSnapshot snapshot, IEnumerable<DocDirTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(targets);

        var invoiceIds = snapshot.Invoices.Select(i => i.Id).ToHashSet();
        var rowsByInvoice = snapshot.DocDirs.GroupBy(d => d.RelId).ToDictionary(g => g.Key, g => g.ToList());

        var items = targets
            .GroupBy(t => t.InvoiceId)
            .Select(g => g.First())   // více souborů jedné faktury = jedna vazba
            .OrderBy(t => t.InvoiceNumber, StringComparer.Ordinal)
            .Select(target => PlanItem(target, invoiceIds, rowsByInvoice))
            .ToList();

        return new DocDirsPlan(mdbPath, items);
    }

    private static DocDirsPlanItem PlanItem(DocDirTarget target, HashSet<int> invoiceIds, Dictionary<int, List<DocDirRow>> rowsByInvoice)
    {
        if (!invoiceIds.Contains(target.InvoiceId))
        {
            return new(target, DocDirAction.MissingInvoice, null, null);
        }

        if (!rowsByInvoice.TryGetValue(target.InvoiceId, out var rows))
        {
            return new(target, DocDirAction.Insert, null, null);
        }

        if (rows.FirstOrDefault(r => string.Equals(r.Dir, target.Dir, StringComparison.OrdinalIgnoreCase)) is { } same)
        {
            return new(target, DocDirAction.AlreadyMarked, same.Id, same.Dir);
        }

        if (rows.FirstOrDefault(r => r.Dir is not null) is { } other)
        {
            return new(target, DocDirAction.Conflict, other.Id, other.Dir);
        }

        return new(target, DocDirAction.Update, rows[0].Id, null);
    }
}
