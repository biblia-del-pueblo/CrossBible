using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using CrossBible.Library;
using CrossBible.Library.UI;

namespace CrossBible.Desktop;

public partial class App : Application
{
    public App()
    {
        // Only macOS has a menu of the application: "About Cross Bible…" and the entries of the Main Menu of the toolbar (see MacOSMenu). It is set here, when the App is
        // created, not in Initialize: Avalonia gives the menu of the application to macOS while the platform starts, and it makes its own default menu ("About Avalonia",
        // Hide, Quit) when the App has none by then.
        if (System.OperatingSystem.IsMacOS()) NativeMenu.SetMenu(this, MacOSMenu.Create(this));
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Register the SVG plugin so XAML knows how to parse SVGs
        System.GC.KeepAlive(typeof(SvgImageExtension));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        UiScale.SetSystemFactor(this, 1); // there is no font size of the system here: the size of the settings is used
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        // A change of language or of the size of the interface builds all the texts and sizes again: the view in the main window is
        // replaced by a new one, which opens the Bible where the reader was. The window itself stays (its place, size and state too),
        // as the Android app does with the content of its activity.
        Localizer.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(OpenMainViewAgain);
        UiScale.Changed += (_, _) => Dispatcher.UIThread.Post(OpenMainViewAgain);

        base.OnFrameworkInitializationCompleted();
    }

    private void OpenMainViewAgain()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window }) return;
        var old = window.View;
        old.SaveStateNow(); // the new view opens at the saved position
        bool settings = old.ShowSettings; // the reader stays on the page they were on

        var view = new BibleView { Name = "bibleView" };
        if (settings) view.ShowSettings = true;
        window.ReplaceView(view);
    }
}
