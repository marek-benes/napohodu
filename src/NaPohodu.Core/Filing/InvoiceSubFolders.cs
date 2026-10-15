using NaPohodu.Core.Models;

namespace NaPohodu.Core.Filing;

/// <summary>Podsložka dokladu relativně ke složce firmy (tvar <c>DocDirs.Dir</c>) a odkud se vzala.</summary>
public sealed record SubFolderResolution(string? SubFolder, string Source)
{
    public bool IsResolved => SubFolder is not null;
}

/// <summary>
/// Určí podsložku dokladu přijaté faktury. Pořadí:
/// 1. <c>subFolder</c> z exportu, pokud faktura už dokumenty má (bez ohledu na tvar, i plochý),
/// 2. <c>Fakturace\Přijaté faktury\&lt;prefix&gt;\&lt;číslo&gt;</c>, kde prefix je <c>number/ids</c> faktury,
/// 3. … nebo <c>ids</c> jiné faktury z exportu, jejíž číslo začíná stejně (Pohoda u faktur
///    z řady minulého roku <c>ids</c> neposílá),
/// 4. … nebo jediná existující složka pod <c>Přijaté faktury</c>, jejíž název je prefixem čísla.
/// Jinak nevyřešeno (nikdy „první 4 znaky čísla“ jako v 1. generaci).
/// </summary>
public sealed class InvoiceSubFolders
{
    public const string ReceivedInvoicesFolder = @"Fakturace\Přijaté faktury";

    private readonly IReadOnlyList<string> _knownSeries;
    private readonly IReadOnlyList<string> _seriesFoldersOnDisk;

    /// <param name="invoices">Všechny faktury z exportu jednotky (zdroj známých prefixů řad).</param>
    /// <param name="companyFolder">Složka firmy, nebo <c>null</c>, když není známá (pak se disk neprochází).</param>
    public InvoiceSubFolders(IEnumerable<Invoice> invoices, string? companyFolder)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        _knownSeries =
        [
            .. invoices
                .Where(i => i.NumberSeries is { Length: > 0 } ids && i.Number.StartsWith(ids, StringComparison.Ordinal))
                .Select(i => i.NumberSeries!)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(s => s.Length),
        ];

        var receivedFolder = companyFolder is null ? null : Path.Combine(companyFolder, ReceivedInvoicesFolder);
        _seriesFoldersOnDisk = receivedFolder is not null && Directory.Exists(receivedFolder)
            ? [.. Directory.EnumerateDirectories(receivedFolder).Select(Path.GetFileName).OfType<string>()]
            : [];
    }

    public SubFolderResolution Resolve(Invoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.Documents?.SubFolder is { } existing)
        {
            return IsSafeRelativePath(existing)
                ? new SubFolderResolution(existing, "existující podsložka z Pohody")
                : new SubFolderResolution(null, $"podsložka z Pohody „{existing}“ není bezpečná relativní cesta");
        }

        if (!IsSafeFolderName(invoice.Number))
        {
            return new SubFolderResolution(null, $"číslo faktury „{invoice.Number}“ nelze použít jako název složky");
        }

        if (invoice.NumberSeries is { Length: > 0 } ids && invoice.Number.StartsWith(ids, StringComparison.Ordinal))
        {
            return Build(ids, invoice.Number, "prefix řady z Pohody");
        }

        if (_knownSeries.FirstOrDefault(s => invoice.Number.StartsWith(s, StringComparison.Ordinal) && invoice.Number.Length > s.Length) is { } known)
        {
            return Build(known, invoice.Number, "prefix řady jiné faktury");
        }

        var onDisk = _seriesFoldersOnDisk
            .Where(f => f.Length < invoice.Number.Length && invoice.Number.StartsWith(f, StringComparison.Ordinal))
            .ToList();
        return onDisk.Count switch
        {
            1 => Build(onDisk[0], invoice.Number, "existující složka řady na disku"),
            0 => new SubFolderResolution(null, "prefix číselné řady není známý (Pohoda neposlala ids a složka řady na disku není)"),
            _ => new SubFolderResolution(null, $"čísla odpovídá více složek řady: {string.Join(", ", onDisk)}"),
        };
    }

    private static SubFolderResolution Build(string series, string number, string source) =>
        IsSafeFolderName(series)
            ? new SubFolderResolution($@"{ReceivedInvoicesFolder}\{series}\{number}", source)
            : new SubFolderResolution(null, $"prefix řady „{series}“ nelze použít jako název složky");

    private static bool IsSafeFolderName(string name) =>
        name.Length > 0
        && name.Trim() == name
        && name is not "." and not ".."
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    // Relativní cesta bez kořene a bez „..“, aby cíl nemohl vyjít ze složky firmy.
    private static bool IsSafeRelativePath(string path) =>
        !Path.IsPathRooted(path) && path.Split('\\', '/').All(IsSafeFolderName);
}
