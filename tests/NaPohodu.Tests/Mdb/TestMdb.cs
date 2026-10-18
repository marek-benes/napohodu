using System.Data.OleDb;
using System.Runtime.InteropServices;

namespace NaPohodu.Tests.Mdb;

/// <summary>
/// Testovací MDB se schématem jako v Pohodě (ověřeno 2026-10-15 na reálném datovém souboru):
/// <c>FA(ID AutoNumber, RelTpFak, Cislo TEXT(32))</c>, <c>DocDirs(ID AutoNumber, RelAgID, RelSubID, RelID, Dir TEXT(255))</c>.
/// </summary>
internal static class TestMdb
{
    // Bez OLE DB poolingu jako v produkčním kódu, aby po zavření nezůstal zámek .ldb.
    private const string Provider = "Provider=Microsoft.Jet.OLEDB.4.0;OLE DB Services=-4;Data Source=";

    public static string Create(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var catalogType = Type.GetTypeFromProgID("ADOX.Catalog") ?? throw new InvalidOperationException("ADOX není k dispozici.");
        dynamic catalog = Activator.CreateInstance(catalogType)!;
        try
        {
            // ADOX při vytvoření databáze „OLE DB Services“ nepřijme, proto vlastní řetězec.
            dynamic connection = catalog.Create("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + path + ";Jet OLEDB:Engine Type=5");
            connection.Close();
        }
        finally
        {
            Marshal.FinalReleaseComObject(catalog);
        }

        Execute(path, "CREATE TABLE [FA] (ID COUNTER PRIMARY KEY, RelTpFak INTEGER, Cislo TEXT(32))");
        Execute(path, "CREATE TABLE [DocDirs] (ID COUNTER PRIMARY KEY, RelAgID INTEGER, RelSubID INTEGER, RelID INTEGER, Dir TEXT(255))");
        return path;
    }

    public static void AddInvoice(string path, int id, string number, int type = 11) =>
        Execute(path, "INSERT INTO [FA] (ID, RelTpFak, Cislo) VALUES (?, ?, ?)", id, type, number);

    public static void AddDocDir(string path, int relId, string? dir, int agenda = 3) =>
        Execute(path, "INSERT INTO [DocDirs] (RelAgID, RelID, Dir) VALUES (?, ?, ?)", agenda, relId, (object?)dir ?? DBNull.Value);

    public static List<(int Id, int RelAgId, int RelId, string? Dir)> DocDirs(string path)
    {
        using var connection = Open(path);
        using var command = new OleDbCommand("SELECT ID, RelAgID, RelID, Dir FROM [DocDirs] ORDER BY ID", connection);
        using var reader = command.ExecuteReader();
        var rows = new List<(int, int, int, string?)>();
        while (reader.Read())
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    public static void Execute(string path, string sql, params object[] parameters)
    {
        using var connection = Open(path);
        using var command = new OleDbCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new OleDbParameter { Value = parameter });
        }

        command.ExecuteNonQuery();
    }

    private static OleDbConnection Open(string path)
    {
        var connection = new OleDbConnection(Provider + path);
        connection.Open();
        return connection;
    }
}
