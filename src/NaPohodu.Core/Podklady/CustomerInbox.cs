using NaPohodu.Core.Filing;

namespace NaPohodu.Core.Podklady;

/// <summary>Výsledek hledání inboxu zákazníka v Podkladech.</summary>
/// <param name="Path">Absolutní cesta ke složce <c>Ke zpracování</c>, nebo <c>null</c>, když ji nelze určit.</param>
/// <param name="Source">Odkud se cesta vzala, nebo proč ji nelze určit (pro výpis uživateli).</param>
public sealed record InboxResolution(string? Path, string Source)
{
    public bool IsResolved => Path is not null;
}

/// <summary>
/// Inbox zákazníka = <c>&lt;kořen Podkladů&gt;\&lt;složka firmy&gt;\Ke zpracování</c>. Složka zákazníka
/// v Podkladech se jmenuje přesně jako jeho složka v Dokumentech Pohody (ověřeno 2026-10-18),
/// takže se odvozuje z <see cref="CompanyFolderResolution"/>. Nic nezakládá, jen hlásí, co chybí.
/// </summary>
public static class CustomerInbox
{
    public const string FolderName = "Ke zpracování";

    public static InboxResolution Resolve(string rootPath, CompanyFolderResolution companyFolder)
    {
        ArgumentNullException.ThrowIfNull(companyFolder);

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return new InboxResolution(null, "není nastaven kořen Podkladů (Podklady:RootPath)");
        }

        if (!Directory.Exists(rootPath))
        {
            return new InboxResolution(null, $"složka Podkladů neexistuje ({rootPath})");
        }

        if (companyFolder.Path is null)
        {
            return new InboxResolution(null, "nelze určit, dokud není známa složka firmy v Dokumentech");
        }

        var name = System.IO.Path.GetFileName(companyFolder.Path.TrimEnd('\\', '/'));
        var customerFolder = System.IO.Path.Combine(rootPath, name);
        if (!Directory.Exists(customerFolder))
        {
            return new InboxResolution(null, $"v Podkladech chybí složka zákazníka „{name}“ ({customerFolder})");
        }

        var inbox = System.IO.Path.Combine(customerFolder, FolderName);
        return Directory.Exists(inbox)
            ? new InboxResolution(inbox, "Ke zpracování zákazníka v Podkladech")
            : new InboxResolution(null, $"zákazník „{name}“ nemá v Podkladech složku {FolderName} ({inbox})");
    }
}
