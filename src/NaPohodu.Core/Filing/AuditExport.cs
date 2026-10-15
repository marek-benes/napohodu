using NaPohodu.Core.Models;

namespace NaPohodu.Core.Filing;

/// <summary>Soubor k exportu pro kontrolu.</summary>
/// <param name="ExportName">Název v cílové složce: začíná číslem faktury (kvůli řazení a jednoznačnosti).</param>
public sealed record AuditExportFile(string InvoiceNumber, string SourcePath, string ExportName);

/// <summary>Plán exportu pro kontrolu za období.</summary>
/// <param name="Invoices">Faktury v období (podle DUZP, hranice včetně).</param>
/// <param name="WithoutDocuments">Faktury v období bez dokumentů (Pohoda u nich podsložku nevede).</param>
/// <param name="MissingFiles">Soubory, které Pohoda u faktury eviduje, ale na disku nejsou.</param>
public sealed record AuditExportPlan(
    IReadOnlyList<Invoice> Invoices,
    IReadOnlyList<AuditExportFile> Files,
    IReadOnlyList<string> WithoutDocuments,
    IReadOnlyList<string> MissingFiles);

public enum AuditExportOutcome
{
    Copied,
    CopiedRenamed,
    AlreadyThere,
    Failed,
}

public sealed record AuditExportResultItem(AuditExportFile File, AuditExportOutcome Outcome, string? TargetPath, string? Error);

/// <summary>
/// Export souborů přijatých faktur za období (podle data zdanitelného plnění, jako 1. generace)
/// do jedné ploché složky pro kontrolu. Zdrojem je podsložka dokladu evidovaná v Pohodě (<c>subFolder</c>),
/// ne odhad cesty. Soubory se kopírují (originál zůstane), cíl se nikdy nepřepisuje:
/// název dostane prefix čísla faktury, totožný soubor se přeskočí, jiný se uloží jako „(2)“.
/// </summary>
public static class AuditExport
{
    public static AuditExportPlan Plan(IEnumerable<Invoice> invoices, string companyFolder, DateOnly? from, DateOnly? to)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentException.ThrowIfNullOrWhiteSpace(companyFolder);

        var inPeriod = invoices
            .Where(i => i.DateTax is { } date && (from is null || date >= from) && (to is null || date <= to))
            .OrderBy(i => i.DateTax)
            .ThenBy(i => i.Number, StringComparer.Ordinal)
            .ToList();

        var files = new List<AuditExportFile>();
        var withoutDocuments = new List<string>();
        var missing = new List<string>();

        foreach (var invoice in inPeriod)
        {
            if (invoice.Documents?.SubFolder is not { } subFolder || !IsSafeRelative(subFolder))
            {
                withoutDocuments.Add(invoice.Number);
                continue;
            }

            var folder = Path.Combine(companyFolder, subFolder);
            var onDisk = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder)
                    .Where(f => !DocumentFileNames.IsHiddenOrSystem(Path.GetFileName(f)))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : [];

            files.AddRange(onDisk.Select(f => new AuditExportFile(invoice.Number, f, ExportName(invoice.Number, Path.GetFileName(f)))));

            var namesOnDisk = onDisk.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            missing.AddRange(invoice.Documents.Files
                .Where(name => !namesOnDisk.Contains(name))
                .Select(name => $@"{invoice.Number}: {subFolder}\{name}"));
        }

        return new AuditExportPlan(inPeriod, files, withoutDocuments, missing);
    }

    public static IReadOnlyList<AuditExportResultItem> Execute(AuditExportPlan plan, string destination)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        Directory.CreateDirectory(destination);
        var results = new List<AuditExportResultItem>(plan.Files.Count);
        foreach (var file in plan.Files)
        {
            var target = Path.Combine(destination, file.ExportName);
            try
            {
                if (ExistingCopy(file.SourcePath, target) is { } existing)
                {
                    results.Add(new(file, AuditExportOutcome.AlreadyThere, existing, null));
                    continue;
                }

                var outcome = AuditExportOutcome.Copied;
                if (File.Exists(target))
                {
                    target = FilingExecutor.FreeName(target);
                    outcome = AuditExportOutcome.CopiedRenamed;
                }

                File.Copy(file.SourcePath, target, overwrite: false);
                results.Add(new(file, outcome, target, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new(file, AuditExportOutcome.Failed, target, ex.Message));
            }
        }

        return results;
    }

    /// <summary>
    /// Totožná kopie už v cíli – pod původním názvem nebo jako „název (n)“ z dřívějšího běhu.
    /// Díky tomu opakovaný export nevytváří další a další kopie.
    /// </summary>
    private static string? ExistingCopy(string source, string target)
    {
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);
        var candidates = Directory.EnumerateFiles(Path.GetDirectoryName(target)!, $"{name}*{extension}")
            .Where(c => string.Equals(c, target, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(c).StartsWith($"{name} (", StringComparison.OrdinalIgnoreCase));
        return candidates.FirstOrDefault(c => FilingPlanner.SameContent(source, c));
    }

    /// <summary>
    /// Název v ploché exportní složce. Soubory pojmenované číslem faktury (i přílohy <c>&lt;číslo&gt;_…</c>)
    /// zůstanou, ostatní (např. „Sken 1.pdf“ ručně přidaný v Pohodě) dostanou prefix čísla, aby
    /// se soubory různých faktur nepletly a nekolidovaly (chyba 1. generace).
    /// </summary>
    internal static string ExportName(string invoiceNumber, string fileName) =>
        DocumentFileNames.MatchInvoiceNumber(fileName, new HashSet<string> { invoiceNumber }) is not null
            ? fileName
            : $"{invoiceNumber}_{fileName}";

    private static bool IsSafeRelative(string path) =>
        !Path.IsPathRooted(path) && !path.Split('\\', '/').Any(p => p is "" or "." or "..");
}
