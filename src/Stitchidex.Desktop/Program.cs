using Avalonia;
namespace Stitchidex.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    static void WriteCrashLog(Exception ex)
    {
        var text = $"[{DateTime.Now:O}] stichidexdotnet startup crash\r\n{ex}\r\n";
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.GetTempPath() })
        {
            try { File.WriteAllText(Path.Combine(dir, "stichidexdotnet-crash.log"), text); return; }
            catch { /* try next location */ }
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
