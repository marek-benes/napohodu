namespace NaPohodu.Core.Filing;

/// <summary>Stav souboru v plánu zatřídění.</summary>
public enum FilingStatus
{
    /// <summary>Cíl neexistuje, soubor lze přesunout.</summary>
    Ready,

    /// <summary>V cíli už je soubor se stejným názvem i obsahem; nic se nedělá.</summary>
    AlreadyFiled,

    /// <summary>V cíli je jiný soubor se stejným názvem; rozhoduje uživatel (přeskočit / ponechat oba).</summary>
    Conflict,

    /// <summary>Soubor patří k faktuře, ale cíl nelze určit (viz <see cref="FilingPlanItem.Reason"/>).</summary>
    Unresolved,
}

/// <summary>Jeden spárovaný soubor v plánu zatřídění.</summary>
public sealed record FilingPlanItem
{
    public required string SourcePath { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public required string InvoiceNumber { get; init; }

    /// <summary><c>FA.ID</c> faktury (pro pozdější poznačení <c>DocDirs</c>); <c>null</c> u nejednoznačného čísla.</summary>
    public int? InvoiceId { get; init; }

    /// <summary>Podsložka dokladu relativně ke složce firmy (tvar <c>DocDirs.Dir</c>).</summary>
    public string? SubFolder { get; init; }

    public string? TargetPath { get; init; }

    public required FilingStatus Status { get; init; }

    /// <summary>Proč je soubor nevyřešený, nebo odkud se vzal cíl.</summary>
    public required string Reason { get; init; }
}

/// <summary>Plán zatřídění jednoho zdrojového adresáře do Dokumentů jedné jednotky.</summary>
public sealed record FilingPlan
{
    public required string SourceDirectory { get; init; }

    public required CompanyFolderResolution CompanyFolder { get; init; }

    public required IReadOnlyList<FilingPlanItem> Items { get; init; }

    /// <summary>Soubory, jejichž název neodpovídá žádné faktuře ročníku.</summary>
    public required IReadOnlyList<string> Unmatched { get; init; }

    public int Count(FilingStatus status) => Items.Count(i => i.Status == status);
}
