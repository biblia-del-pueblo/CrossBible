using Avalonia.Controls;
using BibleReader.Library;
using System;
using System.IO;

namespace BibleReader.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, WindowProc);

        // Scrolling past the end of a book continues in the next one, back past the start in the previous one.
        //bibleView.Books = Bible.OpenFrom("C:\\GitHub\\BibliaLibrePrivate\\Bibles\\English.WorldEnglishBibleUS").Books;
        //_ = bibleView.ShowBookAsync(4);
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
