namespace NaPohodu.App;

/// <summary>
/// Identita běžící instalace. Na jednom počítači a jednom účtu běží produkce (zástupce na ploše,
/// výchozí hodnoty) a vývoj z repozitáře (<c>dotnet run</c>, nastaví <c>Properties/launchSettings.json</c>).
/// Liší se názvem → vlastní datová složka (zálohy MDB), port a zámek „jen jedna kopie“, takže se nemíchají.
/// </summary>
internal sealed class InstanceOptions
{
    public const string SectionName = "Instance";
    public const string ProductionName = "NaPohodu";

    /// <summary>Název instance; určuje datovou složku a zámek jedné kopie.</summary>
    public string Name { get; set; } = ProductionName;

    /// <summary>Port webového UI na 127.0.0.1.</summary>
    public int Port { get; set; } = 5080;

    /// <summary>Vypnutí po zavření posledního okna (a bez rozběhnuté operace); nula = nikdy.</summary>
    public TimeSpan IdleShutdown { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Data z Pohody starší než tohle se při otevření stránky načtou znovu (aplikace může běžet dlouho).</summary>
    public TimeSpan CacheMaxAge { get; set; } = TimeSpan.FromHours(4);

    public bool IsProduction => string.Equals(Name, ProductionName, StringComparison.Ordinal);

    /// <summary><c>%LOCALAPPDATA%\&lt;Name&gt;</c> – data instance (zálohy MDB), mimo složku programu i datovou složku Pohody.</summary>
    public string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);

    public string Url => $"http://127.0.0.1:{Port}";
}
