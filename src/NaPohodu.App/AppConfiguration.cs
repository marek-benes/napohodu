using Microsoft.Extensions.Configuration;
using NaPohodu.Core.Configuration;

namespace NaPohodu.App;

/// <summary>
/// Konfigurace pro CLI i <c>serve</c>. Pořadí (pozdější vyhrává):
/// <c>appsettings.json</c> (v gitu, bez tajných údajů) → <c>appsettings.Local.json</c> vedle exe
/// (mimo git) → proměnné prostředí s prefixem <c>NAPOHODU_</c> (např. <c>NAPOHODU_Pohoda__Password</c>).
/// </summary>
internal static class AppConfiguration
{
    public const string LocalFileName = "appsettings.Local.json";

    public static IConfigurationRoot Build() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(LocalFileName, optional: true)
            .AddEnvironmentVariables("NAPOHODU_")
            .Build();

    public static InstanceOptions GetInstanceOptions(IConfiguration configuration) =>
        configuration.GetSection(InstanceOptions.SectionName).Get<InstanceOptions>() ?? new InstanceOptions();

    /// <summary>Nastavení Pohody; zálohy MDB bez explicitní cesty jdou do datové složky instance.</summary>
    public static PohodaOptions GetPohodaOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection(PohodaOptions.SectionName).Get<PohodaOptions>() ?? new PohodaOptions();
        if (string.IsNullOrWhiteSpace(options.BackupPath))
        {
            options.BackupPath = Path.Combine(GetInstanceOptions(configuration).DataDirectory, "Zalohy");
        }

        return options;
    }

    public static PodkladyOptions GetPodkladyOptions(IConfiguration configuration) =>
        configuration.GetSection(PodkladyOptions.SectionName).Get<PodkladyOptions>() ?? new PodkladyOptions();

    /// <summary>Chyby konfigurace pro volání Pohody, česky; prázdný seznam = v pořádku.</summary>
    public static IReadOnlyList<string> Validate(PohodaOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ExePath))
        {
            errors.Add("Není nastavena cesta k Pohoda.exe (Pohoda:ExePath).");
        }
        else if (!File.Exists(options.ExePath))
        {
            errors.Add($"Pohoda.exe nebyla nalezena na cestě {options.ExePath} (Pohoda:ExePath).");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            errors.Add("Není nastaveno uživatelské jméno do Pohody (Pohoda:Username).");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            errors.Add("Timeout musí být kladný (Pohoda:Timeout, např. 00:02:00).");
        }

        return errors;
    }
}
