using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace NaPohodu.App.Cli;

/// <summary>Volby sdílené více příkazy.</summary>
internal static class CliOptions
{
    public static readonly Option<bool> Verbose = new("--verbose", "-v")
    {
        Description = "Podrobný výpis průběhu (logy na stderr).",
        Recursive = true,
    };

    public static Option<FileInfo?> SaveResponse() =>
        new("--save-response")
        {
            Description = "Uloží původní XML odpověď Pohody do souboru (podklad pro testy; obsahuje osobní údaje).",
        };

    public static Option<DateOnly?> Date(string name, string description) =>
        new(name)
        {
            Description = $"{description} (RRRR-MM-DD).",
            CustomParser = ParseDate,
        };

    private static DateOnly? ParseDate(ArgumentResult result)
    {
        var token = result.Tokens.Count == 1 ? result.Tokens[0].Value : string.Empty;
        if (DateOnly.TryParseExact(token, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        result.AddError($"Neplatné datum „{token}“, očekávám formát RRRR-MM-DD.");
        return null;
    }
}
