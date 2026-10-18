using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using NaPohodu.App;
using NaPohodu.App.Web;

namespace NaPohodu.Desktop;

/// <summary>
/// Ikona u hodin: že aplikace běží (bublina s instancí, adresou a počtem oken), „Otevřít NaPohodu“
/// a „Ukončit“ (počká na rozběhnuté operace). Nahrazuje okno konzole, které dřív sloužilo
/// jako jediný ukazatel běhu. Vlastní logika (okna, operace, vypnutí) je v NaPohodu.App.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private static readonly TimeSpan OperationsWaitLimit = TimeSpan.FromMinutes(2);

    private readonly InstanceOptions _instance;
    private readonly NotifyIcon _icon;
    private readonly Control _uiThread;
    private WebApplication? _app;
    private AppActivity? _activity;
    private bool _stopping;

    public TrayApplication(InstanceOptions instance)
    {
        _instance = instance;

        // Neviditelný ovládací prvek = cesta zpět na vlákno UI (ikona, dialogy) z vláken serveru.
        _uiThread = new Control();
        _uiThread.CreateControl();
        FolderPicker = new WinFormsFolderPicker(_uiThread);

        var menu = new ContextMenuStrip();
        var open = menu.Items.Add("Otevřít NaPohodu", null, (_, _) => AppWindow.Open(_instance.Url));
        open.Font = new Font(open.Font, FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Ukončit", null, (_, _) => _ = StopAsync());

        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Text = Title,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => AppWindow.Open(_instance.Url);
    }

    public IFolderPicker FolderPicker { get; }

    private string Title => _instance.IsProduction ? "NaPohodu" : $"{_instance.Name} (VÝVOJ)";

    /// <summary>Napojí ikonu na běžící server: bublina podle aktivity, konec aplikace při vypnutí serveru.</summary>
    public void Attach(WebApplication app)
    {
        _app = app;
        _activity = app.Services.GetRequiredService<AppActivity>();
        _activity.Changed += () => OnUiThread(UpdateText);
        app.Lifetime.ApplicationStopping.Register(() => OnUiThread(ExitThread));
        UpdateText();
    }

    private async Task StopAsync()
    {
        if (_stopping || _app is null || _activity is null)
        {
            return;
        }

        _stopping = true;
        if (_activity.Operations > 0)
        {
            SetText($"{Title} – ukončuji, čekám na dokončení operací…");
            await _activity.WaitForOperationsAsync(OperationsWaitLimit, TimeProvider.System).ConfigureAwait(true);
        }

        _app.Lifetime.StopApplication();
    }

    private void UpdateText()
    {
        if (_stopping || _activity is null)
        {
            return;
        }

        var windows = _activity.Windows;
        var state = windows switch
        {
            0 => "žádné okno",
            1 => "1 okno",
            _ => string.Create(CultureInfo.InvariantCulture, $"{windows} okna"),
        };
        SetText($"{Title} běží · {_instance.Url} · {state}");
    }

    // NotifyIcon.Text má limit 127 znaků.
    private void SetText(string text) => _icon.Text = text.Length > 127 ? text[..127] : text;

    private void OnUiThread(Action action)
    {
        if (_uiThread.IsDisposed)
        {
            return;
        }

        if (_uiThread.InvokeRequired)
        {
            _uiThread.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;   // jinak ikona u hodin zůstane „viset“ do najetí myší
            _icon.Dispose();
            _uiThread.Dispose();
        }

        base.Dispose(disposing);
    }
}
