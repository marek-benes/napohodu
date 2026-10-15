using NaPohodu.Core.Models;

namespace NaPohodu.Core.Filing;

/// <summary>
/// Sestaví plán zatřídění: soubory ze zdrojového adresáře (jen první úroveň, bez skrytých
/// a systémových) spáruje s fakturami ročníku podle názvu a určí cíl
/// <c>&lt;složka firmy&gt;\&lt;podsložka dokladu&gt;\&lt;soubor&gt;</c>. Nic nemění na disku.
/// </summary>
public static class FilingPlanner
{
    public static FilingPlan Plan(string sourceDirectory, IReadOnlyList<Invoice> invoices, CompanyFolderResolution companyFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(companyFolder);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Zdrojový adresář neexistuje: {sourceDirectory}");
        }

        var byNumber = invoices
            .Where(i => i.Number.Length > 0)
            .GroupBy(i => i.Number, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var numbers = byNumber.Keys.ToHashSet(StringComparer.Ordinal);
        var subFolders = new InvoiceSubFolders(invoices, companyFolder.Path);

        var items = new List<FilingPlanItem>();
        var unmatched = new List<string>();

        // Ordinální řazení: stejné pořadí na každém stroji bez ohledu na jazyk systému.
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(sourcePath);
            if (DocumentFileNames.IsHiddenOrSystem(fileName))
            {
                continue;
            }

            if (DocumentFileNames.MatchInvoiceNumber(fileName, numbers) is not { } number)
            {
                unmatched.Add(fileName);
                continue;
            }

            items.Add(PlanItem(sourcePath, number, byNumber[number], subFolders, companyFolder));
        }

        return new FilingPlan
        {
            SourceDirectory = sourceDirectory,
            CompanyFolder = companyFolder,
            Items = items,
            Unmatched = unmatched,
        };
    }

    private static FilingPlanItem PlanItem(
        string sourcePath,
        string number,
        List<Invoice> candidates,
        InvoiceSubFolders subFolders,
        CompanyFolderResolution companyFolder)
    {
        if (candidates.Count > 1)
        {
            return Unresolved(sourcePath, number, null, $"číslo {number} má v ročníku {candidates.Count} faktury");
        }

        var invoice = candidates[0];
        if (companyFolder.Path is not { } folder)
        {
            return Unresolved(sourcePath, number, invoice.Id, $"složka firmy není určená ({companyFolder.Source})");
        }

        var subFolder = subFolders.Resolve(invoice);
        if (subFolder.SubFolder is not { } relative)
        {
            return Unresolved(sourcePath, number, invoice.Id, subFolder.Source);
        }

        var targetPath = Path.Combine(folder, relative, Path.GetFileName(sourcePath));
        var status = !File.Exists(targetPath) ? FilingStatus.Ready
            : SameContent(sourcePath, targetPath) ? FilingStatus.AlreadyFiled
            : FilingStatus.Conflict;

        return new FilingPlanItem
        {
            SourcePath = sourcePath,
            InvoiceNumber = number,
            InvoiceId = invoice.Id,
            SubFolder = relative,
            TargetPath = targetPath,
            Status = status,
            Reason = subFolder.Source,
        };
    }

    private static FilingPlanItem Unresolved(string sourcePath, string number, int? invoiceId, string reason) =>
        new()
        {
            SourcePath = sourcePath,
            InvoiceNumber = number,
            InvoiceId = invoiceId,
            Status = FilingStatus.Unresolved,
            Reason = reason,
        };

    internal static bool SameContent(string first, string second)
    {
        var a = new FileInfo(first);
        var b = new FileInfo(second);
        if (a.Length != b.Length)
        {
            return false;
        }

        const int BufferSize = 81920;
        using var streamA = a.OpenRead();
        using var streamB = b.OpenRead();
        var bufferA = new byte[BufferSize];
        var bufferB = new byte[BufferSize];
        int read;
        while ((read = streamA.ReadAtLeast(bufferA, BufferSize, throwOnEndOfStream: false)) > 0)
        {
            streamB.ReadExactly(bufferB, 0, read);
            if (!bufferA.AsSpan(0, read).SequenceEqual(bufferB.AsSpan(0, read)))
            {
                return false;
            }
        }

        return true;
    }
}
