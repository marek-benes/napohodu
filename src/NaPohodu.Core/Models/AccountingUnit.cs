namespace NaPohodu.Core.Models;

/// <summary>
/// Účetní jednotka Pohody v jednom roce (= „zákazník“), <c>acu:itemAccountingUnitType</c>.
/// Identifikátor je <see cref="DataFile"/> (název datového souboru MDB).
/// </summary>
public sealed record AccountingUnit
{
    /// <summary>Název datového souboru (<c>acu:dataFile</c>); předává se jako <c>database</c> v INI.</summary>
    public required string DataFile { get; init; }

    public int? Year { get; init; }

    /// <summary><c>calendar</c> nebo <c>business</c>.</summary>
    public string? YearType { get; init; }

    public DateOnly? DateFrom { get; init; }

    public DateOnly? DateTo { get; init; }

    /// <summary><c>singleEntry</c>, <c>doubleEntry</c>, <c>doubleEntryNonProfit</c>, <c>doubleEntryAllowance</c>.</summary>
    public string? UnitType { get; init; }

    /// <summary><c>naturalPerson</c> nebo <c>legalPerson</c>.</summary>
    public string? StateType { get; init; }

    /// <summary>Jedinečný identifikátor účetnictví firmy (<c>acu:key</c>).</summary>
    public string? Key { get; init; }

    public string? Company { get; init; }

    /// <summary>Jméno a příjmení u fyzické osoby bez názvu firmy.</summary>
    public string? PersonName { get; init; }

    public string? Ico { get; init; }

    public string? Dic { get; init; }

    public string? Street { get; init; }

    public string? City { get; init; }

    public string? Zip { get; init; }

    /// <summary>Název pro zobrazení: firma, jinak jméno osoby, jinak datový soubor.</summary>
    public string DisplayName => Company ?? PersonName ?? DataFile;

    /// <summary>Identita pro <c>dat:dataPack</c> (IČ nebo key); <c>null</c>, pokud Pohoda nevrátila ani jedno.</summary>
    public Pohoda.UnitIdentity? Identity =>
        string.IsNullOrWhiteSpace(Ico) && string.IsNullOrWhiteSpace(Key) ? null : new Pohoda.UnitIdentity(Ico, Key);

    /// <summary>
    /// Shoda s názvem datového souboru zadaným uživatelem: bez ohledu na velikost písmen
    /// a s příponou <c>.mdb</c> i bez ní (Pohoda vrací <c>08215839_2026.mdb</c>).
    /// </summary>
    public bool MatchesDataFile(string dataFile) =>
        string.Equals(WithoutMdbExtension(DataFile), WithoutMdbExtension(dataFile.Trim()), StringComparison.OrdinalIgnoreCase);

    private static string WithoutMdbExtension(string dataFile) =>
        dataFile.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase) ? dataFile[..^4] : dataFile;
}
