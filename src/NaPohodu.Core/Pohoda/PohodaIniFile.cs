using System.Text;

namespace NaPohodu.Core.Pohoda;

/// <summary>
/// INI soubor pro <c>Pohoda.exe /XML</c> (sekce <c>[XML]</c>).
/// </summary>
internal static class PohodaIniFile
{
    /// <param name="database">Název datového souboru jednotky; <c>null</c> jen pro seznam účetních jednotek.</param>
    public static string Build(string inputXmlPath, string responseXmlPath, string? database)
    {
        var ini = new StringBuilder();
        ini.Append("[XML]\r\n");
        ini.Append("input_xml=").Append(inputXmlPath).Append("\r\n");
        ini.Append("response_xml=").Append(responseXmlPath).Append("\r\n");
        if (database is not null)
        {
            ini.Append("database=").Append(database).Append("\r\n");
        }

        ini.Append("format_output=1\r\n");
        return ini.ToString();
    }
}
