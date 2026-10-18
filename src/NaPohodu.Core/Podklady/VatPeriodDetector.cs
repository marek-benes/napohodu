using System.Globalization;
using System.Text.RegularExpressions;

namespace NaPohodu.Core.Podklady;

/// <summary>Periodicita zjištěná mimo Šanony a odkud se vzala (pro výpis uživateli).</summary>
public sealed record DetectedVatPeriod(VatPeriod Period, string Source);

/// <summary>
/// Periodicita DPH podle podaných přiznání v Dokumentech firmy: Pohoda ukládá podání jako
/// <c>DPHDP-&lt;IČO&gt;-&lt;rok&gt;-&lt;období&gt;_&lt;druh&gt;_&lt;datum&gt;_Veta.xml</c> do <c>ELvykazy\DPH</c> a odeslané
/// zprávy do <c>ISDS\&lt;schránka&gt;\&lt;id&gt;</c>. Období je název měsíce (<c>zari</c>) nebo čtvrtletí (<c>3q</c>),
/// ověřeno 2026-10-18. Kontrolní hlášení (<c>DPHKH</c>) se nepoužívá: právnické osoby ho podávají měsíčně vždy.
/// </summary>
public static partial class VatPeriodDetector
{
    private static readonly string[] MonthNames =
        ["leden", "unor", "brezen", "duben", "kveten", "cerven", "cervenec", "srpen", "zari", "rijen", "listopad", "prosinec"];

    private static readonly string[] SearchedFolders = ["ELvykazy", "ISDS"];

    /// <summary>
    /// Periodicita podle posledního přiznání za rok nejvýše <paramref name="year"/> (periodicita se může
    /// měnit), jinak podle posledního vůbec; <c>null</c>, když firma žádné přiznání v Dokumentech nemá.
    /// </summary>
    public static DetectedVatPeriod? FromFilings(string companyFolder, int year)
    {
        var filings = SearchedFolders
            .Select(f => Path.Combine(companyFolder, f))
            .Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "DPHDP-*", SearchOption.AllDirectories))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(Parse)
            .OfType<Filing>()
            .ToList();

        var latest = filings.Where(f => f.Year <= year).DefaultIfEmpty().Max()
            ?? filings.DefaultIfEmpty().Max();
        return latest is null ? null : new DetectedVatPeriod(latest.Period, $"podle přiznání {latest.FileName}");
    }

    internal static Filing? Parse(string fileName)
    {
        var match = FilingName().Match(fileName);
        if (!match.Success)
        {
            return null;
        }

        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        var period = match.Groups["period"].Value.ToLowerInvariant();
        if (period.Length == 2 && period[1] == 'q' && period[0] is >= '1' and <= '4')
        {
            return new Filing(fileName, year, VatPeriod.Quarterly, (period[0] - '0') * 3);
        }

        var month = Array.IndexOf(MonthNames, period);
        return month < 0 ? null : new Filing(fileName, year, VatPeriod.Monthly, month + 1);
    }

    /// <summary>Jedno podání; pořadí podle roku a konce období (měsíc, u čtvrtletí jeho poslední měsíc).</summary>
    internal sealed record Filing(string FileName, int Year, VatPeriod Period, int EndMonth) : IComparable<Filing>
    {
        public int CompareTo(Filing? other) =>
            other is null ? 1
            : Year != other.Year ? Year.CompareTo(other.Year)
            : EndMonth != other.EndMonth ? EndMonth.CompareTo(other.EndMonth)
            : string.CompareOrdinal(FileName, other.FileName);
    }

    [GeneratedRegex(@"^DPHDP-\d+-(?<year>\d{4})-(?<period>[a-z0-9]+)_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilingName();
}
