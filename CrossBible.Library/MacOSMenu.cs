using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CrossBible.Library;
using CrossBible.Library.UI;

namespace CrossBible.Library;

/// <summary>
/// The menu of the application of macOS (the one named after the program, at the left of the menu bar): "About Cross Bible…" and the entries of the Main Menu of the
/// <see cref="BibleToolbar"/> (Install Bibles…, Settings…), which act on the toolbar that is shown. The items of the NativeMenu of the Application that have no submenu
/// are put in the application menu, above the entries that macOS itself has (Hide, Quit).
/// </summary>
public static class MacOSMenu
{
    public static NativeMenu Create(Application application)
    {
        var about = new NativeMenuItem();
        about.Click += (_, _) => ShowAbout(application);
        var install = new NativeMenuItem();
        install.Click += (_, _) =>
        {
            if (BibleToolbar.Current is { } bar) bar.ShowOnlineBibles = !bar.ShowOnlineBibles;
        };
        // Cmd+, is the shortcut of the settings (the preferences) of every Mac program.
        var settings = new NativeMenuItem { Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) };
        settings.Click += (_, _) =>
        {
            if (BibleToolbar.Current is { } bar) bar.ShowSettings = !bar.ShowSettings;
        };

        // The same entries and texts as OnMenuClick of the toolbar: "Back to reading" instead of the entry whose page is shown, and no "Back to reading" for the
        // online Bibles while there is no Bible to read.
        void Update()
        {
            var bar = BibleToolbar.Current;
            about.Header = Localizer.Get("MenuAbout");
            install.Header = Localizer.Get(bar?.ShowOnlineBibles == true ? "MenuBackToReading" : "MenuInstallBibles");
            install.IsEnabled = bar is not null && !(bar.ShowOnlineBibles && OnlineBiblesView.NoneInstalled);
            settings.Header = Localizer.Get(bar?.ShowSettings == true ? "MenuBackToReading" : "MenuSettings");
            settings.IsEnabled = bar is not null;
        }
        BibleToolbar.MenuStateChanged += (_, _) => Update();
        Localizer.LanguageChanged += (_, _) => Update();
        Update();

        return new NativeMenu { Items = { about, new NativeMenuItemSeparator(), install, settings } };
    }

    private static void ShowAbout(Application application)
    {
        if (application.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner }) return;
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
        TextBlock Line(string text, double size = 14, FontWeight weight = FontWeight.Normal) =>
            new() { Text = text, FontSize = size, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var window = new Window
        {
            Title = Localizer.Get("MenuAbout"),
            Width = 340,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = Localizer.FlowDirection,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 8,
                Children =
                {
                    Line(application.Name ?? "Cross Bible", 22, FontWeight.SemiBold),
                    Line(version is { Length: > 0 } ? $"Version {version}" : ""),
                    Line("Copyright © 2026 Biblia del Pueblo. MIT License.", 12),
                }
            }
        };
        window.ShowDialog(owner);
    }
}
