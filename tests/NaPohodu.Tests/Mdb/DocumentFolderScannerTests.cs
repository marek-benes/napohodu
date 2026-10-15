using NaPohodu.Core.Filing;
using NaPohodu.Core.Mdb;
using NaPohodu.Tests.Filing;

namespace NaPohodu.Tests.Mdb;

public sealed class DocumentFolderScannerTests : IDisposable
{
    private const string Received = @"Fakturace\Přijaté faktury";

    private readonly TempDirectory _company = new();

    public void Dispose() => _company.Dispose();

    [Fact]
    public void Scan_FindsFlatAndPrefixedFoldersAndDuplicates()
    {
        // Stav převzatý z reálné složky: 251100001 je na disku plochá i pod prefixem.
        _company.File("x", "Fakturace", "Přijaté faktury", "251100001", "251100001.pdf");
        _company.File("x", "Fakturace", "Přijaté faktury", "2511", "251100001", "251100001.pdf");
        _company.File("x", "Fakturace", "Přijaté faktury", "2511", "251100002", "k251100002.pdf");
        _company.File("x", "Fakturace", "Přijaté faktury", "2511", "251100003", "příloha", "sken.pdf");
        _company.File("x", "Fakturace", "Přijaté faktury", "2511", "251100009", "desktop.ini");   // jen systémový soubor
        _company.Dir("Fakturace", "Přijaté faktury", "2511", "251100010");                       // prázdná

        var scan = DocumentFolderScanner.Scan(_company.Path);

        Assert.Equal(["251100001"], scan.Duplicates);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["251100002"] = $@"{Received}\2511\251100002",
                ["251100003"] = $@"{Received}\2511\251100003",
            },
            scan.Folders);
    }

    [Fact]
    public void Scan_MissingReceivedFolder_IsEmpty()
    {
        var scan = DocumentFolderScanner.Scan(_company.Path);

        Assert.Empty(scan.Folders);
    }

    [Fact]
    public void Targets_FromDisk_MapsNumbersToInvoicesOfThisYear()
    {
        var scan = new DocumentFolderScan(
            new Dictionary<string, string> { ["251100002"] = "a", ["241100001"] = "b", ["251100007"] = "c" },
            []);
        var snapshot = new DocDirsSnapshot(
            [new MdbInvoice(2, "251100002"), new MdbInvoice(7, "251100007"), new MdbInvoice(8, "251100007")],
            []);

        var targets = DocDirsTargets.FromDisk(scan, snapshot, out var notInDataFile);

        Assert.Equal([new DocDirTarget(2, "251100002", "a")], targets);
        Assert.Equal(["241100001", "251100007"], notInDataFile);   // 251100007 je v MDB dvakrát → nejednoznačné
    }

    [Fact]
    public void Targets_FromFiling_TakesFilesThatEndedInTheirFolder()
    {
        FilingResultItem Result(FilingOutcome outcome, FilingStatus status, int? id) =>
            new(new FilingPlanItem
            {
                SourcePath = $@"C:\zdroj\{id}.pdf",
                InvoiceNumber = $"{id}",
                InvoiceId = id,
                SubFolder = $@"{Received}\{id}",
                Status = status,
                Reason = "test",
            }, outcome, null, "test");

        var targets = DocDirsTargets.FromFiling(
        [
            Result(FilingOutcome.Moved, FilingStatus.Ready, 1),
            Result(FilingOutcome.MovedRenamed, FilingStatus.Conflict, 2),
            Result(FilingOutcome.Skipped, FilingStatus.AlreadyFiled, 3),
            Result(FilingOutcome.Skipped, FilingStatus.Conflict, 4),
            Result(FilingOutcome.Failed, FilingStatus.Ready, 5),
        ]);

        Assert.Equal([1, 2, 3], targets.Select(t => t.InvoiceId));
    }
}
