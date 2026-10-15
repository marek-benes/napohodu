namespace NaPohodu.Core.Configuration;

/// <summary>
/// Nastavení přístupu k Pohodě. Heslo patří jen do lokální konfigurace mimo git
/// (<c>appsettings.Local.json</c> nebo proměnná prostředí) a nikdy se neloguje.
/// </summary>
public sealed class PohodaOptions
{
    public const string SectionName = "Pohoda";

    /// <summary>Cesta k <c>Pohoda.exe</c>.</summary>
    public string ExePath { get; set; } = string.Empty;

    /// <summary>Složka s datovými soubory jednotek (<c>&lt;dataFile&gt;.mdb</c>).</summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>Kořen Dokumentů, ve kterém má každá firma svou složku.</summary>
    public string DocumentsPath { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Nejdelší povolená doba jednoho běhu <c>Pohoda.exe /XML</c>; potom se proces ukončí.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Složka pro zálohy MDB před zápisem. Nikdy datová složka Pohody (1. generace tam zálohy hromadila).
    /// Prázdné = <c>%LOCALAPPDATA%\NaPohodu\Zalohy</c>.
    /// </summary>
    public string BackupPath { get; set; } = string.Empty;

    /// <summary>Kolik posledních záloh jednoho datového souboru ponechat; starší se mažou.</summary>
    public int BackupsToKeep { get; set; } = 10;

    public string EffectiveBackupPath =>
        string.IsNullOrWhiteSpace(BackupPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NaPohodu", "Zalohy")
            : BackupPath;
}
