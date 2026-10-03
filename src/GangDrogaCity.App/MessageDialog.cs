using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GangDrogaCity.Core;

namespace GangDrogaCity.App;

/// <summary>
/// Finestra di messaggio minimale (OK oppure Si/No), sostituisce MessageBox di WinForms.
/// </summary>
public static class MessageDialog
{
    public static Task ShowAsync(Window owner, string title, string message, AlertKind kind = AlertKind.Info)
        => ShowCoreAsync(owner, title, message, kind, confirm: false);

    public static async Task<bool> ConfirmAsync(Window owner, string title, string message, AlertKind kind = AlertKind.Question)
        => await ShowCoreAsync(owner, title, message, kind, confirm: true);

    private static async Task<bool> ShowCoreAsync(Window owner, string title, string message, AlertKind kind, bool confirm)
    {
        var accent = kind switch
        {
            AlertKind.Error => Brushes.Firebrick,
            AlertKind.Warning => Brushes.DarkGoldenrod,
            AlertKind.Question => Brushes.Teal,
            _ => Brushes.SteelBlue,
        };

        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.None,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            ShowInTaskbar = false,
        };

        var result = false;

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(14, 10),
        };
        var header = new Border { Background = accent, Child = titleBlock };

        var body = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 16, 16, 8),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16),
            Spacing = 8,
        };

        if (confirm)
        {
            var yes = MakeButton("Sì", Brushes.Green);
            yes.Click += (_, _) => { result = true; dialog.Close(); };
            var no = MakeButton("No", new SolidColorBrush(Color.FromRgb(80, 80, 80)));
            no.Click += (_, _) => { result = false; dialog.Close(); };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = MakeButton("OK", new SolidColorBrush(Color.FromRgb(0, 120, 215)));
            ok.Click += (_, _) => { result = true; dialog.Close(); };
            buttons.Children.Add(ok);
        }

        var root = new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { header, body, buttons } },
        };
        dialog.Content = root;

        if (owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            dialog.Closed += (_, _) => tcs.TrySetResult();
            dialog.Show();
            await tcs.Task;
        }
        return result;
    }

    private static Button MakeButton(string text, IBrush background)
    {
        var button = new Button
        {
            Content = text,
            Background = background,
            Foreground = Brushes.White,
            MinWidth = 90,
            Height = 32,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("flat");
        return button;
    }
}
