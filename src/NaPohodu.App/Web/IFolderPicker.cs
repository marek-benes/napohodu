namespace NaPohodu.App.Web;

/// <summary>
/// Systémový dialog pro výběr složky. Server běží na stejném počítači jako okno aplikace, takže
/// dialog se může ukázat přímo na ploše účetní a vrátit plnou cestu (prohlížeč ji z bezpečnostních
/// důvodů nevrátí). Implementace je jen v okenní aplikaci (<c>NaPohodu.Desktop</c>); bez ní
/// (CLI <c>serve</c>, testy) tlačítko „Vybrat…“ není a cesta se píše ručně.
/// </summary>
public interface IFolderPicker
{
    /// <returns>Vybraná složka, nebo <c>null</c>, když uživatel dialog zrušil.</returns>
    Task<string?> PickFolderAsync(string title, string? initialFolder);
}
