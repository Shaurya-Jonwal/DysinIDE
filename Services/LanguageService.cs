namespace DysinIDE.Services;

public static class LanguageService
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".csx"] = "C#",
        [".axaml"] = "XML",
        [".xaml"] = "XML",
        [".xml"] = "XML",
        [".csproj"] = "XML",
        [".props"] = "XML",
        [".sln"] = "Solution",
        [".json"] = "JSON",
        [".md"] = "Markdown",
        [".txt"] = "Plain Text",
        [".py"] = "Python",
        [".js"] = "JavaScript",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript React",
        [".jsx"] = "JavaScript React",
        [".html"] = "HTML",
        [".css"] = "CSS",
        [".c"] = "C",
        [".h"] = "C",
        [".cpp"] = "C++",
        [".hpp"] = "C++",
        [".rs"] = "Rust",
        [".go"] = "Go",
        [".java"] = "Java",
        [".kt"] = "Kotlin",
        [".sh"] = "Shell",
        [".bat"] = "Batch",
        [".ps1"] = "PowerShell",
        [".yml"] = "YAML",
        [".yaml"] = "YAML",
        [".toml"] = "TOML",
        [".sql"] = "SQL",
        [".lua"] = "Lua",
        [".rb"] = "Ruby",
        [".php"] = "PHP",
        [".swift"] = "Swift",
        [".asm"] = "Assembly",
        [".ini"] = "INI"
    };

    public static string NameFor(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return "Plain Text";
        var ext = Path.GetExtension(path);
        return Names.TryGetValue(ext, out var n) ? n : "Plain Text";
    }

    public static string ExtensionFor(string? path) =>
        string.IsNullOrEmpty(path) ? ".txt" : Path.GetExtension(path);
}
