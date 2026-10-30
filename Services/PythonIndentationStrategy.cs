using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace DysinIDE.Services;

public sealed class PythonIndentationStrategy : DefaultIndentationStrategy
{
    private const string Unit = "    ";
    private static readonly string[] Dedenters = { "return", "pass", "break", "continue", "raise" };

    public override void IndentLine(TextDocument document, DocumentLine line)
    {
        var previous = line.PreviousLine;
        if (previous is null)
            return;

        var previousText = document.GetText(previous.Offset, previous.Length);
        var indent = LeadingWhitespace(previousText);
        var body = StripComment(previousText).TrimEnd();

        if (body.EndsWith(':'))
        {
            indent += Unit;
        }
        else if (StartsWithWord(body.TrimStart(), Dedenters))
        {
            if (indent.EndsWith('\t'))
                indent = indent[..^1];
            else if (indent.EndsWith(Unit))
                indent = indent[..^Unit.Length];
        }

        var current = document.GetText(line.Offset, line.Length);
        var existing = LeadingWhitespace(current).Length;
        document.Replace(line.Offset, existing, indent);
    }

    private static string LeadingWhitespace(string text)
    {
        var k = 0;
        while (k < text.Length && (text[k] == ' ' || text[k] == '\t'))
            k++;
        return text[..k];
    }

    private static bool StartsWithWord(string text, string[] words)
    {
        foreach (var word in words)
        {
            if (!text.StartsWith(word, StringComparison.Ordinal))
                continue;
            if (text.Length == word.Length || !(char.IsLetterOrDigit(text[word.Length]) || text[word.Length] == '_'))
                return true;
        }

        return false;
    }

    private static string StripComment(string text)
    {
        var quote = '\0';
        for (var k = 0; k < text.Length; k++)
        {
            var c = text[k];
            if (quote != '\0')
            {
                if (c == '\\')
                    k++;
                else if (c == quote)
                    quote = '\0';
            }
            else if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '#')
            {
                return text[..k];
            }
        }

        return text;
    }
}
