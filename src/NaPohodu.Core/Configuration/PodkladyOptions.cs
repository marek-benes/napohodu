namespace NaPohodu.Core.Configuration;

/// <summary>
/// Sdílený disk s podklady zákazníků (<c>&lt;RootPath&gt;\&lt;složka firmy&gt;\Ke zpracování</c>, <c>Šanony</c>).
/// Pravidla: <c>docs/podklady.md</c>.
/// </summary>
public sealed class PodkladyOptions
{
    public const string SectionName = "Podklady";

    /// <summary>Kořen Podkladů, ve kterém má každý zákazník složku pojmenovanou jako jeho složka v Dokumentech.</summary>
    public string RootPath { get; set; } = string.Empty;
}
