using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CrossBible.Library.Downloads;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace CrossBible.Library.UI;

/// <summary>
/// A bar that shows where a <see cref="BibleTextBlock"/> currently is ("Genesis 3:5"). Place it above the text
/// block and point <see cref="Target"/> at it; the bar follows the text block's <see cref="BibleTextBlock.Position"/>.
/// Clicking the position opens a grid of the text block's <see cref="BibleTextBlock.Books"/>; choosing a book shows
/// a grid of its chapters, and choosing a chapter scrolls the text block to it.
/// The layout and styling are in BibleToolbar.axaml.
/// </summary>
/// <example>
/// <code>
/// &lt;StackPanel&gt;
///   &lt;local:BibleToolbar Target="{Binding #text}"/&gt;
///   &lt;local:BibleTextBlock x:Name="text"/&gt;
/// &lt;/StackPanel&gt;
/// </code>
/// </example>
public partial class BibleToolbar : UserControl
{
    /// <summary>The text block whose position is shown and which the book list navigates.</summary>
    public static readonly StyledProperty<BibleTextBlock?> TargetProperty =
        AvaloniaProperty.Register<BibleToolbar, BibleTextBlock?>(nameof(Target));

    /// <summary>The position shown. Follows <see cref="Target"/>; can also be set directly.</summary>
    public static readonly StyledProperty<BookText.VersePosition?> PositionProperty =
        AvaloniaProperty.Register<BibleToolbar, BookText.VersePosition?>(nameof(Position));

    /// <summary>
    /// True while the online Bibles are shown instead of the text. Set by the "Online Bibles…" menu entry; the host
    /// (see <see cref="BibleView"/>) binds to it to swap its content. Choosing a chapter resets it.
    /// </summary>
    public static readonly StyledProperty<bool> ShowOnlineBiblesProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowOnlineBibles));
    /// <summary>The Bible chosen in the Bibles popup. Bind a <see cref="BibleTextBlock.BookText"/> to it to read it.</summary>
    public static readonly StyledProperty<BookText?> BookTextProperty =
        AvaloniaProperty.Register<BibleToolbar, BookText?>(nameof(BookText));

    /// <summary>True while the settings page is shown instead of the text. Set by the "Settings…" menu entry.</summary>
    public static readonly StyledProperty<bool> ShowSettingsProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowSettings));

    /// <summary>True while neither the online Bibles nor the settings are shown: the host shows the text then.</summary>
    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowText), true);

    /// <summary>True while the translations page is shown instead of the text. Set by the "Translate…" menu entry.</summary>
    public static readonly StyledProperty<bool> ShowTranslationsProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowTranslations));

    /// <summary>True while the raw markdown file of the book is shown instead of the text (the popup of the pen button).</summary>
    public static readonly StyledProperty<bool> ShowRawFileProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(ShowRawFile));

    public bool ShowRawFile { get => GetValue(ShowRawFileProperty); set => SetValue(ShowRawFileProperty, value); }
    public bool ShowTranslations { get => GetValue(ShowTranslationsProperty); set => SetValue(ShowTranslationsProperty, value); }
    public bool ShowOnlineBibles { get => GetValue(ShowOnlineBiblesProperty); set => SetValue(ShowOnlineBiblesProperty, value); }
    public bool ShowSettings { get => GetValue(ShowSettingsProperty); set => SetValue(ShowSettingsProperty, value); }
    public bool ShowText { get => GetValue(ShowTextProperty); private set => SetValue(ShowTextProperty, value); }
    private const double WideWidth = 500;

    /// <summary>True while the view is wider than <see cref="WideWidth"/>.</summary>
    public static readonly StyledProperty<bool> IsWideProperty =
        AvaloniaProperty.Register<BibleToolbar, bool>(nameof(IsWide));

    public bool IsWide { get => GetValue(IsWideProperty); private set => SetValue(IsWideProperty, value); }

    public BibleTextBlock? Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public BookText.VersePosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public BookText? BookText
    {
        get => GetValue(BookTextProperty);
        set => SetValue(BookTextProperty, value);
    }

    private DateTime _closedAt = DateTime.MinValue;
    private DateTime _menuClosedAt = DateTime.MinValue;
    private DateTime _biblesClosedAt = DateTime.MinValue;
    private DateTime _strongsClosedAt = DateTime.MinValue;

    private BookText.Book? _chaptersFor; // the book whose chapters the popup shows (null: the book page)

    static BibleToolbar()
    {
        TargetProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
            bar.OnTargetChanged(e.OldValue as BibleTextBlock, e.NewValue as BibleTextBlock));
        PositionProperty.Changed.AddClassHandler<BibleToolbar>((bar, _) => bar.UpdateText());
        IsWideProperty.Changed.AddClassHandler<BibleToolbar>((bar, _) => bar.UpdateText());
        // The online Bibles and the settings replace the text, one at a time.
        ShowOnlineBiblesProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) { bar.ShowSettings = false; bar.ShowTranslations = false; bar.ShowRawFile = false; }
            bar.ShowWhatIsThere();
        });
        ShowSettingsProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) { bar.ShowOnlineBibles = false; bar.ShowTranslations = false; bar.ShowRawFile = false; }
            bar.ShowWhatIsThere();
        });
        ShowTranslationsProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) { bar.ShowOnlineBibles = false; bar.ShowSettings = false; bar.ShowRawFile = false; }
            bar.ShowWhatIsThere();
        });
        ShowRawFileProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) =>
        {
            if (e.NewValue is true) { bar.ShowOnlineBibles = false; bar.ShowSettings = false; bar.ShowTranslations = false; }
            bar.ShowWhatIsThere();
        });
        IsWideProperty.Changed.AddClassHandler<BibleToolbar>((bar, e) => bar.UpdateIsWide());
    }

    /// <summary>
    /// Set when the app opens its main window again (after a change of language): the next toolbar shows the settings page
    /// that the reader was on, not the text.
    /// </summary>
    public static bool ReturnToSettings { get; set; }

    /// <summary>
    /// Sets what is shown: the settings or the online Bibles, else the text. Without a Bible there is no text to show: the online Bibles are
    /// shown in its place (and the settings go back to them). Call it when the Bibles that are installed have changed.
    /// </summary>
    public void ShowWhatIsThere()
    {
        if (!ShowOnlineBibles && !ShowSettings && !ShowTranslations && !ShowRawFile && OnlineBiblesView.NoneInstalled) { ShowOnlineBibles = true; return; }
        ShowText = !ShowOnlineBibles && !ShowSettings && !ShowTranslations && !ShowRawFile;
        UpdateEditButton();
        if (!ShowText && Target?.IsEditing == true) Target.IsEditing = false;
        MenuStateChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The toolbar that is shown (null while there is none): the menu of the application of macOS (see MacOSMenu) has the entries of its Main Menu and acts on it.</summary>
    public static BibleToolbar? Current { get; private set; }

    /// <summary>Raised when <see cref="Current"/> changed, or what it shows (the text, the online Bibles, the settings): the entries of the menu of the application follow it.</summary>
    public static event EventHandler? MenuStateChanged;

    /// <summary>Whether the toolbar is the title bar of its window (the desktop app, whose window has no title bar of its own): an empty part of it moves the window, a double click maximizes it.</summary>
    public static readonly StyledProperty<bool> IsTitleBarProperty = AvaloniaProperty.Register<BibleToolbar, bool>(nameof(IsTitleBar));
    public bool IsTitleBar { get => GetValue(IsTitleBarProperty); set => SetValue(IsTitleBarProperty, value); }

    /// <summary>The space at the left and at the right of the window that the system buttons of the window take (as seen on the screen, whatever the flow direction): the toolbar leaves it free.</summary>
    public static readonly StyledProperty<Thickness> TitleBarInsetProperty = AvaloniaProperty.Register<BibleToolbar, Thickness>(nameof(TitleBarInset));
    public Thickness TitleBarInset { get => GetValue(TitleBarInsetProperty); set => SetValue(TitleBarInsetProperty, value); }

    private static readonly Thickness BarPadding = new(8, 2);

    private void UpdateBarPadding()
    {
        // The toolbar is mirrored for the right to left languages, and its padding with it.
        var inset = TitleBarInset;
        bool rtl = FlowDirection == FlowDirection.RightToLeft;
        double left = rtl ? inset.Right : inset.Left, right = rtl ? inset.Left : inset.Right;
        Bar.Padding = new Thickness(BarPadding.Left + left, BarPadding.Top, BarPadding.Right + right, BarPadding.Bottom);
    }

    private void OnBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // The buttons handle their own presses, so this is only the empty part of the bar.
        if (!IsTitleBar || TopLevel.GetTopLevel(this) is not Window window || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && window.CanResize)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else if (e.ClickCount == 1)
            window.BeginMoveDrag(e);
    }

    public BibleToolbar()
    {
        InitializeComponent();
        Bar.PointerPressed += OnBarPressed;
        PropertyChanged += (_, e) => { if (e.Property == TitleBarInsetProperty) UpdateBarPadding(); };
        SizeChanged += (_, e) => IsWide = e.NewSize.Width > WideWidth;
        AttachedToVisualTree += (_, _) => { Current = this; MenuStateChanged?.Invoke(null, EventArgs.Empty); };
        DetachedFromVisualTree += (_, _) =>
        {
            if (Current != this) return;
            Current = null;
            MenuStateChanged?.Invoke(null, EventArgs.Empty);
        };
        if (ReturnToSettings) { ReturnToSettings = false; ShowSettings = true; }
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        UpdateBarPadding();
        MenuPopup.PlacementTarget = MenuButton;
        BiblesPopup.PlacementTarget = BiblesButton;
        StrongsPopup.PlacementTarget = StrongsButton;
        SearchPopup.PlacementTarget = SearchButton;
        // Tunnelled: the presses are seen before the button and the popup that it light-dismisses can handle them.
        StrongsButton.AddHandler(PointerPressedEvent, OnStrongsPressed, RoutingStrategies.Tunnel);
        StrongsButton.AddHandler(PointerReleasedEvent, OnStrongsReleased, RoutingStrategies.Tunnel);
        StrongsButton.AddHandler(PointerCaptureLostEvent, OnStrongsReleased);
        // A long press or double click on the back or forward button, or a long press on the position (the only way in a narrow bar), lists the history.
        foreach (var button in new[] { BackButton, ForwardButton, PositionButton })
        {
            button.AddHandler(PointerPressedEvent, OnHistoryPressed, RoutingStrategies.Tunnel);
            button.AddHandler(PointerReleasedEvent, OnHistoryReleased, RoutingStrategies.Tunnel);
            button.AddHandler(PointerCaptureLostEvent, OnHistoryReleased);
        }
        // A horizontal swipe anywhere on the bar goes back or forward in the history. Tunnelled, to see the gesture
        // whichever button it starts on.
        AddHandler(PointerPressedEvent, OnSwipePressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnSwipeMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnSwipeReleased, RoutingStrategies.Tunnel);
        HistoryPopup.Closed += (_, _) => _historyClosedAt = DateTime.UtcNow;
        BookPopup.PlacementTarget = PositionButton; // a reference to a named sibling, set here rather than as a binding
        UpdateHistoryButtons(); // the history of the last session
        UpdateStrongsSelected();
        UpdateText();
        UpdateIsWide();
        UpdateEditButton();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CrossBible.Library.Downloads.StrongsDictionaries.AnyChanged += OnStrongsAnyChanged;
        CrossBible.Library.Downloads.StrongsDictionaries.Refreshed += OnStrongsRefreshed;
        UpdateText(); // a dictionary may have been installed or deleted while the bar was away
        // The Bible lives as long as the app: while the bar is away it must not be held by the Bible's event.
        if (_watchedBible is not null) { _watchedBible.PropertyChanged -= OnBiblePropertyChanged; _watchedBible.PropertyChanged += OnBiblePropertyChanged; }
        UpdateStrongsSelected(); // it may have changed while the bar was away
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CrossBible.Library.Downloads.StrongsDictionaries.AnyChanged -= OnStrongsAnyChanged;
        CrossBible.Library.Downloads.StrongsDictionaries.Refreshed -= OnStrongsRefreshed;
        // Without this, every view that was made again (a change of language or size) stayed in memory, with all its
        // controls and layout, because the Bible's event kept the bar alive.
        if (_watchedBible is not null) _watchedBible.PropertyChanged -= OnBiblePropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    // Raised when a dictionary is switched on or off, installed or deleted, possibly on another thread.
    private void OnStrongsAnyChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateStrongsSelected);

    // Raised when a dictionary was installed, deleted, or switched on or off: the Strongs button is there only while a dictionary is installed.
    private void OnStrongsRefreshed(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateText);

    /// <summary>The Strongs button is selected while <see cref="Settings.ShowStrongs"/> is on.</summary>
    private void UpdateStrongsSelected() =>
        StrongsButton.Classes.Set("selected", Settings.Current.ShowStrongs);

    private void OnTargetChanged(BibleTextBlock? old, BibleTextBlock? target)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnTargetPropertyChanged;
            old.BookChanged -= OnTargetBookChanged;
            old.RemoveHandler(PointerPressedEvent, OnTextSwipePressed);
            old.RemoveHandler(PointerMovedEvent, OnSwipeMoved);
            old.RemoveHandler(PointerReleasedEvent, OnSwipeReleased);
        }
        if (target is not null)
        {
            target.PropertyChanged += OnTargetPropertyChanged;
            target.BookChanged += OnTargetBookChanged;
            // A swipe on the text goes through the history too (tunnelled: the text block handles its own presses).
            target.AddHandler(PointerPressedEvent, OnTextSwipePressed, RoutingStrategies.Tunnel);
            target.AddHandler(PointerMovedEvent, OnSwipeMoved, RoutingStrategies.Tunnel);
            target.AddHandler(PointerReleasedEvent, OnSwipeReleased, RoutingStrategies.Tunnel);
        }
        Position = target?.Position;
    }

    // The books of another Bible were loaded and shown: the names of the bar follow them (Position is kept as it is).
    private void OnTargetBookChanged(object? sender, EventArgs e) { UpdateText(); UpdateEditButton(); }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BibleTextBlock.PositionProperty)
        {
            Position = Target?.Position;
            UpdateEditButton();
        }
        else if (e.Property == BibleTextBlock.IsEditingProperty) UpdateEditButton();
    }

    /// <summary>Whether a Bible that is shown (one of the Bibles of a combined text) is editable (<see cref="BookText.IsEditable"/>).</summary>
    private bool ShownBiblesEditable()
    {
        var shown = Position?.Book?.BookText ?? BookText;
        if (shown is null) return false;
        return shown.HasEditableText;
    }

    /// <summary>The pen button is there only while the text is shown and a Bible that is read is editable; it looks selected while the text is edited.</summary>
    private void UpdateEditButton()
    {
        EditButton.IsVisible = ShowText && ShownBiblesEditable();
        EditButton.Classes.Set("selected", Target?.IsEditing == true);
        EditButton.IsEnabled = Target is { } t && (t.IsEditing || t.CanEdit);
    }

    private DateTime _editMenuOpenedAt = DateTime.MinValue;

    /// <summary>A right click or a long tap on the pen button opens its menu (the entry "Show raw file").</summary>
    private void OnEditContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (Position?.Book is null) return;
        EditPopup.PlacementTarget = EditButton;
        EditPopup.IsOpen = true;
        _editMenuOpenedAt = DateTime.UtcNow;
    }

    private void OnShowRawFileClick(object? sender, RoutedEventArgs e)
    {
        EditPopup.IsOpen = false;
        if (Position?.Book is null) return;
        if (Target?.IsEditing == true) Target.IsEditing = false; // the edits are saved first
        ShowRawFile = true;
    }

    private void OnEditClick(object? sender, RoutedEventArgs e)
    {
        // The release of the long tap that opened the menu is no click on the button.
        if ((DateTime.UtcNow - _editMenuOpenedAt).TotalMilliseconds < 800) return;
        if (Target is null) return;
        Target.IsEditing = !Target.IsEditing;
        UpdateEditButton();
    }

    private void OnPositionClick(object? sender, RoutedEventArgs e)
    {
        if (_historyLongPressed) { _historyLongPressed = false; return; } // the long press listed the history: the release is no click
        if (BookPopup.IsOpen) { BookPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 300) return;
        if (Target?.Books is not { } books) return;

        ShowBooks(books);
        BookPopup.IsOpen = true;
    }

    /// <summary>The menu: copy the current reference or verse text, and switch between reading and the online Bibles.</summary>
    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (MenuPopup.IsOpen) { MenuPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _menuClosedAt).TotalMilliseconds < 300) return;

        MenuItems.Children.Clear();
        /*if (Position is { Book: not null } position)
        {
            AddMenuEntry("copy.svg", "Copy reference", () => CopyAsync(ReferenceOf(position)));
            // Only a verse has a text; the start of a book or chapter has none.
            if (VerseTextOf(position) is { Length: > 0 } text)
                AddMenuEntry("copy.svg", "Copy verse text", () => CopyAsync(text));
        }*/
        // "Back to reading" is not offered while there is no Bible to read: the online Bibles are all there is.
        if (!(ShowOnlineBibles && OnlineBiblesView.NoneInstalled))
            AddMenuEntry(ShowOnlineBibles ? null : "download.svg", ShowOnlineBibles ? Localizer.Get("MenuBackToReading") : Localizer.Get("MenuInstallBibles"), () =>
            {
                ShowOnlineBibles = !ShowOnlineBibles;
                return Task.CompletedTask;
            });
        AddMenuEntry(ShowTranslations ? null : "language.svg", ShowTranslations ? Localizer.Get("MenuBackToReading") : Localizer.Get("MenuTranslate"), () =>
        {
            ShowTranslations = !ShowTranslations;
            return Task.CompletedTask;
        }); 
        AddMenuEntry(ShowSettings ? null : "gear.svg", ShowSettings ? Localizer.Get("MenuBackToReading") : Localizer.Get("MenuSettings"), () =>
        {
            ShowSettings = !ShowSettings;
            return Task.CompletedTask;
        });
        MenuPopup.IsOpen = true;
    }

    // ---- History ---------------------------------------------------------------------------------------

    // The history lives in the settings (saved when the app ends, see Settings.SaveOnExit), oldest entry first.
    private static List<Settings.HistoryEntry> _history => Settings.Current.History ??= new();
    private static int _historyIndex
    {
        get => Math.Clamp(Settings.Current.HistoryIndex, -1, _history.Count - 1);
        set => Settings.Current.HistoryIndex = value;
    }
    private bool _historyLongPressed;
    private Control? _historyHoldTarget;
    private Avalonia.Threading.DispatcherTimer? _historyHold;

    private static Settings.HistoryEntry EntryOf(BookText.VersePosition p) => new() { BookIndex = p.Book.Index, Chapter = p.Chapter, Verse = p.Verse };

    /// <summary>The book of the entry in the Bible that is read (the history is of positions, not of Bibles).</summary>
    private BookText.Book? BookOf(Settings.HistoryEntry entry) => Target?.Books?.FirstOrDefault(b => b.Index == entry.BookIndex);

    /// <summary>
    /// Notes where the reader is now in the current entry, so that coming back lands there. Scrolling does not make
    /// history: only going somewhere does. A place in another chapter than the entry's becomes an entry of its own.
    /// </summary>
    private void SnapshotCurrent()
    {
        if (Position is not { Book: not null } position) return;
        int current = _historyIndex;
        if (current >= 0 && _history[current].BookIndex == position.Book.Index && _history[current].Chapter == position.Chapter)
        {
            _history[current].Verse = position.Verse;
            return;
        }
        _history.Insert(current + 1, EntryOf(position));
        _historyIndex = current + 1;
    }

    /// <summary>
    /// Adds the place that the reader is about to go to (a chapter, a search result) as the newest entry; the entries ahead
    /// of the current one are dropped. Call it before the text block is moved.
    /// </summary>
    private void PushHistory(BookText.VersePosition target)
    {
        if (target.Book is null) return;
        SnapshotCurrent();
        int current = _historyIndex;
        if (current >= 0 && _history[current].BookIndex == target.Book.Index && _history[current].Chapter == target.Chapter)
            _history[current].Verse = target.Verse;
        else
        {
            _history.RemoveRange(current + 1, _history.Count - current - 1);
            _history.Add(EntryOf(target));
            _historyIndex = _history.Count - 1;
        }
        UpdateHistoryButtons();
        SaveHistory();
    }

    /// <summary>Saves the settings (off the UI thread), with the history, when the reader goes somewhere (not for every verse that scrolls by).</summary>
    private static async void SaveHistory()
    {
        try { await Settings.Current.SaveAsync(); } // the file is written off the UI thread
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    private void UpdateHistoryButtons()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex >= 0 && _historyIndex < _history.Count - 1;
    }

    private void GoToHistory(int index)
    {
        if (index < 0 || index >= _history.Count || Target is null) return;
        var entry = _history[index];
        if (BookOf(entry) is not { } book) return;
        SnapshotCurrent(); // where the reader is now; may add an entry, which moves the one that is wanted
        _historyIndex = _history.IndexOf(entry);
        ShowOnlineBibles = false;
        ShowSettings = false;
        ShowTranslations = false;
        ShowRawFile = false;
        Target.Position = new BookText.VersePosition { Book = book, Chapter = entry.Chapter, Verse = entry.Verse };
        UpdateHistoryButtons();
        SaveHistory();
    }

    /// <summary>
    /// One step back in the history, as the back button of the toolbar takes it (at once, with no wait for a double click): to the last
    /// place the reader was at. False, and nothing moved, when there is no earlier place: the app is left then.
    /// </summary>
    public bool GoBack()
    {
        // Where the reader is now counts: a chapter that was scrolled into has no entry before this makes it one, and the one it came from is the earlier place.
        SnapshotCurrent();
        UpdateHistoryButtons();
        if (_historyIndex <= 0) return false;
        GoToHistory(_historyIndex - 1);
        return true;
    }

    private object? _backPending;      // the step back that waits for a possible second press
    private bool _historyOpenOnRelease; // the second press of a double click on the back button

    /// <summary>
    /// A click goes back one step, unless a second press follows: a double click (like a long press) lists the history
    /// instead, so the step waits for as long as a second press may take (as the Strongs button does).
    /// </summary>
    private void OnBackHistoryClick(object? sender, RoutedEventArgs e) => _ = StepHistoryAsync(BackButton, -1);

    private void OnForwardHistoryClick(object? sender, RoutedEventArgs e) => _ = StepHistoryAsync(ForwardButton, 1);

    private async Task StepHistoryAsync(Button button, int step)
    {
        if (_historyLongPressed) { _historyLongPressed = false; return; } // the long press opened the list: the release is no click
        // The list opens when the second press ends: a popup that opens during a press is light-dismissed by it.
        if (_historyOpenOnRelease) { _historyOpenOnRelease = false; OpenHistoryPopup(button); return; }
        var pending = _backPending = new object();
        await Task.Delay(StrongsDoubleClickDelay);
        if (!ReferenceEquals(pending, _backPending)) return;
        GoToHistory(_historyIndex + step);
    }

    private void OnHistoryPressed(object? sender, PointerPressedEventArgs e)
    {
        _historyLongPressed = false;
        _historyHoldTarget = sender as Control;
        _historyOpenOnRelease = false;
        _historyRightPressed = false;
        if (sender is Control pressed && e.GetCurrentPoint(pressed).Properties.IsRightButtonPressed)
        {
            // A right click lists the history, like a long press; it is no click. The list opens on release,
            // since a popup that opens during a press is light-dismissed by it.
            _historyHold?.Stop();
            _backPending = null;
            _historyRightPressed = true;
            e.Handled = true;
            return;
        }
        if ((ReferenceEquals(sender, BackButton) || ReferenceEquals(sender, ForwardButton)) && e.ClickCount == 2)
        {
            _backPending = null; // the step back that waited does not happen
            _historyOpenOnRelease = true;
            _historyHold?.Stop();
            return;
        }
        _historyHold ??= new Avalonia.Threading.DispatcherTimer { Interval = LongPressDelay };
        _historyHold.Tick -= OnHistoryHeld;
        _historyHold.Tick += OnHistoryHeld;
        _historyHold.Stop();
        _historyHold.Start();
    }

    private void OnHistoryHeld(object? sender, EventArgs e)
    {
        _historyHold?.Stop();
        if (_historyHoldTarget is { IsEnabled: false }) return; // a disabled back button has nothing to go back to
        _historyLongPressed = true; // the release that follows is not a click
        OpenHistoryPopup(_historyHoldTarget ?? PositionButton);
    }

    private bool _historyRightPressed;
    private DateTime _historyClosedAt = DateTime.MinValue;

    private void OnHistoryReleased(object? sender, RoutedEventArgs e)
    {
        _historyHold?.Stop();
        if (!_historyRightPressed) return;
        _historyRightPressed = false;
        if (e is not PointerReleasedEventArgs || sender is not Control target || !target.IsEnabled) return;
        // Pressing already light-dismissed an open list: this right click closes it, it does not open it again.
        if ((DateTime.UtcNow - _historyClosedAt).TotalMilliseconds < 300) return;
        OpenHistoryPopup(target);
    }

    private const double SwipeDistance = 40;
    private Point _swipeStart;
    private bool _swipeTracked;

    /// <summary>Starts to track a swipe, unless the press is in a popup of the bar (the book grid, the lists).</summary>
    private void OnSwipePressed(object? sender, PointerPressedEventArgs e)
    {
        _historyLongPressed = false; // from here on, a release is a click again
        _swipeTracked = e.Source is Visual source && ReferenceEquals(TopLevel.GetTopLevel(source), TopLevel.GetTopLevel(this));
        _swipeStart = e.GetPosition(this);
    }

    /// <summary>
    /// A swipe on the text: only a finger or a pen, since a mouse drag selects text, and only while the text has a single
    /// column, since with more a horizontal drag turns the pages.
    /// </summary>
    private void OnTextSwipePressed(object? sender, PointerPressedEventArgs e)
    {
        OnSwipePressed(sender, e);
        if (e.Pointer.Type == PointerType.Mouse || Target?.IsSingleColumn != true || Target.IsEditing) _swipeTracked = false;
    }

    /// <summary>A swipe is mostly horizontal and long enough; <paramref name="dx"/> is how far the pointer went from the press.</summary>
    private bool IsSwipe(PointerEventArgs e, out double dx)
    {
        var moved = e.GetPosition(this) - _swipeStart;
        dx = moved.X;
        // Not while a finger selects text: a long press that selects a word and a drag that extends the selection is no swipe, however horizontal it is.
        return _swipeTracked && Target?.IsSelectingByTouch != true && Math.Abs(moved.X) >= SwipeDistance && Math.Abs(moved.X) > 2 * Math.Abs(moved.Y);
    }

    /// <summary>Once the pointer has swiped, the press is no long press any more.</summary>
    private void OnSwipeMoved(object? sender, PointerEventArgs e)
    {
        if (_historyHold is { IsEnabled: true } && IsSwipe(e, out _)) _historyHold.Stop();
    }

    private void OnSwipeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsSwipe(e, out double dx) || _historyLongPressed) { _swipeTracked = false; return; }
        _swipeTracked = false;
        _historyLongPressed = true; // the release is no click on the back or position button
        // To the right goes back, to the left forward; the other way round in a mirrored bar.
        bool right = dx > 0;
        if (FlowDirection == FlowDirection.RightToLeft) right = !right;
        GoToHistory(_historyIndex + (right ? -1 : 1));
    }

    /// <summary>
    /// Lists the history, the newest first; choosing an entry goes to it. From the forward button it lists only what
    /// is ahead of the position, the nearest first.
    /// </summary>
    private void OpenHistoryPopup(Control anchor)
    {
        HistoryItems.Children.Clear();
        bool ahead = ReferenceEquals(anchor, ForwardButton);
        for (int n = 0; n < _history.Count; n++)
        {
            int i = ahead ? _historyIndex + 1 + n : _history.Count - 1 - n;
            if (i < 0 || i >= _history.Count) break;

            int index = i;
            // The book, and chapter:verse beside it (the start of a chapter is verse 1).
            var entry = _history[i];
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new TextBlock { Text = ShortenWithEllipsis(BookOf(entry)?.Name ?? string.Empty, 30) });
            content.Children.Add(new TextBlock { Text = $"{Math.Max(1, entry.Chapter)}:{Math.Max(1, entry.Verse)}", Opacity = 0.7 });
            var button = new Button { Content = content, };
            button.Classes.Add("entry");
            if (i == _historyIndex) button.Classes.Add("current");
            button.Click += (_, _) => { HistoryPopup.IsOpen = false; GoToHistory(index); };
            HistoryItems.Children.Add(button);
        }
        if (HistoryItems.Children.Count == 0)
            HistoryItems.Children.Add(new TextBlock { Text = Localizer.Get("NoHistory"), Opacity = 0.7, Margin = new Thickness(12, 8) });
        HistoryPopup.PlacementTarget = anchor;
        HistoryPopup.IsOpen = true;
    }

    /// <summary>Lists the installed Bibles (<see cref="Settings.Books"/>); choosing one sets <see cref="BookText"/>.</summary>
    private void OnBiblesClick(object? sender, RoutedEventArgs e)
    {
        if (BiblesPopup.IsOpen) { BiblesPopup.IsOpen = false; return; }
        if ((DateTime.UtcNow - _biblesClosedAt).TotalMilliseconds < 300) return;

        // Reading Bibles together needs more than one: with a single Bible installed there is nothing to choose, and the box is not there (and is
        // not checked, so the Bible is a button as it always was).
        bool several = InstalledBibles().Count > 1;
        InterlinearBox.IsVisible = InterlinearSeparator.IsVisible = several;
        if (!several && InterlinearBox.IsChecked == true) InterlinearBox.IsChecked = false;
        SyncSelectionFromShown();
        // Several Bibles are read together (also when they were opened again at the start, with no box checked): they are shown as check boxes.
        if (several && _selectedBibles.Count > 1 && !MultipleBibles) InterlinearBox.IsChecked = true;
        FillBibles();
        BiblesPopup.IsOpen = true;
    }

    /// <summary>Whether the Bibles in the list are check boxes ("Interlinear" is checked) and not buttons.</summary>
    private bool MultipleBibles => InterlinearBox.IsChecked == true;

    /// <summary>The Bibles that are checked, in the order they were checked: the first is the one that the titles are read from (see <see cref="BookText.Combine"/>).</summary>
    private readonly List<BookText> _selectedBibles = new();
    private bool _syncingBibles;
    private object? _applyingSelection; // the selection that is being opened; a newer one drops it

    /// <summary>The checked Bibles are those that are shown: one, or the texts that a combined text is made of.</summary>
    private void SyncSelectionFromShown()
    {
        _selectedBibles.Clear();
        var shown = Position?.Book?.BookText ?? BookText;
        if (shown?.Sources is { } sources) _selectedBibles.AddRange(sources);
        else if (shown is not null) _selectedBibles.Add(shown);
    }

    /// <summary>The installed Bibles in the order of the list. Strong's dictionaries are not read like a Bible: they are switched on and off in the Strongs list.</summary>
    private static List<BookText> InstalledBibles() =>
        (Settings.Current.Books ?? new List<BookText>()).Where(b => !string.IsNullOrEmpty(b.EnglishName) && b is not IStrongDictionary)
            .OrderBy(b => b is InstallableBook ibook ? ibook.LanguageCode : "")
            .ThenBy(b => b.Name ?? b.EnglishName ?? "")
            .ToList();

    /// <summary>Fills the list of Bibles: buttons, or check boxes while <see cref="MultipleBibles"/>.</summary>
    private void FillBibles()
    {
        BiblesItems.Children.Clear();
        string? current = Position?.Book?.BookText?.Name;
        bool multiple = MultipleBibles;
        foreach (var bible in InstalledBibles())
        {
            var chosen = bible;
            var name = ShortenWithEllipsis(chosen.Name ?? chosen.EnglishName, 40);
            if (multiple)
            {
                var box = new CheckBox { Content = name, IsChecked = _selectedBibles.Contains(chosen), Margin = new Thickness(4, 2) };
                box.IsCheckedChanged += (_, _) =>
                {
                    if (_syncingBibles) return;
                    if (box.IsChecked == true)
                    {
                        if (!_selectedBibles.Contains(chosen)) _selectedBibles.Add(chosen);
                    }
                    else if (_selectedBibles.Count <= 1)
                    {
                        // One Bible stays checked: there is always something to read.
                        _syncingBibles = true;
                        try { box.IsChecked = true; }
                        finally { _syncingBibles = false; }
                        return;
                    }
                    else _selectedBibles.Remove(chosen);
                    _ = ApplySelectionAsync();
                };
                BiblesItems.Children.Add(box);
                continue;
            }
            var button = new Button { Content = name };
            button.Classes.Add("entry");
            if (chosen.EnglishName == current) button.Classes.Add("current");
            button.Click += (_, _) =>
            {
                BiblesPopup.IsOpen = false;
                ShowOnlineBibles = false;
                ShowSettings = false;
                ShowTranslations = false;
                ShowRawFile = false;
                if (BookText != chosen) BookText = chosen;
            };
            BiblesItems.Children.Add(button);
        }
        if (BiblesItems.Children.Count == 0)
            BiblesItems.Children.Add(new TextBlock { Text = Localizer.Get("NoBiblesInstalled"), Opacity = 0.7, Margin = new Thickness(12, 8) });
    }

    /// <summary>
    /// "Interlinear" was checked or unchecked: the Bibles become check boxes, or buttons again. With it unchecked only one Bible is read
    /// (the first of the checked ones), as it was before.
    /// </summary>
    private void OnBiblesModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!MultipleBibles && _selectedBibles.Count > 1)
        {
            _selectedBibles.RemoveRange(1, _selectedBibles.Count - 1);
            _ = ApplySelectionAsync();
        }
        FillBibles();
    }

    /// <summary>
    /// Reads the checked Bibles: the one that is checked, or, when there are several, the text made of them with <see cref="BookText.Combine"/>
    /// (the books of the Bibles are read first). The place in the text stays.
    /// </summary>
    private async Task ApplySelectionAsync()
    {
        var selection = _selectedBibles.ToList();
        if (selection.Count == 0) return;
        var token = _applyingSelection = new object();
        ShowOnlineBibles = false;
        ShowSettings = false;
        ShowTranslations = false;
        ShowRawFile = false;
        try
        {
            BookText target = selection[0];
            if (selection.Count > 1)
            {
                await Task.WhenAll(selection.Select(bible => bible.EnsureBooksAsync()));
                if (!ReferenceEquals(token, _applyingSelection)) return; // another selection was made while the books were read
                target = BookText.Combine(selection);
            }
            if (!ReferenceEquals(BookText, target)) BookText = target;
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"Could not read the Bibles: {ex}");
        }
    }

    private void OnBiblesPopupClosed(object? sender, EventArgs e) => _biblesClosedAt = DateTime.UtcNow;

    /// <summary>
    /// A click on the Strongs button switches all the installed Strong's dictionaries on, or, when they all are on,
    /// off. A double click, a right click or a long press opens the list, in which every dictionary is switched on or
    /// off by itself.
    /// </summary>
    private async void OnStrongsClick(object? sender, RoutedEventArgs e)
    {
        if (_strongsLongPressed) { _strongsLongPressed = false; return; } // the long press opened the list: the release is no click
        if (_strongsOpenOnRelease) { _strongsOpenOnRelease = false; OpenStrongsPopup(); return; } // the end of a double click
        if (StrongsPopup.IsOpen) { StrongsPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; this click is not a toggle.
        if ((DateTime.UtcNow - _strongsClosedAt).TotalMilliseconds < 300) return;

        // A click toggles only when no second press follows: for a double click the list opens instead.
        var pending = _strongsPending = new object();
        await Task.Delay(StrongsDoubleClickDelay);
        if (!ReferenceEquals(pending, _strongsPending)) return;
        SetShowStrongs(!Settings.Current.ShowStrongs);
    }

    /// <summary>
    /// Lists the installed Strong's dictionaries (the <see cref="IStrongDictionary"/> entries of
    /// <see cref="Settings.Books"/>); checking one sets its <see cref="IStrongDictionary.IsEnabled"/>; the "All" box above them
    /// switches all of them.
    /// </summary>
    private void OpenStrongsPopup()
    {
        StrongsItems.Children.Clear();
        _strongsBoxes.Clear();
        var dictionaries = StrongsDictionariesInstalled();
        if (dictionaries.Count > 0)
        {
            // "All" switches all the dictionaries on, or, when they all are, off; it is unchecked when none is on and
            // shows a dash when some are. It does not change Settings.ShowStrongs (that is the Strongs button).
            _strongsAllBox = new CheckBox { Content = Localizer.Get("All"), IsThreeState = true, Margin = new Thickness(4, 2), FontWeight = FontWeight.SemiBold };
            _strongsAllBox.Click += (_, _) => SetAllStrongs(!dictionaries.All(d => d.IsEnabled));
            StrongsItems.Children.Add(_strongsAllBox);
            StrongsItems.Children.Add(new Border { Height = 1, Margin = new Thickness(0, 2), Opacity = 0.3, Background = Brushes.Gray });
        }
        foreach (var dictionary in dictionaries)
        {
            var chosen = dictionary;
            var name = (chosen as BookText) is { } book && !string.IsNullOrEmpty(book.Name ?? book.EnglishName)
                ? book.Name ?? book.EnglishName : chosen.GetType().Name;
            var box = new CheckBox { Content = ShortenWithEllipsis(name, 40), IsChecked = chosen.IsEnabled, Margin = new Thickness(4, 2) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (_syncingStrongs) return;
                chosen.IsEnabled = box.IsChecked == true;
                StrongsDictionariesChanged();
            };
            _strongsBoxes.Add((chosen, box));
            StrongsItems.Children.Add(box);
        }
        if (dictionaries.Count == 0)
            StrongsItems.Children.Add(new TextBlock { Text = Localizer.Get("NoStrongsInstalled"), Opacity = 0.7, Margin = new Thickness(4, 8) });
        SyncStrongsBoxes();
        StrongsPopup.IsOpen = true;
    }

    private readonly List<(IStrongDictionary Dictionary, CheckBox Box)> _strongsBoxes = new();
    private CheckBox? _strongsAllBox;
    private bool _syncingStrongs;

    private static List<IStrongDictionary> StrongsDictionariesInstalled() =>
        (Settings.Current.Books ?? new List<BookText>()).OfType<IStrongDictionary>().ToList();

    /// <summary>A dictionary was switched on or off: tell <see cref="StrongsDictionaries.Any"/>, save, and show the state of the list.</summary>
    private void StrongsDictionariesChanged()
    {
        StrongsDictionaries.Refresh(); // whatever the dictionary does itself, Any is told that one was switched on or off
        try { Settings.Current.Save(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
        SyncStrongsBoxes();
    }

    /// <summary>Switches all the installed dictionaries on or off (their <see cref="IStrongDictionary.IsEnabled"/>); <see cref="Settings.ShowStrongs"/> is left as it is.</summary>
    private void SetAllStrongs(bool enabled)
    {
        var dictionaries = StrongsDictionariesInstalled();
        if (dictionaries.Count == 0) return;
        foreach (var dictionary in dictionaries) dictionary.IsEnabled = enabled;
        StrongsDictionariesChanged();
    }

    /// <summary>Switches <see cref="Settings.ShowStrongs"/> (the Strongs button); the individual dictionaries are left as they are.</summary>
    private void SetShowStrongs(bool show)
    {
        Settings.Current.ShowStrongs = show;
        UpdateStrongsSelected(); // Any does not change (and so tells nobody) when no dictionary is switched on
        StrongsDictionariesChanged();
    }

    /// <summary>Shows the state of the dictionaries in the check boxes, without the boxes changing the dictionaries back.</summary>
    private void SyncStrongsBoxes()
    {
        _syncingStrongs = true;
        try
        {
            foreach (var (dictionary, box) in _strongsBoxes) box.IsChecked = dictionary.IsEnabled;
            if (_strongsAllBox is not null)
                _strongsAllBox.IsChecked = _strongsBoxes.All(b => b.Dictionary.IsEnabled) ? true
                    : _strongsBoxes.Any(b => b.Dictionary.IsEnabled) ? null : false;
        }
        finally { _syncingStrongs = false; }
    }

    private const ulong DoubleClickMilliseconds = 400;
    private static readonly TimeSpan StrongsDoubleClickDelay = TimeSpan.FromMilliseconds(420); // a little more than the time for a second press
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(500);
    private object? _strongsPending;      // the toggle that waits for a possible second press
    private ulong _strongsPressedAt;
    private bool _strongsLongPressed, _strongsOpenOnRelease, _strongsRightPressed;
    private Avalonia.Threading.DispatcherTimer? _strongsHold;

    /// <summary>
    /// Two presses of the button in quick succession, or one that is held, open the list. The presses are timed here,
    /// not taken from the Tapped events, which do not tell a double tap reliably next to a popup.
    /// </summary>
    private void OnStrongsPressed(object? sender, PointerPressedEventArgs e)
    {
        _strongsLongPressed = false;
        _strongsOpenOnRelease = false;
        _strongsRightPressed = false;
        if (e.GetCurrentPoint(StrongsButton).Properties.IsRightButtonPressed)
        {
            // A right click opens the list, like a long press; it is no click, so the button is not pressed by it.
            // The list opens when the button is released: a popup that opens during a press is light-dismissed by it.
            _strongsHold?.Stop();
            _strongsPending = null;
            _strongsPressedAt = 0;
            _strongsRightPressed = true;
            e.Handled = true;
            return;
        }
        if (_strongsPressedAt != 0 && e.Timestamp - _strongsPressedAt < DoubleClickMilliseconds)
        {
            _strongsPressedAt = 0;
            _strongsPending = null; // the toggle that waited does not happen
            // The list opens when this press ends: a popup that opens during a press is light-dismissed by it.
            _strongsOpenOnRelease = true;
            return;
        }
        _strongsPressedAt = e.Timestamp;

        // Held long enough, it opens the list while the finger is still down.
        _strongsHold ??= new Avalonia.Threading.DispatcherTimer { Interval = LongPressDelay };
        _strongsHold.Tick -= OnStrongsHeld;
        _strongsHold.Tick += OnStrongsHeld;
        _strongsHold.Stop();
        _strongsHold.Start();
    }

    private void OnStrongsHeld(object? sender, EventArgs e)
    {
        _strongsHold?.Stop();
        _strongsLongPressed = true; // the release that follows is not a click
        _strongsPending = null;
        _strongsPressedAt = 0;
        OpenStrongsPopup();
    }

    /// <summary>The press ended (or was taken away): it was not held long enough.</summary>
    private void OnStrongsReleased(object? sender, RoutedEventArgs e)
    {
        _strongsHold?.Stop();
        if (!_strongsRightPressed) return;
        _strongsRightPressed = false;
        if (e is not PointerReleasedEventArgs) return; // the press was taken away: no list
        // Pressing already light-dismissed an open list: this right click closes it, it does not open it again.
        if ((DateTime.UtcNow - _strongsClosedAt).TotalMilliseconds < 300) return;
        OpenStrongsPopup();
    }

    private void OnStrongsPopupClosed(object? sender, EventArgs e) => _strongsClosedAt = DateTime.UtcNow;

    // ---- Search ----------------------------------------------------------------------------------------

    private DateTime _searchClosedAt = DateTime.MinValue;
    private object? _searchRunning; // the search whose results are wanted: an older one that ends later is dropped

    private void OnSearchClick(object? sender, RoutedEventArgs e)
    {
        if (SearchPopup.IsOpen) { SearchPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _searchClosedAt).TotalMilliseconds < 300) return;
        SearchPopup.IsOpen = true;
    }

    private void OnSearchPopupOpened(object? sender, EventArgs e) => SearchBox.Focus();

    private void OnSearchPopupClosed(object? sender, EventArgs e) => _searchClosedAt = DateTime.UtcNow;

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = SearchAsync(false);
    }

    private void OnSearchGoClick(object? sender, RoutedEventArgs e) => _ = SearchAsync(false);

    private Avalonia.Threading.DispatcherTimer? _typingTimer;
    private bool _settingSearchText;

    /// <summary>Search as the reader types: a short pause after the last key press runs the search, with the matching words on top.</summary>
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_settingSearchText) return;
        _typingTimer ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _typingTimer.Tick -= OnTyped;
        _typingTimer.Tick += OnTyped;
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private void OnTyped(object? sender, EventArgs e)
    {
        _typingTimer?.Stop();
        _ = SearchAsync(true);
    }

    private static readonly Regex LastWordRx = new(@"[\p{L}\p{M}\p{Nd}]+$", RegexOptions.Compiled);

    /// <summary>The words that contain the last word typed, each with how often it occurs, as buttons that wrap over the width.</summary>
    private Control WordsList(List<(string Word, int Count)> words, string typed)
    {
        var panel = new WrapPanel();
        foreach (var (word, count) in words)
        {
            var chosen = word;
            var content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(new TextBlock { Text = chosen, FontSize = UiScale.Size(14) });
            content.Children.Add(new TextBlock { Text = count.ToString(), Opacity = 0.6, FontSize = UiScale.Size(12), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var button = new Button { Content = content, Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 4, 4) };
            button.Click += (_, _) =>
            {
                // the last word typed becomes the chosen one, and the search runs
                _typingTimer?.Stop();
                _settingSearchText = true;
                SearchBox.Text = typed[..^LastWordRx.Match(typed).Length] + chosen;
                _settingSearchText = false;
                SearchBox.CaretIndex = SearchBox.Text.Length;
                _ = SearchAsync(false);
            };
            panel.Children.Add(button);
        }
        return panel;
    }

    /// <summary>
    /// Searches the Bible that is shown (all the Bibles of an interlinear text) for the words in the box (<see cref="InstallableBook.Search"/>) and lists the
    /// verses, the best match first, with the words in bold. Choosing a verse goes to it.
    /// </summary>
    private async Task SearchAsync(bool showWords)
    {
        _typingTimer?.Stop(); // Enter or the button: the search that is due now replaces the one that waited
        var typed = SearchBox.Text ?? "";
        var phrase = typed.Trim();
        if (string.IsNullOrEmpty(phrase))
        {
            _searchRunning = null;
            SearchResults.Children.Clear();
            SearchStatus.IsVisible = false;
            return;
        }
        SearchStatus.IsVisible = true;

        var shown = Position?.Book?.BookText ?? BookText;
        if (shown is null) { SearchStatus.Text = Localizer.Get("OpenBibleFirst"); return; }
        // An interlinear text is searched in all the Bibles it is made of, a single Bible in itself.
        var texts = shown.Sources is { Count: > 0 } sources ? sources.ToList() : new List<BookText> { shown };
        var searchables = texts.Select(SearchableFor).ToList();

        var running = _searchRunning = new object();
        if (!showWords) SearchResults.Children.Clear(); // while typing, the old list stays until the new one is ready (no flicker)
        SearchStatus.Text = Localizer.Get("Searching");
        List<SearchHit> results;
        List<(string Word, int Count)> words = new();
        try
        {
            var lastWord = typed.Length > 0 && !char.IsWhiteSpace(typed[^1]) ? LastWordRx.Match(typed).Value : "";
            var wordsTasks = searchables.Select(s => showWords && lastWord.Length > 0 ? s.WordsContaining(lastWord) : Task.FromResult(new List<(string Word, int Count)>())).ToList();
            var found = await Task.WhenAll(searchables.Select(s => s.Search(phrase)));
            // The best match first, whichever Bible it is in; on equal scores the Bibles in the order they were chosen, then the order of the Bible.
            var order = texts.Select((text, i) => (text, i)).ToDictionary(x => x.text, x => x.i);
            results = found.SelectMany((list, bible) => list.Select(result => new SearchHit(texts[bible], result)))
                .OrderByDescending(r => r.Result.Score).ThenBy(r => order[r.Bible]).ToList();
            // The words of all the Bibles, a word that is in several once, with its counts added.
            words = (await Task.WhenAll(wordsTasks)).SelectMany(list => list)
                .GroupBy(w => w.Word, StringComparer.CurrentCultureIgnoreCase).Select(g => (g.First().Word, g.Sum(w => w.Count)))
                .OrderByDescending(w => w.Item2).Take(50).ToList();
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(running, _searchRunning)) SearchStatus.Text = Localizer.Format("SearchFailed", ex.Message);
            return;
        }
        if (!ReferenceEquals(running, _searchRunning)) return;

        SearchStatus.Text = results.Count == 0 ? Localizer.Get("NoVersesFound") : Localizer.Format("VersesFound", results.Count);
        SearchResults.Children.Clear();
        if (words.Count > 0) SearchResults.Children.Add(WordsList(words, typed)); // at the top of the results
        foreach (var found in results)
        {
            var hit = found.Result;
            var source = found.Bible;
            var verse = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiScale.Size(14) };
            foreach (var inline in BibleTextBlock.MarkdownInlines(BibleMarkdown.WordIndex.WithoutFootnotes(hit.Markdown), UiScale.Size(14)))
                verse.Inlines!.Add(inline);
            // A verse of Hebrew or Arabic is written from right to left, and so is its reference above it. The popup is its own visual root,
            // so the direction is decided here, per verse (by its first letter): an English Bible in an Arabic interface stays left to right.
            bool rtl = BibleTextBlock.StartsRightToLeft(BibleTextBlock.PlainText(verse.Inlines!));
            // With several Bibles the reference says which one the verse is from.
            var reference = $"{hit.Position.Book.Name} {hit.Position.Chapter}:{hit.Position.Verse}" + (texts.Count > 1 ? $" · {source.Name}" : "");
            var content = new StackPanel
            {
                Spacing = 2,
                FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                Children =
                {
                    new TextBlock { Text = reference, FontWeight = FontWeight.SemiBold, FontSize = UiScale.Size(12), Opacity = 0.7 },
                    verse,
                },
            };
            var button = new Button { Content = content };
            button.Classes.Add("entry");
            button.Click += (_, _) => GoToSearchResult(found);
            SearchResults.Children.Add(button);
        }
    }

    /// <summary>The Bible that is read is a plain Bible; the installed one of the same folder does the search.</summary>
    private static InstallableBook SearchableFor(BookText text) =>
        (Settings.Current.Books ?? new List<BookText>()).OfType<InstallableBook>()
            .FirstOrDefault(b => b.EnglishName == text.EnglishName || b.EnglishName == text.Name)
        ?? new InstallableBook { EnglishName = text.EnglishName ?? text.Name, Name = text.Name };

    /// <summary>A verse that a search found, with the Bible (the text, with its books) it was found in.</summary>
    private sealed record SearchHit(BookText Bible, InstallableBook.SearchResult Result);

    /// <summary>
    /// Goes to a verse that was found, in the text that is shown (an interlinear text stays one). The verse of another Bible is put where the first Bible of the text has
    /// it when the versifications of the two are known (see <see cref="VerseMapper"/>), else it keeps its numbers. A book that the text shown does not have (the books
    /// of an interlinear text are the first Bible's) cannot be gone to: nothing happens.
    /// </summary>
    private void GoToSearchResult(SearchHit hit)
    {
        if (Target is null) return;
        var result = hit.Result;
        var book = Target.Books?.FirstOrDefault(b => b.Index == result.Position.Book.Index);
        if (book is null) return;
        SearchPopup.IsOpen = false;
        ShowOnlineBibles = false;
        ShowSettings = false;
        ShowTranslations = false;
        ShowRawFile = false;

        int chapter = result.Position.Chapter, verse = result.Position.Verse;
        var shown = Position?.Book?.BookText ?? BookText;
        var first = shown?.Sources is { Count: > 0 } sources ? sources[0] : shown;
        if (first is not null && !ReferenceEquals(hit.Bible, first) &&
            VerseMapper.Create(VerseMapper.VersificationOf(hit.Bible), VerseMapper.VersificationOf(first)) is { } mapper)
        {
            (chapter, verse) = mapper.Map(result.Position.Book.Index, chapter, verse);
            if (verse == 0) verse = 1; // the heading of a chapter: its first verse
        }
        var place = new BookText.VersePosition { Book = book, Chapter = chapter, Verse = verse };
        PushHistory(place);
        Target.Position = place;
    }

    private void AddMenuEntry(string? image, string header, Func<Task> action)
    {
        Button button;
        if (image == null) button = new Button { Content = header };
        else
        {
            var panel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal };
            var svg = new ThemedSvg { Path = $"/Assets/{image}", Size = 26 }; // coloured for the theme, sized for the interface
            panel.Children.Add(svg);
            panel.Children.Add(new TextBlock() { Text = header, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            button = new Button { Content = panel };
        }
        button.Classes.Add("entry");
        button.FontSize = 18;
        button.MinHeight = 48;
        button.Click += async (_, _) =>
        {
            MenuPopup.IsOpen = false;
            await action();
        };
        MenuItems.Children.Add(button);
    }

    private void OnMenuPopupClosed(object? sender, EventArgs e) => _menuClosedAt = DateTime.UtcNow;

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>"Genesis 1:9", "Genesis 1" or "Genesis", as far as the position goes.</summary>
    private static string ReferenceOf(BookText.VersePosition position)
    {
        string name = position.Book?.Name ?? string.Empty;
        return position.Verse > 0 ? $"{name} {position.Chapter}:{position.Verse}"
            : position.Chapter > 0 ? $"{name} {position.Chapter}"
            : name;
    }

    /// <summary>The words of the verse, without footnotes or markup; empty when the position is not a verse.</summary>
    private static string VerseTextOf(BookText.VersePosition position)
    {
        if (position.Book is not { } book || position.Chapter <= 0 || position.Verse <= 0) return string.Empty;
        var verse = book.Runs
            .Where(run => run.Class == BookText.Run.Classes.Chapter && run.Chapter == position.Chapter)
            .SelectMany(chapter => chapter.Runs ?? [])
            .FirstOrDefault(run => run.Class == BookText.Run.Classes.Verse && run.Position?.Verse == position.Verse);
        return verse is null ? string.Empty : Regex.Replace(PlainText(verse), @"\s+", " ").Trim();

        static string PlainText(BookText.Run run) =>
            run.Class == BookText.Run.Classes.Footnote ? string.Empty
            : run.Runs?.Any() == true ? string.Concat(run.Runs.Select(PlainText))
            : run.Text ?? string.Empty;
    }

    private void OnPopupClosed(object? sender, EventArgs e) => _closedAt = DateTime.UtcNow;

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (Target?.Books is { } books) ShowBooks(books);
    }

    /// <summary>First page of the popup: one button per book.</summary>
    private void ShowBooks(IReadOnlyList<BookText.Book> books)
    {
        _chaptersFor = null;
        ChapterHeader.IsVisible = false;
        BookGrid.Children.Clear();
        // Front matter (table of contents, preface, glossary…) has no chapters to scroll to.
        foreach (var book in books.Where(book => !IsFrontMatter(book)))
            BookGrid.Children.Add(CreateButton(Abbreviation(book), book.Name, ReferenceEquals(book, Position?.Book), () => ShowChapters(book), book.Class.ToString()));
    }

    /// <summary>Second page of the popup: one button per chapter of <paramref name="book"/>.</summary>
    private async void ShowChapters(BookText.Book book)
    {
        _chaptersFor = book;
        TitleText.Text = book.Name;
        ChapterHeader.IsVisible = true;
        BookGrid.Children.Clear();
        BookGrid.Children.Add(new TextBlock { Text = "…", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(8) });

        // The chapters are counted from the markdown headings: far cheaper than parsing the whole book.
        List<int> chapters;
        try { chapters = await Task.Run(() => ChapterNumbers(book)); }
        catch { chapters = new List<int>(); }
        if (!ReferenceEquals(_chaptersFor, book) || !BookPopup.IsOpen) return; // went back or closed in the meantime

        if (chapters.Count == 0) { SelectChapter(book, 1); return; }
        BookGrid.Children.Clear();
        // Chapter 0 is the book's heading, above chapter 1.
        int current = ReferenceEquals(book, Position?.Book) ? Math.Max(1, Position!.Chapter) : -1;
        foreach (int chapter in chapters)
        {
            int number = chapter;
            BookGrid.Children.Add(CreateButton(number.ToString(), null, number == current, () => SelectChapter(book, number)));
        }
    }

    private static List<int> ChapterNumbers(BookText.Book book) =>
        Regex.Matches(book.Markdown, @"(?:^|\n)#\s+(?<n>[0-9]+)\s*\r?\n")
            .Select(m => int.Parse(m.Groups["n"].Value))
            .Distinct()
            .ToList();

    private static string Abbreviation(BookText.Book book)
    {
        if (!string.IsNullOrWhiteSpace(book.Abbrevation)) return book.Abbrevation.Replace(" ", "").Replace(".", "");
        string name = book.Name?.Replace(" ", "").Replace(".", "") ?? string.Empty;
        return name.Length > 3 ? name[..3] : name;
    }

    /// <summary>One cell of a grid; the look comes from the <c>cell</c> and <c>current</c> styles in the AXAML.</summary>
    private static Button CreateButton(string text, string? tip, bool isCurrent, Action onClick, string? category = null)
    {
        var button = new Button { Content = text };
        button.Classes.Add("cell");
        if (category is not null) button.Classes.Add(category); // the colour per Bible.Book.Classes value, see the AXAML
        if (isCurrent) button.Classes.Add("current");
        if (tip is not null) ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static bool IsFrontMatter(BookText.Book book) =>
        System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal);

    private void SelectChapter(BookText.Book book, int chapter)
    {
        BookPopup.IsOpen = false;
        // Verse 0 is the start of the chapter; the text block loads the book if it is not shown yet.
        GoTo(new BookText.VersePosition { Book = book, Chapter = chapter, Verse = 0 });
    }

    /// <summary>
    /// Takes the reader to a place of the Bible that is shown, as the book and chapter buttons do: the place is noted in the history (the back and forward
    /// buttons, and the list of places) and the position indicator names it. Used by the text block when a chapter number or a verse number was picked.
    /// </summary>
    public void GoTo(BookText.VersePosition place)
    {
        ShowOnlineBibles = false; // a place is read in the text, not in the online list
        ShowSettings = false;
        ShowTranslations = false;
        ShowRawFile = false;
        if (Target is null) return;
        PushHistory(place);
        Target.Position = place;
    }

    private static FontFamily GreekFont => SettingsView.GreekFamily;
    private static FontFamily HebrewFont => SettingsView.HebrewFamily;

    /// <summary>Alpha, beta and aleph, bet: the language of the Strong's numbers of the testament that is read.</summary>
    private void UpdateStrongsLetters(BookText.Book? book)
    {
        // The Old Testament has Hebrew Strong's numbers (H), the New Testament Greek ones (G). The books of the
        // Apocrypha are Greek; front and back matter say nothing, so the letters stay as they are.
        bool? hebrew = book?.Class switch
        {
            BookText.Book.Classes.Pentateuch or BookText.Book.Classes.Historical or BookText.Book.Classes.PoetryAndWisdom
                or BookText.Book.Classes.MajorProphets or BookText.Book.Classes.MinorProphets => true,
            BookText.Book.Classes.Gospels or BookText.Book.Classes.Acts or BookText.Book.Classes.Epistles
                or BookText.Book.Classes.Revelation or BookText.Book.Classes.Apocrypha => false,
            _ => null,
        };
        if (hebrew is null && !string.IsNullOrEmpty(StrongsLetters.Text)) return;
        StrongsLetters.FontFamily = hebrew == true ? HebrewFont : GreekFont;
        StrongsLetters.FontSize = UiScale.Size(hebrew == true ? 30 : 20);
        StrongsLetters.Text = hebrew == true ? "אב" : "αβ";
    }

    private BookText? _watchedBible;

    /// <summary>Follows <see cref="BookText.HasStrongs"/> of the Bible that is shown, so the Strongs button appears as soon as it is known.</summary>
    private void WatchBible(BookText? bible)
    {
        if (ReferenceEquals(bible, _watchedBible)) return;
        if (_watchedBible is not null) _watchedBible.PropertyChanged -= OnBiblePropertyChanged;
        _watchedBible = bible;
        if (bible is not null) bible.PropertyChanged += OnBiblePropertyChanged;
    }

    private void OnBiblePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // A book is read on any thread, the button belongs to the UI thread.
        if (e.PropertyName == nameof(BookText.HasStrongs)) Avalonia.Threading.Dispatcher.UIThread.Post(UpdateText);
    }

    /// <summary>
    /// The book that the bar names. It is the book of <see cref="Position"/>, but after a switch of the Bible, Position can still be the
    /// one of the Bible that was replaced (a position at the start of a book is not published again): then it is the book of the Bible
    /// that is shown with the same number, or the book that is shown if that Bible has no such book. Position itself is not changed.
    /// </summary>
    private BookText.Book? NamedBook(BookText.VersePosition? position)
    {
        var book = position?.Book;
        if (Target?.BookText is not { } bible || book is null || ReferenceEquals(book.BookText, bible)) return book;
        if (bible.Books.FirstOrDefault(b => b.Index == book.Index) is { } same) return same;
        return Target.Books is { } books && Target.CurrentBookIndex < books.Count ? books[Target.CurrentBookIndex] : book;
    }

    private void UpdateText()
    {
        var position = Position;
        var book = NamedBook(position);
        WatchBible(book?.BookText);
        BackButton.IsVisible = ForwardButton.IsVisible = IsWide;
        // The Strongs button is only there for a Bible that has Strong's numbers (known once a book of it has been read), and while a Strong's
        // dictionary is installed (without one the button has nothing to switch on).
        StrongsButton.IsVisible = book?.BookText?.HasStrongs == true && StrongsDictionariesInstalled().Count > 0;
        UpdateStrongsLetters(book);
        BookName.Text = book?.Name ?? string.Empty;
        BibleName.Text = book?.BookText?.Name ?? string.Empty;
        ReferenceText.Text = position is null ? "—"
            : position.Verse > 0 ? $"{position.Chapter}:{position.Verse}"
            : position.Chapter > 0 ? position.Chapter.ToString() : string.Empty;
    }

    public void UpdateIsWide()
    {
        // The widths of the existing columns are changed: a Grid that is given a new ColumnDefinitions after it was
        // laid out can keep its old measure, which left the position button without any size in a wide bar.
        var width = IsWide ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        //ToolbarGrid.ColumnDefinitions[3].Width = width; // the position button: the room that is left in a narrow bar
        PositionGrid.ColumnDefinitions[0].Width = width;  // its book and Bible name

        // Tighter buttons in a narrow bar, where every pixel counts; roomier ones in a wide bar.
        var padding = IsWide ? new Thickness(4, 4) : new Thickness(2, 4);
        foreach (var button in new[] { MenuButton, BackButton, ForwardButton, PositionButton, BiblesButton, SearchButton, StrongsButton })
            button.Padding = padding;
    }

    private string ShortenWithEllipsis(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text[..(maxLength - 3)] + "...";
    }
}
