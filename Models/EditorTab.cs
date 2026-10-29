using AvaloniaEdit.Document;
using DysinIDE.Services;

namespace DysinIDE.Models;

public sealed class EditorTab : ObservableBase
{
    private string? _path;
    private bool _isDirty;
    private string _title;
    private static int _untitled;

    public EditorTab(string? path, string text)
    {
        _path = path;
        _title = path is null ? $"Untitled-{++_untitled}" : System.IO.Path.GetFileName(path);
        Document = new TextDocument(text);
        Document.UndoStack.MarkAsOriginalFile();
        Document.TextChanged += (_, _) => IsDirty = !Document.UndoStack.IsOriginalFile;
    }

    public TextDocument Document { get; }
    public int CaretOffset { get; set; }
    public double ScrollOffsetY { get; set; }

    public string? Path
    {
        get => _path;
        set
        {
            if (Set(ref _path, value))
            {
                Title = value is null ? Title : System.IO.Path.GetFileName(value);
                Raise(nameof(Language));
            }
        }
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (Set(ref _isDirty, value))
                Raise(nameof(DirtyMarker));
        }
    }

    public string DirtyMarker => IsDirty ? "●" : "";
    public string Language => LanguageService.NameFor(_path);

    public void MarkSaved()
    {
        Document.UndoStack.MarkAsOriginalFile();
        IsDirty = false;
    }
}
