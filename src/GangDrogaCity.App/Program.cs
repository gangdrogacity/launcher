using Avalonia;

namespace GangDrogaCity.App;

internal static class Program
{
    // Avalonia configuration, don't remove; also used by visual designer.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(Path.GetTempPath(), "gangdrogacity_launcher_crash.log");
                File.WriteAllText(logPath, ex.ToString());
            }
            catch
            {
                // ignore
            }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
