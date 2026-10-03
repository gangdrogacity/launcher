using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using GangDrogaCity.Core;

namespace GangDrogaCity.App;

/// <summary>
/// Implementazione di ILauncherHost per Avalonia: ogni chiamata viene portata sul thread UI,
/// il Core puo' chiamare da qualsiasi thread.
/// </summary>
public sealed class AvaloniaHost : ILauncherHost
{
    private readonly MainWindow _window;
    private readonly object _logLock = new();
    private string? _logFile;

    public AvaloniaHost(MainWindow window)
    {
        _window = window;
    }

    /// <summary>File di log del launcher (launcher.log nella cartella .gangdrogacity).</summary>
    public string? LogFile
    {
        get => _logFile;
        set
        {
            _logFile = value;
            try
            {
                if (value is not null)
                {
                    // Tieni il log compatto: riparti da zero se supera 2 MB
                    if (File.Exists(value) && new FileInfo(value).Length > 2 * 1024 * 1024) File.Delete(value);
                    AppendToFile($"===== GangDrogaCity Launcher v{Platform.AppVersionString} ({Platform.RuntimeIdentifier}) avviato {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private void AppendToFile(string line)
    {
        if (_logFile is null) return;
        try
        {
            lock (_logLock)
            {
                File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // ignore
        }
    }

    public void Log(string message)
    {
        AppendToFile(message);
        Dispatcher.UIThread.Post(() => _window.SetStatus(message, isError: false));
    }

    public void LogError(string message)
    {
        AppendToFile("ERRORE: " + message);
        Dispatcher.UIThread.Post(() => _window.SetStatus(message, isError: true));
    }

    public void SetProgress(int value)
    {
        Dispatcher.UIThread.Post(() => _window.SetProgress(value));
    }

    public Task AlertAsync(string title, string message, AlertKind kind = AlertKind.Info)
    {
        return Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ShowAsync(_window, title, message, kind));
    }

    public Task<bool> ConfirmAsync(string title, string message, AlertKind kind = AlertKind.Question)
    {
        return Dispatcher.UIThread.InvokeAsync(() => MessageDialog.ConfirmAsync(_window, title, message, kind));
    }

    public Task<string?> SelectBranchAsync(IList<string> branches, string current)
    {
        return Dispatcher.UIThread.InvokeAsync(() => BranchDialog.ShowAsync(_window, branches, current));
    }

    public (int Width, int Height) GetScreenSize()
    {
        try
        {
            return Dispatcher.UIThread.Invoke(() =>
            {
                var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
                if (screen is null) return (0, 0);
                return (screen.Bounds.Width, screen.Bounds.Height);
            });
        }
        catch
        {
            return (0, 0);
        }
    }

    public void ExitApplication()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        });
    }
}
