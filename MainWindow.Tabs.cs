using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using DysinIDE.Dialogs;
using DysinIDE.Models;
using DysinIDE.Services;

namespace DysinIDE;

public partial class MainWindow
{
    private void OnTabSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_switching)
            return;
        Activate(TabList.SelectedItem as EditorTab);
    }

    private void Activate(EditorTab? tab)
    {
        if (_current is not null)
        {
            _current.CaretOffset = Editor.CaretOffset;
            _current.ScrollOffsetY = Editor.VerticalOffset;
        }

        _current = tab;
        if (tab is null)
        {
            Editor.Document = new TextDocument();
        }
        else
        {
            Editor.Document = tab.Document;
            ApplyGrammar(tab);
            Editor.CaretOffset = Math.Min(tab.CaretOffset, tab.Document.TextLength);
            Editor.ScrollToVerticalOffset(tab.ScrollOffsetY);
            Editor.TextArea.Focus();
        }

        UpdateChrome();
        UpdateStatus();
    }

    private void ApplyGrammar(EditorTab tab)
    {
        if (_registry is null || _textMate is null)
            return;

        var isPython = tab.Language == "Python";
        _python.Active = isPython;
        Editor.TextArea.IndentationStrategy = isPython ? _pythonIndent : _csharpIndent;
        if (isPython)
        {
            _textMate.SetGrammar(null);
            Editor.TextArea.TextView.Redraw();
            return;
        }

        Editor.TextArea.TextView.Redraw();
        var ext = LanguageService.ExtensionFor(tab.Path).ToLowerInvariant();
        if (ext is ".axaml" or ".xaml" or ".csproj" or ".props" or ".targets")
            ext = ".xml";
        if (ext is ".csx")
            ext = ".cs";
        var lang = _registry.GetLanguageByExtension(ext);
        _textMate.SetGrammar(lang is null ? null : _registry.GetScopeByLanguageId(lang.Id));
    }

    private void Select(EditorTab tab)
    {
        if (!_tabs.Contains(tab))
            _tabs.Add(tab);
        TabList.SelectedItem = tab;
        TabList.ScrollIntoView(tab);
        if (!ReferenceEquals(_current, tab))
            Activate(tab);
    }

    public void OpenPath(string path)
    {
        var existing = _tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Select(existing);
            return;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return;
            if (info.Length > 20_000_000)
            {
                AppendTerminal($"[skipped] {path} is larger than 20 MB\n");
                ShowTerminal();
                return;
            }

            var tab = new EditorTab(path, File.ReadAllText(path));
            Select(tab);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
        }
    }

    private async Task<bool> SaveTab(EditorTab tab)
    {
        if (tab.Path is null)
            return await SaveTabAs(tab);

        try
        {
            await File.WriteAllTextAsync(tab.Path, tab.Document.Text, new UTF8Encoding(false));
            tab.MarkSaved();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
            return false;
        }
    }

    private async Task<bool> SaveTabAs(EditorTab tab)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = tab.Title,
            ShowOverwritePrompt = true
        });
        var path = file?.TryGetLocalPath();
        if (path is null)
            return false;

        tab.Path = path;
        var ok = await SaveTab(tab);
        if (ok)
        {
            ApplyGrammar(tab);
            UpdateChrome();
            UpdateStatus();
            if (_workspace is not null && path.StartsWith(_workspace, StringComparison.OrdinalIgnoreCase))
                RefreshTree();
        }

        return ok;
    }

    private async Task CloseTab(EditorTab tab)
    {
        if (tab.IsDirty)
        {
            var choice = await Dlg.ConfirmSave(this, tab.Title);
            if (choice == 2)
                return;
            if (choice == 0 && !await SaveTab(tab))
                return;
        }

        var index = _tabs.IndexOf(tab);
        if (index < 0)
            return;

        var wasCurrent = ReferenceEquals(tab, _current);
        _switching = true;
        _tabs.RemoveAt(index);
        _switching = false;

        if (!wasCurrent)
            return;

        _current = null;
        if (_tabs.Count == 0)
        {
            TabList.SelectedItem = null;
            Activate(null);
        }
        else
        {
            var next = _tabs[Math.Min(index, _tabs.Count - 1)];
            TabList.SelectedItem = next;
            Activate(next);
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: EditorTab tab })
            return;
        if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed)
        {
            _ = CloseTab(tab);
            e.Handled = true;
        }
    }

    private void OnTabClose(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditorTab tab })
            _ = CloseTab(tab);
        e.Handled = true;
    }

    private void OnNewFile(object? sender, RoutedEventArgs e) => Select(new EditorTab(null, ""));

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true
        });
        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (path is not null)
                OpenPath(path);
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_current is not null)
            await SaveTab(_current);
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        if (_current is not null)
            await SaveTabAs(_current);
    }

    private async void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        foreach (var tab in _tabs.Where(t => t.IsDirty).ToList())
            await SaveTab(tab);
    }

    private void OnCloseTab(object? sender, RoutedEventArgs e)
    {
        if (_current is not null)
            _ = CloseTab(_current);
    }
}