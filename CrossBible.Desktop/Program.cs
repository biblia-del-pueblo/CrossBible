using System;
using System.IO;
using System.Net;
using Avalonia;
using CrossBible.Library;

namespace CrossBible.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Settings.Current.Load();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // In WSL (WSLg) the popups (the footnotes and the Strong's entries) are drawn inside the window, not in windows of their own: WSLg shows the last picture of a
        // closed popup window again for about a second after a click (the popup went away, came back and went away again). Elsewhere the popups are windows, as usual.
        // CROSSBIBLE_OVERLAY_POPUPS=1 or 0 decides it, whatever the system is.
        if (UseOverlayPopups()) builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
        return builder.WithInterFont().LogToTrace();
    }

    private static bool UseOverlayPopups()
    {
        switch (Environment.GetEnvironmentVariable("CROSSBIBLE_OVERLAY_POPUPS"))
        {
            case "1": return true;
            case "0": return false;
        }
        return IsWsl();
    }

    /// <summary>Whether this is Linux in WSL: the kernel of WSL has "microsoft" in its release (5.15.153.1-microsoft-standard-WSL2), which no other Linux has.</summary>
    private static bool IsWsl()
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"))) return true;
        try
        {
            return File.ReadAllText("/proc/sys/kernel/osrelease").Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
