using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;

namespace DysinIDE;

public partial class App : Application
{
    private const string FontUri = "avares://DysinIDE/assets/ibm-plex-mono";
    private const string FontName = "IBM Plex Mono";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ResolveFont();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    private void ResolveFont()
    {
        var loaded = false;
        try
        {
            var family = new FontFamily($"{FontUri}#{FontName}");
            loaded = FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out _);
        }
        catch (InvalidOperationException)
        {
        }

        if (loaded)
            return;

        Console.WriteLine($"[font] {FontName} could not be loaded from {FontUri}");
        try
        {
            var count = 0;
            foreach (var asset in AssetLoader.GetAssets(new Uri(FontUri), null))
            {
                Console.WriteLine($"[font] embedded: {asset}");
                count++;
            }

            if (count == 0)
                Console.WriteLine("[font] no font files are embedded in that folder");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[font] {ex.Message}");
        }

        Resources["UiFont"] = new FontFamily("Cascadia Code, Consolas, Menlo, DejaVu Sans Mono, monospace");
    }
}