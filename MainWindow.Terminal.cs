using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DysinIDE;

public partial class MainWindow
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);
    private readonly List<string> _history = new();
    private int _historyIndex;

    private string TerminalRoot() =>
        _workspace ?? (_current?.Path is { } p ? Path.GetDirectoryName(p)! : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private void StartTerminal(string directory)
    {
        TerminalOutput.Text = "";
        try
        {
            _terminal.Start(directory);
            AppendTerminal($"[{directory}]\n");
        }
        catch (Exception ex)
        {
            AppendTerminal($"[error] could not start shell: {ex.Message}\n");
        }
    }

    private void ShowTerminal()
    {
        TerminalPanel.IsVisible = true;
        TerminalSplitter.IsVisible = true;
        if (!_terminal.IsRunning)
            StartTerminal(TerminalRoot());
        TerminalInput.Focus();
    }

    private void OnToggleTerminal(object? sender, RoutedEventArgs e)
    {
        if (TerminalPanel.IsVisible)
        {
            TerminalPanel.IsVisible = false;
            TerminalSplitter.IsVisible = false;
            if (_current is not null)
                Editor.TextArea.Focus();
        }
        else
        {
            ShowTerminal();
        }
    }

    private void OnNewTerminal(object? sender, RoutedEventArgs e)
    {
        TerminalPanel.IsVisible = true;
        TerminalSplitter.IsVisible = true;
        StartTerminal(TerminalRoot());
        TerminalInput.Focus();
    }

    private void OnClearTerminal(object? sender, RoutedEventArgs e) => TerminalOutput.Text = "";

    private void AppendTerminal(string text)
    {
        var clean = Ansi.Replace(text, "").Replace("\r", "");
        var current = TerminalOutput.Text ?? "";
        var combined = current + clean;
        if (combined.Length > 200_000)
            combined = combined[^150_000..];
        TerminalOutput.Text = combined;
        TerminalOutput.CaretIndex = combined.Length;
    }

    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            {
                var line = TerminalInput.Text ?? "";
                TerminalInput.Text = "";
                if (line.Length > 0)
                {
                    _history.Add(line);
                    _historyIndex = _history.Count;
                }

                AppendTerminal($"❯ {line}\n");
                if (!_terminal.IsRunning)
                    StartTerminal(TerminalRoot());
                _terminal.Send(line);
                e.Handled = true;
                break;
            }
            case Key.Up when _history.Count > 0:
                _historyIndex = Math.Max(0, _historyIndex - 1);
                TerminalInput.Text = _history[_historyIndex];
                TerminalInput.CaretIndex = TerminalInput.Text.Length;
                e.Handled = true;
                break;
            case Key.Down when _history.Count > 0:
                _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                TerminalInput.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                TerminalInput.CaretIndex = TerminalInput.Text.Length;
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control) && string.IsNullOrEmpty(TerminalInput.SelectedText):
                _terminal.Stop();
                AppendTerminal("^C\n");
                StartTerminal(TerminalRoot());
                e.Handled = true;
                break;
        }
    }

    private async void RunInTerminal(string command)
    {
        foreach (var tab in _tabs.Where(t => t.IsDirty && t.Path is not null).ToList())
            await SaveTab(tab);
        ShowTerminal();
        AppendTerminal($"❯ {command}\n");
        _terminal.Send(command);
    }

    private void OnDotnetRun(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet run");
    private void OnDotnetBuild(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet build");
    private void OnDotnetTest(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet test");
}
