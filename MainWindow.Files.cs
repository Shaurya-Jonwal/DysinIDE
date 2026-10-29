using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DysinIDE.Dialogs;
using DysinIDE.Models;

namespace DysinIDE;

public partial class MainWindow
{
    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null)
            OpenWorkspace(path);
    }

    public void OpenWorkspace(string path)
    {
        _workspace = path;
        _roots.Clear();
        var root = new FileNode(path, true);
        _roots.Add(root);
        root.IsExpanded = true;
        SearchResults.ItemsSource = null;
        SearchSummary.Text = "";
        ShowSidebarPanel(true);
        UpdateChrome();
        if (_terminal.IsRunning)
            _terminal.Send(OperatingSystem.IsWindows() ? $"cd /d \"{path}\"" : $"cd \"{path}\"");
    }

    private void OnCloseFolder(object? sender, RoutedEventArgs e)
    {
        _workspace = null;
        _roots.Clear();
        UpdateChrome();
    }

    private void RefreshTree()
    {
        foreach (var root in _roots)
            RefreshNode(root);
    }

    private static void RefreshNode(FileNode node)
    {
        if (!node.IsDirectory)
            return;
        var expandedChildren = node.Children
            .Where(c => c.IsDirectory && c.IsExpanded)
            .Select(c => c.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        node.Reload();
        foreach (var child in node.Children.Where(c => c.IsDirectory && expandedChildren.Contains(c.FullPath)))
            child.IsExpanded = true;
    }

    private void OnRefreshTree(object? sender, RoutedEventArgs e) => RefreshTree();

    private void OnCollapseAll(object? sender, RoutedEventArgs e)
    {
        foreach (var root in _roots)
            Collapse(root, true);
    }

    private static void Collapse(FileNode node, bool keepRoot)
    {
        foreach (var child in node.Children)
            Collapse(child, false);
        if (!keepRoot)
            node.IsExpanded = false;
    }

    private FileNode? SelectedNode => FileTree.SelectedItem as FileNode;

    private string? TargetDirectory()
    {
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder)
            return _workspace;
        return node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
    }

    private FileNode? TargetNode()
    {
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder)
            return _roots.FirstOrDefault();
        return node.IsDirectory ? node : node.Parent;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { IsDirectory: false, IsPlaceholder: false } node)
            OpenPath(node.FullPath);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SelectedNode is { IsDirectory: false, IsPlaceholder: false } node:
                OpenPath(node.FullPath);
                e.Handled = true;
                break;
            case Key.F2:
                OnTreeRename(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Delete:
                OnTreeDelete(sender, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    private async void OnTreeNewFile(object? sender, RoutedEventArgs e)
    {
        var dir = TargetDirectory();
        if (dir is null)
            return;
        var name = await Dlg.Prompt(this, "New File", "File name", "");
        if (string.IsNullOrWhiteSpace(name))
            return;
        var path = Path.Combine(dir, name.Trim());
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
                await File.WriteAllTextAsync(path, "");
            var node = TargetNode();
            if (node is not null)
            {
                node.IsExpanded = true;
                RefreshNode(node);
            }

            OpenPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
        }
    }

    private async void OnTreeNewFolder(object? sender, RoutedEventArgs e)
    {
        var dir = TargetDirectory();
        if (dir is null)
            return;
        var name = await Dlg.Prompt(this, "New Folder", "Folder name", "");
        if (string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, name.Trim()));
            var node = TargetNode();
            if (node is not null)
            {
                node.IsExpanded = true;
                RefreshNode(node);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
        }
    }

    private async void OnTreeRename(object? sender, RoutedEventArgs e)
    {
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder || node.Parent is null && _roots.Contains(node))
            return;
        var name = await Dlg.Prompt(this, "Rename", "New name", node.Name);
        if (string.IsNullOrWhiteSpace(name) || name == node.Name)
            return;

        var oldPath = node.FullPath;
        var newPath = Path.Combine(Path.GetDirectoryName(oldPath)!, name.Trim());
        try
        {
            if (node.IsDirectory)
                Directory.Move(oldPath, newPath);
            else
                File.Move(oldPath, newPath);

            foreach (var tab in _tabs.Where(t => t.Path is not null && t.Path.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase)))
                tab.Path = newPath + tab.Path![oldPath.Length..];

            var parent = node.Parent;
            if (parent is not null)
                RefreshNode(parent);
            UpdateChrome();
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
        }
    }

    private async void OnTreeDelete(object? sender, RoutedEventArgs e)
    {
        var node = SelectedNode;
        if (node is null || node.IsPlaceholder || node.Parent is null && _roots.Contains(node))
            return;
        var ok = await Dlg.Confirm(this, "Delete", $"Delete '{node.Name}'? This cannot be undone.", "Delete");
        if (!ok)
            return;

        try
        {
            if (node.IsDirectory)
                Directory.Delete(node.FullPath, true);
            else
                File.Delete(node.FullPath);

            var doomed = _tabs
                .Where(t => t.Path is not null && t.Path.StartsWith(node.FullPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var tab in doomed)
            {
                tab.MarkSaved();
                await CloseTab(tab);
            }

            if (node.Parent is not null)
                RefreshNode(node.Parent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendTerminal($"[error] {ex.Message}\n");
            ShowTerminal();
        }
    }

    private void OnTreeOpenTerminal(object? sender, RoutedEventArgs e)
    {
        var dir = TargetDirectory();
        if (dir is null)
            return;
        ShowTerminal();
        StartTerminal(dir);
    }

    private async void OnTreeCopyPath(object? sender, RoutedEventArgs e)
    {
        var node = SelectedNode;
        if (node is null || Clipboard is null)
            return;
        await Clipboard.SetTextAsync(node.FullPath);
    }
}
