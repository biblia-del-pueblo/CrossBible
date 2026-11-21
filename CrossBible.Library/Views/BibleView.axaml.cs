using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Runtime.CompilerServices;

namespace CrossBible.Library.UI;

/// <summary>
/// A <see cref="BibleToolbar"/> above a <see cref="BibleTextBlock"/>, ready to read: give it a folder with the
/// Bible's markdown files (<see cref="OpenAsync"/>) and it shows the first book. The layout is in BibleView.axaml.
/// </summary>
public partial class BibleView : UserControl
{
    public BibleView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyColumns(e.NewSize);
        // Remember the open Bible, and open the remembered one when the view is first shown.
        Block.PropertyChanged += (_, e) =>
        {
            if (e.Property == BibleTextBlock.PositionProperty) { SavePositionSoon(); return; }
            if (e.Property != BibleTextBlock.BookTextProperty) return;
            // The Bibles that are read, in the order they were checked: one, or the texts that a combined text is made of.
            var ids = (e.NewValue as BookText)?.PersistedIds.ToList();
            if (SameIds(Settings.Current.CurrentBible, ids)) return;
            Settings.Current.CurrentBible = ids;
            try { Settings.Current.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
        };
        // A chapter or a verse picked in the popup of a number goes through the toolbar: it is in the history and the position indicator shows it.
        Block.PlaceRequested += (_, e) => Toolbar.GoTo(e.Place);
        AttachedToVisualTree += (_, _) => { RestoreBible(); Toolbar.ShowWhatIsThere(); UpdateNoBible(); UpdateNoBibleFill(); };
        ActualThemeVariantChanged += (_, _) =>
        {
            UpdateNoBibleFill();
            Block.Foreground = TextColor.Brush(Settings.Current); // black or white with the theme, at the opacity that the reader chose
        };
        // The sign follows what is shown (the text, or the list of the online Bibles that replaces it) and whether a Bible is open.
        Toolbar.PropertyChanged += (_, e) =>
        {
            if (e.Property == BibleToolbar.ShowTextProperty || e.Property == BibleToolbar.BookTextProperty) UpdateNoBible();
        };
    }

    /// <summary>True while the text is shown and there is no Bible to show: none is installed. The reader then shows a big gray ban sign.</summary>
    /// <summary>Makes the toolbar the title bar of the window (see <see cref="BibleToolbar.IsTitleBar"/>). Only the desktop app uses it.</summary>
    public static readonly StyledProperty<bool> IsTitleBarProperty = AvaloniaProperty.Register<BibleView, bool>(nameof(IsTitleBar));
    public bool IsTitleBar { get => GetValue(IsTitleBarProperty); set => SetValue(IsTitleBarProperty, value); }

    /// <summary>The space of the system buttons of the window that the toolbar leaves free (see <see cref="BibleToolbar.TitleBarInset"/>).</summary>
    public static readonly StyledProperty<Thickness> TitleBarInsetProperty = AvaloniaProperty.Register<BibleView, Thickness>(nameof(TitleBarInset));
    public Thickness TitleBarInset { get => GetValue(TitleBarInsetProperty); set => SetValue(TitleBarInsetProperty, value); }

    public static readonly StyledProperty<bool> NoBibleVisibleProperty = AvaloniaProperty.Register<BibleView, bool>(nameof(NoBibleVisible));

    public bool NoBibleVisible { get => GetValue(NoBibleVisibleProperty); private set => SetValue(NoBibleVisibleProperty, value); }

    /// <summary>The gray of the ban sign: a little off the background, so it says "nothing here" without being a thing to look at. Light on white, dark on the dark theme.</summary>
    public static readonly StyledProperty<string> NoBibleFillProperty = AvaloniaProperty.Register<BibleView, string>(nameof(NoBibleFill), "#D8D8D8");

    public string NoBibleFill { get => GetValue(NoBibleFillProperty); private set => SetValue(NoBibleFillProperty, value); }

    private void UpdateNoBibleFill() =>
        NoBibleFill = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "#3C3C3C" : "#D8D8D8";

    private void UpdateNoBible() =>
        NoBibleVisible = Toolbar.ShowText && Toolbar.BookText is null && OnlineBiblesView.NoneInstalled;

    private TopLevel? _backTopLevel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The back button of Android (it is raised by the other platforms for their own back gestures, if they have any).
        _backTopLevel = TopLevel.GetTopLevel(this);        if (_backTopLevel is not null) _backTopLevel.BackRequested += OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_backTopLevel is not null) _backTopLevel.BackRequested -= OnBackRequested;
        _backTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Back while the settings are shown goes back to the text, or, without a Bible (there is no text then), to the online Bibles. Back while
    /// the online Bibles are shown goes back to the text, and without a Bible leaves the app: there is nothing behind them. From the text
    /// it goes back to the last place in the history (the back button of the toolbar); without an earlier place it is not handled, so the
    /// system does what it does (leaves the app).
    /// </summary>
    private void OnBackRequested(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Toolbar.ShowSettings)
        {
            Toolbar.ShowSettings = false; // the toolbar shows the text, or the online Bibles when there is no Bible
            e.Handled = true;
        }
        else if (Toolbar.ShowOnlineBibles)
        {
            if (OnlineBiblesView.NoneInstalled) return; // nothing to go back to
            Toolbar.ShowOnlineBibles = false;
            e.Handled = true;
        }
        else if (Toolbar.GoBack()) e.Handled = true;
    }

    private bool _restored;
    private Avalonia.Threading.DispatcherTimer? _saveTimer;

    /// <summary>Saves the position a second after it stopped changing: it changes with every scroll.</summary>
    private void SavePositionSoon()
    {
        _saveTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick -= OnSaveTimer;
        _saveTimer.Tick += OnSaveTimer;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Saves the position at once (it is saved a second after the last change otherwise), for a view that is about to be
    /// replaced by a new one, which opens the Bible at the saved position.
    /// </summary>
    public void SaveStateNow()
    {
        _saveTimer?.Stop();
        SavePosition(force: true);
    }

    private void OnSaveTimer(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        SavePosition(force: false);
    }

    private void SavePosition(bool force)
    {
        if (Block.IsBusy) { if (!force) SavePositionSoon(); return; } // still opening: the position is not the reader's yet
        if (Block.BookText is null || Block.Position is not { Book: { } book } position) return;
        var settings = Settings.Current;
        if (!SameIds(settings.CurrentBible, Block.BookText.PersistedIds)) return; // the Bible is being switched
        if (settings.CurrentBookIndex == book.Index && settings.CurrentChapter == position.Chapter && settings.CurrentVerse == position.Verse) return;
        settings.CurrentBookIndex = book.Index;
        settings.CurrentChapter = position.Chapter;
        settings.CurrentVerse = position.Verse;
        try { settings.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    private void RestoreBible()
    {
        if (_restored) return;
        _restored = true;
        SettingsView.ApplyTheme(Settings.Current.Theme);
        ApplyAppearance(); // before the first Bible is opened below, so its text is built with these
        // With no Bible to read, the list of the online Bibles is what the reader needs first.
        if (Block.BookText is null && OnlineBiblesView.NoneInstalled) { Toolbar.ShowOnlineBibles = true; return; }
        if (Block.BookText is not null || Settings.Current.CurrentBible is not { Count: > 0 } ids) return;
        // The Bibles of the saved list that are still installed, in the saved order (the first is the one the titles are read from).
        var bibles = ids.Select(id => Settings.Current.Books?.FirstOrDefault(b => b.Id == id)).OfType<BookText>().Distinct().ToList();
        if (bibles.Count == 0) return;
        var settings = Settings.Current;
        Block.OpenNextAt(settings.CurrentBookIndex, settings.CurrentChapter, settings.CurrentVerse);
        if (bibles.Count == 1) Toolbar.BookText = bibles[0]; // the text block follows, and opens at the saved position
        else _ = RestoreCombinedAsync(bibles);
    }

    /// <summary>Reads the Bibles that were read together: their books first, then the text made of them (see <see cref="BookText.Combine"/>) is shown.</summary>
    private async Task RestoreCombinedAsync(List<BookText> bibles)
    {
        try
        {
            await Task.WhenAll(bibles.Select(bible => bible.EnsureBooksAsync()));
            if (Block.BookText is null) Toolbar.BookText = BookText.Combine(bibles);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read the Bibles: {ex}");
            Toolbar.BookText = bibles[0]; // the first one alone
        }
    }

    /// <summary>Whether two lists of Bible Ids are the same (none and an empty list are).</summary>
    private static bool SameIds(IReadOnlyList<string>? a, IReadOnlyList<string>? b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>());

    /// <summary>
    /// Screens whose shorter side is at least this many device-independent pixels (a tablet; a phone has about 360-430)
    /// show several columns that are paged sideways; narrower ones show one column that scrolls up and down.
    /// <see cref="double.PositiveInfinity"/> keeps one column everywhere.
    /// </summary>
    public double TwoColumnsMinSize { get; set; } = 600;

    /// <summary>
    /// The least width of a column on a tablet, in device-independent units (the 96 dpi measure of Avalonia, which
    /// the platform's density scaling is applied to: a 1280 wide tablet screen is 1280 of them). As many columns as
    /// fit at this width are shown (two or more), and they are then widened to fill the screen: 1280 gives three.
    /// When not even two fit, one scrolling column is shown instead.
    /// </summary>
    public double MinColumnWidth { get; set; } = 350;

    public IReadOnlyList<BookText.Book>? Books
    {
        get => Block.Books;
        set => Block.Books = value;
    }
    /// <summary>The Bible shown; the text block follows the toolbar's <see cref="BibleToolbar.BookText"/>.</summary>
    public BookText? Bible
    {
        get => Block.BookText;
        set => Block.BookText = value;
    }

    public async Task ShowBookAsync(int n)=> await Block.ShowBookAsync(n);

    /// <summary>
    /// Whether the settings page is shown instead of the text. A view that replaces this one (a change of language or
    /// size) takes it over, so the reader stays on the settings page.
    /// </summary>
    public bool ShowSettings
    {
        get => Toolbar.ShowSettings;
        set
        {
            Toolbar.ShowSettings = value;
            // Once the view is in the tree: what it does when it is attached (opening the Bible) must not leave the page.
            if (value) AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Toolbar.ShowSettings = true, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    private void ApplyColumns(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;

        const double spacing = 32;
        double available = size.Width - Block.Margin.Left - Block.Margin.Right;
        double minColumn = Math.Max(1, MinColumnWidth);
        int columns = Math.Min(size.Width, size.Height) >= TwoColumnsMinSize
            ? (int)Math.Floor((available + spacing) / (minColumn + spacing))
            : 1;
        if (columns >= 2)
        {
            // A fixed column width makes the text block page its columns: it shows as many whole columns as fit.
            // The width is the share of the screen for that many columns (a pixel less, against rounding).
            Block.ColumnSpacing = spacing;
            Block.ColumnWidth = Math.Floor((available - (columns - 1) * spacing) / columns) - 1;
            Block.Orientation = Orientation.Horizontal;
        }
        else
        {
            Block.Orientation = Orientation.Vertical;
            Block.ColumnWidth = double.NaN;
        }
    }

    private Avalonia.Threading.DispatcherTimer? _appearanceTimer;

    /// <summary>Takes the font size, font and words of Jesus from the settings (the text is not rebuilt here).</summary>
    private void ApplyAppearance()
    {
        var settings = Settings.Current;
        Block.FontSize = settings.FontSize ?? SettingsView.DefaultFontSize;
        Block.Foreground = TextColor.Brush(settings);
        Block.FontFamily = SettingsView.FamilyOf(settings.FontFamily);
        Block.ShowWordsOfJesus = settings.ShowWordsOfJesus ?? true;
        Block.ShowVerseNumbersInGreen = settings.ShowVerseNumbersInGreen ?? false;
        Block.Hyphenate = settings.Hyphenate ?? true;
    }

    /// <summary>The settings changed: the text is built again a moment after the last change (a slider fires often).</summary>
    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        _appearanceTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _appearanceTimer.Tick -= OnAppearanceTimer;
        _appearanceTimer.Tick += OnAppearanceTimer;
        _appearanceTimer.Stop();
        _appearanceTimer.Start();
    }

    private async void OnAppearanceTimer(object? sender, EventArgs e)
    {
        _appearanceTimer?.Stop();
        ApplyAppearance();
        try { await Block.RebuildAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not rebuild the text: {ex}"); }
    }

    /// <summary>The close button of the online Bibles or the settings: back to the text.</summary>
    private void OnCloseOverlay(object? sender, EventArgs e)
    {
        Toolbar.ShowOnlineBibles = false;
        Toolbar.ShowSettings = false;
    }

    /// <summary>
    /// The first Bible that is installed (the list of <see cref="Settings.Books"/> had no Bible before it; the Strong's dictionaries are not Bibles) is
    /// opened at once, at its start: the toolbar and so the text block take it, and the position and the Bible are saved as they are for any Bible
    /// that is opened. A Bible that is installed later is only added to the list: what the reader is reading stays.
    /// </summary>
    private void OnBibleInstalled(object? sender, InstallableBook bible)
    {
        if (bible is IStrongDictionary || Toolbar.BookText is not null) return;
        var books = Settings.Current.Books;
        if (books is null || !books.Contains(bible) || books.Count(b => b is not IStrongDictionary) != 1) return;
        Toolbar.BookText = bible;
        UpdateNoBible();
    }

    /// <summary>A deleted Bible can no longer be read: when it is the one shown, the toolbar and the text block drop it.</summary>
    private void OnBibleDeleted(object? sender, InstallableBook bible)
    {
        if (Toolbar.BookText?.EnglishName == bible.EnglishName) Toolbar.BookText = null;
        if (Block.BookText?.EnglishName == bible.EnglishName) Block.BookText = null; // also when it was opened with OpenAsync
        Toolbar.ShowWhatIsThere(); // the last Bible that is deleted leaves no text to show: the online Bibles take its place
        UpdateNoBible();
    }

    /// <summary>The text block, for settings the view does not expose itself.</summary>
    public BibleTextBlock TextBlock => Block;

    /// <summary>Vertical (default): one column that scrolls up and down. Horizontal: columns scrolled sideways.</summary>
    public Orientation Orientation
    {
        get => Block.Orientation;
        set => Block.Orientation = value;
    }

    /// <summary>
    /// Reads the books in <paramref name="directory"/> and shows <paramref name="bookIndex"/> (default: the first
    /// real book, skipping the front matter such as the table of contents).
    /// </summary>
    public async Task OpenAsync(string directory, int bookIndex = -1) => await OpenAsync(await Task.Run(() => BookText.OpenFrom(directory)), bookIndex);

    public async Task OpenAsync(BookText bible, int bookIndex = -1)
    {
        var books = bible.Books;
        Block.Books = books;
        if (bookIndex < 0)
        {
            bookIndex = books.FindIndex(book =>
                !System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal));
            if (bookIndex < 0) bookIndex = 0;
        }
        await Block.ShowBookAsync(bookIndex);
    }

}
