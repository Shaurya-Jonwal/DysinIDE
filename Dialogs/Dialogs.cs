using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DysinIDE.Dialogs;

public static class Dlg
{
    private static IBrush Res(Window w, string key)
    {
        if (w.TryFindResource(key, w.ActualThemeVariant, out var v) && v is IBrush b)
            return b;
        return Brushes.Gray;
    }

    private static Window Shell(Window owner, string title, double width)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Res(owner, "Bg2Brush"),
            Foreground = Res(owner, "Fg1Brush")
        };
    }

    private static Button MakeButton(Window owner, string text, bool primary)
    {
        return new Button
        {
            Content = text,
            MinWidth = 84,
            Padding = new Thickness(14, 6),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = primary ? Brushes.White : Res(owner, "Bg4Brush"),
            Foreground = primary ? Brushes.Black : Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4)
        };
    }

    public static async Task<string?> Prompt(Window owner, string title, string message, string initial = "")
    {
        var dlg = Shell(owner, title, 420);
        string? result = null;

        var box = new TextBox { Text = initial, Margin = new Thickness(0, 10, 0, 16) };
        var ok = MakeButton(owner, "OK", true);
        var cancel = MakeButton(owner, "Cancel", false);

        ok.Click += (_, _) =>
        {
            result = box.Text;
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = box.Text;
                dlg.Close();
            }
            else if (e.Key == Key.Escape)
            {
                dlg.Close();
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, ok }
        };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = message, Foreground = Res(owner, "Fg0Brush") }, box, buttons }
        };

        dlg.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task<int> ConfirmSave(Window owner, string name)
    {
        var dlg = Shell(owner, "Unsaved changes", 440);
        var result = 2;

        var save = MakeButton(owner, "Save", true);
        var discard = MakeButton(owner, "Don't Save", false);
        var cancel = MakeButton(owner, "Cancel", false);

        save.Click += (_, _) =>
        {
            result = 0;
            dlg.Close();
        };
        discard.Click += (_, _) =>
        {
            result = 1;
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { cancel, discard, save }
        };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = $"Do you want to save the changes you made to {name}?",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Res(owner, "Fg0Brush")
                },
                new TextBlock
                {
                    Text = "Your changes will be lost if you don't save them.",
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = Res(owner, "Fg2Brush")
                },
                buttons
            }
        };

        await dlg.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> Confirm(Window owner, string title, string message, string action)
    {
        var dlg = Shell(owner, title, 440);
        var result = false;

        var ok = MakeButton(owner, action, true);
        var cancel = MakeButton(owner, "Cancel", false);
        ok.Click += (_, _) =>
        {
            result = true;
            dlg.Close();
        };
        cancel.Click += (_, _) => dlg.Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { cancel, ok }
        };

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Res(owner, "Fg0Brush") },
                buttons
            }
        };

        await dlg.ShowDialog(owner);
        return result;
    }
}
