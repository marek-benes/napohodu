using System.Globalization;

namespace NaPohodu.Core.Filing;

/// <summary>Co dělat se souborem, jehož název už v cíli existuje s jiným obsahem.</summary>
public enum ConflictMode
{
    /// <summary>Přeskočit a nahlásit (výchozí).</summary>
    Skip,

    /// <summary>Přesunout pod volným názvem „soubor (2).pdf“; existující soubor zůstane.</summary>
    KeepBoth,
}

/// <summary>Výsledek zpracování jednoho souboru.</summary>
public enum FilingOutcome
{
    Moved,
    MovedRenamed,
    Skipped,
    Failed,
}

/// <param name="TargetPath">Skutečná cílová cesta (u <see cref="FilingOutcome.MovedRenamed"/> nový název).</param>
public sealed record FilingResultItem(FilingPlanItem Item, FilingOutcome Outcome, string? TargetPath, string Message);

/// <summary>
/// Provede plán zatřídění: přesune soubory <see cref="FilingStatus.Ready"/> (a podle volby konflikty).
/// Nikdy nepřepisuje existující soubor. Přesun přes hranice svazků řeší <see cref="File.Move(string, string, bool)"/>
/// (kopie + smazání). Bezpečné při opakování: co už je v cíli, se znovu nepřesouvá.
/// </summary>
public static class FilingExecutor
{
    private const int MaxRenameAttempts = 1000;

    public static IReadOnlyList<FilingResultItem> Execute(FilingPlan plan, ConflictMode conflictMode = ConflictMode.Skip) =>
        Execute(plan, _ => conflictMode);

    /// <param name="decideConflict">Rozhodnutí pro každý soubor v konfliktu zvlášť (UI se ptá po souborech).</param>
    public static IReadOnlyList<FilingResultItem> Execute(FilingPlan plan, Func<FilingPlanItem, ConflictMode> decideConflict)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(decideConflict);

        var results = new List<FilingResultItem>(plan.Items.Count);
        foreach (var item in plan.Items)
        {
            results.Add(item.Status switch
            {
                FilingStatus.Ready => Move(item, item.TargetPath!, FilingOutcome.Moved),
                FilingStatus.Conflict when decideConflict(item) == ConflictMode.KeepBoth => Move(item, FreeName(item.TargetPath!), FilingOutcome.MovedRenamed),
                FilingStatus.Conflict => new(item, FilingOutcome.Skipped, item.TargetPath, "v cíli je jiný soubor se stejným názvem"),
                FilingStatus.AlreadyFiled => new(item, FilingOutcome.Skipped, item.TargetPath, "v cíli už je stejný soubor"),
                _ => new(item, FilingOutcome.Skipped, null, item.Reason),
            });
        }

        return results;
    }

    private static FilingResultItem Move(FilingPlanItem item, string targetPath, FilingOutcome outcome)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            // overwrite: false — když se cíl mezitím objevil, vyhodí IOException a nic se nepřepíše.
            File.Move(item.SourcePath, targetPath, overwrite: false);
            return new(item, outcome, targetPath, outcome == FilingOutcome.MovedRenamed ? "přesunuto pod novým názvem" : "přesunuto");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(item, FilingOutcome.Failed, targetPath, ex.Message);
        }
    }

    internal static string FreeName(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);
        for (var i = 2; i < MaxRenameAttempts; i++)
        {
            var candidate = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{name} ({i}){extension}"));
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Nenalezen volný název pro {targetPath}.");
    }
}
