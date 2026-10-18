using System.ComponentModel;
using System.Diagnostics;

namespace NaPohodu.App.Web;

/// <summary>
/// Otevře okno aplikace: Edge v režimu aplikace (<c>--app</c>) – vlastní okno bez adresního řádku
/// a záložek, s ikonou NaPohodu na hlavním panelu, takže působí jako běžný program. Edge je ve
/// Windows 11 vždy; když chybí, otevře se výchozí prohlížeč.
/// </summary>
internal static class AppWindow
{
    public static void Open(string url)
    {
        if (!TryStart("msedge", $"--app={url} --window-size=1280,860"))
        {
            TryStart(url, arguments: null);
        }
    }

    private static bool TryStart(string fileName, string? arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = true };
            if (arguments is not null)
            {
                startInfo.Arguments = arguments;
            }

            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
