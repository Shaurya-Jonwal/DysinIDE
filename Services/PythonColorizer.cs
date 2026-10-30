using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace DysinIDE.Services;

public sealed class PythonColorizer : DocumentColorizingTransformer
{
    private readonly record struct Token(int Start, int End, IBrush Brush);

    private enum Expect
    {
        None,
        Function,
        Class
    }

    private static readonly Dictionary<string, string> KeywordColors = new()
    {
        ["def"] = "#C792EA",
        ["class"] = "#E8C878",
        ["return"] = "#FF6B9D",
        ["if"] = "#A78BFA",
        ["elif"] = "#A78BFA",
        ["else"] = "#7DD3FC",
        ["for"] = "#F0A6CA",
        ["while"] = "#9B8AFB",
        ["import"] = "#D4AF37",
        ["from"] = "#F3DFA2",
        ["async"] = "#E879F9",
        ["await"] = "#C084FC",
        ["try"] = "#67E8F9",
        ["except"] = "#FB7185",
        ["raise"] = "#F97373",
        ["with"] = "#A3E635",
        ["as"] = "#94A3B8",
        ["pass"] = "#BCA7FF",
        ["break"] = "#FF9F68",
        ["continue"] = "#5EEAD4",
        ["yield"] = "#F9A8D4",
        ["lambda"] = "#D8B4FE",
        ["and"] = "#B9A0FF",
        ["or"] = "#C4B5FD",
        ["not"] = "#E9D5FF",
        ["in"] = "#A5B4FC",
        ["is"] = "#DDD6FE",
        ["True"] = "#FDE68A",
        ["False"] = "#FCA5A5",
        ["None"] = "#CBD5E1",
        ["assert"] = "#FBBF24",
        ["del"] = "#F87171",
        ["global"] = "#D6A8FF",
        ["nonlocal"] = "#E2C2FF",
        ["finally"] = "#F5D0FE"
    };

    private static readonly HashSet<string> Builtins = new()
    {
        "print", "len", "range", "int", "str", "float", "bool", "list", "dict", "set", "tuple",
        "open", "input", "type", "isinstance", "issubclass", "enumerate", "zip", "map", "filter",
        "sorted", "reversed", "sum", "min", "max", "abs", "round", "any", "all", "super", "object",
        "getattr", "setattr", "hasattr", "repr", "bytes", "bytearray", "iter", "next", "id", "hash",
        "format", "vars", "dir", "callable", "staticmethod", "classmethod", "property", "Exception",
        "ValueError", "TypeError", "KeyError", "IndexError", "RuntimeError", "OSError", "StopIteration"
    };

    private static readonly Dictionary<string, IBrush> KeywordBrushes =
        KeywordColors.ToDictionary(k => k.Key, k => (IBrush)Solid(k.Value));

    private static readonly IBrush StringBrush = Solid("#A8E6CF");
    private static readonly IBrush CommentBrush = Solid("#6B7280");
    private static readonly IBrush NumberBrush = Solid("#FFB86C");
    private static readonly IBrush DecoratorBrush = Solid("#FFD6A5");
    private static readonly IBrush FunctionBrush = Solid("#82AAFF");
    private static readonly IBrush ClassNameBrush = Solid("#F8E7A8");
    private static readonly IBrush BuiltinBrush = Solid("#93C5FD");
    private static readonly IBrush SelfBrush = Solid("#FDA4AF");

    private TextDocument? _document;
    private int[] _states = Array.Empty<int>();
    private bool _dirty = true;

    public bool Active { get; set; }

    private static SolidColorBrush Solid(string hex) => new(Color.Parse(hex));

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!Active)
            return;

        var document = CurrentContext.Document;
        Attach(document);
        if (_dirty || line.LineNumber > _states.Length)
            Refresh(document);

        var text = document.GetText(line.Offset, line.Length);
        var tokens = new List<Token>();
        Scan(text, _states[line.LineNumber - 1], tokens);

        foreach (var token in tokens)
        {
            var start = line.Offset + token.Start;
            var end = Math.Min(line.Offset + token.End, line.EndOffset);
            if (end <= start)
                continue;
            var brush = token.Brush;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private void Attach(TextDocument document)
    {
        if (ReferenceEquals(_document, document))
            return;
        if (_document is not null)
            _document.TextChanged -= OnTextChanged;
        _document = document;
        _document.TextChanged += OnTextChanged;
        _dirty = true;
    }

    private void OnTextChanged(object? sender, EventArgs e) => _dirty = true;

    private void Refresh(TextDocument document)
    {
        var states = new int[document.LineCount + 1];
        var state = 0;
        var index = 0;
        for (var line = document.GetLineByNumber(1); line is not null; line = line.NextLine)
        {
            states[index++] = state;
            state = Scan(document.GetText(line.Offset, line.Length), state, null);
        }

        states[index] = state;
        _states = states;
        _dirty = false;
    }

    private static void Add(List<Token>? tokens, int start, int end, IBrush brush)
    {
        if (tokens is not null && end > start)
            tokens.Add(new Token(start, end, brush));
    }

    private static int FindTripleEnd(string text, int from, char quote)
    {
        for (var j = from; j < text.Length; j++)
        {
            if (text[j] == '\\')
            {
                j++;
                continue;
            }

            if (text[j] == quote && j + 2 < text.Length && text[j + 1] == quote && text[j + 2] == quote)
                return j + 3;
        }

        return -1;
    }

    private static (int End, int State) ScanString(string text, int tokenStart, int quoteIndex, List<Token>? tokens)
    {
        var n = text.Length;
        var q = text[quoteIndex];

        if (quoteIndex + 2 < n && text[quoteIndex + 1] == q && text[quoteIndex + 2] == q)
        {
            var end = FindTripleEnd(text, quoteIndex + 3, q);
            if (end < 0)
            {
                Add(tokens, tokenStart, n, StringBrush);
                return (n, q == '"' ? 1 : 2);
            }

            Add(tokens, tokenStart, end, StringBrush);
            return (end, 0);
        }

        var j = quoteIndex + 1;
        while (j < n)
        {
            if (text[j] == '\\')
            {
                j += 2;
                continue;
            }

            if (text[j] == q)
            {
                j++;
                break;
            }

            j++;
        }

        if (j > n)
            j = n;
        Add(tokens, tokenStart, j, StringBrush);
        return (j, 0);
    }

    private static bool IsStringPrefix(string text, int start, int end)
    {
        if (end - start > 2)
            return false;
        for (var k = start; k < end; k++)
            if ("rRuUbBfF".IndexOf(text[k]) < 0)
                return false;
        return true;
    }

    private static bool OnlyWhitespaceBefore(string text, int index)
    {
        for (var k = 0; k < index; k++)
            if (!char.IsWhiteSpace(text[k]))
                return false;
        return true;
    }

    private static int Scan(string text, int state, List<Token>? tokens)
    {
        var n = text.Length;
        var i = 0;

        if (state != 0)
        {
            var q = state == 1 ? '"' : '\'';
            var end = FindTripleEnd(text, 0, q);
            if (end < 0)
            {
                Add(tokens, 0, n, StringBrush);
                return state;
            }

            Add(tokens, 0, end, StringBrush);
            i = end;
        }

        var expect = Expect.None;
        while (i < n)
        {
            var c = text[i];

            if (c == '#')
            {
                Add(tokens, i, n, CommentBrush);
                return 0;
            }

            if (c == '"' || c == '\'')
            {
                var result = ScanString(text, i, i, tokens);
                if (result.State != 0)
                    return result.State;
                i = result.End;
                expect = Expect.None;
                continue;
            }

            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(text[i + 1])))
            {
                var j = i + 1;
                while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.'))
                    j++;
                Add(tokens, i, j, NumberBrush);
                i = j;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i + 1;
                while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '_'))
                    j++;

                if (j < n && (text[j] == '"' || text[j] == '\'') && IsStringPrefix(text, i, j))
                {
                    var result = ScanString(text, i, j, tokens);
                    if (result.State != 0)
                        return result.State;
                    i = result.End;
                    expect = Expect.None;
                    continue;
                }

                var word = text.Substring(i, j - i);
                IBrush? brush = null;
                if (KeywordBrushes.TryGetValue(word, out var keywordBrush))
                {
                    brush = keywordBrush;
                    expect = word == "def" ? Expect.Function : word == "class" ? Expect.Class : Expect.None;
                }
                else if (expect == Expect.Function)
                {
                    brush = FunctionBrush;
                    expect = Expect.None;
                }
                else if (expect == Expect.Class)
                {
                    brush = ClassNameBrush;
                    expect = Expect.None;
                }
                else if (word is "self" or "cls")
                {
                    brush = SelfBrush;
                }
                else if (Builtins.Contains(word))
                {
                    brush = BuiltinBrush;
                }

                if (brush is not null)
                    Add(tokens, i, j, brush);
                i = j;
                continue;
            }

            if (c == '@' && OnlyWhitespaceBefore(text, i))
            {
                var j = i + 1;
                while (j < n && (char.IsLetterOrDigit(text[j]) || text[j] == '_' || text[j] == '.'))
                    j++;
                Add(tokens, i, j, DecoratorBrush);
                i = j;
                continue;
            }

            if (!char.IsWhiteSpace(c))
                expect = Expect.None;
            i++;
        }

        return 0;
    }
}
