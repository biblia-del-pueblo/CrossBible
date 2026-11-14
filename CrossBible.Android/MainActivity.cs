using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace CrossBible.Android;

// Avalonia 12: the activity is plain; the app (theme, fonts, the Bible folder) is set up in BibleApplication.
[Activity(
    Label = "CrossBible.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
