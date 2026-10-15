namespace NaPohodu.Tests.Filing;

/// <summary>Dočasný adresář pro testy se soubory; po testu se smaže.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Path = Directory.CreateTempSubdirectory("napohodu-tests-").FullName;

    public string Path { get; }

    public string Dir(params string[] parts)
    {
        var path = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    public string File(string content, params string[] parts)
    {
        var path = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => DeleteWithRetry(Path);

    /// <summary>Smaže adresář; ukončený proces (např. ping v testu timeoutu) může soubory chvíli držet.</summary>
    public static void DeleteWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(200);
            }
        }
    }
}
