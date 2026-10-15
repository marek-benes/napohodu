using System.Globalization;
using NaPohodu.Core.Models;

namespace NaPohodu.App.Components.Common;

/// <summary>České formátování hodnot v UI.</summary>
internal static class Format
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    public static string Date(DateOnly? date) => date?.ToString("d.M.yyyy", Czech) ?? string.Empty;

    public static string Money(decimal amount) => amount.ToString("N2", Czech);

    public static string Total(Invoice invoice) =>
        invoice.TotalForeign is { } foreign
            ? $"{Money(foreign.Total)} {foreign.Currency}"
            : $"{Money(invoice.TotalHome)} CZK";

    public static string LoadedAt(DateTimeOffset loadedAt) =>
        $"načteno {loadedAt.ToLocalTime().ToString("d.M. H:mm", Czech)}";

    public static string UnitType(string? unitType) =>
        unitType switch
        {
            "singleEntry" => "daňová evidence",
            "doubleEntry" => "podvojné",
            "doubleEntryNonProfit" => "podvojné (nezisková)",
            "doubleEntryAllowance" => "podvojné (příspěvková)",
            _ => unitType ?? string.Empty,
        };
}
