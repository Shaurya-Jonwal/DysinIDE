using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DysinIDE;

public partial class MainWindow
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07", RegexOptions.Compiled);
    private static readonly Regex ChangeDir = new(@"^(cd|chdir)(\s+/d)?(\s+(?<arg>.+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReplCommand = new(@"^(python3?|py|node)(\.exe)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DriveSwitch = new(@"^[A-Za-z]:$", RegexOptions.Compiled);
    private readonly List<string> _history = new();
    private int _historyIndex;
    private bool _terminalReady;
    private const bool TerminalDebug = true;

    private string TerminalRoot() =>
        _workspace ?? (_current?.Path is { } p ? Path.GetDirectoryName(p)! : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private void StartTerminal(string directory)
    {
        TerminalOutput.Text = "";
        _terminal.Verbose = TerminalDebug;
        _terminal.Start(directory);
        _terminalReady = true;
        AppendTerminal($"{_terminal.WorkingDirectory}\n");
        if (TerminalDebug)
            _terminal.Run("echo terminal self-test ok");
    }

    private void ShowTerminal()
    {
        TerminalPanel.IsVisible = true;
        TerminalSplitter.IsVisible = true;
        if (!_terminalReady)
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

    private void AppendTerminalLine(string text)
    {
        var current = TerminalOutput.Text ?? "";
        if (current.Length > 0 && !current.EndsWith('\n'))
            text = "\n" + text;
        AppendTerminal(text + "\n");
    }

    private string PromptPath()
    {
        var dir = _terminal.WorkingDirectory;
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? dir : name;
    }

    private void OnTerminalFinished(int code, bool stopped)
    {
        if (stopped)
            AppendTerminalLine("[stopped]");
        else if (TerminalDebug || code != 0)
            AppendTerminalLine($"[exit code {code}]");
    }

    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.Enter:
                SubmitTerminalLine();
                e.Handled = true;
                break;
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
            case Key.C when ctrl && _terminal.IsBusy && string.IsNullOrEmpty(TerminalInput.SelectedText):
                _terminal.Stop();
                e.Handled = true;
                break;
            case Key.D when ctrl && _terminal.IsBusy:
                _terminal.CloseInput();
                e.Handled = true;
                break;
        }
    }

    private void SubmitTerminalLine()
    {
        var line = TerminalInput.Text ?? "";
        TerminalInput.Text = "";

        if (TerminalDebug)
            AppendTerminalLine($"[enter] busy={_terminal.IsBusy} text='{line}'");

        if (_terminal.IsBusy)
        {
            AppendTerminalLine(line);
            _terminal.SendInput(line);
            return;
        }

        var command = line.Trim();
        if (command.Length == 0)
            return;

        _history.Add(command);
        _historyIndex = _history.Count;
        AppendTerminalLine($"{PromptPath()} ❯ {command}");

        if (HandleBuiltIn(command))
            return;

        _terminal.Run(ReplCommand.IsMatch(command) ? command + " -i" : command);
    }

    private bool HandleBuiltIn(string command)
    {
        if (command.Equals("cls", StringComparison.OrdinalIgnoreCase) || command.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            TerminalOutput.Text = "";
            return true;
        }

        if (OperatingSystem.IsWindows() && DriveSwitch.IsMatch(command))
        {
            var root = command.ToUpperInvariant() + Path.DirectorySeparatorChar;
            if (Directory.Exists(root))
                _terminal.SetWorkingDirectory(root);
            else
                AppendTerminalLine("The system cannot find the drive specified.");
            return true;
        }

        var match = ChangeDir.Match(command);
        if (!match.Success)
            return false;

        var arg = match.Groups["arg"].Success ? match.Groups["arg"].Value.Trim().Trim('"') : "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (arg.Length == 0)
        {
            if (OperatingSystem.IsWindows())
                AppendTerminalLine(_terminal.WorkingDirectory);
            else
                _terminal.SetWorkingDirectory(home);
            return true;
        }

        if (arg == "~")
            arg = home;
        else if (arg.StartsWith("~/") || arg.StartsWith("~\\"))
            arg = Path.Combine(home, arg[2..]);

        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(_terminal.WorkingDirectory, arg));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AppendTerminalLine("The filename, directory name, or volume label syntax is incorrect.");
            return true;
        }

        if (Directory.Exists(target))
            _terminal.SetWorkingDirectory(target);
        else
            AppendTerminalLine("The system cannot find the path specified.");
        return true;
    }

    private async void RunInTerminal(string command)
    {
        foreach (var tab in _tabs.Where(t => t.IsDirty && t.Path is not null).ToList())
            await SaveTab(tab);
        ShowTerminal();
        AppendTerminalLine($"{PromptPath()} ❯ {command}");
        _terminal.Run(command);
    }

    private void RunCurrentFile()
    {
        if (_current is { Path: { } path } && _current.Language == "Python")
        {
            var exe = OperatingSystem.IsWindows() ? "python" : "python3";
            RunInTerminal($"{exe} \"{path}\"");
            return;
        }

        RunInTerminal("dotnet run");
    }

    private void OnDotnetRun(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet run");
    private void OnDotnetBuild(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet build");
    private void OnDotnetTest(object? sender, RoutedEventArgs e) => RunInTerminal("dotnet test");
}