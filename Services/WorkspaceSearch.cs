namespace DysinIDE.Services;

public sealed record SearchHit(string Path, int Line, string Text, string Display);

public static class WorkspaceSearch
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".vs", ".idea", "__pycache__", ".venv"
    };

    public static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subs;
            IEnumerable<string> files;
            try
            {
                subs = Directory.EnumerateDirectories(dir).ToList();
                files = Directory.EnumerateFiles(dir).ToList();
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var s in subs)
                if (!SkipDirs.Contains(Path.GetFileName(s)))
                    stack.Push(s);

            foreach (var f in files)
                yield return f;
        }
    }

    public static List<SearchHit> Search(string root, string query, bool matchCase, int limit, CancellationToken token)
    {
        var results = new List<SearchHit>();
        if (string.IsNullOrEmpty(query) || !Directory.Exists(root))
            return results;

        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var file in EnumerateFiles(root))
        {
            if (token.IsCancellationRequested || results.Count >= limit)
                break;
            try
            {
                var info = new FileInfo(file);
                if (info.Length > 2_000_000 || LooksBinary(file))
                    continue;

                var lineNo = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNo++;
                    if (line.IndexOf(query, cmp) < 0)
                        continue;
                    var rel = Path.GetRelativePath(root, file);
                    var trimmed = line.Trim();
                    if (trimmed.Length > 120)
                        trimmed = trimmed[..120];
                    results.Add(new SearchHit(file, lineNo, trimmed, $"{rel}:{lineNo}   {trimmed}"));
                    if (results.Count >= limit)
                        break;
                }
            }
            catch (Exception)
            {
            }
        }

        return results;
    }

    private static bool LooksBinary(string file)
    {
        var buffer = new byte[512];
        using var fs = File.OpenRead(file);
        var read = fs.Read(buffer, 0, buffer.Length);
        for (var i = 0; i < read; i++)
            if (buffer[i] == 0)
                return true;
        return false;
    }
}
