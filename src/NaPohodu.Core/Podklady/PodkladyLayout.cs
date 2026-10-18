using System.Globalization;
using NaPohodu.Core.Filing;

namespace NaPohodu.Core.Podklady;

/// <summary>Periodicita DPH zákazníka; určuje období v Šanonech (01…12, nebo Q1…Q4).</summary>
public enum VatPeriod
{
    Monthly,
    Quarterly,
}

/// <summary>
/// Struktura složky zákazníka v Podkladech (<c>docs/podklady.md</c>):
/// <c>Ke zpracování\Vráceno</c> a <c>Šanony\&lt;RRRR&gt;\&lt;období&gt;\&lt;agenda&gt;</c>, <c>Mzdy\&lt;MM&gt;</c>,
/// <c>Mzdy\Mzdové listy</c>, <c>Závěrka</c>, <c>Dokumenty</c>. Periodicita se nikde neukládá:
/// pozná se podle založených období.
/// </summary>
public static class PodkladyLayout
{
    public const string Returned = "Vráceno";
    public const string Binders = "Šanony";
    public const string Archive = "Archiv";
    public const string Payroll = "Mzdy";
    public const string PayrollSheets = "Mzdové listy";
    public const string YearEnd = "Závěrka";
    public const string Documents = "Dokumenty";

    public static readonly IReadOnlyList<string> PeriodAgendas = ["Přijaté faktury", "Vydané faktury", "Banka", "Pokladna", "DPH"];

    public static readonly IReadOnlyList<string> Months =
        [.. Enumerable.Range(1, 12).Select(m => m.ToString("00", CultureInfo.InvariantCulture))];

    public static readonly IReadOnlyList<string> Quarters = ["Q1", "Q2", "Q3", "Q4"];

    public static IReadOnlyList<string> Periods(VatPeriod period) => period == VatPeriod.Monthly ? Months : Quarters;

    /// <summary>Podsložky jednoho roku relativně k <c>Šanony\&lt;RRRR&gt;</c>, rodiče před dětmi.</summary>
    public static IEnumerable<string> YearFolders(VatPeriod period)
    {
        foreach (var p in Periods(period))
        {
            yield return p;
            foreach (var agenda in PeriodAgendas)
            {
                yield return Path.Combine(p, agenda);
            }
        }

        yield return Payroll;
        foreach (var month in Months)
        {
            yield return Path.Combine(Payroll, month);
        }

        yield return Path.Combine(Payroll, PayrollSheets);
        yield return YearEnd;
        yield return Documents;
    }

    /// <summary>
    /// Periodicita založeného roku podle názvů období; <c>null</c>, když rok neexistuje, nemá období,
    /// nebo má obojí (to hlásí <see cref="Nonconforming"/>).
    /// </summary>
    public static VatPeriod? DetectPeriod(string yearPath)
    {
        if (!Directory.Exists(yearPath))
        {
            return null;
        }

        var names = ChildDirectories(yearPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var monthly = Months.Any(names.Contains);
        var quarterly = Quarters.Any(names.Contains);
        return monthly == quarterly ? null : monthly ? VatPeriod.Monthly : VatPeriod.Quarterly;
    }

    /// <summary>
    /// Položky Šanonů, které do struktury nepatří (relativně k Šanonům): neznámé složky, volné soubory
    /// mimo listové složky, rok s měsíci i čtvrtletími. Obsah listových složek (agendy, měsíce mezd,
    /// Mzdové listy, Závěrka, Dokumenty) se nekontroluje. Skryté a systémové soubory se ignorují.
    /// </summary>
    public static IReadOnlyList<string> Nonconforming(string bindersPath)
    {
        var found = new List<string>();
        if (!Directory.Exists(bindersPath))
        {
            return found;
        }

        AddFiles(bindersPath, string.Empty, found);
        foreach (var year in ChildDirectories(bindersPath))
        {
            if (year.Length != 4 || !year.All(char.IsAsciiDigit))
            {
                found.Add(year + "\\");
                continue;
            }

            var yearPath = Path.Combine(bindersPath, year);
            AddFiles(yearPath, year, found);
            var children = ChildDirectories(yearPath).ToList();
            if (children.Any(Months.Contains) && children.Any(Quarters.Contains))
            {
                found.Add($"{year}\\ (měsíce i čtvrtletí)");
            }

            foreach (var child in children)
            {
                var childPath = Path.Combine(yearPath, child);
                var relative = Path.Combine(year, child);
                if (Months.Contains(child) || Quarters.Contains(child))
                {
                    CheckChildren(childPath, relative, PeriodAgendas, found);
                }
                else if (child == Payroll)
                {
                    CheckChildren(childPath, relative, [.. Months, PayrollSheets], found);
                }
                else if (child is not (YearEnd or Documents))
                {
                    found.Add(relative + "\\");
                }
            }
        }

        return found;
    }

    private static void CheckChildren(string path, string relative, IReadOnlyList<string> allowed, List<string> found)
    {
        AddFiles(path, relative, found);
        found.AddRange(ChildDirectories(path).Where(d => !allowed.Contains(d)).Select(d => Path.Combine(relative, d) + "\\"));
    }

    private static void AddFiles(string path, string relative, List<string> found) =>
        found.AddRange(Directory.EnumerateFiles(path)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(f => !DocumentFileNames.IsHiddenOrSystem(f))
            .Select(f => Path.Combine(relative, f)));

    private static IEnumerable<string> ChildDirectories(string path) =>
        Directory.EnumerateDirectories(path).Select(Path.GetFileName).OfType<string>();
}
