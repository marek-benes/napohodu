using System.Data.OleDb;

namespace NaPohodu.Core.Mdb;

/// <summary>Přijatá faktura v MDB (<c>FA</c> s <c>RelTpFak = 11</c>).</summary>
public sealed record MdbInvoice(int Id, string Number);

/// <summary>Řádek vazby doklad → adresář (<c>DocDirs</c>); <see cref="Dir"/> je relativně ke složce firmy, může být prázdný.</summary>
public sealed record DocDirRow(int Id, int RelId, string? Dir);

/// <summary>
/// Stav přijatých faktur a jejich <c>DocDirs</c> v jednom datovém souboru.
/// Konstanty ověřené na reálných datech (docs/pohoda/mdb/NOTES.md, 2026-10-15).
/// </summary>
public sealed record DocDirsSnapshot(IReadOnlyList<MdbInvoice> Invoices, IReadOnlyList<DocDirRow> DocDirs)
{
    /// <summary><c>DocDirs.RelAgID</c> agendy přijatých faktur.</summary>
    public const int ReceivedInvoicesAgenda = 3;

    /// <summary><c>FA.RelTpFak</c> přijaté faktury (dobropisy a zálohové mají jiné hodnoty, zatím neřešeno).</summary>
    public const int ReceivedInvoiceType = 11;

    /// <summary>Přečte přijaté faktury a jejich DocDirs. Sdíleně, jen čtení; smí běžet i při otevřené Pohodě.</summary>
    public static DocDirsSnapshot Read(string mdbPath)
    {
        using var connection = PohodaDataFile.OpenShared(mdbPath);
        return Read(connection, transaction: null);
    }

    internal static DocDirsSnapshot Read(OleDbConnection connection, OleDbTransaction? transaction)
    {
        var invoices = new List<MdbInvoice>();
        using (var command = new OleDbCommand("SELECT ID, Cislo FROM [FA] WHERE RelTpFak = ?", connection, transaction))
        {
            command.Parameters.AddWithValue("RelTpFak", ReceivedInvoiceType);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var number = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                invoices.Add(new MdbInvoice(reader.GetInt32(0), number));
            }
        }

        var docDirs = new List<DocDirRow>();
        using (var command = new OleDbCommand("SELECT ID, RelID, Dir FROM [DocDirs] WHERE RelAgID = ?", connection, transaction))
        {
            command.Parameters.AddWithValue("RelAgID", ReceivedInvoicesAgenda);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(1))
                {
                    continue;
                }

                var dir = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                docDirs.Add(new DocDirRow(reader.GetInt32(0), reader.GetInt32(1), string.IsNullOrEmpty(dir) ? null : dir));
            }
        }

        return new DocDirsSnapshot(invoices, docDirs);
    }
}
