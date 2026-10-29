using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DysinIDE.Models;
using DysinIDE.Services;

namespace DysinIDE;

public partial class MainWindow
{
    private List<PaletteItem> _paletteSource = new();
    private CancellationTokenSource? _searchCts;

    private void ShowSidebarPanel(bool explorer)
    {
        Sidebar.IsVisible = true;
        SidebarSplitter.IsVisible = true;
        ExplorerPanel.IsVisible = explorer;
        SearchPanelHost.IsVisible = !explorer;
        SetActive(ActExplorer, explorer);
        SetActive(ActSearch, !explorer);
    }

    private static void SetActive(Button button, bool active)
    {
        if (active)
            button.Classes.Add("active");
        else
            button.Classes.Remove("active");
    }

    private void OnShowExplorer(object? sender, RoutedEventArgs e)
    {
        if (Sidebar.IsVisible && ExplorerPanel.IsVisible)
        {
            Sidebar.IsVisible = false;
            SidebarSplitter.IsVisible = false;
            return;
        }

        ShowSidebarPanel(true);
        FileTree.Focus();
    }

    private void OnToggleSidebar(object? sender, RoutedEventArgs e)
    {
        var show = !Sidebar.IsVisible;
        Sidebar.IsVisible = show;
        SidebarSplitter.IsVisible = show;
    }

    private void OnFindInFiles(object? sender, RoutedEventArgs e)
    {
        ShowSidebarPanel(false);
        if (_current is not null && Editor.SelectionLength > 0 && !Editor.SelectedText.Contains('\n'))
            SearchBox.Text = Editor.SelectedText;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;

        var query = SearchBox.Text ?? "";
        if (_workspace is null)
        {
            SearchSummary.Text = "Open a folder to search";
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var root = _workspace;
        var matchCase = SearchCase.IsChecked == true;
        SearchSummary.Text = "Searching...";

        var hits = await Task.Run(() => WorkspaceSearch.Search(root, query, matchCase, 500, token), token)
            .ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : new List<SearchHit>());

        if (token.IsCancellationRequested)
            return;

        SearchResults.ItemsSource = hits;
        var files = hits.Select(h => h.Path).Distinct().Count();
        SearchSummary.Text = hits.Count == 0
            ? "No results"
            : $"{hits.Count}{(hits.Count >= 500 ? "+" : "")} results in {files} files";
    }

    private void OnSearchResultOpen(object? sender, TappedEventArgs e) => OpenSelectedHit();

    private void OnSearchResultKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        OpenSelectedHit();
        e.Handled = true;
    }

    private void OpenSelectedHit()
    {
        if (SearchResults.SelectedItem is not SearchHit hit)
            return;
        OpenPath(hit.Path);
        if (_current is null)
            return;
        var line = Math.Clamp(hit.Line, 1, Editor.Document.LineCount);
        var docLine = Editor.Document.GetLineByNumber(line);
        Editor.Select(docLine.Offset, docLine.Length);
        Editor.ScrollToLine(line);
    }

    private void OnCommandPalette(object? sender, RoutedEventArgs e)
    {
        _paletteSource = BuildCommands();
        ShowPalette("Type a command", "");
    }

    private void OnQuickOpen(object? sender, RoutedEventArgs e)
    {
        var items = new List<PaletteItem>();
        if (_workspace is not null)
        {
            foreach (var file in WorkspaceSearch.EnumerateFiles(_workspace).Take(8000))
            {
                var path = file;
                var rel = Path.GetRelativePath(_workspace, path);
                var dir = Path.GetDirectoryName(rel) ?? "";
                items.Add(new PaletteItem(Path.GetFileName(path), dir, () => OpenPath(path)));
            }
        }

        foreach (var tab in _tabs.Where(t => t.Path is null))
        {
            var t = tab;
            items.Add(new PaletteItem(t.Title, "unsaved", () => Select(t)));
        }

        _paletteSource = items;
        ShowPalette("Go to file", "");
    }

    private void ShowPalette(string watermark, string text)
    {
        PaletteBox.Watermark = watermark;
        PaletteBox.Text = text;
        PaletteOverlay.IsVisible = true;
        RefreshPalette();
        Dispatcher.UIThread.Post(() => PaletteBox.Focus(), DispatcherPriority.Input);
    }

    private void HidePalette()
    {
        PaletteOverlay.IsVisible = false;
        if (_current is not null)
            Editor.TextArea.Focus();
    }

    private void RefreshPalette()
    {
        var query = (PaletteBox.Text ?? "").Trim();
        IEnumerable<PaletteItem> result = _paletteSource;
        if (query.Length > 0)
        {
            result = _paletteSource
                .Select(i => (Item: i, Score: Score(i.Title, query)))
                .Where(x => x.Score >= 0)
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Item.Title.Length)
                .Select(x => x.Item);
        }

        var list = result.Take(60).ToList();
        PaletteList.ItemsSource = list;
        if (list.Count > 0)
            PaletteList.SelectedIndex = 0;
    }

    private static int Score(string text, string query)
    {
        var idx = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return idx;

        var ti = 0;
        var gaps = 0;
        foreach (var c in query)
        {
            var found = text.IndexOf(c.ToString(), ti, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return -1;
            gaps += found - ti;
            ti = found + 1;
        }

        return 1000 + gaps;
    }

    private void OnPaletteTextChanged(object? sender, TextChangedEventArgs e) => RefreshPalette();

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        var count = (PaletteList.ItemsSource as IReadOnlyCollection<PaletteItem>)?.Count ?? 0;
        switch (e.Key)
        {
            case Key.Down when count > 0:
                PaletteList.SelectedIndex = Math.Min(count - 1, PaletteList.SelectedIndex + 1);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem!);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                PaletteList.SelectedIndex = Math.Max(0, PaletteList.SelectedIndex - 1);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem!);
                e.Handled = true;
                break;
            case Key.Enter:
                RunPaletteSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                HidePalette();
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteDoubleTapped(object? sender, TappedEventArgs e) => RunPaletteSelection();

    private void RunPaletteSelection()
    {
        if (PaletteList.SelectedItem is not PaletteItem item)
            return;
        HidePalette();
        Dispatcher.UIThread.Post(item.Run, DispatcherPriority.Background);
    }

    private void OnOverlayPressed(object? sender, PointerPressedEventArgs e) => HidePalette();
    private void OnPalettePressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private List<PaletteItem> BuildCommands()
    {
        PaletteItem Cmd(string title, string key, Action run) => new(title, key, run);
        RoutedEventArgs Args() => new();

        return new List<PaletteItem>
        {
            Cmd("File: New File", "Ctrl+N", () => OnNewFile(this, Args())),
            Cmd("File: Open File...", "Ctrl+O", () => OnOpenFile(this, Args())),
            Cmd("File: Open Folder...", "Ctrl+K", () => OnOpenFolder(this, Args())),
            Cmd("File: Save", "Ctrl+S", () => OnSave(this, Args())),
            Cmd("File: Save As...", "Ctrl+Shift+S", () => OnSaveAs(this, Args())),
            Cmd("File: Save All", "", () => OnSaveAll(this, Args())),
            Cmd("File: Close Editor", "Ctrl+W", () => OnCloseTab(this, Args())),
            Cmd("File: Close Folder", "", () => OnCloseFolder(this, Args())),
            Cmd("Go to File...", "Ctrl+P", () => OnQuickOpen(this, Args())),
            Cmd("Go to Line...", "Ctrl+G", () => OnGoToLine(this, Args())),
            Cmd("Edit: Find", "Ctrl+F", () => OnFind(this, Args())),
            Cmd("Edit: Find in Files", "Ctrl+Shift+F", () => OnFindInFiles(this, Args())),
            Cmd("Edit: Duplicate Line", "Ctrl+D", () => OnDuplicateLine(this, Args())),
            Cmd("Edit: Delete Line", "Ctrl+Shift+K", () => OnDeleteLine(this, Args())),
            Cmd("Edit: Toggle Line Comment", "Ctrl+/", () => OnToggleComment(this, Args())),
            Cmd("View: Toggle Sidebar", "Ctrl+B", () => OnToggleSidebar(this, Args())),
            Cmd("View: Show Explorer", "Ctrl+Shift+E", () => OnShowExplorer(this, Args())),
            Cmd("View: Toggle Terminal", "Ctrl+`", () => OnToggleTerminal(this, Args())),
            Cmd("View: Toggle Word Wrap", "Alt+Z", () => OnToggleWrap(this, Args())),
            Cmd("View: Zoom In", "Ctrl+=", () => OnZoomIn(this, Args())),
            Cmd("View: Zoom Out", "Ctrl+-", () => OnZoomOut(this, Args())),
            Cmd("View: Reset Zoom", "Ctrl+0", () => OnZoomReset(this, Args())),
            Cmd("Terminal: New Terminal", "", () => OnNewTerminal(this, Args())),
            Cmd("Terminal: Clear", "", () => OnClearTerminal(this, Args())),
            Cmd("Run: dotnet run", "F5", () => OnDotnetRun(this, Args())),
            Cmd("Run: dotnet build", "Ctrl+Shift+B", () => OnDotnetBuild(this, Args())),
            Cmd("Run: dotnet test", "", () => OnDotnetTest(this, Args())),
            Cmd("Application: Exit", "", () => Close())
        };
    }
}
