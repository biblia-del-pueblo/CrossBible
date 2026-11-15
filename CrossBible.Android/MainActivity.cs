using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace CrossBible.Android;

// Avalonia 12: the activity is plain; the app (theme, fonts, the Bible folder) is set up in BibleApplication.
[Activity(
    Label = "Cross Bible",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>The activity that is shown, so that the app can make it again (see <see cref="App"/>).</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle?savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this)) Current = null;
        // Take the view out of the visual tree. A destroyed activity does not do that by itself, so the view never
        // learned that it was gone: its timers kept running and its subscriptions to long-lived objects (the Bibles,
        // the dictionaries) were never removed, and every view that was made again stayed in memory.
        try { Content = null; }
        catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not detach the view: {ex}"); }
        base.OnDestroy();
    }
}
