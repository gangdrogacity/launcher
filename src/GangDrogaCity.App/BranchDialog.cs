using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace GangDrogaCity.App;

/// <summary>Selezione del branch del modpack (funzione DEV).</summary>
public static class BranchDialog
{
    public static async Task<string?> ShowAsync(Window owner, IList<string> branches, string current)
    {
        var dialog = new Window
        {
            Title = "[DEV] Seleziona Branch",
            Width = 380,
            Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            ShowInTaskbar = false,
        };

        string? result = null;

        var label = new TextBlock
        {
            Text = "Seleziona il branch da utilizzare:",
            Foreground = Brushes.White,
            FontSize = 14,
            Margin = new Thickness(12, 12, 12, 6),
        };

        var list = new ListBox
        {
            ItemsSource = branches,
            Height = 290,
            Margin = new Thickness(12, 0),
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
            Foreground = Brushes.LightGreen,
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            SelectedItem = branches.Contains(current) ? current : null,
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is string s)
            {
                result = s;
                dialog.Close();
            }
        };

        var ok = new Button
        {
            Content = "Conferma",
            Width = 90,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        ok.Classes.Add("flat");
        ok.Click += (_, _) =>
        {
            result = list.SelectedItem as string;
            dialog.Close();
        };

        var cancel = new Button
        {
            Content = "Annulla",
            Width = 90,
            Height = 32,
            Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Foreground = Brushes.White,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancel.Classes.Add("flat");
        cancel.Click += (_, _) => { result = null; dialog.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(12, 10),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        dialog.Content = new StackPanel { Children = { label, list, buttons } };

        await dialog.ShowDialog(owner);
        return result;
    }
}
