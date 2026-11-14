using Avalonia.Controls;
using CrossBible.Library;
using System;
using System.IO;

namespace CrossBible.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

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
