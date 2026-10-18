using NaPohodu.App.Web;

namespace NaPohodu.Desktop;

/// <summary>
/// Systémový dialog výběru složky pro tlačítko „Vybrat…“ ve stránkách. Volá se z vláken serveru,
/// dialog ale musí běžet na vlákně UI (STA) a být vidět nad oknem aplikace v Edge, proto má
/// neviditelného vlastníka „vždy navrchu“.
/// </summary>
internal sealed class WinFormsFolderPicker(Control uiThread) : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title, string? initialFolder)
    {
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        uiThread.BeginInvoke(() =>
        {
            try
            {
                result.SetResult(Show(title, initialFolder));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                result.SetException(ex);
            }
        });
        return result.Task;
    }

    private static string? Show(string title, string? initialFolder)
    {
        using var owner = new Form
        {
            TopMost = true,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            Size = new Size(1, 1),
            Opacity = 0,
        };
        owner.Show();
        owner.Activate();

        using var dialog = new FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (initialFolder is not null)
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
