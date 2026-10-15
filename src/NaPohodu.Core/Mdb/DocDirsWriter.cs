using System.Data.OleDb;
using System.Globalization;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.Core.Mdb;

/// <param name="BackupPath">Záloha MDB pořízená před zápisem; <c>null</c>, když nebylo co zapsat.</param>
/// <param name="Changed">Vazby, které se mezi plánem a zápisem změnily a byly přeskočeny.</param>
public sealed record DocDirsWriteResult(int Inserted, int Updated, string? BackupPath, IReadOnlyList<string> Changed);

/// <summary>
/// Zapíše plán do <c>DocDirs</c>: zámek (<c>.ldb</c>) neexistuje → záloha mimo datovou složku Pohody
/// (s úklidem starých) → exkluzivní otevření → jedna transakce. Uvnitř transakce se stav znovu ověří:
/// INSERT jen když faktura stále nemá řádek, UPDATE jen řádku s prázdným Dir. Nic existujícího se nepřepíše.
/// Nové <c>DocDirs.ID</c> přidělí AutoNumber (ověřeno na schématu reálné MDB).
/// </summary>
public static class DocDirsWriter
{
    private const string BackupTimestampFormat = "yyyyMMdd-HHmmss";

    public static DocDirsWriteResult Apply(DocDirsPlan plan, string backupDirectory, int backupsToKeep)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        var writes = plan.Writes.ToList();
        if (writes.Count == 0)
        {
            return new DocDirsWriteResult(0, 0, null, []);
        }

        if (PohodaDataFile.FindLock(plan.MdbPath) is { } lockFile)
        {
            throw new PohodaException(
                $"Datový soubor má někdo otevřený (zámek {Path.GetFileName(lockFile)}). Zavřete účetní jednotku v Pohodě na všech stanicích a zkuste to znovu.");
        }

        if (IsUnder(backupDirectory, Path.GetDirectoryName(plan.MdbPath)!))
        {
            throw new PohodaException("Zálohy MDB nesmí být v datové složce Pohody (Pohoda:BackupPath).");
        }

        var backupPath = Backup(plan.MdbPath, backupDirectory, backupsToKeep);

        using var connection = PohodaDataFile.OpenExclusive(plan.MdbPath);
        using var transaction = connection.BeginTransaction();
        var inserted = 0;
        var updated = 0;
        var changed = new List<string>();
        try
        {
            foreach (var item in writes)
            {
                var done = item.Action == DocDirAction.Insert
                    ? Insert(connection, transaction, item.Target)
                    : Update(connection, transaction, item);
                if (done)
                {
                    _ = item.Action == DocDirAction.Insert ? inserted++ : updated++;
                }
                else
                {
                    changed.Add($"{item.Target.InvoiceNumber}: vazba se mezitím změnila, přeskočeno");
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return new DocDirsWriteResult(inserted, updated, backupPath, changed);
    }

    private static bool Insert(OleDbConnection connection, OleDbTransaction transaction, DocDirTarget target)
    {
        using (var check = new OleDbCommand("SELECT COUNT(*) FROM [DocDirs] WHERE RelAgID = ? AND RelID = ?", connection, transaction))
        {
            check.Parameters.AddWithValue("RelAgID", DocDirsSnapshot.ReceivedInvoicesAgenda);
            check.Parameters.AddWithValue("RelID", target.InvoiceId);
            if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                return false;
            }
        }

        using var insert = new OleDbCommand("INSERT INTO [DocDirs] (RelAgID, RelID, Dir) VALUES (?, ?, ?)", connection, transaction);
        insert.Parameters.AddWithValue("RelAgID", DocDirsSnapshot.ReceivedInvoicesAgenda);
        insert.Parameters.AddWithValue("RelID", target.InvoiceId);
        insert.Parameters.Add("Dir", OleDbType.VarWChar, 255).Value = target.Dir;
        return insert.ExecuteNonQuery() == 1;
    }

    private static bool Update(OleDbConnection connection, OleDbTransaction transaction, DocDirsPlanItem item)
    {
        using var update = new OleDbCommand(
            "UPDATE [DocDirs] SET Dir = ? WHERE ID = ? AND RelAgID = ? AND RelID = ? AND (Dir IS NULL OR Dir = '')",
            connection,
            transaction);
        update.Parameters.Add("Dir", OleDbType.VarWChar, 255).Value = item.Target.Dir;
        update.Parameters.AddWithValue("ID", item.DocDirId!.Value);
        update.Parameters.AddWithValue("RelAgID", DocDirsSnapshot.ReceivedInvoicesAgenda);
        update.Parameters.AddWithValue("RelID", item.Target.InvoiceId);
        return update.ExecuteNonQuery() == 1;
    }

    internal static string Backup(string mdbPath, string backupDirectory, int backupsToKeep)
    {
        Directory.CreateDirectory(backupDirectory);
        var name = Path.GetFileNameWithoutExtension(mdbPath);
        var stamp = DateTime.Now.ToString(BackupTimestampFormat, CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"{name}.{stamp}.mdb");
        File.Copy(mdbPath, backupPath, overwrite: false);

        // Úklid: nejstarší zálohy téhož datového souboru nad limit.
        var old = Directory.EnumerateFiles(backupDirectory, $"{name}.*.mdb")
            .Where(f => DateTime.TryParseExact(
                Path.GetFileNameWithoutExtension(f)[(name.Length + 1)..], BackupTimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Order(StringComparer.Ordinal)
            .SkipLast(Math.Max(backupsToKeep, 1));
        foreach (var file in old)
        {
            File.Delete(file);
        }

        return backupPath;
    }

    private static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        var dir = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
        return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }
}
