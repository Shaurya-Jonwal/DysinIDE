using Avalonia;
using System;
using System.IO;

namespace DysinIDE;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var log = Path.Combine(AppContext.BaseDirectory, "crash.log");
            File.WriteAllText(log, ex.ToString());
            Console.WriteLine(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}