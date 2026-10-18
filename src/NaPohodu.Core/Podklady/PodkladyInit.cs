namespace NaPohodu.Core.Podklady;

public enum PodkladyActionKind
{
    /// <summary>Založit složku.</summary>
    Create,

    /// <summary>Přejmenovat stávající Šanony na Archiv (sdílení i ID na Google Drive zůstávají).</summary>
    Archive,
}

/// <param name="Path">Absolutní cesta; u <see cref="PodkladyActionKind.Archive"/> zdroj.</param>
/// <param name="Target">Cíl přejmenování.</param>
public sealed record PodkladyAction(PodkladyActionKind Kind, string Path, string? Target = null);

/// <summary>Plán založení struktury jednoho zákazníka a roku. Při <see cref="Error"/> se nic neprovádí.</summary>
public sealed record PodkladyInitPlan(
    string CustomerFolder,
    int Year,
    VatPeriod? Period,
    string PeriodSource,
    IReadOnlyList<PodkladyAction> Actions,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public bool CanApply => Error is null && Actions.Count > 0;

    /// <summary>Plán selhal jen proto, že periodicitu nejde zjistit ze založených roků a je třeba ji zadat.</summary>
    public bool NeedsPeriod { get; init; }
}

public sealed record PodkladyActionResult(PodkladyAction Action, bool Succeeded, string? Message);

/// <summary>
/// Založení struktury zákazníka v Podkladech a převod na místě (<c>docs/podklady.md</c>, „Přístup zákazníků
/// a přechod“): chybějící složka zákazníka, <c>Ke zpracování</c> a <c>Vráceno</c>; stávající Šanony mimo
/// strukturu → <c>Archiv</c> (jen když Archiv ještě není); kostra roku. Nic nemaže ani nepřesouvá dovnitř
/// složek, bezpečné při opakování. Práva na Google Drive aplikace nevidí: co je potřeba nasdílet, hlásí.
/// </summary>
public static class PodkladyInit
{
    private const int MaxListed = 10;

    public static PodkladyInitPlan Plan(string rootPath, string customerFolderName, int year, VatPeriod? requestedPeriod)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerFolderName);
        var customer = Path.Combine(rootPath, customerFolderName);
        var actions = new List<PodkladyAction>();
        var warnings = new List<string>();

        PodkladyInitPlan Fail(string error) => new(customer, year, null, string.Empty, [], warnings, error);

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return Fail($"složka Podkladů neexistuje ({rootPath})");
        }

        if (!Directory.Exists(customer))
        {
            actions.Add(new(PodkladyActionKind.Create, customer));
            warnings.Add("Složka zákazníka je nová: nasdílejte ji zákazníkovi pro čtení.");
        }

        var inbox = Path.Combine(customer, CustomerInbox.FolderName);
        if (!Directory.Exists(inbox))
        {
            actions.Add(new(PodkladyActionKind.Create, inbox));
            warnings.Add($"{CustomerInbox.FolderName} je nová: nasdílejte ji zákazníkovi pro zápis.");
        }

        var returned = Path.Combine(inbox, PodkladyLayout.Returned);
        if (!Directory.Exists(returned))
        {
            actions.Add(new(PodkladyActionKind.Create, returned));
        }

        var binders = Path.Combine(customer, PodkladyLayout.Binders);
        var archive = Path.Combine(customer, PodkladyLayout.Archive);
        var keepBinders = Directory.Exists(binders);
        var foreign = PodkladyLayout.Nonconforming(binders);
        if (foreign.Count > 0)
        {
            if (Directory.Exists(archive))
            {
                warnings.Add($"Šanony obsahují položky mimo strukturu, Archiv už existuje – nechávám je: {List(foreign)}");
            }
            else
            {
                actions.Add(new(PodkladyActionKind.Archive, binders, archive));
                keepBinders = false;
            }
        }

        var yearPath = Path.Combine(binders, year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var existing = keepBinders ? PodkladyLayout.DetectPeriod(yearPath) : null;
        if (requestedPeriod is { } requested && existing is { } current && requested != current)
        {
            return Fail($"rok {year} je už založen {Describe(current)}; periodicitu nejde změnit založením");
        }

        var (period, source) = requestedPeriod is { } r ? (r, "zadáno")
            : existing is { } e ? (e, $"podle založeného roku {year}")
            : keepBinders && FromOtherYears(binders, year) is { } other ? (other.Period, $"podle roku {other.Year}")
            : ((VatPeriod?)null, string.Empty);
        if (period is not { } effective)
        {
            return Fail("periodicitu DPH nelze zjistit ze založených roků, je potřeba ji zadat") with { NeedsPeriod = true };
        }

        if (!keepBinders)
        {
            actions.Add(new(PodkladyActionKind.Create, binders));
        }

        foreach (var relative in PodkladyLayout.YearFolders(effective).Prepend(string.Empty))
        {
            var path = relative.Length == 0 ? yearPath : Path.Combine(yearPath, relative);
            if (!keepBinders || !Directory.Exists(path))
            {
                actions.Add(new(PodkladyActionKind.Create, path));
            }
        }

        return new PodkladyInitPlan(customer, year, effective, source, actions, warnings, null);
    }

    /// <summary>Provede plán v pořadí; přejmenování jen když cíl stále neexistuje. Chyba jedné akce zastaví další.</summary>
    public static IReadOnlyList<PodkladyActionResult> Execute(PodkladyInitPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Error is not null)
        {
            throw new InvalidOperationException($"Plán nelze provést: {plan.Error}");
        }

        var results = new List<PodkladyActionResult>();
        foreach (var action in plan.Actions)
        {
            try
            {
                if (action.Kind == PodkladyActionKind.Archive)
                {
                    if (Directory.Exists(action.Target))
                    {
                        results.Add(new(action, false, $"{action.Target} mezitím vznikl, Šanony nepřejmenovány"));
                        break;
                    }

                    Directory.Move(action.Path, action.Target!);
                }
                else
                {
                    Directory.CreateDirectory(action.Path);
                }

                results.Add(new(action, true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new(action, false, ex.Message));
                break;
            }
        }

        return results;
    }

    public static string Describe(VatPeriod period) => period == VatPeriod.Monthly ? "měsíčně" : "čtvrtletně";

    // Nejbližší předchozí rok, jinak nejbližší další.
    private static (int Year, VatPeriod Period)? FromOtherYears(string binders, int year) =>
        Directory.EnumerateDirectories(binders)
            .Select(d => (Name: Path.GetFileName(d), Path: d))
            .Where(d => d.Name.Length == 4 && int.TryParse(d.Name, out _))
            .Select(d => (Year: int.Parse(d.Name, System.Globalization.CultureInfo.InvariantCulture), Period: PodkladyLayout.DetectPeriod(d.Path)))
            .Where(d => d.Year != year && d.Period is not null)
            .OrderBy(d => d.Year > year ? 1 : 0)
            .ThenBy(d => Math.Abs(d.Year - year))
            .Select(d => ((int Year, VatPeriod Period)?)(d.Year, d.Period!.Value))
            .FirstOrDefault();

    private static string List(IReadOnlyList<string> items) =>
        string.Join(", ", items.Take(MaxListed)) + (items.Count > MaxListed ? $" a dalších {items.Count - MaxListed}" : string.Empty);
}
