using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GangDrogaCity.Core;

namespace GangDrogaCity.App;

public partial class SettingsWindow : Window
{
    private readonly MainWindow _owner;
    private readonly LauncherEngine _engine;

    public SettingsWindow(MainWindow owner, LauncherEngine engine)
    {
        InitializeComponent();
        _owner = owner;
        _engine = engine;

        usernameTxt.Text = _engine.Settings.Username;

        closeBtn.Click += (_, _) => Close();
        saveBtn.Click += OnSave;
        verifyBtn.Click += OnVerify;
        reinstallMcBtn.Click += OnReinstallMinecraft;
        reinstallAllBtn.Click += OnReinstallAll;
        branchBtn.Click += OnChangeBranch;

        RootCanvas.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button && e.Source is not TextBox)
            {
                BeginMoveDrag(e);
            }
        };
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var username = (usernameTxt.Text ?? "").Trim();
        if (username.Length < 3)
        {
            await MessageDialog.ShowAsync(this, "Nome utente non valido", "Il nome utente deve essere di almeno 3 caratteri.", AlertKind.Warning);
            return;
        }
        _engine.SetUsername(username);
        Close();
    }

    private async Task<bool> EnsureGameNotRunning()
    {
        if (_engine.IsGameRunning)
        {
            await MessageDialog.ShowAsync(this, "Minecraft in esecuzione", "Chiudi prima Minecraft, poi riprova.", AlertKind.Info);
            return false;
        }
        return true;
    }

    private async void OnVerify(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureGameNotRunning()) return;
        Close();
        _ = _owner.RunSafely(_engine.VerifyInstallationAsync());
    }

    private async void OnReinstallMinecraft(object? sender, RoutedEventArgs e)
    {
        var ok = await MessageDialog.ConfirmAsync(this, "Conferma", "Sei sicuro di voler reinstallare minecraft?", AlertKind.Question);
        if (!ok) return;
        if (!await EnsureGameNotRunning()) return;
        Close();
        _ = _owner.RunSafely(_engine.ReinstallMinecraftAsync());
    }

    private async void OnReinstallAll(object? sender, RoutedEventArgs e)
    {
        var ok = await MessageDialog.ConfirmAsync(this, "Conferma",
            "Sei sicuro di voler reinstallare tutto? Verranno rimossi Minecraft, mod, cache, download e dati pacchetti. Le tue impostazioni, i keybind e i waypoint verranno conservati.",
            AlertKind.Warning);
        if (!ok) return;
        if (!await EnsureGameNotRunning()) return;
        Close();
        _ = _owner.RunSafely(_engine.ReinstallAllAsync());
    }

    private async void OnChangeBranch(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureGameNotRunning()) return;
        Close();
        _ = _owner.RunSafely(_engine.ChangeBranchAsync());
    }
}
