using Avalonia.Controls;
using Avalonia.Styling;
using CrossBible.Library;
using System;
using System.IO;

namespace CrossBible.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Cross Bible";
        // Avalonia draws the title of the window (PART_TitleTextPanel, in the layer of the window buttons) over the toolbar. It is not shown: the title of the window
        // stays "Cross Bible" for the taskbar and Alt+Tab. A style of the application, as that layer is above the window and not part of it.
        Avalonia.Application.Current?.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Panel>().Name("PART_TitleTextPanel"))
        {
            Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 0.0) }
        });
        // The window has no title bar of its own: the toolbar of the Bible is the title bar, and leaves free the space of the buttons of the window (they are at the
        // top left on macOS, at the top right elsewhere). Avalonia draws the buttons (fullscreen, minimize, maximize, close: 189 wide, with a little air) on Windows.
        bibleView.TitleBarInset = OperatingSystem.IsMacOS() ? new Avalonia.Thickness(80, 0, 0, 0) : new Avalonia.Thickness(0, 0, 184, 0);
        if (OperatingSystem.IsLinux()) UseOwnCaptionButtons();

        // The window opens as large as it was when the app was closed (sizes that make no sense, or that no screen has, are not taken).
        if (Settings.Current is { WindowWidth: >= 300 and <= 10000, WindowHeight: >= 200 and <= 10000 } settings)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }
        // The size of the window while it is neither maximized nor minimized: a maximized window is closed with the size of the screen, which
        // is not the one to come back to.
        SizeChanged += (_, e) =>
        {
            if (WindowState == WindowState.Normal) _normalSize = e.NewSize;
        };

        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, WindowProc);

        // Scrolling past the end of a book continues in the next one, back past the start in the previous one.
        //bibleView.Books = Bible.OpenFrom("C:\\GitHub\\BibliaLibrePrivate\\Bibles\\English.WorldEnglishBibleUS").Books;
        //_ = bibleView.ShowBookAsync(4);
    }

    private Avalonia.Size _normalSize;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _normalSize.Width < 300 || _normalSize.Height < 200) return;
        Settings.Current.WindowWidth = _normalSize.Width;
        Settings.Current.WindowHeight = _normalSize.Height;
        try { Settings.Current.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    /// <summary>
    /// Linux: the window has no frame (BorderOnly: no title bar, whatever draws one, WSLg for example, and Avalonia draws no buttons on X11), and the buttons for full screen,
    /// minimize, maximize and close are the ones of the XAML, over the right end of the toolbar (it leaves 184 free for them, as Avalonia's own on Windows).
    /// </summary>
    private void UseOwnCaptionButtons()
    {
        WindowDecorations = WindowDecorations.BorderOnly;
        CaptionButtons.IsVisible = true;
        bibleView.TitleBarInset = new Avalonia.Thickness(0, 0, 184, 0);
        void ToggleFullScreen() => WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        FullScreenButton.Click += (_, _) => ToggleFullScreen();
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        CloseButton.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F11) { ToggleFullScreen(); e.Handled = true; } };
        // The icons follow the state: two squares behind each other to restore a maximized window, arrows that point inwards to leave full screen.
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            MaximizeIcon.Data = Avalonia.Media.Geometry.Parse(WindowState == WindowState.Maximized ? "M4,4 H13 V13 H4 Z M1,10 V1 H10 V4" : "M1,1 H13 V13 H1 Z");
            FullScreenIcon.Data = Avalonia.Media.Geometry.Parse(WindowState == WindowState.FullScreen ? "M13,6 H8 V1 M8,6 L13.5,0.5 M1,8 H6 V13 M6,8 L0.5,13.5" : "M8,1 H13 V6 M13,1 L7.5,6.5 M6,13 H1 V8 M1,13 L6.5,7.5");
        };
    }

    const uint WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02CC;
    const int TABLET_DISABLE_PRESSANDHOLD = 0x00000001, TABLET_DISABLE_PENTAPFEEDBACK = 0x00000008, TABLET_DISABLE_PENBARRELFEEDBACK = 0x00000010;

    /// <summary>
    /// Windows asks the window which of its touch and pen gestures it wants. Its press and hold (a long press is a right click, with a
    /// square that closes in as visual feedback) is turned off: the reader uses the long press to select text, and while the
    /// system gesture runs the window is not repainted, so the selection did not show until the mouse was moved.
    /// </summary>
    static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_TABLET_QUERYSYSTEMGESTURESTATUS) return IntPtr.Zero;
        handled = true;
        return (IntPtr)(TABLET_DISABLE_PRESSANDHOLD | TABLET_DISABLE_PENTAPFEEDBACK | TABLET_DISABLE_PENBARRELFEEDBACK);
    }
}
