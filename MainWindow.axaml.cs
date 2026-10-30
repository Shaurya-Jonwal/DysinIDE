using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Indentation.CSharp;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using DysinIDE.Dialogs;
using DysinIDE.Models;
using DysinIDE.Services;
using TextMateSharp.Grammars;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DysinIDE;

public partial class MainWindow : Window
{
    private const double DefaultFontSize = 14;

    private readonly ObservableCollection<EditorTab> _tabs = new();
    private readonly ObservableCollection<FileNode> _roots = new();
    private readonly TerminalSession _terminal = new();
    private AvaloniaEdit.TextMate.TextMate.Installation? _textMate;
    private RegistryOptions? _registry;
    private SearchPanel? _searchPanel;
    private readonly PythonColorizer _python = new();
    private readonly PythonIndentationStrategy _pythonIndent = new();
    private AvaloniaEdit.Indentation.IIndentationStrategy? _csharpIndent;
    private string? _workspace;
    private EditorTab? _current;
    private bool _switching;
    private bool _allowClose;
    private double _fontSize = DefaultFontSize;

    public MainWindow()
    {
        InitializeComponent();
        Icon = new WindowIcon(
            AssetLoader.Open(
                new Uri("avares://DysinIDE/Assets/favicon.ico")
            )
        );
        ConfigureEditor();
        TabList.ItemsSource = _tabs;
        FileTree.ItemsSource = _roots;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        _terminal.Output += text => Dispatcher.UIThread.Post(() => AppendTerminal(text));
        _terminal.Finished += (code, stopped) => Dispatcher.UIThread.Post(() => OnTerminalFinished(code, stopped));
        TerminalInput.AddHandler(KeyDownEvent, OnTerminalKeyDown, RoutingStrategies.Tunnel);
        UpdateChrome();
        UpdateStatus();
    }

    private void ConfigureEditor()
    {
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.AllowScrollBelowDocument = true;
        Editor.LineNumbersForeground = new SolidColorBrush(Color.Parse("#4A4A4A"));
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#111111"));
        Editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#1E1E1E")), 1);
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#3A3A3A"));
        Editor.TextArea.SelectionCornerRadius = 2;
        _csharpIndent = new CSharpIndentationStrategy(Editor.Options);
        Editor.TextArea.IndentationStrategy = _csharpIndent;
        Editor.TextArea.TextView.LineTransformers.Add(_python);
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        Editor.TextArea.SelectionChanged += (_, _) => UpdateStatus();
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.PointerWheelChanged += OnEditorWheel;
        _searchPanel = SearchPanel.Install(Editor);

        _registry = new RegistryOptions(ThemeName.HighContrastDark);
        _textMate = Editor.InstallTextMate(_registry);
        Editor.Background = new SolidColorBrush(Colors.Black);
    }

    private void OnEditorWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        SetZoom(_fontSize + (e.Delta.Y > 0 ? 1 : -1));
        e.Handled = true;
    }

    private void SetZoom(double size)
    {
        _fontSize = Math.Clamp(size, 8, 40);
        Editor.FontSize = _fontSize;
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 })
            return;
        var c = e.Text[0];
        if (c is not (')' or ']' or '}' or '"' or '\''))
            return;
        var offset = Editor.CaretOffset;
        if (offset < Editor.Document.TextLength && Editor.Document.GetCharAt(offset) == c)
        {
            Editor.CaretOffset = offset + 1;
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: 1 })
            return;
        var c = e.Text[0];
        var closer = c switch
        {
            '(' => ")",
            '[' => "]",
            '{' => "}",
            '"' => "\"",
            '\'' => "'",
            _ => null
        };
        if (closer is null)
            return;

        var offset = Editor.CaretOffset;
        if (c is '"' or '\'')
        {
            if (offset >= 2)
            {
                var before = Editor.Document.GetCharAt(offset - 2);
                if (char.IsLetterOrDigit(before))
                    return;
            }
        }

        if (offset < Editor.Document.TextLength)
        {
            var next = Editor.Document.GetCharAt(offset);
            if (char.IsLetterOrDigit(next))
                return;
        }

        Editor.Document.Insert(offset, closer);
        Editor.CaretOffset = offset;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (e.Key == Key.Escape && PaletteOverlay.IsVisible)
        {
            HidePalette();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            RunCurrentFile();
            e.Handled = true;
            return;
        }

        if (alt && e.Key == Key.Z)
        {
            OnToggleWrap(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (!ctrl)
            return;

        if (e.Key == Key.D && TerminalInput.IsFocused)
            return;

        var handled = true;
        switch (e.Key)
        {
            case Key.S when shift: OnSaveAs(this, new RoutedEventArgs()); break;
            case Key.S: OnSave(this, new RoutedEventArgs()); break;
            case Key.N: OnNewFile(this, new RoutedEventArgs()); break;
            case Key.O: OnOpenFile(this, new RoutedEventArgs()); break;
            case Key.K when shift: OnDeleteLine(this, new RoutedEventArgs()); break;
            case Key.K: OnOpenFolder(this, new RoutedEventArgs()); break;
            case Key.W: OnCloseTab(this, new RoutedEventArgs()); break;
            case Key.P when shift: OnCommandPalette(this, new RoutedEventArgs()); break;
            case Key.P: OnQuickOpen(this, new RoutedEventArgs()); break;
            case Key.B when shift: OnDotnetBuild(this, new RoutedEventArgs()); break;
            case Key.B: OnToggleSidebar(this, new RoutedEventArgs()); break;
            case Key.E when shift: OnShowExplorer(this, new RoutedEventArgs()); break;
            case Key.F when shift: OnFindInFiles(this, new RoutedEventArgs()); break;
            case Key.F: OnFind(this, new RoutedEventArgs()); break;
            case Key.G: OnGoToLine(this, new RoutedEventArgs()); break;
            case Key.D: OnDuplicateLine(this, new RoutedEventArgs()); break;
            case Key.Oem2: OnToggleComment(this, new RoutedEventArgs()); break;
            case Key.Oem3: OnToggleTerminal(this, new RoutedEventArgs()); break;
            case Key.OemPlus: OnZoomIn(this, new RoutedEventArgs()); break;
            case Key.OemMinus: OnZoomOut(this, new RoutedEventArgs()); break;
            case Key.D0: OnZoomReset(this, new RoutedEventArgs()); break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    private void UpdateStatus()
    {
        if (_current is null)
        {
            StatusPosition.Text = "";
            StatusSelection.Text = "";
            StatusLanguage.Text = "";
            return;
        }

        var caret = Editor.TextArea.Caret;
        StatusPosition.Text = $"Ln {caret.Line}, Col {caret.Column}";
        StatusSelection.Text = Editor.SelectionLength > 0 ? $"({Editor.SelectionLength} selected)" : "";
        StatusLanguage.Text = _current.Language;
    }

    private void UpdateChrome()
    {
        var hasFolder = _workspace is not null;
        NoFolderPanel.IsVisible = !hasFolder;
        FileTree.IsVisible = hasFolder;
        ExplorerTitle.Text = hasFolder ? Path.GetFileName(_workspace!.TrimEnd(Path.DirectorySeparatorChar)).ToUpperInvariant() : "EXPLORER";
        StatusFolder.Text = hasFolder ? _workspace : "";
        StatusBranch.Text = hasFolder ? ReadBranch(_workspace!) : "";

        var hasTab = _current is not null;
        Editor.IsVisible = hasTab;
        Welcome.IsVisible = !hasTab;
        TitleText.Text = hasTab
            ? $"{_current!.Title} — {(hasFolder ? Path.GetFileName(_workspace!.TrimEnd(Path.DirectorySeparatorChar)) : "DysinIDE")}"
            : (hasFolder ? Path.GetFileName(_workspace!.TrimEnd(Path.DirectorySeparatorChar)) + " — DysinIDE" : "DysinIDE");
        Title = TitleText.Text ?? "DysinIDE";
    }

    private static string ReadBranch(string root)
    {
        try
        {
            var head = Path.Combine(root, ".git", "HEAD");
            if (!File.Exists(head))
                return "";
            var text = File.ReadAllText(head).Trim();
            const string prefix = "ref: refs/heads/";
            return text.StartsWith(prefix) ? "⎇ " + text[prefix.Length..] : "⎇ " + text[..Math.Min(7, text.Length)];
        }
        catch (IOException)
        {
            return "";
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose || !_tabs.Any(t => t.IsDirty))
        {
            _terminal.Dispose();
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        foreach (var tab in _tabs.Where(t => t.IsDirty).ToList())
        {
            var choice = await Dlg.ConfirmSave(this, tab.Title);
            if (choice == 2)
                return;
            if (choice == 0 && !await SaveTab(tab))
                return;
        }

        _allowClose = true;
        Close();
    }

    private void OnExit(object? sender, RoutedEventArgs e) => Close();
}