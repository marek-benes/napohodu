using NaPohodu.Core.Mdb;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Filing;

namespace NaPohodu.Tests.Mdb;

public sealed class DocDirsTests : IDisposable
{
    private const string Received = @"Fakturace\Přijaté faktury";

    private readonly TempDirectory _temp = new();
    private readonly string _mdb;
    private readonly string _backups;

    public DocDirsTests()
    {
        _mdb = TestMdb.Create(Path.Combine(_temp.Path, "data", "90000010_2025.mdb"));
        _backups = Path.Combine(_temp.Path, "zalohy");
        TestMdb.AddInvoice(_mdb, 101, "251100001");
        TestMdb.AddInvoice(_mdb, 102, "251100002");
        TestMdb.AddInvoice(_mdb, 103, "251100003");
        TestMdb.AddInvoice(_mdb, 104, "251100004");
        TestMdb.AddInvoice(_mdb, 105, "251100005");
        TestMdb.AddInvoice(_mdb, 201, "250100001", type: 1);           // vydaná faktura
        TestMdb.AddDocDir(_mdb, 102, $@"{Received}\2511\251100002");    // už poznačeno
        TestMdb.AddDocDir(_mdb, 103, null);                             // prázdný Dir
        TestMdb.AddDocDir(_mdb, 104, $@"{Received}\JINAM\251100004");   // jiný Dir
        TestMdb.AddDocDir(_mdb, 201, @"Fakturace\Vydané faktury\250100001", agenda: 2);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void PathFor_AddsExtensionAndRejectsInvalidNames()
    {
        Assert.Equal(@"C:\Data\90000010_2025.mdb", PohodaDataFile.PathFor(@"C:\Data", "90000010_2025"));
        Assert.Equal(@"C:\Data\90000010_2025.mdb", PohodaDataFile.PathFor(@"C:\Data", "90000010_2025.mdb"));
        Assert.Throws<PohodaException>(() => PohodaDataFile.PathFor(@"C:\Data", @"..\x|y.mdb"));
    }

    [Fact]
    public void Snapshot_ReadsOnlyReceivedInvoicesAndTheirDocDirs()
    {
        var snapshot = DocDirsSnapshot.Read(_mdb);

        Assert.Equal([101, 102, 103, 104, 105], snapshot.Invoices.Select(i => i.Id).Order());
        Assert.Equal(3, snapshot.DocDirs.Count);
        Assert.Null(snapshot.DocDirs.Single(d => d.RelId == 103).Dir);
        Assert.Null(PohodaDataFile.FindLock(_mdb));
    }

    [Fact]
    public void Plan_ClassifiesTargets()
    {
        var plan = Plan(
            Target(101, "2511"),
            Target(101, "2511"),                                           // druhý soubor téže faktury
            new DocDirTarget(102, "251100002", $@"{Received}\2511\251100002".ToUpperInvariant()),
            Target(103, "2511"),
            Target(104, "2511"),
            Target(999, "2511"));

        Assert.Equal(
            [DocDirAction.Insert, DocDirAction.AlreadyMarked, DocDirAction.Update, DocDirAction.Conflict, DocDirAction.MissingInvoice],
            plan.Items.Select(i => i.Action));
        Assert.Equal($@"{Received}\JINAM\251100004", plan.Items.Single(i => i.Action == DocDirAction.Conflict).CurrentDir);
    }

    [Fact]
    public void Apply_InsertsAndUpdatesWithoutTouchingOthers()
    {
        var plan = Plan(Target(101, "2511"), Target(103, "2511"), Target(104, "2511"), Target(105, "2511"));

        var result = DocDirsWriter.Apply(plan, _backups, backupsToKeep: 5);

        Assert.Equal((2, 1), (result.Inserted, result.Updated));
        Assert.Empty(result.Changed);
        Assert.True(File.Exists(result.BackupPath));
        Assert.StartsWith(_backups, result.BackupPath, StringComparison.OrdinalIgnoreCase);

        var rows = TestMdb.DocDirs(_mdb);
        Assert.Equal($@"{Received}\2511\251100001", rows.Single(r => r.RelId == 101).Dir);
        Assert.Equal($@"{Received}\2511\251100003", rows.Single(r => r.RelId == 103).Dir);
        Assert.Equal($@"{Received}\JINAM\251100004", rows.Single(r => r.RelId == 104).Dir);
        Assert.Equal(3, rows.Single(r => r.RelId == 105).RelAgId);
        Assert.True(rows.Single(r => r.RelId == 105).Id > rows.Single(r => r.RelId == 201).Id);   // AutoNumber
        Assert.Null(PohodaDataFile.FindLock(_mdb));
    }

    [Fact]
    public void Apply_IsSafeToRepeat()
    {
        DocDirsWriter.Apply(Plan(Target(101, "2511")), _backups, 5);

        var second = Plan(Target(101, "2511"));

        Assert.Equal(DocDirAction.AlreadyMarked, Assert.Single(second.Items).Action);
        Assert.Equal(new DocDirsWriteResult(0, 0, null, []), DocDirsWriter.Apply(second, _backups, 5) with { Changed = [] });
    }

    [Fact]
    public void Apply_RowChangedAfterPlanning_IsSkipped()
    {
        var plan = Plan(Target(101, "2511"), Target(103, "2511"));
        TestMdb.AddDocDir(_mdb, 101, $@"{Received}\mezitím\251100001");
        TestMdb.Execute(_mdb, "UPDATE [DocDirs] SET Dir = 'mezitím' WHERE RelID = 103");

        var result = DocDirsWriter.Apply(plan, _backups, 5);

        Assert.Equal((0, 0, 2), (result.Inserted, result.Updated, result.Changed.Count));
        Assert.Equal("mezitím", TestMdb.DocDirs(_mdb).Single(r => r.RelId == 103).Dir);
    }

    [Fact]
    public void Apply_OpenedDataFile_IsRefusedBeforeBackup()
    {
        // Plán (sdílené čtení) dřív než zámek: Jet by po čtení „svůj“ .ldb smazal.
        var plan = Plan(Target(101, "2511"));
        File.WriteAllText(Path.ChangeExtension(_mdb, ".ldb"), "zámek");

        var ex = Assert.Throws<PohodaException>(() => DocDirsWriter.Apply(plan, _backups, 5));

        Assert.Contains("otevřený", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_backups));
        Assert.Equal(4, TestMdb.DocDirs(_mdb).Count);
    }

    [Fact]
    public void Apply_BackupInsideDataFolder_IsRefused()
    {
        var inside = Path.Combine(Path.GetDirectoryName(_mdb)!, "zalohy");

        Assert.Throws<PohodaException>(() => DocDirsWriter.Apply(Plan(Target(101, "2511")), inside, 5));
    }

    [Fact]
    public void Backup_KeepsOnlyNewest()
    {
        Directory.CreateDirectory(_backups);
        foreach (var stamp in new[] { "20260101-080000", "20260102-080000", "20260103-080000" })
        {
            File.WriteAllText(Path.Combine(_backups, $"90000010_2025.{stamp}.mdb"), "stará");
        }

        File.WriteAllText(Path.Combine(_backups, "90000010_2026.20260101-080000.mdb"), "jiný ročník");

        var backup = DocDirsWriter.Backup(_mdb, _backups, backupsToKeep: 2);

        var remaining = Directory.EnumerateFiles(_backups).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["90000010_2025.20260103-080000.mdb", Path.GetFileName(backup), "90000010_2026.20260101-080000.mdb"], remaining);
    }

    private DocDirsPlan Plan(params DocDirTarget[] targets) =>
        DocDirsPlanner.Plan(_mdb, DocDirsSnapshot.Read(_mdb), targets);

    private static DocDirTarget Target(int id, string series)
    {
        var number = $"2511000{id - 100:00}";
        return new DocDirTarget(id, number, $@"{Received}\{series}\{number}");
    }
}
