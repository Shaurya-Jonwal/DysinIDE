using System.Collections.ObjectModel;

namespace DysinIDE.Models;

public sealed class FileNode : ObservableBase
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", "__pycache__", ".venv"
    };

    private bool _isExpanded;
    private bool _loaded;
    private string _name;
    private string _fullPath;

    public FileNode(string fullPath, bool isDirectory)
    {
        _fullPath = fullPath;
        _name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(_name))
            _name = fullPath;
        IsDirectory = isDirectory;
        if (isDirectory)
            Children.Add(Placeholder());
    }

    public bool IsDirectory { get; }
    public ObservableCollection<FileNode> Children { get; } = new();
    public FileNode? Parent { get; private set; }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public string FullPath
    {
        get => _fullPath;
        private set => Set(ref _fullPath, value);
    }

    public string Icon => IsPlaceholder ? "" : IsDirectory ? "▣" : "▢";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value))
                return;
            if (value && IsDirectory)
                Load();
        }
    }

    public bool IsPlaceholder { get; private init; }

    private static FileNode Placeholder() => new("…", false) { IsPlaceholder = true };

    public void Reload()
    {
        _loaded = false;
        Children.Clear();
        Children.Add(Placeholder());
        if (_isExpanded)
            Load();
    }

    public void Rename(string newPath)
    {
        FullPath = newPath;
        Name = Path.GetFileName(newPath);
    }

    private void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        Children.Clear();
        try
        {
            var dirs = Directory.EnumerateDirectories(FullPath)
                .Where(d => !Ignored.Contains(Path.GetFileName(d)))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirs)
                Children.Add(new FileNode(d, true) { Parent = this });

            var files = Directory.EnumerateFiles(FullPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                Children.Add(new FileNode(f, false) { Parent = this });
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }
}
