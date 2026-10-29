using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using DysinIDE.Dialogs;

namespace DysinIDE;

public partial class MainWindow
{
    private void OnUndo(object? sender, RoutedEventArgs e) => Editor.Undo();
    private void OnRedo(object? sender, RoutedEventArgs e) => Editor.Redo();
    private void OnCut(object? sender, RoutedEventArgs e) => Editor.Cut();
    private void OnCopy(object? sender, RoutedEventArgs e) => Editor.Copy();
    private void OnPaste(object? sender, RoutedEventArgs e) => Editor.Paste();

    private void OnFind(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        _searchPanel?.Open();
        if (Editor.SelectionLength > 0 && _searchPanel is not null)
            _searchPanel.SearchPattern = Editor.SelectedText;
    }

    private async void OnGoToLine(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var total = Editor.Document.LineCount;
        var text = await Dlg.Prompt(this, "Go to Line", $"Line number (1 - {total})", Editor.TextArea.Caret.Line.ToString());
        if (!int.TryParse(text, out var line))
            return;
        line = Math.Clamp(line, 1, total);
        var docLine = Editor.Document.GetLineByNumber(line);
        Editor.CaretOffset = docLine.Offset;
        Editor.ScrollToLine(line);
        Editor.TextArea.Focus();
    }

    private void OnDuplicateLine(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var doc = Editor.Document;
        if (Editor.SelectionLength > 0)
        {
            var start = Editor.SelectionStart;
            var length = Editor.SelectionLength;
            var text = doc.GetText(start, length);
            doc.Insert(start + length, text);
            Editor.Select(start + length, length);
            return;
        }

        var line = doc.GetLineByOffset(Editor.CaretOffset);
        var lineText = doc.GetText(line.Offset, line.Length);
        var column = Editor.CaretOffset - line.Offset;
        doc.Insert(line.EndOffset, "\n" + lineText);
        Editor.CaretOffset = line.EndOffset + 1 + column;
    }

    private void OnDeleteLine(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var doc = Editor.Document;
        var first = doc.GetLineByOffset(Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset);
        var last = doc.GetLineByOffset(Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength : Editor.CaretOffset);
        var start = first.Offset;
        var end = last.NextLine is not null ? last.NextLine.Offset : last.EndOffset;
        if (last.NextLine is null && first.PreviousLine is not null)
            start = first.PreviousLine.EndOffset;
        doc.Remove(start, end - start);
    }

    private static string CommentPrefix(string language) => language switch
    {
        "Python" or "Shell" or "YAML" or "TOML" or "Ruby" or "PowerShell" or "INI" => "# ",
        "SQL" or "Lua" => "-- ",
        "Batch" => "REM ",
        "HTML" or "XML" or "Markdown" => "<!-- ",
        "Assembly" => "; ",
        _ => "// "
    };

    private void OnToggleComment(object? sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        var prefix = CommentPrefix(_current.Language);
        var closing = prefix == "<!-- " ? " -->" : "";
        var doc = Editor.Document;
        var startLine = doc.GetLineByOffset(Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset);
        var endLine = doc.GetLineByOffset(Editor.SelectionLength > 0 ? Editor.SelectionStart + Editor.SelectionLength : Editor.CaretOffset);

        var lines = new List<DocumentLine>();
        for (var l = startLine; l is not null && l.LineNumber <= endLine.LineNumber; l = l.NextLine)
            lines.Add(l);

        var trimmedPrefix = prefix.TrimEnd();
        var allCommented = lines
            .Where(l => l.Length > 0 && doc.GetText(l.Offset, l.Length).Trim().Length > 0)
            .All(l => doc.GetText(l.Offset, l.Length).TrimStart().StartsWith(trimmedPrefix));

        doc.BeginUpdate();
        try
        {
            foreach (var line in Enumerable.Reverse(lines))
            {
                var text = doc.GetText(line.Offset, line.Length);
                if (text.Trim().Length == 0)
                    continue;
                var indent = text.Length - text.TrimStart().Length;
                if (allCommented)
                {
                    var removeAt = line.Offset + indent;
                    var rest = text[indent..];
                    var removeLen = rest.StartsWith(prefix) ? prefix.Length : trimmedPrefix.Length;
                    if (closing.Length > 0 && text.TrimEnd().EndsWith(closing.Trim()))
                    {
                        var trimmedEnd = text.TrimEnd();
                        var tail = closing.Trim();
                        var tailStart = line.Offset + trimmedEnd.Length - tail.Length;
                        var spaceBefore = trimmedEnd.Length - tail.Length > 0 && trimmedEnd[trimmedEnd.Length - tail.Length - 1] == ' ' ? 1 : 0;
                        doc.Remove(tailStart - spaceBefore, tail.Length + spaceBefore);
                    }

                    doc.Remove(removeAt, removeLen);
                }
                else
                {
                    if (closing.Length > 0)
                        doc.Insert(line.Offset + line.Length, closing);
                    doc.Insert(line.Offset + indent, prefix);
                }
            }
        }
        finally
        {
            doc.EndUpdate();
        }
    }

    private void OnToggleWrap(object? sender, RoutedEventArgs e) => Editor.WordWrap = !Editor.WordWrap;
    private void OnZoomIn(object? sender, RoutedEventArgs e) => SetZoom(_fontSize + 1);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => SetZoom(_fontSize - 1);
    private void OnZoomReset(object? sender, RoutedEventArgs e) => SetZoom(DefaultFontSize);
}
