using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GangDrogaCity.Core;

namespace GangDrogaCity.App;

public partial class MainWindow : Window
{
    private readonly AvaloniaHost _host;
    private readonly LauncherEngine _engine;
    private bool _booted;

    public LauncherEngine Engine => _engine;

    public MainWindow()
    {
        InitializeComponent();

        _host = new AvaloniaHost(this);
        _engine = new LauncherEngine(_host);
        _host.LogFile = Path.Combine(_engine.Paths.MinecraftDir, "launcher.log");

        _engine.PhaseChanged += phase => Dispatcher.UIThread.Post(() => ApplyPhase(phase));
        _engine.BusyChanged += (busy, message) => Dispatcher.UIThread.Post(() =>
        {
            operationPanel.IsVisible = busy;
            if (!string.IsNullOrEmpty(message)) operationText.Text = message;
        });
        _engine.DoNotPowerOffChanged += visible => Dispatcher.UIThread.Post(() => doNotPowerOffPanel.IsVisible = visible);
        _engine.GameExited += exitCode => Dispatcher.UIThread.Post(() =>
        {
            if (exitCode != 0) crashPanel.IsVisible = true;
        });
        _engine.SettingsChanged += () => Dispatcher.UIThread.Post(RefreshUserLabel);

        versionLabel.Text = _engine.VersionLabel;
        RefreshUserLabel();
        ApplyPhase(_engine.Phase);

        closeBtn.Click += (_, _) => Close();
        minimizeBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        playBtn.Click += OnPlayClick;
        reducedGraphicsBtn.Click += OnReducedGraphicsClick;
        settingsBtn.Click += (_, _) => OpenSettings();
        userLabel.Click += (_, _) => OpenSettings();
        sendCrashBtn.Click += OnSendCrashClick;

        RootCanvas.PointerPressed += OnDragStart;
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_booted) return;
        _booted = true;
        // Come nel vecchio launcher: un secondo di attesa prima del boot
        DispatcherTimer.RunOnce(() => _ = RunSafely(_engine.BootAsync()), TimeSpan.FromSeconds(1));
    }

    private void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button)
        {
            BeginMoveDrag(e);
        }
    }

    public void SetStatus(string message, bool isError)
    {
        statusText.Text = message;
        operationText.Text = message;
        statusBorder.Background = isError ? Brushes.Red : Brushes.Transparent;
    }

    public void SetProgress(int value)
    {
        progressBar.Value = Math.Clamp(value, 0, 100);
    }

    private void ApplyPhase(LauncherPhase phase)
    {
        switch (phase)
        {
            case LauncherPhase.Installing:
                installPanel.IsVisible = true;
                menuPanel.IsVisible = false;
                crashPanel.IsVisible = false;
                break;
            case LauncherPhase.Ready:
                installPanel.IsVisible = false;
                menuPanel.IsVisible = true;
                SetPlayButton("PLAY", Brushes.Green, Brushes.White, enabled: true);
                reducedGraphicsBtn.IsEnabled = true;
                RefreshUserLabel();
                break;
            case LauncherPhase.Launching:
                installPanel.IsVisible = false;
                menuPanel.IsVisible = true;
                crashPanel.IsVisible = false;
                SetPlayButton("ATTENDI", Brushes.Yellow, Brushes.Black, enabled: false);
                reducedGraphicsBtn.IsEnabled = false;
                break;
            case LauncherPhase.Playing:
                installPanel.IsVisible = false;
                menuPanel.IsVisible = true;
                SetPlayButton("CHIUDI", Brushes.Red, Brushes.White, enabled: true);
                reducedGraphicsBtn.IsEnabled = false;
                break;
        }
    }

    private void SetPlayButton(string text, IBrush background, IBrush foreground, bool enabled)
    {
        playBtn.Content = text;
        playBtn.Background = background;
        playBtn.Foreground = foreground;
        playBtn.IsEnabled = enabled;
    }

    private void RefreshUserLabel()
    {
        var username = _engine.Settings.Username;
        userLabel.Content = string.IsNullOrWhiteSpace(username) || username.Length < 3
            ? " Username non impostato.\nClicca qui per impostarlo"
            : username;
    }

    private async void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.Phase == LauncherPhase.Ready)
            {
                crashPanel.IsVisible = false;
                var started = await _engine.PlayAsync(false);
                if (!started && (_engine.Settings.Username?.Trim().Length ?? 0) < 3)
                {
                    OpenSettings();
                }
            }
            else if (_engine.Phase == LauncherPhase.Playing)
            {
                _engine.StopGame();
            }
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Errore", ex.Message, AlertKind.Error);
        }
    }

    private async void OnReducedGraphicsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_engine.Phase != LauncherPhase.Ready) return;
            var ok = await MessageDialog.ConfirmAsync(this, "Modalità grafica ridotta",
                "Vuoi avviare GangDrogaCity in grafica ridotta? Verranno disabilitati shaders, animazioni e dettagli per migliorare le prestazioni. Puoi sempre modificare questa impostazione in seguito dalle opzioni grafiche di Minecraft.",
                AlertKind.Question);
            if (!ok) return;
            crashPanel.IsVisible = false;
            var started = await _engine.PlayAsync(true);
            if (!started && (_engine.Settings.Username?.Trim().Length ?? 0) < 3)
            {
                OpenSettings();
            }
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Errore", ex.Message, AlertKind.Error);
        }
    }

    private async void OnSendCrashClick(object? sender, RoutedEventArgs e)
    {
        sendCrashBtn.IsEnabled = false;
        sendCrashBtn.Content = "ATTENDI";
        try
        {
            var url = await _engine.UploadCrashReportAsync();
            var copy = await MessageDialog.ConfirmAsync(this, "Upload completato",
                $"Log caricato con successo! Copiare il link negli appunti?{Environment.NewLine}{url}", AlertKind.Info);
            if (copy && Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(url);
            }
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Errore upload", $"Si è verificato un errore durante l'upload del log: {ex.Message}", AlertKind.Error);
        }
        finally
        {
            sendCrashBtn.IsEnabled = true;
            sendCrashBtn.Content = "Invia";
        }
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this, _engine);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    public async Task RunSafely(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(this, "Errore", ex.Message, AlertKind.Error);
        }
    }
}
