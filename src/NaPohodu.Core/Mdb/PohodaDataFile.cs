using System.Data.OleDb;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.Core.Mdb;

/// <summary>
/// Datový soubor jednotky (<c>&lt;dataPath&gt;\&lt;dataFile&gt;</c>, jeden na jednotku a rok) a přístup přes
/// Jet 4.0 OLEDB (jen 32bit proces). Čtení sdíleně (smí běžet i při otevřené Pohodě),
/// zápis jen exkluzivně a jen když jednotku nikdo nemá otevřenou.
/// </summary>
public static class PohodaDataFile
{
    private const string Provider = "Microsoft.Jet.OLEDB.4.0";

    /// <param name="dataFile">Název z <c>acu:dataFile</c> (včetně <c>.mdb</c>; bez přípony se doplní).</param>
    public static string PathFor(string dataPath, string dataFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFile);

        var fileName = dataFile.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase) ? dataFile : dataFile + ".mdb";
        if (fileName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new PohodaException($"Neplatný název datového souboru: {dataFile}");
        }

        return System.IO.Path.Combine(dataPath, fileName);
    }

    /// <summary>
    /// Zámek vedle MDB (<c>.ldb</c>) znamená, že soubor má někdo otevřený (Pohoda, i na jiné stanici).
    /// Vrací cestu k zámku, nebo <c>null</c>.
    /// </summary>
    public static string? FindLock(string mdbPath)
    {
        var basePath = System.IO.Path.ChangeExtension(mdbPath, null);
        return new[] { basePath + ".ldb", basePath + ".laccdb" }.FirstOrDefault(File.Exists);
    }

    internal static OleDbConnection OpenShared(string mdbPath) => Open(mdbPath, exclusive: false);

    internal static OleDbConnection OpenExclusive(string mdbPath) => Open(mdbPath, exclusive: true);

    private static OleDbConnection Open(string mdbPath, bool exclusive)
    {
        if (!File.Exists(mdbPath))
        {
            throw new PohodaException($"Datový soubor nebyl nalezen: {mdbPath}");
        }

        if (Environment.Is64BitProcess)
        {
            throw new PohodaException("Přístup k MDB vyžaduje 32bit proces (Jet 4.0 OLEDB).");
        }

        // Bez OLE DB poolingu (-4): zavřené spojení jinak zůstane chvíli otevřené v poolu i se zámkem .ldb,
        // a kontrola „jednotku má někdo otevřenou“ by před zápisem našla vlastní zámek z předchozího čtení.
        var builder = new OleDbConnectionStringBuilder { Provider = Provider, DataSource = mdbPath, OleDbServices = -4 };
        builder["Mode"] = exclusive ? "Share Exclusive" : "Read";
        var connection = new OleDbConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch (OleDbException ex)
        {
            connection.Dispose();
            throw new PohodaException(
                exclusive
                    ? $"Datový soubor nelze otevřít exkluzivně (nejspíš je otevřený v Pohodě): {ex.Message}"
                    : $"Datový soubor nelze otevřít: {ex.Message}",
                ex);
        }
    }
}
