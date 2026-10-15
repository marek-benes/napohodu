using Microsoft.Extensions.Configuration;

namespace NaPohodu.App;

/// <summary>
/// Konfigurace pro CLI i <c>serve</c>. Pořadí (pozdější vyhrává):
/// <c>appsettings.json</c> (v gitu, bez tajných údajů) → <c>appsettings.Local.json</c> vedle exe
/// (mimo git) → proměnné prostředí s prefixem <c>NAPOHODU_</c> (např. <c>NAPOHODU_Pohoda__Password</c>).
/// </summary>
internal static class AppConfiguration
{
    public static IConfigurationRoot Build() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables("NAPOHODU_")
            .Build();
}
