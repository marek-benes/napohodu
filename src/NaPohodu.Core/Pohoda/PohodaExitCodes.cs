namespace NaPohodu.Core.Pohoda;

/// <summary>
/// Návratové kódy <c>Pohoda.exe /XML</c>: 0 = v pořádku, 101–113 chyby zpracování,
/// 551–557 chyby přípravy (konfigurace, konverze jednotky, poškozený soubor, přihlášení,
/// práva, aktivace), 601–609 varování. Přesný význam jednotlivých kódů viz dokumentace Stormware.
/// </summary>
internal static class PohodaExitCodes
{
    public static bool IsWarning(int exitCode) => exitCode is >= 600 and < 700;

    public static bool IsError(int exitCode) => exitCode != 0 && !IsWarning(exitCode);

    public static string Describe(int exitCode, string? responseNote)
    {
        var kind = exitCode switch
        {
            >= 100 and < 200 => "chyba zpracování",
            >= 500 and < 600 => "chyba přípravy (konfigurace, konverze jednotky, poškozený soubor, přihlášení, práva nebo aktivace)",
            _ => "neznámá chyba",
        };

        var message = $"Pohoda.exe skončila s kódem {exitCode}: {kind}.";
        return string.IsNullOrWhiteSpace(responseNote) ? message : $"{message} {responseNote}";
    }
}
