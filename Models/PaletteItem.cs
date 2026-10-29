namespace DysinIDE.Models;

public sealed class PaletteItem
{
    public PaletteItem(string title, string detail, Action run)
    {
        Title = title;
        Detail = detail;
        Run = run;
    }

    public string Title { get; }
    public string Detail { get; }
    public Action Run { get; }
}
