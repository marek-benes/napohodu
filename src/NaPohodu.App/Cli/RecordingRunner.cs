using System.Xml.Linq;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.App.Cli;

/// <summary>
/// Uloží původní bajty odpovědi Pohody do souboru (podklad pro fixtures; obsahuje osobní údaje,
/// před commitem anonymizovat).
/// </summary>
internal sealed class RecordingRunner(IPohodaXmlRunner inner, string path) : IPohodaXmlRunner
{
    public async Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default)
    {
        var response = await inner.RunAsync(request, database, cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, response.RawBytes, cancellationToken).ConfigureAwait(false);
        return response;
    }
}
