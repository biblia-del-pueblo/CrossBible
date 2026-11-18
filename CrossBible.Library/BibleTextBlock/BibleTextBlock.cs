using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrossBible.Library.Downloads;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Metadata;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using System.Text.RegularExpressions;

namespace CrossBible.Library.UI;

/// <summary>Raised when the user scrolls past the first or the last page of the shown text.</summary>
public sealed class BoundaryEventArgs(bool atEnd) : EventArgs
{
    /// <summary>True: scrolled past the end; false: scrolled back past the start.</summary>
    public bool AtEnd { get; } = atEnd;
}

/// <summary>Which side a child of <see cref="BibleTextBlock"/> floats to.</summary>
public enum FloatSide
{
    Left,
    Right,
}

/// <summary>
/// A panel that flows <see cref="Text"/> around its children, like CSS <c>float: left | right</c>.
/// Every child is a float; set <see cref="SideProperty"/> (FloatTextBlock.Side) on it to choose the side.
/// Floats on the same side stack vertically; text lines are narrowed to whatever space the floats leave.
/// </summary>
public partial class BibleTextBlock : Panel
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<BibleTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<BibleTextBlock>();

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<BibleTextBlock, TextAlignment>(nameof(TextAlignment), TextAlignment.Justify);

    /// <summary>Gap between a float and the text wrapping around it.</summary>
    public static readonly StyledProperty<double> FloatSpacingProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(FloatSpacing), 12);

    /// <summary>
    /// Number of text columns; 0 (default) means automatic: 1 column, or as many as fit when
    /// <see cref="ColumnWidth"/> is set.
    /// </summary>
    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<BibleTextBlock, int>(nameof(ColumnCount), 0);

    /// <summary>
    /// Fixed width of each column (NaN = columns share the available width). With a bounded height the text
    /// fills a column to that height and then simply adds more columns, so the control grows wider.
    /// </summary>
    public static readonly StyledProperty<double> ColumnWidthProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(ColumnWidth), double.NaN);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<BibleTextBlock, double>(nameof(ColumnSpacing), 24);

    /// <summary>
    /// <see cref="Orientation.Horizontal"/> (default): columns side by side, scrolled one whole column at a time.
    /// <see cref="Orientation.Vertical"/>: a single column as wide as the control, scrolled vertically
    /// (touch drag with inertia, mouse wheel, scroll bar), meant for phones.
    /// </summary>
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<BibleTextBlock, Orientation>(nameof(Orientation), Orientation.Horizontal);

    /// <summary>
    /// The verse at the start of the view (the first visible column, or the top edge in vertical mode).
    /// Setting it scrolls there, loading the verse's books first when they are not shown.
    /// </summary>
    public static readonly StyledProperty<BookText.VersePosition?> PositionProperty =
        AvaloniaProperty.Register<BibleTextBlock, BookText.VersePosition?>(nameof(Position),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// The Bible to read. Setting it (or binding it, e.g. to <see cref="BibleToolbar.BookText"/>) loads its books when
    /// they are not loaded yet and shows the first real book, skipping the front matter.
    /// </summary>
    public static readonly StyledProperty<BookText?> BookTextProperty =
        AvaloniaProperty.Register<BibleTextBlock, BookText?>(nameof(BookText));

    /// <summary>
    /// True while the text asked for is still being prepared: a requested verse that is not on screen yet, or the very
    /// first text. Show a spinner while it is set.
    /// </summary>
    public static readonly DirectProperty<BibleTextBlock, bool> IsBusyProperty =
        AvaloniaProperty.RegisterDirect<BibleTextBlock, bool>(nameof(IsBusy), o => o.IsBusy);

    public static readonly AttachedProperty<FloatSide> SideProperty =
        AvaloniaProperty.RegisterAttached<BibleTextBlock, Control, FloatSide>("Side");

    /// <summary>
    /// The height that a float takes in the layout, when that is less than its own height (NaN: its own height). The
    /// control is still arranged at its full size, so nothing of it is cut off; it only reaches over the lines below
    /// it, which are not shortened by it.
    /// </summary>
    public static readonly AttachedProperty<double> LayoutHeightProperty =
        AvaloniaProperty.RegisterAttached<BibleTextBlock, Control, double>("LayoutHeight", double.NaN);

    static BibleTextBlock()
    {
        AffectsMeasure<BibleTextBlock>(TextProperty, FontSizeProperty, FontFamilyProperty,
            TextAlignmentProperty, FloatSpacingProperty, ColumnCountProperty, ColumnWidthProperty, ColumnSpacingProperty, OrientationProperty);
        AffectsArrange<BibleTextBlock>(SideProperty, LayoutHeightProperty);
        PositionProperty.Changed.AddClassHandler<BibleTextBlock>((c, e) =>
        {
            if (!c._settingPosition) c.ScrollToPosition(e.NewValue as BookText.VersePosition);
        });
        BookTextProperty.Changed.AddClassHandler<BibleTextBlock>((c, e) => c.OnBibleChanged(e.NewValue as BookText));
        AffectsMeasure<BibleTextBlock>(ForegroundProperty);
        SideProperty.Changed.AddClassHandler<Control>((c, _) => (c.Parent as BibleTextBlock)?.MarkDirty());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }
    public double FloatSpacing { get => GetValue(FloatSpacingProperty); set => SetValue(FloatSpacingProperty, value); }

    /// <summary>Whether the words of Jesus are coloured red. Call <see cref="RebuildAsync"/> after changing it.</summary>
    public bool ShowWordsOfJesus { get; set; } = true;
    /// <summary>Whether verse numbers are green; like <see cref="ShowWordsOfJesus"/> it applies when the text is rebuilt.</summary>
    public bool ShowVerseNumbersInGreen { get; set; }

    /// <summary>
    /// Whether words are split at the end of a line, for the languages that have hyphenation patterns (see
    /// <see cref="Hyphenation"/>). The text is flowed again when it changes.
    /// </summary>
    public bool Hyphenate
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            MarkDirty();
        }
    } = true;

    private BookText? _hyphenBible;
    private string? _hyphenCode;

    /// <summary>The language of the patterns for the text that is shown; null when it is not hyphenated.</summary>
    private string? HyphenationLanguage
    {
        get
        {
            if (!Hyphenate) return null;
            var bible = BookText;
            if (!ReferenceEquals(bible, _hyphenBible) || (_hyphenCode is null && bible is not null))
            {
                _hyphenBible = bible;
                _hyphenCode = Hyphenation.LanguageOf(bible);
            }
            return _hyphenCode;
        }
    }

    public BookText? BookText { get => GetValue(BookTextProperty); set => SetValue(BookTextProperty, value); }
    public BookText.VersePosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public int ColumnCount { get => GetValue(ColumnCountProperty); set => SetValue(ColumnCountProperty, value); }

    /// <summary>
    /// True while the text shows a single column that no horizontal drag pans (in columns that are paged sideways, a
    /// drag turns the pages), so a horizontal swipe on it is free for other uses.
    /// </summary>
    public bool IsSingleColumn => _visibleColumns <= 1 && !(_scrolling && Orientation != Orientation.Vertical);
    public double ColumnWidth { get => GetValue(ColumnWidthProperty); set => SetValue(ColumnWidthProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    public static FloatSide GetSide(Control control) => control.GetValue(SideProperty);
    public static void SetSide(Control control, FloatSide value) => control.SetValue(SideProperty, value);
    public static double GetLayoutHeight(Control control) => control.GetValue(LayoutHeightProperty);
    public static void SetLayoutHeight(Control control, double value) => control.SetValue(LayoutHeightProperty, value);

    /// <param name="PageStart">The line follows a <see cref="PageBreak"/>: it starts a column.</param>
    /// <summary>
    /// A line in its place. The line was formatted from the text of its chapter (its text positions count from the start of that), which is
    /// <paramref name="Base"/> characters into the text of the window; <see cref="Start"/> and <see cref="Bounds"/> are in the text of the window.
    /// So the lines of a chapter can be kept when the window is made again, and be moved in it.
    /// </summary>
    private readonly record struct PlacedLine(TextLine Line, Point Origin, bool PageStart = false, int Base = 0)
    {
        /// <summary>The first character of the line, in the text of the window.</summary>
        public int Start => Base + Line.FirstTextSourceIndex;

        /// <summary>The end of the line (behind its last character), in the text of the window.</summary>
        public int End => Start + Line.Length;

        /// <summary>The boxes of the characters from <paramref name="start"/> on (the text of the window).</summary>
        public IReadOnlyList<TextBounds> Bounds(int start, int length) => Line.GetTextBounds(start - Base, length);
    }

    /// <summary>Flattened-text index at which each line following a <see cref="PageBreak"/> starts.</summary>
    private HashSet<int> _breakStarts = new();

    private readonly List<PlacedLine> _lines = new();
    private readonly List<Rect> _floatRects = new();

    // ---- Position ------------------------------------------------------------------------------------
    // Verse (and chapter) starts are VerseRun inlines; Flatten records where each one lands in the flowed text.
    // Position is the last mark at or before the first visible line; setting it scrolls to the line of its mark.

    private List<(int Index, BookText.VersePosition Pos)> _markers = new();
    private BookText.VersePosition? _pendingPosition; // to scroll to once the books that contain it are laid out

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => SetAndRaise(IsBusyProperty, ref _isBusy, value); }

    /// <summary>
    /// Busy: a verse is wanted that is not shown yet, books are set but no text has been laid out, or another Bible is
    /// being opened (the text of the one before stays shown until the new one is laid out). Never without a Bible (no
    /// books): there is nothing to wait for, and a spinner would only say that something is coming.
    /// </summary>
    private void UpdateBusy() =>
        IsBusy = Books is { Count: > 0 } && (_pendingPosition is not null || _switchingBible || _lines.Count == 0);
    private bool _switchingBible;
    private bool _settingPosition, _positionQueued, _programmatic, _keepPosition;

    private void RequestPositionUpdate()
    {
        if (_positionQueued) return;
        _positionQueued = true;
        Dispatcher.UIThread.Post(UpdatePosition, DispatcherPriority.Background);
    }

    /// <summary>Publishes the verse at the start of the view as <see cref="Position"/>.</summary>
    private void UpdatePosition()
    {
        _positionQueued = false;
        // After scrolling to a requested verse the view may start a verse or two earlier (columns have fixed
        // tops); the requested value stays until the user scrolls on.
        if (_keepPosition || _markers.Count == 0 || !_hasLayout || _dirty || _pitch <= 0) return;

        PlacedLine? top = null;
        if (Orientation == Orientation.Vertical)
        {
            foreach (var pl in _lines)
                if (pl.Origin.Y + pl.Line.Height > _offsetY + 0.5) { top = pl; break; }
        }
        else
        {
            int column = _scrolling ? _firstColumn : 0;
            foreach (var pl in _lines)
                if (LogicalColumn(pl) >= column) { top = pl; break; }
        }
        if (top is not { } first) return;

        int index = first.Start;
        var best = _markers[0].Pos;
        foreach (var (at, pos) in _markers)
        {
            if (at > index) break;
            best = pos;
        }
        if (ReferenceEquals(best, Position)) return;
        _settingPosition = true;
        try { SetCurrentValue(PositionProperty, best); }
        finally { _settingPosition = false; }
    }

    /// <summary>
    /// The mark for <paramref name="pos"/> in the laid-out text: the exact verse, else the next verse of the
    /// chapter, else the chapter start, else the start of the book (also for 1:1, so the title shows).
    /// Null when the book is not in the text.
    /// </summary>
    private static (int Index, BookText.VersePosition Pos)? ResolveMarker(BookText.VersePosition pos,
        List<(int Index, BookText.VersePosition Pos)> markers)
    {
        (int, BookText.VersePosition)? nextVerse = null, chapterStart = null, bookStart = null;
        foreach (var m in markers)
        {
            if (!ReferenceEquals(m.Pos.Book, pos.Book)) continue;
            bookStart ??= m;
            // The beginning of a book (1:1, or the chapter 1 button) shows the book's title too, which sits above
            // chapter 1: the book's first mark is its start. This comes first, as chapter 1 has an exact mark.
            if (pos.Chapter <= 1 && pos.Verse <= 1 && bookStart.Value.Item2.Chapter == 0) return bookStart;
            if (m.Pos.Chapter != pos.Chapter) continue;
            chapterStart ??= m;
            if (m.Pos.Verse == pos.Verse) return m;
            if (m.Pos.Verse > pos.Verse) nextVerse ??= m;
        }
        return nextVerse ?? chapterStart ?? bookStart;
    }

    /// <summary>The first line that reaches the text at <paramref name="index"/>.</summary>
    private PlacedLine? LineAt(int index)
    {
        foreach (var pl in _lines)
            if (pl.End > index) return pl;
        return null;
    }

    /// <summary>Whether the chapters that are shown (their text, as it is laid out) include the chapter of <paramref name="pos"/>.</summary>
    private bool WindowHolds(BookText.VersePosition pos)
    {
        if (Books is not { } books || pos.Book is null) return false;
        int book = -1;
        for (int i = BookIndex; i <= _lastBookIndex && i < books.Count; i++)
            if (ReferenceEquals(books[i], pos.Book)) { book = i; break; }
        if (book < 0) return false;
        // The first book is shown from its chapter ChapterIndex on (all of it, with its title, when that is the first), the last one up to _lastChapterIndex.
        bool fromOk = book > BookIndex || ChapterIndex == 0 || pos.Chapter >= _shownChapterNumber;
        bool toOk = book < _lastBookIndex || pos.Chapter <= _shownLastChapterNumber;
        return fromOk && toOk;
    }

    private void ScrollToPosition(BookText.VersePosition? pos)
    {
        if (pos?.Book is null) return;
        if (TryScrollTo(pos)) return;
        if (Books is not { } books) return;

        // Not in the shown text: show the chapter of the verse (of its book) and scroll there once laid out.
        int index = -1;
        for (int i = 0; i < books.Count; i++)
            if (ReferenceEquals(books[i], pos.Book)) { index = i; break; }
        if (index < 0) return;
        _pendingPosition = pos;
        _keepPosition = true;
        UpdateBusy();
        // The chapter is the one that is shown, but not laid out yet (the Bible was just switched): that layout resolves the position.
        // The window must not be loaded again: its content would replace the scroll anchor that this layout finds with none, and the
        // reader would be at the start of the chapter.
        if ((!_hasLayout || _dirty) && ReferenceEquals(Books, books) && WindowHolds(pos)) return;
        if (!_loadingBook) _ = SwitchWindowAsync(books, index, ChapterRequest.ByNumber(pos.Chapter), null);
    }

    private bool TryScrollTo(BookText.VersePosition pos)
    {
        // Only when the shown chapters include the one wanted: else the lookup falls back to the start of the book (of the markers that
        // there are) and the reader is scrolled to the start of the chapter that is shown, and the wanted one is never loaded.
        if (!_hasLayout || _dirty || !WindowHolds(pos) || ResolveMarker(pos, _markers) is not { } mark || LineAt(mark.Index) is not { } line) return false;
        _keepPosition = true;
        _programmatic = true;
        try
        {
            if (Orientation == Orientation.Vertical) { if (_vScrolling) SetOffsetY(line.Origin.Y); }
            else if (_scrolling && _pitch > 0) ScrollToColumn(LogicalColumn(line));
        }
        finally { _programmatic = false; }
        return true;
    }

    /// <summary>Makes <paramref name="column"/> the first visible one: glides when near, jumps when far.</summary>
    private void ScrollToColumn(int column)
    {
        column = Math.Clamp(column, 0, Math.Max(0, (int)_scrollBar.Maximum));
        if (Math.Abs(column - _firstColumn) <= Math.Max(2, _visibleColumns * 2)) { ApplyColumn(column); return; }

        StopAnimation();
        _firstColumn = column;
        _offsetX = OffsetOf(column);
        _updatingBar = true;
        _scrollBar.Value = column;
        _updatingBar = false;
        _popup.IsOpen = false;
        InvalidateArrange();
        _layer.InvalidateVisual();
        RequestWindowCheck();
    }

    // ---- Scrolling across chapters ------------------------------------------------------------------
    // With <see cref="Books"/> set, one chapter of one book is shown (see MoveChapter and the members above).

    // Where the next Bible opens (see OpenNextAt); else it opens where the reader was in the Bible it replaces.
    private (int BookIndex, int Chapter, int Verse)? _openAt;

    /// <summary>
    /// The next Bible set (or bound) opens at this book (<c>Book.Index</c>), chapter and verse instead of where the
    /// reader is now, e.g. at the last position of a previous run.
    /// </summary>
    public void OpenNextAt(int bookIndex, int chapter, int verse) => _openAt = (bookIndex, chapter, verse);

    private async void OnBibleChanged(BookText? bible)
    {
        // Where the reader was in the Bible that is replaced: the new one opens there.
        (int BookIndex, int Chapter, int Verse)? previous = _openAt
            ?? (Position is { Book: { } shown } pos ? (shown.Index, pos.Chapter, pos.Verse) : null);
        _openAt = null;
        if (bible is null)
        {
            // No Bible: forget the books and empty the view (a book still loading is dropped when it arrives).
            Books = null;
            _pendingPosition = null;
            _switchingBible = false;
            UpdateBusy();
            if (_flat is not null) { _flat = null; OnInlinesChanged(); }
            Inlines.Clear();
            return;
        }
        // Until the new text is laid out (see MeasureCore) the spinner shows over the text of the Bible before.
        _switchingBible = true;
        UpdateBusy();
        try
        {
            if (bible.Books.Count == 0)
            {
                // Reading the folder is work for a worker thread; the books belong to the chosen Bible, not the temporary one.
                var loaded = await Task.Run(() => BookText.OpenFrom(bible.ConfigPath));
                foreach (var book in loaded.Books) book.BookText = bible;
                bible.Books = loaded.Books;
            }
            if (!ReferenceEquals(BookText, bible)) return; // another Bible was chosen while this one loaded
            var books = bible.Books;
            Books = books;
            int index = books.FindIndex(book =>
                !System.IO.Path.GetFileName(book.File ?? string.Empty).StartsWith("00.", StringComparison.Ordinal));
            // Keep the position: the same book (by its number), chapter and verse of the new Bible, when it has them.
            int kept = previous is { } old ? books.FindIndex(book => book.Index == old.BookIndex) : -1;
            int showIndex = kept >= 0 ? kept : index < 0 ? 0 : index;
            await ShowBookAsync(showIndex);
            if (books.Count > 0 && ReferenceEquals(BookText, bible))
            {
                // Position is the same place in the new Bible (its book object, which the toolbar and the book and chapter grids compare
                // with); the start of its first book when it has not the book that was shown.
                var place = kept >= 0 && previous is { } p
                    ? new BookText.VersePosition { Book = books[kept], Chapter = p.Chapter, Verse = p.Verse }
                    : new BookText.VersePosition { Book = books[showIndex], Chapter = 0, Verse = 0 };
                if (place.Chapter > 0 || place.Verse > 0) Position = place; // scrolls to it
                else
                {
                    // The start of a book is shown already: published only, not scrolled to.
                    _settingPosition = true;
                    try { SetCurrentValue(PositionProperty, place); }
                    finally { _settingPosition = false; }
                }
            }
            if (books.Count == 0) { _switchingBible = false; UpdateBusy(); } // nothing to lay out
        }
        catch (Exception ex)
        {
            _switchingBible = false;
            UpdateBusy();
            System.Diagnostics.Debug.WriteLine($"Could not open {bible.EnglishName}: {ex}");
        }
    }

    /// <summary>The books to scroll through; use <see cref="ShowBookAsync"/> to start at one.</summary>
    public IReadOnlyList<BookText.Book>? Books { get; set; }

    /// <summary>Index in <see cref="Books"/> of the book the shown text starts with (the text goes on into the books after it).</summary>
    public int BookIndex { get; private set; }

    /// <summary>Index in <see cref="Books"/> of the book the reader is in: the book of <see cref="Position"/>.</summary>
    public int CurrentBookIndex
    {
        get
        {
            if (Position?.Book is { } book && Books is { } books)
                for (int i = Math.Max(0, BookIndex); i < books.Count; i++)
                    if (ReferenceEquals(books[i], book)) return i;
            return BookIndex;
        }
    }

    /// <summary>
    /// The chapter that is shown, as an index into the chapters of the book (0 is the first chapter, which comes with the title of the
    /// book). A text without chapters is shown as one.
    /// </summary>
    public int ChapterIndex { get; private set; }

    /// <summary>The shown book or chapter changed (<see cref="ShowBookAsync"/>, a jump to a verse, or a move on past the end of the chapter).</summary>
    public event EventHandler? BookChanged;

    /// <summary>The user scrolled past the first or last page. Raised whether or not <see cref="Books"/> is set.</summary>
    public event EventHandler<BoundaryEventArgs>? BoundaryReached;

    private bool _loadingBook;
    private int _totalColumns;
    // The window is the chapters from (BookIndex, ChapterIndex) to (_lastBookIndex, _lastChapterIndex): it goes over the border of books, so the text
    // goes on from the end of one book into the next (see CheckWindow).
    private int _lastBookIndex;          // the book of the last chapter that is shown
    private int _lastChapterIndex;       // the last chapter that is shown: the window is several chapters (see CheckWindow)
    private int _windowSlots = 1;        // how many chapters the window holds (a text without chapters counts as one)
    private int _shownChapterNumber;     // the number of the first shown chapter (0 for a text without chapters)
    private int _shownLastChapterNumber; // and of the last one
    private bool _checkQueued;           // a check of the window is waiting (see RequestWindowCheck)
    private double _screenHeight;        // the height of the screen the text is laid out for (0 when it is not known)

    /// <summary>
    /// Where the reader is in the text, when the window of chapters is made again around it (see CheckWindow): the verse at the top of the screen,
    /// how far into its line the screen starts, and the scroll position then. The new text is laid out in the same place, so nothing seems to move.
    /// </summary>
    private (BookText.VersePosition Pos, double Delta, double Offset, int CharDelta)? _readingAnchor;

    // In columns: the line that starts the first visible column, as a verse mark and the characters after it. The columns are cut from the top of the
    // window, so when the window grows or shrinks at its start the greedy cut would end the columns somewhere else and the page would change under
    // the reader; this line is kept as the start of a column instead (see SplitColumns), and everything after it is cut as it was.
    private (BookText.VersePosition Pos, int CharDelta)? _columnAnchor;
    private int _forcedColumnStart = -1;
    private double? _pendingAnchor;    // scroll position to restore once the new chapter is laid out (see EndOfChapter)
    private readonly System.Diagnostics.Stopwatch _boundaryClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastBoundary = TimeSpan.FromSeconds(-10);

    /// <summary>As a scroll anchor: far beyond the last position, which the layout clamps to the end of the chapter.</summary>
    private const double EndOfChapter = 1e9;

    // ---- One chapter at a time -----------------------------------------------------------------------
    // One chapter of one book is shown (the text, and the objects that it is made of, stay small), or, when it does not fill the screen
    // (a short psalm), as many of the chapters next to it as it takes to fill it. Scrolling past the end shows the next chapter, past the
    // start the previous one (at its end), also over the border of two books.

    /// <summary>Which chapter of a book is wanted: the one with a number, the one at an index, or the last.</summary>
    private readonly record struct ChapterRequest(int Number = -1, int Index = -1, bool Last = false)
    {
        public static ChapterRequest First => new(Index: 0);
        public static ChapterRequest LastOne => new(Last: true);
        public static ChapterRequest ByNumber(int number) => new(Number: number);
        public static ChapterRequest ByIndex(int index) => new(Index: index);
    }

    /// <summary>The chapters of a book (parsing the book, if that has not happened): none for a text that is not a Bible book.</summary>
    private static List<BookText.Run> ChaptersOf(BookText.Book book) => book.Chapters;

    /// <summary>How many places a book takes in the window: its chapters (a text without chapters is one). Parses the book if that has not happened.</summary>
    private static int SlotCountOf(BookText.Book book) => Math.Max(1, ChaptersOf(book).Count);

    /// <summary>
    /// The chapter <paramref name="n"/> places on from <paramref name="at"/> (before it when negative), counted over the border of books, as far as there are
    /// books. Parses the books it passes: meant for a worker.
    /// </summary>
    private static (int Book, int Chapter) StepSlots(IReadOnlyList<BookText.Book> books, (int Book, int Chapter) at, int n)
    {
        var (b, c) = at;
        for (; n > 0; n--)
        {
            if (c + 1 < SlotCountOf(books[b])) c++;
            else if (b + 1 < books.Count) { b++; c = 0; }
            else break;
        }
        for (; n < 0; n++)
        {
            if (c > 0) c--;
            else if (b > 0) { b--; c = SlotCountOf(books[b]) - 1; }
            else break;
        }
        return (b, c);
    }

    /// <summary>The places (book index and chapter index) of a window, in the order of the text.</summary>
    private static IEnumerable<(int Book, int Chapter)> SlotsOf(IReadOnlyList<BookText.Book> books, int firstBook, int first, int lastBook, int last)
    {
        for (int b = firstBook; b <= lastBook; b++)
        {
            int from = b == firstBook ? first : 0, to = b == lastBook ? last : SlotCountOf(books[b]) - 1;
            for (int c = from; c <= to; c++) yield return (b, c);
        }
    }

    /// <summary>The index of the wanted chapter in <paramref name="chapters"/>; the first when a number is not found (the last when it is beyond the end).</summary>
    private static int ResolveChapter(List<BookText.Run> chapters, ChapterRequest request)
    {
        if (chapters.Count == 0) return 0;
        if (request.Last) return chapters.Count - 1;
        if (request.Index >= 0) return Math.Min(request.Index, chapters.Count - 1);
        if (request.Number > 1)
        {
            int i = chapters.FindIndex(c => c.Chapter == request.Number);
            if (i >= 0) return i;
            if (request.Number > chapters[^1].Chapter) return chapters.Count - 1;
        }
        return 0;
    }

    /// <summary>
    /// Shows the first chapter of <c>Books[index]</c>, from its start (with the title of the book).
    /// Reading and parsing the book happens off the UI thread.
    /// </summary>
    public async Task ShowBookAsync(int index)
    {
        if (Books is not { } books || index < 0 || index >= books.Count || _loadingBook) return;
        await SwitchWindowAsync(books, index, ChapterRequest.First, null);
    }

    /// <param name="count">How many chapters, from the first one of the window on, over the border of books (see <see cref="CheckWindow"/>).</param>
    /// <param name="keepReading">True when the window is only made larger or smaller around the reader: the text stays where it is on the screen.</param>
    /// <param name="shift">The window starts this many chapters after the wanted one (before it when negative).</param>
    private async Task SwitchWindowAsync(IReadOnlyList<BookText.Book> books, int bookIndex, ChapterRequest request, double? anchor,
        int count = 1, bool keepReading = false, int shift = 0)
    {
        _loadingBook = true;
        UpdateBusy();
        try
        {
            // The books' Runs are read and parsed lazily, so ask for them off the UI thread. The text of the chapters is made from the runs on the worker too
            // (see BuildWindow): nothing but the chapter numerals is made on the UI thread.
            var cx = MakeFlatContext();
            var (firstBook, first, lastBook, last, firstNumber, lastNumber, slots, content) = await Task.Run(() =>
            {
                var start = (Book: bookIndex, Chapter: ResolveChapter(ChaptersOf(books[bookIndex]), request)); // reading and parsing the markdown is the slow part (once per book)
                if (shift != 0) start = StepSlots(books, start, shift);
                var end = StepSlots(books, start, Math.Max(1, count) - 1);
                var window = BuildWindow(cx, books, start.Book, start.Chapter, end.Book, end.Chapter);
                int firstNumber = ChaptersOf(books[start.Book]) is { Count: > 0 } a ? a[start.Chapter].Chapter : 0;
                int lastNumber = ChaptersOf(books[end.Book]) is { Count: > 0 } z ? z[end.Chapter].Chapter : 0;
                return (start.Book, start.Chapter, end.Book, end.Chapter, firstNumber, lastNumber, SlotsOf(books, start.Book, start.Chapter, end.Book, end.Chapter).Count(), window);
            });
            PrunePieceCache(books, firstBook, first, lastBook, last);
            BookIndex = firstBook;
            ChapterIndex = first;
            _lastBookIndex = lastBook;
            _lastChapterIndex = last;
            _windowSlots = slots;
            _shownChapterNumber = firstNumber;
            _shownLastChapterNumber = lastNumber;
            _pendingAnchor = anchor;
            // Taken now, from the text that is shown (not when the work began: the reader may have scrolled on since).
            _readingAnchor = keepReading ? CaptureReading() : null;
            if (!keepReading) _columnAnchor = null;
            SetContent(content);
            BookChanged?.Invoke(this, EventArgs.Empty);
            PrefetchNeighbours(books);
        }
        finally { _loadingBook = false; UpdateBusy(); }
    }

    /// <summary>
    /// The reader pushed against the end (<paramref name="delta"/> 1) or the start (-1) of the loaded text, faster than the check of the window
    /// (see <see cref="CheckWindow"/>) made it grow: it grows by a chapter now, also into the next book or the one before. The text stays where it is
    /// on the screen, and goes on as soon as the new chapter is ready.
    /// </summary>
    private void MoveChapter(int delta)
    {
        if (_loadingBook || Books is not { } books) return;
        bool more = delta > 0
            ? _lastBookIndex + 1 < books.Count || _lastChapterIndex + 1 < SlotCountOf(books[_lastBookIndex])
            : BookIndex > 0 || ChapterIndex > 0;
        if (!more) return;
        _ = SwitchWindowAsync(books, BookIndex, ChapterRequest.ByIndex(ChapterIndex), null, _windowSlots + 1, keepReading: true, shift: delta > 0 ? 0 : -1);
    }

    // ---- The window of chapters ----------------------------------------------------------------------
    // At least TotalScreens screens of text are always loaded: the screen that is shown, with SideScreens before it and after it. The window
    // of chapters grows on the side where the reader comes near its edge, and shrinks on the side that is far behind, so scrolling never waits
    // for the next chapter and the text stays small. It is counted in screens, not in chapters: a short psalm brings the next ones with it, a
    // long chapter is enough alone. Where the book ends on one side, the other side makes up for it.

    private const double SideScreens = 2, TotalScreens = 5;

    /// <summary>
    /// A chapter goes only when that leaves this many screens on its side, more than the SideScreens that make the window grow on that side again:
    /// with the same number every small shift of the position (the columns are cut again when the window changes, which moves the first column by one) made
    /// the window grow and shrink at every step.
    /// </summary>
    private const double TrimScreens = 3;

    /// <summary>Asks for a check of the window, when the text was laid out and when it was scrolled (a check at a time, once the UI is free).</summary>
    private void RequestWindowCheck()
    {
        if (_checkQueued || Books is null) return;
        _checkQueued = true;
        Dispatcher.UIThread.Post(CheckWindow, DispatcherPriority.Background);
    }

    /// <summary>How many chapters before the end (or after the start) of a book the book next to it is parsed, so that it is ready long before the reader gets there.</summary>
    private const int PrefetchChapters = 4;

    private BookText.Book? _prefetchedNext, _prefetchedPrevious;

    /// <summary>
    /// Near the end or the start of the book the book that follows or comes before may be next: it is parsed on a worker now (the parsing takes
    /// about a second on a phone), so that moving on to it only has to lay out.
    /// </summary>
    private void PrefetchNeighbours(IReadOnlyList<BookText.Book> books)
    {
        if (_lastBookIndex + 1 < books.Count && _lastChapterIndex >= SlotCountOf(books[_lastBookIndex]) - 1 - PrefetchChapters && !ReferenceEquals(_prefetchedNext, books[_lastBookIndex + 1]))
        {
            var next = _prefetchedNext = books[_lastBookIndex + 1];
            _ = Task.Run(() => next.Runs);
        }
        if (BookIndex > 0 && ChapterIndex <= PrefetchChapters && !ReferenceEquals(_prefetchedPrevious, books[BookIndex - 1]))
        {
            var previous = _prefetchedPrevious = books[BookIndex - 1];
            _ = Task.Run(() => previous.Runs);
        }
    }

    private void CheckWindow()
    {
        _checkQueued = false;
        if (_loadingBook || _dirty || !_hasLayout || _flowRunning is not null || _lines.Count == 0 || _screenHeight <= 0
            || Books is not { } books || BookIndex >= books.Count || _lastBookIndex >= books.Count) return;
        PrefetchNeighbours(books);
        bool vertical = Orientation == Orientation.Vertical;
        // The sizes are in the unit of the scrolling: pixels, or columns when the text is in columns.
        double unit = vertical ? _screenHeight : Math.Max(1, _visibleColumns);
        double pos = vertical ? _offsetY : _firstColumn;
        double total = vertical ? _contentHeight : _totalColumns;
        double ahead = Math.Max(0, total - (pos + unit)), behind = pos;
        // The text goes on over the border of books: only the first chapter of the first book and the last of the last book end it.
        bool canAhead = _lastBookIndex + 1 < books.Count || _lastChapterIndex + 1 < SlotCountOf(books[_lastBookIndex]);
        bool canBehind = BookIndex > 0 || ChapterIndex > 0;
        int shown = _windowSlots;
        double average = Math.Max(1, total / shown); // how large a chapter is, as far as the shown ones tell

        bool wantAhead = canAhead && ahead < SideScreens * unit;
        bool wantBehind = canBehind && behind < SideScreens * unit;
        // The five screens in all, however near the reader is to an edge of the text: the other side makes up for it.
        double missing = TotalScreens * unit - total;
        if (missing > 0 && !wantAhead && !wantBehind)
        {
            if (canAhead) wantAhead = true;
            else if (canBehind) wantBehind = true;
        }

        // How the window changes, in chapters: where its start moves to (before it when negative) and its end (before it when negative).
        int startShift = 0, endShift = 0;
        if (wantAhead)
            endShift = Math.Clamp((int)Math.Ceiling(Math.Max(SideScreens * unit - ahead, missing) / average), 1, 6);
        if (wantBehind)
            startShift = -Math.Clamp((int)Math.Ceiling(Math.Max(SideScreens * unit - behind, missing) / average), 1, 6);

        // Chapters that are far enough away go, as long as that leaves the two screens on that side (so the window does not grow again at once).
        if (!wantBehind || !wantAhead)
        {
            var starts = ChapterStarts(vertical);
            if (starts.Count == shown) // one mark for each chapter of the window (else it is not known which chapter is where)
            {
                // Never trim below the TotalScreens in all: at the very start or end of the Bible the other side cannot make up for it, and the window
                // would grow and shrink again and again (the scroll bar flickered).
                double keep = TotalScreens * unit;
                if (!wantBehind)
                    for (int k = 0; k + 1 < starts.Count && starts[k + 1] <= pos - TrimScreens * unit && total - starts[k + 1] >= keep; k++)
                        startShift = k + 1;
                if (!wantAhead)
                    for (int k = starts.Count - 1; k >= 1 && starts[k] >= pos + unit + TrimScreens * unit && starts[k] >= keep; k--)
                        endShift = -(shown - k);
            }
        }

        if (startShift == 0 && endShift == 0) return;
        _ = SwitchWindowAsync(books, BookIndex, ChapterRequest.ByIndex(ChapterIndex), null, shown - startShift + endShift, keepReading: true, shift: startShift);
    }

    /// <summary>Where each chapter of the window starts (in pixels, or in columns), in the order of the text.</summary>
    private List<double> ChapterStarts(bool vertical)
    {
        var starts = new List<double>();
        foreach (var (index, pos) in _markers)
        {
            if (pos.Verse != 0 || pos.Chapter < 1) continue; // the chapter marks are verse 0 of the chapter
            if (LineAt(index) is not { } line) continue;
            starts.Add(vertical ? line.Origin.Y : LogicalColumn(line));
        }
        return starts;
    }

    /// <summary>
    /// Where the reader is, so a new window can be laid out with the text in the same place: the last verse mark at or before the first line on
    /// the screen (as <see cref="UpdatePosition"/> finds it), how far into its line the screen starts, and the scroll position now.
    /// </summary>
    private (BookText.VersePosition Pos, double Delta, double Offset, int CharDelta)? CaptureReading()
    {
        if (_markers.Count == 0 || _lines.Count == 0) return null;
        bool vertical = Orientation == Orientation.Vertical;
        PlacedLine? top = null;
        foreach (var pl in _lines)
        {
            if (vertical ? pl.Origin.Y + pl.Line.Height > _offsetY + 0.5 : LogicalColumn(pl) >= (_scrolling ? _firstColumn : 0)) { top = pl; break; }
        }
        if (top is not { } first) return null;
        int at = first.Start;
        var best = _markers[0];
        foreach (var marker in _markers)
        {
            if (marker.Index > at) break;
            best = marker;
        }
        if (LineAt(best.Index) is not { } markLine) return null;
        return (best.Pos, vertical ? _offsetY - markLine.Origin.Y : 0, vertical ? _offsetY : _firstColumn, first.Start - best.Index);
    }

    private void RaiseBoundary(bool atEnd)
    {
        // A flick or a wheel burst raises this many times: act once, then ignore the rest for a moment.
        if (_boundaryClock.Elapsed - _lastBoundary < TimeSpan.FromMilliseconds(700)) return;
        _lastBoundary = _boundaryClock.Elapsed;
        BoundaryReached?.Invoke(this, new BoundaryEventArgs(atEnd));
        MoveChapter(atEnd ? 1 : -1); // pushing against the end of the chapter: the next one comes, against the start: the previous one
    }

    // ---- Debounced re-layout ------------------------------------------------------------------------
    // Flowing a long text is slow, so while the available size keeps changing (window being dragged) the last
    // layout stays on screen and the real re-flow happens once the size has been stable for a moment.

    /// <summary>How long the size must stay unchanged before the text is flowed again.</summary>
    public static readonly TimeSpan ResizeDelay = TimeSpan.FromMilliseconds(150);

    private DispatcherTimer? _resizeTimer;
    private Size _lastAvailable, _lastTotal;
    private bool _hasLayout, _dirty = true, _commitResize;

    /// <summary>Something other than the size changed (text, font, floats…): the next measure must re-flow.</summary>
    private void MarkDirty() { _dirty = true; InvalidateMeasure(); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty
            || change.Property == ForegroundProperty || change.Property == TextAlignmentProperty
            || change.Property == FloatSpacingProperty || change.Property == ColumnCountProperty
            || change.Property == ColumnWidthProperty || change.Property == ColumnSpacingProperty
            || change.Property == OrientationProperty)
            _dirty = true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Only a pure size change is deferred; with an unchanged size the measure always runs, because a float
        // child may have invalidated it.
        if (_hasLayout && !_dirty && !_commitResize && availableSize != _lastAvailable)
        {
            // Vertical mode: a height-only change just changes the viewport, no re-flow is needed.
            if (Orientation == Orientation.Vertical && availableSize.Width == _lastAvailable.Width)
            {
                _lastAvailable = availableSize;
                _lastTotal = UpdateVertical(availableSize, _lastTotal.Width);
                _layer.Measure(_lastTotal);
                _layer.InvalidateVisual();
                return _lastTotal;
            }
            // Resizing: keep showing the old layout and flow again when the size settles.
            _resizeTimer ??= new DispatcherTimer(ResizeDelay, DispatcherPriority.Background, (_, _) =>
            {
                _resizeTimer!.Stop();
                _commitResize = true;
                InvalidateMeasure();
            });
            _resizeTimer.Stop();
            _resizeTimer.Start();
            return _lastTotal;
        }
        _resizeTimer?.Stop();
        _commitResize = false;
        bool wasDirty = _dirty;
        _dirty = false;
        _lastAvailable = availableSize;
        var result = MeasureCore(availableSize, out bool awaiting);
        if (awaiting)
        {
            // The lines are being flowed on a worker thread: keep what is shown (nothing was changed) and measure
            // again when they arrive.
            _dirty = wasDirty;
            return result;
        }
        _lastTotal = result;
        _hasLayout = true;
        return result;
    }

    private Size MeasureCore(Size availableSize, out bool awaiting)
    {
        awaiting = false;
        bool widthFinite = !double.IsInfinity(availableSize.Width);
        bool heightFinite = !double.IsInfinity(availableSize.Height);
        double totalWidth = widthFinite ? availableSize.Width : 400;
        double spacing = ColumnSpacing;
        bool vertical = Orientation == Orientation.Vertical;
        bool fixedWidth = !vertical && !double.IsNaN(ColumnWidth) && ColumnWidth > 0;
        bool unbounded = false; // true: add as many columns as the text needs
        int columns;
        double width;
        if (fixedWidth)
        {
            width = widthFinite ? Math.Min(ColumnWidth, Math.Max(1, availableSize.Width)) : ColumnWidth;
            if (heightFinite) { unbounded = true; columns = int.MaxValue; }
            else if (ColumnCount > 0) columns = ColumnCount;
            else columns = widthFinite ? Math.Max(1, (int)((availableSize.Width + spacing) / (width + spacing))) : 1;
        }
        else
        {
            columns = vertical ? 1 : Math.Max(1, ColumnCount);
            width = Math.Max(1, (totalWidth - (columns - 1) * spacing) / columns);
        }
        // The text is first flowed as ONE tall column of this width; it is cut into columns afterwards.
        bool cutting = columns > 1 || unbounded; // false: one column, a page break is just a gap
        double gap = FloatSpacing;

        // 1. Measure floats. Each one is anchored at the text index where it was declared and gets placed
        //    while flowing, on the line that contains that index (like CSS float).
        var (text, segments, anchors, notes, breaks, markers, strongs) = FlattenCached();
        var pending = new List<(Control Child, Size Size, FloatSide Side, int Anchor)>();
        foreach (var child in Children.OfType<Control>().Where(c => c != _layer && c != _popup && c != _scrollBar && c != _vBar && c != _tail
                && (_currentFloats.Contains(c) || !_ownedFloats.Contains(c)))) // not the floats of replaced content
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            // A float may take less room than it is tall (see LayoutHeightProperty).
            if (GetLayoutHeight(child) is var layoutHeight && !double.IsNaN(layoutHeight)) size = new Size(size.Width, Math.Min(size.Height, layoutHeight));
            pending.Add((child, size, GetSide(child), anchors.TryGetValue(child, out var a) ? a : 0));
        }
        pending = pending.OrderBy(p => p.Anchor).ToList(); // stable: keeps declaration order for equal anchors

        // 2. Flow the text line by line, narrowing each line by the floats it overlaps (RunFlow). A long text is
        //    flowed on a worker thread: the first screens are shown as soon as they are ready, the rest follows.
        bool justify = TextAlignment == Avalonia.Media.TextAlignment.Justify;
        var style = BaseStyle();
        bool rtl = IsRightToLeft(text); // a text of Hebrew or Arabic: the lines run from right to left, the columns too
        var input = new FlowInput(text, segments, pending, breaks, width, gap, cutting, FontSize, style, justify, rtl);
        var segmentHash = new HashCode();
        foreach (var s in segments) { segmentHash.Add(s.Start); segmentHash.Add(s.End); segmentHash.Add(s.Style); }
        var floatHash = new HashCode();
        foreach (var p in pending) { floatHash.Add(p.Child); floatHash.Add(p.Size); floatHash.Add(p.Side); floatHash.Add(p.Anchor); }
        var key = new FlowKey(width, cutting, FontSize, FontFamily, style.Foreground, justify, gap,
            text.GetHashCode(), text.Length, segmentHash.ToHashCode(), floatHash.ToHashCode(), rtl);

        // The same text flowed again in another width or orientation (window resized, device rotated): the reader
        // stays at the line at the top of the view, not at the same column or offset of a different layout.
        int? reflowKeep = null;
        if (_hasLayout && _pendingAnchor is null && _pendingPosition is null && _lines.Count > 0
            && (_appliedVertical != vertical || Math.Abs(_appliedWidth - width) > 0.5) && string.Equals(_text, text))
            reflowKeep = VisibleStartIndex();

        int partialLines = 0, partialFrom = 0;
        if (_pendingPosition is not null || reflowKeep is not null || (_lines.Count == 0 && _pendingAnchor is null))
        {
            if (reflowKeep is { } from) partialFrom = from;
            // Nothing is shown yet, or a verse is wanted: flow only down to it and a few screens beyond, and show that
            // right away; the rest follows.
            if (_pendingPosition is { } wantedAt && ResolveMarker(wantedAt, markers) is { } wantedMark)
                partialFrom = wantedMark.Index;
            int screen = heightFinite ? (int)(availableSize.Height / Math.Max(1, FontSize * 1.25)) + 1 : 60;
            int visibleColumns = vertical || !widthFinite ? 1
                : fixedWidth ? Math.Max(1, (int)((availableSize.Width + spacing) / (width + spacing))) : columns;
            partialLines = Math.Clamp(screen * visibleColumns * 3, 120, 3000);
        }
        // A window of several chapters is flowed by chapter, so the lines of the chapters that were there before are kept (see GetChunkedFlow).
        var chunkStarts = breaks.Count == 0 ? ChunkStarts(text, markers) : null;
        var flow = chunkStarts is { Count: > 1 }
            ? GetChunkedFlow(key, chunkStarts, text, segments, pending, width, gap, cutting, justify, style, rtl)
            : GetFlow(key, input, partialLines, partialFrom);
        if (flow is null)
        {
            awaiting = true;
            return _hasLayout ? _lastTotal : new Size(totalWidth, heightFinite ? availableSize.Height : 0);
        }

        // 3. The lines are ready: only now is the control's state replaced, all at once, with what belongs together.
        bool partial = !flow.Complete;

        int? keepIndex = reflowKeep;
        _appliedVertical = vertical;
        _appliedWidth = width;
        _breakStarts = breaks;
        _markers = markers;
        _notes = notes;
        _strongs = strongs;
        _text = text;
        _anchor = Math.Min(_anchor, text.Length);
        _caret = Math.Min(_caret, text.Length);
        _floatRects.Clear();
        _floatRects.AddRange(flow.FloatRects);
        _lines.Clear();
        _lines.AddRange(flow.Lines);
        var placed = flow.Placed.ToList();
        double textBottom = flow.TextBottom;
        if (partial)
        {
            // Only the first lines are flowed so far: the height of the rest is estimated from them, and the floats
            // of the text that is not flowed yet wait outside the view.
            textBottom = flow.EndIndex > 0 ? flow.TextBottom * text.Length / flow.EndIndex : flow.TextBottom;
            var done = placed.Select(p => p.Child).ToHashSet();
            foreach (var f in pending.Where(u => !done.Contains(u.Child)))
                placed.Add((f.Child, new Rect(-100000 - f.Size.Width, 0, f.Size.Width, f.Size.Height)));
        }
        _arranged = placed;
        RemoveStaleFloats(); // the layout of the old content is replaced: its floats can go now

        double floatsBottom = _floatRects.Count == 0 ? 0 : _floatRects.Max(r => r.Bottom - gap);
        double flowHeight = Math.Max(textBottom, floatsBottom);

        // While the columns follow the finger, carry on after it lifted, or ease to a column, the offset is in pixels of the layout that is replaced here:
        // where it was, as a column of the old layout, so it can be put at the same place of the text in the new one.
        int firstColumnBefore = _firstColumn;
        double? movingAt = !vertical && _scrolling && _pitch > 0 && (_panning || _anim is { IsEnabled: true }) ? ColumnAtOffset(_offsetX) : null;

        // 3. Cut the tall column into `columns` columns.
        double pitch = width + spacing;
        // With a fixed column width and a bounded size only the columns that fit are shown; the rest are
        // reached with the scroll bar, one whole column at a time.
        bool paged = fixedWidth && widthFinite && heightFinite;
        int visible = paged ? Math.Max(1, (int)((availableSize.Width + spacing) / pitch)) : int.MaxValue;

        var flowLines = _lines.ToList();
        var flowArranged = _arranged.ToList();

        (int Used, double Height) Cut(double columnHeight)
        {
            _lines.Clear();
            _lines.AddRange(flowLines);
            _arranged = flowArranged.ToList();
            if (!((columns > 1 || unbounded) && _lines.Count > 0)) return (1, flowHeight);

            double step = _lines.Min(l => l.Line.Height);
            // Finite height: newspaper-style, fill column 1 first. Infinite height: balance the columns.
            double h = Math.Max(columnHeight, step);
            List<double>? starts;
            while ((starts = SplitColumns(h, columns)) is null) h += step;
            int used = starts.Count(x => x < double.MaxValue);

            double Top(int col) => starts[col];
            int ColumnAt(double y)
            {
                int c = 0;
                for (int i = 1; i < starts.Count; i++) if (starts[i] <= y) c = i;
                return c;
            }

            double height = 0;
            var columned = new List<PlacedLine>(_lines.Count);
            foreach (var pl in _lines)
            {
                int c = ColumnAt(pl.Origin.Y);
                var origin = new Point(c * pitch + pl.Origin.X, pl.Origin.Y - Top(c));
                columned.Add(pl with { Origin = origin });
                height = Math.Max(height, origin.Y + pl.Line.Height);
            }
            _lines.Clear();
            _lines.AddRange(columned);

            for (int i = 0; i < _arranged.Count; i++)
            {
                var (child, slot) = _arranged[i];
                int c = ColumnAt(slot.Y);
                slot = new Rect(c * pitch + slot.X, slot.Y - Top(c), slot.Width, slot.Height);
                _arranged[i] = (child, slot);
                height = Math.Max(height, slot.Bottom);
            }
            return (used, height);
        }

        _forcedColumnStart = -1;
        if (paged && !vertical && (_readingAnchor is not null || _columnAnchor is not null))
        {
            var (anchorPos, charDelta) = _readingAnchor is { } r ? (r.Pos, r.CharDelta) : _columnAnchor!.Value;
            if (WindowHolds(anchorPos) && ResolveMarker(anchorPos, markers) is { } at && at.Pos.Chapter == anchorPos.Chapter && at.Pos.Verse == anchorPos.Verse)
                _forcedColumnStart = at.Index + charDelta;
        }
        var (usedColumns, contentHeight) = Cut(heightFinite ? availableSize.Height : flowHeight / columns);
        double barHeight = 0;
        if (paged && usedColumns > visible)
        {
            // The scroll bar takes room from the bottom, so the columns are cut again at a lower height.
            _scrollBar.IsVisible = true; // a hidden control measures to zero
            _scrollBar.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            barHeight = Math.Max(_scrollBar.DesiredSize.Height, 12);
            (usedColumns, contentHeight) = Cut(Math.Max(1, availableSize.Height - barHeight));
        }

        _scrolling = paged && usedColumns > visible;
        _pitch = pitch;
        _barHeight = barHeight;
        // While only the first lines are flowed, a spinner at the end of them shows that the rest is coming.
        _tailRect = null;
        if (partial && _lines.Count > 0)
        {
            var last = _lines[^1];
            double columnLeft = Math.Floor((last.Origin.X + 1) / pitch) * pitch;
            _tailRect = new Rect(columnLeft + (width - TailSize) / 2, last.Origin.Y + last.Line.Height + TailSize / 2, TailSize, TailSize);
        }
        if (_tail.IsVisible != (_tailRect is not null)) _tail.IsVisible = _tailRect is not null; // before measuring: hidden = zero size
        if (_tailRect is not null) _tail.Measure(new Size(TailSize, TailSize));
        _totalColumns = usedColumns;
        if (_pendingPosition is { } wanted && _markers.Count > 0)
        {
            // Content without the wanted chapter (a layout of the chapter that was shown before, or before the books arrived) must not use up
            // the request: it would find the start of the book and scroll to that.
            if (WindowHolds(wanted) && ResolveMarker(wanted, _markers) is { } found && LineAt(found.Index) is { } target)
            {
                _pendingPosition = null;
                _pendingAnchor = vertical ? target.Origin.Y : Math.Floor((target.Origin.X + 1) / pitch);
            }
            else if (!partial && !_loadingBook) _pendingPosition = null; // a partial layout may simply not reach it yet
        }
        // The window of chapters was made larger or smaller around the reader (see CheckWindow): the verse that was at the top of the screen goes to
        // the same place again, however far the reader scrolled meanwhile. A position that was wanted comes first.
        if (_readingAnchor is { } reading && _markers.Count > 0)
        {
            if (WindowHolds(reading.Pos) && ResolveMarker(reading.Pos, _markers) is { } same
                && same.Pos.Chapter == reading.Pos.Chapter && same.Pos.Verse == reading.Pos.Verse && LineAt(same.Index) is { } sameLine)
            {
                _readingAnchor = null;
                if (!vertical) _columnAnchor = (reading.Pos, reading.CharDelta);
                var startLine = !vertical && LineAt(same.Index + reading.CharDelta) is { } exact ? exact : sameLine; // the line that was at the top of the first column
                _pendingAnchor ??= vertical ? sameLine.Origin.Y + reading.Delta + (_offsetY - reading.Offset) : Math.Floor((startLine.Origin.X + 1) / pitch) + (_firstColumn - (int)Math.Round(reading.Offset)); // and the columns the reader turned meanwhile
            }
            else if (!partial && !_loadingBook) _readingAnchor = null;
        }
        if (!_loadingBook) _switchingBible = false; // the text of the new Bible is laid out (not while it is still being built)
        UpdateBusy();
        if (keepIndex is { } keep && _pendingAnchor is null && LineAt(keep) is { } keepLine)
            _pendingAnchor = vertical ? keepLine.Origin.Y : Math.Floor((keepLine.Origin.X + 1) / pitch);
        if (_pendingAnchor is { } anchor && !vertical)
        {
            _firstColumn = (int)Math.Round(Math.Min(anchor, int.MaxValue / 2.0));
            _pendingAnchor = null;
        }
        _firstColumn = _scrolling ? Math.Clamp(_firstColumn, 0, usedColumns - visible) : 0;
        _visibleColumns = _scrolling ? visible : usedColumns;
        // Right to left: the first column is at the right and the text of a line is flush right; everything above (columns, anchors)
        // was worked out in the order of the text, and is turned around here, once.
        _rtl = rtl;
        if (rtl) MirrorLayout(usedColumns, width, pitch);
        _scrollBar.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        // An animation in progress keeps its current offset (it heads for the new target by itself).
        if (!_scrolling) StopAnimation();
        if (_anim is not { IsEnabled: true } && !_panning) _offsetX = OffsetOf(_firstColumn);
        else if (movingAt is { } moving && _scrolling)
        {
            // The window changed: the first column may be another one of the text now (it changed at its start), and in a text from right to left the
            // offset of every column changes when columns are added at its end (the columns are counted from the right, the offset from the left).
            // The motion goes on from the same place of the text.
            double shifted = OffsetOfColumn(moving + (_firstColumn - firstColumnBefore)) - _offsetX;
            _offsetX += shifted;
            _animFrom += shifted;
        }

        double contentWidth = fixedWidth ? _visibleColumns * width + (_visibleColumns - 1) * spacing : totalWidth;
        _viewWidth = contentWidth;
        if (_scrollBar.IsVisible != _scrolling) _scrollBar.IsVisible = _scrolling;
        if (_scrolling)
        {
            _updatingBar = true;
            _scrollBar.Minimum = 0;
            _scrollBar.Maximum = usedColumns - visible;
            _scrollBar.ViewportSize = visible;
            _scrollBar.LargeChange = visible;
            _scrollBar.SmallChange = 1;
            _scrollBar.Value = _firstColumn;
            _updatingBar = false;
            _scrollBar.Measure(new Size(contentWidth, barHeight));
        }

        _contentHeight = contentHeight;
        Size total;
        if (vertical)
        {
            if (_scrollBar.IsVisible) _scrollBar.IsVisible = false;
            total = UpdateVertical(availableSize, contentWidth);
        }
        else
        {
            _vScrolling = false;
            _offsetY = 0;
            if (_vBar.IsVisible) _vBar.IsVisible = false;
            // Scrolling fills the given height exactly, with the scroll bar along the bottom edge.
            total = new Size(contentWidth, _scrolling ? availableSize.Height : contentHeight);
        }
        _layer.Measure(total);
        _layer.InvalidateVisual();
        RequestPositionUpdate();
        // Is the window of chapters large enough around the reader, now that the text is laid out (a partial layout only estimates its height)?
        _screenHeight = heightFinite ? availableSize.Height : 0;
        if (!partial) RequestWindowCheck();
        return total;
    }

    // ---- Flowing the lines (on a worker thread for a long text) ---------------------------------------
    // Flowing two books takes seconds on a phone, and the UI thread must not be blocked that long (the system
    // reports "not responding"). RunFlow only works on the values in a FlowInput: Avalonia objects may only be
    // used on the UI thread, so everything it needs is read before it starts.

    private sealed record FlowInput(string Text, List<Segment> Segments,
        List<(Control Child, Size Size, FloatSide Side, int Anchor)> Pending, HashSet<int> Breaks,
        double Width, double Gap, bool Cutting, double FontSize, RunStyle Base, bool Justify, bool Rtl);

    /// <param name="EndIndex">How much of the text is flowed; the text's length when <paramref name="Complete"/>.</param>
    private sealed record FlowResult(List<PlacedLine> Lines, List<(Control Child, Rect Slot)> Placed,
        List<Rect> FloatRects, double TextBottom, int EndIndex, bool Complete);

    /// <summary>Everything that decides how the lines come out; equal keys give the same lines.</summary>
    private readonly record struct FlowKey(double Width, bool Cutting, double FontSize, FontFamily Family, IBrush? Foreground,
        bool Justify, double Gap, int TextHash, int TextLength, int SegmentHash, int FloatHash, bool Rtl);

    /// <summary>
    /// Texts shorter than this are flowed right away, on the UI thread. It was 40,000 when a window was two books; a window of chapters is a
    /// few thousand characters, and flowing those took 260-300 ms on a phone, which made every move of the window a stall of the whole app.
    /// </summary>
    private const int AsyncFlowChars = 500;

    private (FlowKey Key, FlowResult Result)? _flowCache;
    private FlowKey? _flowRunning; // set while a worker has not delivered the complete result yet
    private CancellationTokenSource? _flowCts;
    private bool _flowFailed;      // flowing on a worker did not work on this platform: flow on the UI thread

    /// <summary>
    /// The lines for <paramref name="key"/>, or null while a worker is still busy with them (measure is repeated
    /// when it delivers). <paramref name="partialLines"/> &gt; 0 also delivers the lines early: down to the text index
    /// <paramref name="partialFrom"/> and that many lines beyond it.
    /// </summary>
    private FlowResult? GetFlow(FlowKey key, FlowInput input, int partialLines, int partialFrom)
    {
        if (_flowCache is { } cached && cached.Key.Equals(key)) return cached.Result;
        if (_flowRunning is { } running && running.Equals(key)) return null;

        _flowCts?.Cancel(); // the input changed: whatever is still being flowed is out of date
        _flowCache = null;
        _flowRunning = null;
        if (input.Text.Length < AsyncFlowChars || _flowFailed)
            return RunFlow(input, int.MaxValue, CancellationToken.None);

        var cts = _flowCts = new CancellationTokenSource();
        var token = cts.Token;
        _flowRunning = key;
        _ = Task.Run(() =>
        {
            try
            {
                if (partialLines > 0)
                {
                    var first = RunFlow(input, partialLines, token, partialFrom);
                    Dispatcher.UIThread.Post(() => FlowDelivered(key, first, token));
                    if (first.Complete) return;
                }
                var all = RunFlow(input, int.MaxValue, token);
                Dispatcher.UIThread.Post(() => FlowDelivered(key, all, token));
            }
            catch (OperationCanceledException) { }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _flowFailed = true; // e.g. a platform whose text stack is not usable from other threads
                    _flowRunning = null;
                    InvalidateMeasure();
                });
            }
        });
        return null;
    }

    private void FlowDelivered(FlowKey key, FlowResult result, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        _flowCache = (key, result);
        if (result.Complete) _flowRunning = null;
        InvalidateMeasure();
    }

    // ---- Flowing by chapter ---------------------------------------------------------------------------
    // The window of chapters is made again every time the reader nears an edge of it, and most of its chapters were there before. Flowing the
    // whole text again made 9-19 MB of garbage each time (the lines of the chapters that were already flowed too), and the collector then stopped
    // the app for 40-50 ms. So the text is cut into chunks, one per chapter, and each chunk is flowed once: its lines are kept, and put one below
    // the other, each moved down by the height of the chunks above it, when the window is laid out. A chunk begins at the start of a line and
    // ends with a line break (the chapter before ends the line that the breaks of a title begin), so how it flows does not depend on its
    // neighbours. Its lines count the text from the start of the chunk; PlacedLine.Base is where that is in the text of the window.

    /// <summary>What decides how a chunk flows: equal keys give the same lines, whatever else is in the window.</summary>
    private readonly record struct ChunkKey(int TextHash, int TextLength, int SegmentHash, int FloatHash, double Width, double FontSize,
        FontFamily Family, IBrush? Foreground, bool Justify, double Gap, bool Rtl);

    /// <summary>The lines of a chunk (counting from its start, from the top of it), where its floats go, and how tall it is.</summary>
    private sealed record ChunkFlow(List<PlacedLine> Lines, List<Rect> FloatSlots, List<Rect> FloatRects, double Height);

    /// <summary>A chunk of the text of the window.</summary>
    private sealed record Chunk(int Start, int End, ChunkKey Key, List<(Control Child, Size Size, FloatSide Side, int Anchor)> Floats);

    private readonly Dictionary<ChunkKey, ChunkFlow> _chunkCache = new();
    private HashSet<ChunkKey> _chunkKeysBefore = new(); // the chunks of the window before this one: kept, the window often goes back to them

    /// <summary>
    /// Where the chunks of a text begin: at a chapter (verse 0 of a chapter), after the line break that precedes it. A title has its breaks after
    /// the mark of the chapter: the first of them then goes to the chunk before, so that one ends with a line break.
    /// </summary>
    private static List<int> ChunkStarts(string text, List<(int Index, BookText.VersePosition Pos)> markers)
    {
        var starts = new List<int> { 0 };
        foreach (var (index, pos) in markers)
        {
            if (pos.Verse != 0 || pos.Chapter < 0) continue; // the start of a book (chapter 0, its title) and of a chapter
            int start = index;
            if (start > 0 && text[start - 1] != '\n')
            {
                if (start < text.Length && text[start] == '\n') start++;
                else continue; // not at the start of a line: no chunk here
            }
            if (start > starts[^1] && start < text.Length) starts.Add(start);
        }
        return starts;
    }

    private FlowResult? GetChunkedFlow(FlowKey key, List<int> starts, string text, List<Segment> segments,
        List<(Control Child, Size Size, FloatSide Side, int Anchor)> pending, double width, double gap, bool cutting, bool justify, RunStyle style, bool rtl)
    {
        if (_flowCache is { } cached && cached.Key.Equals(key)) return cached.Result;
        if (_flowRunning is { } running && running.Equals(key)) return null;
        _flowCts?.Cancel();
        _flowCache = null;
        _flowRunning = null;

        // Describe the chunks, and find the ones that were not flowed yet.
        var chunks = new List<Chunk>();
        var missing = new List<(ChunkKey Key, FlowInput Input)>();
        for (int c = 0; c < starts.Count; c++)
        {
            int start = starts[c], end = c + 1 < starts.Count ? starts[c + 1] : text.Length;
            var floats = new List<(Control Child, Size Size, FloatSide Side, int Anchor)>();
            foreach (var f in pending)
                if (f.Anchor >= start && (f.Anchor < end || (c == starts.Count - 1 && f.Anchor <= end))) floats.Add(f);
            var local = new List<Segment>();
            int first = FindSegment(segments, start);
            for (int s = Math.Max(0, first); s < segments.Count && segments[s].Start < end; s++)
            {
                var seg = segments[s];
                if (seg.End <= start) continue;
                local.Add(new Segment(Math.Max(seg.Start, start) - start, Math.Min(seg.End, end) - start, seg.Style));
            }
            var segmentHash = new HashCode();
            foreach (var s in local) { segmentHash.Add(s.Start); segmentHash.Add(s.End); segmentHash.Add(s.Style); }
            var floatHash = new HashCode(); // not the controls themselves (they are made again with each window), only what decides the flow
            foreach (var f in floats) { floatHash.Add(f.Size); floatHash.Add(f.Side); floatHash.Add(f.Anchor - start); }
            var chunkKey = new ChunkKey(string.GetHashCode(text.AsSpan(start, end - start)), end - start, segmentHash.ToHashCode(), floatHash.ToHashCode(),
                width, FontSize, FontFamily, style.Foreground, justify, gap, rtl);
            var chunk = new Chunk(start, end, chunkKey, floats);
            chunks.Add(chunk);
            if (_chunkCache.ContainsKey(chunkKey) || missing.Any(m => m.Key.Equals(chunkKey))) continue;
            var localFloats = floats.Select(f => (f.Child, f.Size, f.Side, f.Anchor - start)).ToList();
            missing.Add((chunkKey, new FlowInput(text.Substring(start, end - start), local, localFloats, new HashSet<int>(), width, gap, cutting, FontSize, style, justify, rtl)));
        }

        if (missing.Count == 0) return ComposeChunks(key, text, chunks, gap);

        int missingChars = missing.Sum(m => m.Input.Text.Length);
        if (missingChars < AsyncFlowChars || _flowFailed)
        {
            foreach (var (chunkKey, input) in missing) _chunkCache[chunkKey] = FlowChunk(input);
            return ComposeChunks(key, text, chunks, gap);
        }

        var cts = _flowCts = new CancellationTokenSource();
        var token = cts.Token;
        _flowRunning = key;
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var (chunkKey, input) in missing)
                {
                    token.ThrowIfCancellationRequested();
                    var flowed = FlowChunk(input, token);
                    Dispatcher.UIThread.Post(() => _chunkCache[chunkKey] = flowed); // kept even when the window has changed meanwhile: the next one may need it
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _flowRunning = null;
                    InvalidateMeasure();
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;
                    _flowFailed = true;
                    _flowRunning = null;
                    InvalidateMeasure();
                });
            }
        });
        return null;
    }

    /// <summary>Flows one chunk (from the top of it, as the whole text of its own) and notes how tall it is.</summary>
    private static ChunkFlow FlowChunk(FlowInput input, CancellationToken cancel = default)
    {
        var flow = RunFlow(input, int.MaxValue, cancel);
        double floatsBottom = flow.FloatRects.Count == 0 ? 0 : flow.FloatRects.Max(r => r.Bottom - input.Gap);
        return new ChunkFlow(flow.Lines, flow.Placed.Select(p => p.Slot).ToList(), flow.FloatRects, Math.Max(flow.TextBottom, floatsBottom));
    }

    /// <summary>Puts the chunks one below the other: the kept lines, moved down, with the controls of the floats that the window has now.</summary>
    private FlowResult ComposeChunks(FlowKey key, string text, List<Chunk> chunks, double gap)
    {
        var lines = new List<PlacedLine>();
        var placed = new List<(Control Child, Rect Slot)>();
        var floatRects = new List<Rect>();
        double top = 0;
        var used = new HashSet<ChunkKey>();
        foreach (var chunk in chunks)
        {
            var flowed = _chunkCache[chunk.Key];
            used.Add(chunk.Key);
            foreach (var pl in flowed.Lines)
                lines.Add(new PlacedLine(pl.Line, new Point(pl.Origin.X, pl.Origin.Y + top), pl.PageStart, chunk.Start));
            // The k-th float of the chunk is the k-th one that was flowed; the controls are the ones of the window that is laid out now.
            for (int k = 0; k < chunk.Floats.Count && k < flowed.FloatSlots.Count; k++)
                placed.Add((chunk.Floats[k].Child, flowed.FloatSlots[k].Translate(new Vector(0, top))));
            foreach (var r in flowed.FloatRects) floatRects.Add(r.Translate(new Vector(0, top)));
            top += flowed.Height;
        }
        // The chunks of the window before stay for a while (the window goes back to them), the older ones go.
        foreach (var stale in _chunkCache.Keys.Where(k => !used.Contains(k) && !_chunkKeysBefore.Contains(k)).ToList()) _chunkCache.Remove(stale);
        _chunkKeysBefore = used;
        var result = new FlowResult(lines, placed, floatRects, top, text.Length, true);
        _flowCache = (key, result);
        return result;
    }

    /// <summary>
    /// Flows the text into one tall column, line by line, narrowing each line by the floats it overlaps. Stops after
    /// <paramref name="maxLines"/> lines beyond the text index <paramref name="minIndex"/> (an incomplete result).
    /// Touches no Avalonia object, so it can run anywhere.
    /// </summary>
    private static FlowResult RunFlow(FlowInput input, int maxLines, CancellationToken cancel, int minIndex = 0)
    {
        string text = input.Text;
        double width = input.Width, gap = input.Gap;
        var pending = input.Pending.ToList(); // floats not placed yet, in declaration order
        var lines = new List<PlacedLine>();
        var floatRects = new List<Rect>();
        var placed = new List<(Control Child, Rect Slot)>();
        double leftBottom = 0, rightBottom = 0; // floats on one side stack; the gap is included
        double textBottom = 0, top = 0;
        int index = 0;
        bool complete = true;

        void PlaceFloat((Control Child, Size Size, FloatSide Side, int Anchor) f, double lineTop)
        {
            bool isLeft = f.Side == FloatSide.Left;
            double y = Math.Max(lineTop, isLeft ? leftBottom : rightBottom);
            double x = isLeft ? 0 : width - f.Size.Width;
            placed.Add((f.Child, new Rect(x, y, f.Size.Width, f.Size.Height)));
            // Rect used for text avoidance: grown by the gap on the text-facing edges.
            floatRects.Add(isLeft
                ? new Rect(x, y, f.Size.Width + gap, f.Size.Height + gap)
                : new Rect(x - gap, y, f.Size.Width + gap, f.Size.Height + gap));
            if (isLeft) leftBottom = y + f.Size.Height + gap; else rightBottom = y + f.Size.Height + gap;
        }

        // Space taken from the left and right of a line occupying [lineTop, lineTop + height).
        (double Left, double Right) Insets(double lineTop, double height)
        {
            double left = 0, right = 0;
            foreach (var r in floatRects)
            {
                if (r.Bottom <= lineTop || r.Top >= lineTop + height) continue;
                if (r.X <= 0.5) left = Math.Max(left, r.Right);
                else right = Math.Max(right, width - r.X);
            }
            return (left, right);
        }

        if (text.Length > 0)
        {
            var baseProps = ToRunProperties(input.Base);
            // The paragraphs of a right-to-left text run from right to left: the line has its trailing space on the left. The lines are placed
            // at the left of their band (alignment Left); MirrorLayout moves them to where they belong.
            var paraProps = new GenericTextParagraphProperties(input.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Avalonia.Media.TextAlignment.Left,
                true, false, baseProps, TextWrapping.Wrap, 0, 0, 0);
            var source = new RichTextSource(text, input.Segments);
            var formatter = TextFormatter.Current;

            // Formats the line starting at `from` from a window of text just past where it can end (see
            // RichTextSource.Limit). A line that ends close to the window's edge is formatted again with a
            // bigger window, so the break is always decided with enough text after it.
            TextLine? Format(int from, double lineWidth)
            {
                // About the characters that fit in the line (an average character is 0.4 of the font size wide, a little less than usual so a
                // line of narrow letters fits too), and room behind it for the break to be decided: a word or two. It was twice what fits in
                // the line, with a least of 200 and a margin of 80: every line shaped and kept 3-4 times the text it shows, 50 KB a line.
                int window = Math.Max(120, (int)(lineWidth / Math.Max(1, input.FontSize * 0.35)) + 50);
                while (true)
                {
                    source.Limit = Math.Min(text.Length, from + window);
                    var l = formatter.FormatLine(source, from, lineWidth, paraProps);
                    if (l is null || source.Limit >= text.Length || from + l.Length <= source.Limit - 40)
                    {
                        source.Limit = int.MaxValue;
                        return l;
                    }
                    window *= 2;
                }
            }

            // A line that was broken at a soft hyphen shows a hyphen in its place: the line is formatted again with a
            // hyphen there, which may no longer fit; then it ends earlier (the loop looks at where it ended then).
            TextLine WithHyphen(TextLine line, int from, double lineWidth)
            {
                while (line.NewLineLength == 0 && line.Length > 0 && text[from + line.Length - 1] == Hyphenation.SoftHyphen)
                {
                    var hyphenated = formatter.FormatLine(new HyphenSource(text, input.Segments, from + line.Length), from, lineWidth, paraProps);
                    if (hyphenated is null || hyphenated.Length == 0) return line;
                    if (hyphenated.Length == line.Length) return hyphenated;
                    line = hyphenated;
                }
                return line;
            }

            int gapIndex = -1, stopAfter = int.MaxValue;
            while (index < text.Length)
            {
                cancel.ThrowIfCancellationRequested();
                // Stops maxLines lines after the text index minIndex has been reached (at the start: after maxLines lines).
                if (stopAfter == int.MaxValue && maxLines != int.MaxValue && index >= minIndex) stopAfter = lines.Count + maxLines;
                if (lines.Count >= stopAfter) { complete = false; break; }

                // Probe with full width to learn the line height, then recompute with the real band.
                var probe = Format(index, width);
                if (probe is null) break;
                if (!input.Cutting && top > 0 && gapIndex != index && input.Breaks.Contains(index))
                {
                    top += probe.Height * 2; // no next column to break to: leave a visible gap instead
                    gapIndex = index;
                }

                var (left, right) = Insets(top, probe.Height);
                double lineWidth = width - left - right;

                // Float fills the row: drop below the next float edge instead of squeezing text.
                // Only skip when a float is actually responsible; otherwise (narrow window) the text must
                // be squeezed into whatever width exists, or the loop would never consume any text.
                if (lineWidth < input.FontSize * 3 && (left > 0 || right > 0))
                {
                    var blocking = floatRects.Where(r => r.Bottom > top && r.Top < top + probe.Height).Select(r => r.Bottom).ToList();
                    if (blocking.Count > 0)
                    {
                        top = Math.Max(blocking.Min(), top + 1);
                        continue;
                    }
                }
                lineWidth = Math.Max(lineWidth, 1);

                // No float beside this line: the probe is already the line.
                var line = lineWidth == width ? probe : Format(index, lineWidth);
                if (line is null || line.Length == 0) break;
                line = WithHyphen(line, index, lineWidth);

                // Floats declared inside this line start here: place them at this line's top, then lay the
                // line out again, now narrowed by them. (Floats are removed from `pending`, so this happens once.)
                int lineEnd = index + line.Length;
                bool lastLine = lineEnd >= text.Length;
                if (pending.Count > 0 && (pending[0].Anchor < lineEnd || lastLine))
                {
                    while (pending.Count > 0 && (pending[0].Anchor < lineEnd || lastLine))
                    {
                        PlaceFloat(pending[0], top);
                        pending.RemoveAt(0);
                    }
                    continue;
                }

                if (input.Justify && !lastLine && line.NewLineLength == 0)
                    line = JustifyLine(line, index, text, input.Segments, lineWidth, paraProps) ?? line;

                lines.Add(new PlacedLine(line, new Point(left, top), input.Breaks.Contains(index)));
                top += line.Height;
                index = lineEnd;
                textBottom = top;
            }
        }

        // Floats with no text to attach to (empty text) go at the top.
        if (complete) foreach (var f in pending) PlaceFloat(f, top);
        return new FlowResult(lines, placed, floatRects, textBottom, index, complete);
    }

    // ---- Vertical scrolling -------------------------------------------------------------------------

    private readonly ScrollBar _vBar;
    private bool _vScrolling;
    private double _offsetY, _viewHeight, _contentHeight, _barWidth;

    /// <summary>Sizes the viewport of the single column and configures the vertical scroll bar.</summary>
    private Size UpdateVertical(Size available, double width)
    {
        bool heightFinite = !double.IsInfinity(available.Height);
        _vScrolling = heightFinite && _contentHeight > available.Height + 0.5;
        _viewHeight = _vScrolling ? available.Height : _contentHeight;
        if (_pendingAnchor is { } anchor)
        {
            _offsetY = anchor;
            _pendingAnchor = null;
        }
        _offsetY = _vScrolling ? Math.Clamp(_offsetY, 0, _contentHeight - _viewHeight) : 0;
        if (_vBar.IsVisible != _vScrolling) _vBar.IsVisible = _vScrolling; // visible before measuring: hidden = zero size
        if (_vScrolling)
        {
            _updatingBar = true;
            _vBar.Minimum = 0;
            _vBar.Maximum = _contentHeight - _viewHeight;
            _vBar.ViewportSize = _viewHeight;
            _vBar.LargeChange = _viewHeight * 0.9;
            _vBar.SmallChange = FontSize * 2.5;
            _vBar.Value = _offsetY;
            _updatingBar = false;
            _vBar.Measure(new Size(double.PositiveInfinity, _viewHeight));
            _barWidth = Math.Max(_vBar.DesiredSize.Width, 8);
        }
        return new Size(width, _viewHeight);
    }

    private void SetOffsetY(double y)
    {
        y = Math.Clamp(y, 0, Math.Max(0, _contentHeight - _viewHeight));
        if (Math.Abs(y - _offsetY) < 0.01) return;
        if (!_programmatic) _keepPosition = false;
        _offsetY = y;
        if (Math.Abs(_vBar.Value - y) > 0.01)
        {
            _updatingBar = true;
            _vBar.Value = y;
            _updatingBar = false;
        }
        _popup.IsOpen = false;
        InvalidateArrange(); // floats follow the offset
        _layer.InvalidateVisual();
        RequestPositionUpdate();
        RequestWindowCheck();
    }

    // Touch panning: the scroll gesture recognizer turns a finger drag (and its inertia afterwards) into deltas.
    /// <summary>
    /// Whether an event comes from the content of the popup. The events of a popup bubble up to this control (the popup is its child), and a swipe, a
    /// wheel turn or a tap in the popup must not move the text or put the popup away: the card of the popup stops them (see ShowCard), and the handlers
    /// that move the text look here too, whatever the platform does with the events of a popup.
    /// </summary>
    private bool FromPopup(Avalonia.Interactivity.RoutedEventArgs e) =>
        _popup is { IsOpen: true, Child: Visual child } && e.Source is Visual source && (ReferenceEquals(source, child) || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(source).Contains(child));

    /// <summary>
    /// True while a finger that began in the popup is down (and until its inertia is over). The scroll recognizer of this control gets the touches
    /// of the popup too (it is below this control in the tree) and raises its gestures with this control as their source, so the source does not say
    /// that they come from the popup: the press that began them does.
    /// </summary>
    private bool _gestureFromPopup;

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (_gestureFromPopup || FromPopup(e)) { e.Handled = true; return; } // a swipe in the popup scrolls the popup, never the text (and the popup closed at its bottom)
        if (_touchSelecting) { e.Handled = true; return; } // the finger extends the selection, it does not scroll
        if (_vScrolling)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double target = _offsetY + e.Delta.Y;
            bool beyond = (target > max + 0.5 && _offsetY >= max - 0.5) || (target < -0.5 && _offsetY <= 0.5);
            if (beyond)
            {
                _overscroll += e.Delta.Y; // keep pushing against the edge to move on to the next/previous book
                if (Math.Abs(_overscroll) > 100)
                {
                    RaiseBoundary(atEnd: _overscroll > 0);
                    _overscroll = 0;
                }
            }
            else _overscroll = 0;
            SetOffsetY(target);
            e.Handled = true;
        }
        else if (!_scrolling)
        {
            // The whole chapter fits on the screen (a short psalm): there is nothing to scroll, so a drag is a push against the edge, as the
            // wheel and the keys are: forwards moves on to the next chapter, backwards to the previous one. It takes a push of some length.
            double drag = Orientation == Orientation.Vertical ? e.Delta.Y : (_rtl ? -e.Delta.X : e.Delta.X);
            if (_overscroll != 0 && Math.Sign(drag) != Math.Sign(_overscroll)) _overscroll = 0;
            _overscroll += drag;
            if (Math.Abs(_overscroll) > 100)
            {
                RaiseBoundary(atEnd: _overscroll > 0);
                _overscroll = 0;
            }
            e.Handled = true;
        }
        else if (_scrolling && (_touchDown || _inertia))
        {
            // Horizontal mode: the columns follow the finger, and carry on after it lifted (the inertia of a ScrollViewer: the
            // same gesture events, with the speed dying down); they settle on a whole column when the finger lifts slowly,
            // or when the inertia has become slow.
            StopAnimation();
            _panning = true;
            double max = Math.Max(0, _scrollBar.Maximum * _pitch);
            double min = -0.4 * _pitch, upper = max + 0.4 * _pitch;
            // A little rubber band past both ends: letting go there moves on to the neighbouring book.
            _offsetX = Math.Clamp(_offsetX + e.Delta.X, min, upper);
            _popup.IsOpen = false;
            InvalidateArrange();
            _layer.InvalidateVisual();
            e.Handled = true;

            if (_inertia)
            {
                // The speed of the columns between this event and the one before, smoothed a little.
                double seconds = _inertiaClock.Elapsed.TotalSeconds;
                _inertiaClock.Restart();
                if (seconds > 0) _inertiaSpeed = _inertiaSpeed <= 0 ? Math.Abs(e.Delta.X) / seconds : 0.5 * _inertiaSpeed + 0.5 * Math.Abs(e.Delta.X) / seconds;
                bool atEnd = _offsetX <= min + 0.5 || _offsetX >= upper - 0.5; // ran into the rubber band
                if (_inertiaSpeed < SettleSpeed || atEnd) SettleAfterInertia();
            }
        }
    }

    private bool _touchDown, _panning, _inertia;
    private double _overscroll, _inertiaSpeed;
    private readonly System.Diagnostics.Stopwatch _inertiaClock = new();

    /// <summary>Below this speed (pixels per second) the columns settle on a whole column: a slow lift, or an inertia that has died down.</summary>
    private const double SettleSpeed = 250;

    /// <summary>Finger lifted while panning: choose the column to settle on (a short flick is enough).</summary>
    private void OnScrollInertiaStarting(object? sender, ScrollGestureInertiaStartingEventArgs e)
    {
        if (_gestureFromPopup || FromPopup(e)) return; // the popup's own swipe goes on with its inertia
        if (_touchSelecting) { e.Handled = true; return; } // no inertia after selecting
        // Vertical mode: Avalonia's own inertia runs on (the same as in a ScrollViewer): the gesture events it
        // raises carry on arriving in OnScrollGesture, so a fast swipe travels further than a slow one.
        if (!_panning) return;
        if (Math.Abs(e.Inertia.X) < SettleSpeed)
        {
            // A slow lift: settle at once (a small push is enough to turn to the next column).
            SettlePan(e.Inertia.X);
            e.Handled = true;
            return;
        }
        // A fast one: not handled, so the recognizer goes on with its inertia, the events of which move the columns on
        // (see OnScrollGesture) until they have become slow.
        _inertia = true;
        _inertiaSpeed = Math.Abs(e.Inertia.X);
        _inertiaClock.Restart();
    }

    private void OnScrollGestureEnded(object? sender, ScrollGestureEndedEventArgs e)
    {
        if (_gestureFromPopup || FromPopup(e)) return;
        if (_inertia) SettleAfterInertia();
        else if (_panning) SettlePan(0);
    }

    /// <summary>The inertia is over (or too slow to matter): show the whole column that is nearest. Past an end that is the neighbouring book.</summary>
    private void SettleAfterInertia()
    {
        _inertia = false;
        _panning = false;
        _inertiaSpeed = 0;
        double max = Math.Max(0, _scrollBar.Maximum);
        double position = ColumnAtOffset(_offsetX);
        int target = position > max + 0.15 ? (int)max + 1 : position < -0.15 ? -1 : (int)Math.Round(position);
        ApplyColumn(target, force: true);
    }

    private void SettlePan(double velocityX)
    {
        _panning = false;
        _inertia = false;
        double moved = ColumnAtOffset(_offsetX) - _firstColumn; // columns dragged, signed (positive = towards later columns)
        int sign = Math.Sign(moved);
        // The offset grows towards later columns in a text from left to right, and towards earlier ones in a text from right to left.
        bool flick = sign != 0 && Math.Sign(velocityX) == (_rtl ? -sign : sign);
        double threshold = flick ? 0.1 : 0.35;
        int target = sign == 0 ? _firstColumn : _firstColumn + sign * (int)Math.Round(Math.Abs(moved) + 0.5 - threshold);
        ApplyColumn(target, force: true);
    }

    // A tap (touch) opens a footnote; for touch the press can't, because it may be the start of a pan.
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (FromPopup(e)) return; // a tap in the popup is not a tap on the text under it
        if (e.Pointer?.Type != PointerType.Touch) return;
        // Lifting the finger after a long press raises Tapped as well: that tap must not put the new selection away.
        if (_touchSelecting || Environment.TickCount64 - _touchSelectionEnded < 600) { e.Handled = true; return; }
        if (HasSelection)
        {
            // A tap on the selected text offers Copy and Select all; a tap elsewhere puts the selection away.
            if (IsOnSelection(ToContent(e.GetPosition(this))))
            {
                if (ContextMenu is { IsOpen: false } menu) menu.Open(this);
                e.Handled = true;
                return;
            }
            SetSelection(0, 0);
        }
        var position = e.GetPosition(this);
        if (OverScrollBar(position)) return;
        if (HitFootnoteNear(ToContent(position), out var bounds) is { } note)
        {
            bounds = bounds.Translate(new Vector(-_offsetX, -_offsetY));
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowFootnote(note, bounds), DispatcherPriority.Background);
        }
        else if (TryOpenNumbers(ToContent(position), touch: true))
        {
            e.Handled = true;
        }
        else if (HitStrongs(ToContent(position), out var wordBounds) is { } strong)
        {
            wordBounds = wordBounds.Translate(new Vector(-_offsetX, -_offsetY));
            e.Handled = true;
            _ = ShowStrongsAsync(strong, wordBounds);
        }
    }

    // ---- Column scrolling ---------------------------------------------------------------------------

    private readonly ScrollBar _scrollBar;
    private bool _scrolling, _updatingBar;
    private int _firstColumn, _visibleColumns;
    private double _pitch, _offsetX, _viewWidth, _barHeight;
    private bool _rtl; // the text is written from right to left (see MirrorLayout)

    /// <summary>
    /// Whether most of the letters of the text are of a script that is written from right to left: Hebrew, Arabic, Syriac, Thaana and
    /// the like. The text is looked at as far as 4000 letters (a Bible with Strong's numbers has a Latin H or G in front of each number).
    /// </summary>
    private static bool IsRightToLeft(string text)
    {
        int letters = 0, rtl = 0;
        for (int i = 0; i < text.Length && letters < 4000; i++)
        {
            char c = text[i];
            if (!char.IsLetter(c)) continue;
            letters++;
            if (IsRightToLeftLetter(c)) rtl++;
        }
        return letters > 0 && rtl * 2 > letters;
    }

    private static bool IsRightToLeftLetter(char c) =>
        c is >= (char)0x0590 and <= (char)0x08FF or >= (char)0xFB1D and <= (char)0xFDFF or >= (char)0xFE70 and <= (char)0xFEFF;

    /// <summary>
    /// Whether a short text, such as a popup, starts with a letter of a script that is written from right to left (the rule of the
    /// browsers for dir="auto"): the first letter decides, numbers and punctuation do not. The Latin G or H of a Strong's number
    /// ("H7225") is not a letter of its own, so it does not count. A short text can have more Latin letters than Arabic or Hebrew ones
    /// (a book name, a reference, a number) and still be written from right to left.
    /// </summary>
    internal static bool StartsRightToLeft(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsLetter(c)) continue;
            if (c is 'G' or 'H' && i + 1 < text.Length && char.IsDigit(text[i + 1])) continue; // a Strong's number
            return IsRightToLeftLetter(c);
        }
        return false;
    }

    /// <summary>
    /// Turns the layout of a right-to-left text around: the columns are cut and numbered in the order of the text (the first at the left), and
    /// this puts the first at the right. A line (which is a box as wide as its text, formatted right to left) goes to the mirrored place of
    /// the right edge of its band, a float to the mirrored place of its slot, the spinner at the end to the mirrored place too.
    /// </summary>
    private void MirrorLayout(int columns, double width, double pitch)
    {
        int ColumnOf(double x) => Math.Clamp((int)Math.Floor((x + 1) / pitch), 0, Math.Max(0, columns - 1));
        double Mirrored(double x, double w, int column) => (columns - 1 - column) * pitch + (width - (x - column * pitch)) - w;

        for (int i = 0; i < _lines.Count; i++)
        {
            var pl = _lines[i];
            int c = ColumnOf(pl.Origin.X);
            _lines[i] = pl with { Origin = new Point(Mirrored(pl.Origin.X, pl.Line.WidthIncludingTrailingWhitespace, c), pl.Origin.Y) };
        }
        for (int i = 0; i < _arranged.Count; i++)
        {
            var (child, slot) = _arranged[i];
            if (slot.X < -50000) continue; // waiting outside the view
            _arranged[i] = (child, new Rect(Mirrored(slot.X, slot.Width, ColumnOf(slot.X)), slot.Y, slot.Width, slot.Height));
        }
        if (_tailRect is { } tail) _tailRect = new Rect(Mirrored(tail.X, tail.Width, ColumnOf(tail.X)), tail.Y, tail.Width, tail.Height);
    }

    // The scroll offset is where the view starts in the (mirrored) strip of the columns: the offset of a column and the column at an offset
    // are the same in a text that runs from left to right, and turned around in one that does not.
    private double OffsetOfColumn(double column) => _rtl ? Math.Max(0, _totalColumns - column - _visibleColumns) * _pitch : column * _pitch;

    private double OffsetOf(int column) => _rtl ? Math.Max(0, _totalColumns - column - _visibleColumns) * _pitch : column * _pitch;

    private double ColumnAtOffset(double offset) => _rtl ? (_totalColumns - _visibleColumns) - offset / _pitch : offset / _pitch;

    /// <summary>The column (in the order of the text) of a line or float whose left edge is at <paramref name="x"/> in the layout now applied.</summary>
    private int LogicalColumn(double x)
    {
        int c = (int)Math.Floor((x + 1) / _pitch);
        return _rtl ? Math.Max(0, _totalColumns - 1 - c) : c;
    }

    /// <summary>
    /// The column (in the order of the text) of a line: the one its middle is in. Not its left edge: a line of a text from right to left is placed
    /// from its right edge, and one that is wider than the column (it counts its trailing space) starts a little left of the column, where the left
    /// edge belongs to the next column. The first line of a column then counted as a line of the next one, and a window that was made around the reader
    /// started the first column a column too early: a key press or a swipe was lost.
    /// </summary>
    private int LogicalColumn(PlacedLine line) => LogicalColumn(line.Origin.X + line.Line.WidthIncludingTrailingWhitespace / 2 - 1);

    /// <summary>Shows column <paramref name="column"/> as the first visible one (snapped to a whole column).</summary>
    private void ApplyColumn(int column, bool force = false)
    {
        if (_scrolling)
        {
            int last = Math.Max(0, (int)_scrollBar.Maximum);
            if (column > last) RaiseBoundary(atEnd: true); // asked for a page after the last one
            else if (column < 0) RaiseBoundary(atEnd: false);
            column = Math.Clamp(column, 0, last);
        }
        if (Math.Abs(_scrollBar.Value - column) > 1e-6)
        {
            _updatingBar = true;
            _scrollBar.Value = column;
            _updatingBar = false;
        }
        if (column == _firstColumn && !force) return;
        if (!_programmatic) _keepPosition = false; // the user took over
        _firstColumn = column;
        RequestPositionUpdate();
        RequestWindowCheck();
        _popup.IsOpen = false;
        AnimateScroll();
    }

    // Soft scrolling: the offset eases toward the target column instead of jumping. Each new target (wheel
    // notches in quick succession, thumb dragging) restarts the ease from wherever the offset currently is.
    private static readonly TimeSpan ScrollDuration = TimeSpan.FromMilliseconds(280);
    private DispatcherTimer? _anim;
    private readonly System.Diagnostics.Stopwatch _animClock = new();
    private double _animFrom;

    private void AnimateScroll()
    {
        _animFrom = _offsetX;
        _animClock.Restart();
        _anim ??= new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => AnimationTick());
        _anim.Start();
    }

    private void AnimationTick()
    {
        double target = OffsetOf(_firstColumn);
        double t = Math.Min(1, _animClock.Elapsed / ScrollDuration);
        double eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        _offsetX = _animFrom + (target - _animFrom) * eased;
        if (t >= 1) { _offsetX = target; _anim?.Stop(); }
        InvalidateArrange(); // floats follow the offset
        _layer.InvalidateVisual();
    }

    private void StopAnimation()
    {
        _anim?.Stop();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (FromPopup(e)) { e.Handled = true; return; }
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (_vScrolling && e.Delta.Y != 0)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double target = _offsetY - e.Delta.Y * FontSize * 3;
            if (target > max + 0.5 && _offsetY >= max - 0.5) RaiseBoundary(atEnd: true);
            else if (target < -0.5 && _offsetY <= 0.5) RaiseBoundary(atEnd: false);
            SetOffsetY(target);
            e.Handled = true;
            return;
        }
        if (delta == 0) return;
        if (!_scrolling)
        {
            if (!_vScrolling) RaiseBoundary(atEnd: delta < 0); // everything fits: any wheel turn is "past the edge"
            return;
        }
        ApplyColumn(_firstColumn + (delta < 0 ? 1 : -1));
        e.Handled = true;
    }

    /// <summary>Control coordinates → coordinates of the laid-out columns (which are all positioned side by side).</summary>
    private Point ToContent(Point point) => new(point.X + _offsetX, point.Y + _offsetY);

    /// <summary>False for lines in columns scrolled out of view, so they are not drawn.</summary>
    private bool IsInView(PlacedLine pl) =>
        _vScrolling ? pl.Origin.Y + pl.Line.Height > _offsetY && pl.Origin.Y < _offsetY + _viewHeight
        : !_scrolling || (pl.Origin.X + pl.Line.WidthIncludingTrailingWhitespace > _offsetX && pl.Origin.X < _offsetX + _viewWidth);

    /// <summary>Arrow, page and Home/End keys. Going on past the first or last page continues in the next/previous book.</summary>
    private bool NavigateByKey(Key key)
    {
        int direction = key switch
        {
            Key.Down or Key.PageDown or Key.Space => 1,
            Key.Up or Key.PageUp => -1,
            Key.Right => _rtl ? -1 : 1, // in a text from right to left the way on is to the left
            Key.Left => _rtl ? 1 : -1,
            Key.Home => -2,
            Key.End => 2,
            _ => 0,
        };
        if (direction == 0) return false;
        bool page = key is Key.PageDown or Key.PageUp or Key.Space;

        if (_vScrolling)
        {
            double max = Math.Max(0, _contentHeight - _viewHeight);
            double step = page ? _viewHeight * 0.9 : FontSize * 3;
            double target = direction switch { -2 => 0, 2 => max, _ => _offsetY + direction * step };
            if (Math.Abs(direction) == 1)
            {
                if (target > max + 0.5 && _offsetY >= max - 0.5) RaiseBoundary(atEnd: true);
                else if (target < -0.5 && _offsetY <= 0.5) RaiseBoundary(atEnd: false);
            }
            SetOffsetY(target);
        }
        else if (_scrolling)
        {
            int last = Math.Max(0, (int)_scrollBar.Maximum);
            int step = page ? Math.Max(1, _visibleColumns) : 1;
            ApplyColumn(direction switch { -2 => 0, 2 => last, _ => _firstColumn + direction * step });
        }
        else if (Math.Abs(direction) == 1)
        {
            RaiseBoundary(atEnd: direction > 0); // everything fits on one page
        }
        return true;
    }

    private bool OverScrollBar(Point point) =>
        (_scrollBar.IsVisible && point.Y >= _scrollBar.Bounds.Top) || (_vBar.IsVisible && point.X >= _vBar.Bounds.Left);

    /// <summary>
    /// Greedily breaks the flowed lines into at most <paramref name="columns"/> columns of height
    /// <paramref name="height"/>. Returns each column's start offset in flow coordinates, or null if it doesn't fit.
    /// When the reader's first column must keep its first line (<see cref="_forcedColumnStart"/>), the columns from that line on are cut
    /// from it, and the columns before it are cut backwards from it, so each of them is full (only the first column of the window, at its
    /// very top, may be shorter). A short column just before it would show when the reader scrolls back.
    /// </summary>
    private List<double>? SplitColumns(double height, int columns)
    {
        var starts = new List<double> { 0 };
        int from = 0;
        if (_forcedColumnStart >= 0 && columns == int.MaxValue)
        {
            int forced = _lines.FindIndex(l => l.Start == _forcedColumnStart);
            if (forced > 0 && _lines[forced].Origin.Y > 0.01)
            {
                var before = new List<double>(); // the starts of the columns before it, last to first
                double bottom = _lines[forced - 1].Origin.Y + _lines[forced - 1].Line.Height;
                for (int j = forced - 1; j >= 0; j--)
                {
                    var line = _lines[j];
                    if (bottom - line.Origin.Y <= height + 0.01) continue;
                    before.Add(_lines[j + 1].Origin.Y); // line j does not fit with the lines below it: its column starts under it
                    bottom = line.Origin.Y + line.Line.Height;
                }
                before.Reverse();
                starts.AddRange(before);
                starts.Add(_lines[forced].Origin.Y);
                from = forced;
            }
        }
        for (int i = from; i < _lines.Count; i++)
        {
            var pl = _lines[i];
            if (pl.PageStart || pl.Start == _forcedColumnStart)
            {
                // A page break puts this line at the top of the next column (so does the line that the reader's first column starts with).
                if (pl.Origin.Y <= starts[^1] + 0.01) continue; // it already is the first line of its column
                // With a fixed number of columns a break that has no room is ignored (a taller column can't help).
                if (starts.Count < columns)
                {
                    starts.Add(pl.Origin.Y);
                    continue;
                }
            }
            if (pl.Origin.Y + pl.Line.Height - starts[^1] <= height + 0.01) continue;
            if (starts.Count == columns) return null;
            starts.Add(pl.Origin.Y);
        }
        if (columns < int.MaxValue)
            while (starts.Count < columns) starts.Add(double.MaxValue); // unused columns
        return starts;
    }

    /// <summary>
    /// Justifies by hand. Avalonia's own TextLine.Justify spreads the extra space over text-run boundaries
    /// instead of over the spaces (visible as a gap between a word and its footnote marker, or around any
    /// differently styled run). Instead the line is formatted again with every inner space replaced by an
    /// invisible <see cref="GapRun"/> that is wider by an equal share of the leftover width.
    /// Each gap is one character, so text indices (selection, hit testing, footnotes) stay valid.
    /// </summary>
    private static TextLine? JustifyLine(TextLine line, int start, string text, List<Segment> segments,
        double width, TextParagraphProperties paragraph)
    {
        int end = start + line.Length - line.NewLineLength;
        while (end > start && text[end - 1] == ' ') end--; // trailing spaces keep their natural width

        // Avalonia's bidi analysis does not handle a non-text run well (found in its source, TextFormatterImpl):
        //  - a run without Text counts as a strong Latin letter "a" for the bidi algorithm, so a gap in a right-to-left line would
        //    be taken as a left-to-right letter;
        //  - CoalesceLevels gives the first character after a non-text run the level of that run, not its own;
        //  - the line puts a non-text run at the level of the run before it.
        // So a gap keeps the bidi levels of the line only when the character before it, the gap and the character behind it all have
        // the same level. The levels of the line formatted without gaps are right, they are the reference: a space becomes a gap only
        // if it has the same level as both of its neighbours (not before a number: "16" would be shown as "61"; not behind one: the gap
        // would join the number and be shown on its outer side), and the gap gets the text of a strong character that resolves to
        // that level (RLM for an odd, LRM for an even level). A space that does not qualify stays a real space at its natural
        // width. If the levels of the justified line are not the same as the reference, the line is not justified.
        var levels = BidiLevels(line, start, text);
        var spaces = new Dictionary<int, double>();
        var gapChars = new Dictionary<int, char>();
        for (int i = start; i < end; i++)
        {
            if (text[i] != ' ') continue;
            if (i == start || i + 1 >= start + levels.Length) continue;
            sbyte level = levels[i - start], before = levels[i - 1 - start], next = levels[i + 1 - start];
            if (level < 0 || level != before || level != next) continue;
            var bounds = line.GetTextBounds(i, 1);
            if (bounds.Count == 0) return null;
            spaces[i] = bounds[0].Rectangle.Width;
            gapChars[i] = (level & 1) != 0 ? '‏' : '‎';
        }
        if (spaces.Count == 0) return null;

        double extra = (width - line.Width) / spaces.Count;
        if (extra < 0.01) return null;

        bool hyphen = line.NewLineLength == 0 && text[start + line.Length - 1] == Hyphenation.SoftHyphen;
        var source = new JustifiedSource(text, segments, start + line.Length, spaces, gapChars, extra, line.Height, line.Baseline, hyphen);
        var justified = TextFormatter.Current.FormatLine(source, start, 100_000, paragraph);
        if (justified is null || justified.Length != line.Length) return null;

        var justifiedLevels = BidiLevels(justified, start, text);
        for (int i = 0; i < levels.Length && i < justifiedLevels.Length; i++)
            if (levels[i] >= 0 && justifiedLevels[i] >= 0 && levels[i] != justifiedLevels[i]) return null;
        return justified;
    }

    /// <summary>
    /// The bidi level of each character of a line (-1 where the line has none: a run that is not text). Each glyph says in its cluster
    /// which character it starts; the characters that a glyph covers in addition (marks) have the level of the one before.
    /// </summary>
    private static sbyte[] BidiLevels(TextLine line, int start, string text)
    {
        var levels = new sbyte[line.Length];
        Array.Fill(levels, (sbyte)-1);
        foreach (var run in line.TextRuns)
        {
            // The text of a shaped run is a part of the text of the block (the sources slice it), so where it starts in it says
            // which characters the run is. (The clusters of its glyphs only count from the start of the run, and the runs of
            // a line are in visual order.) A run of other text, such as the hyphen at the end of a line, has no level here.
            if (run is not ShapedTextRun shaped ||
                !MemoryMarshal.TryGetString(shaped.Text, out var source, out int from, out int length) || !ReferenceEquals(source, text)) continue;
            for (int i = Math.Max(from, start); i < from + length && i < start + levels.Length; i++)
                levels[i - start] = (sbyte)shaped.BidiLevel;
        }
        return levels;
    }

    /// <summary>An invisible, fixed-width stand-in for one space character.</summary>
    private sealed class GapRun(double width, double height, double baseline, TextRunProperties properties, char bidiChar) : DrawableTextRun
    {
        public override TextRunProperties? Properties => properties; // required: Avalonia reads BaselineAlignment from it
        // Without a text Avalonia takes the run as the letter "a" in the bidi analysis. The text is never drawn.
        public override ReadOnlyMemory<char> Text => bidiChar.ToString().AsMemory();
        public override int Length => 1;
        public override Size Size => new(width, height);
        public override double Baseline => baseline;
        public override void Draw(DrawingContext drawingContext, Point origin) { }
    }

    /// <summary>Like <see cref="RichTextSource"/> for one line, with its inner spaces widened.</summary>
    private sealed class JustifiedSource(string text, List<Segment> segments, int lineEnd,
        Dictionary<int, double> spaces, Dictionary<int, char> gapChars, double extra, double height, double baseline, bool hyphen) : ITextSource
    {
        public TextRun? GetTextRun(int index)
        {
            if (index >= lineEnd) return null;
            if (hyphen && index == lineEnd - 1) // the line ends at a soft hyphen: a hyphen is shown
                return new TextCharacters("-".AsMemory(), ToRunProperties(segments[FindSegment(segments, index)].Style));
            if (spaces.TryGetValue(index, out double natural))
            {
                var style = segments[FindSegment(segments, index)].Style with { Baseline = BaselineAlignment.Baseline };
                return new GapRun(natural + extra, height, baseline, ToRunProperties(style), gapChars[index]);
            }

            int found = FindSegment(segments, index);
            if (found < 0) return null;
            var seg = segments[found];
            int stop = Math.Min(seg.End, hyphen ? lineEnd - 1 : lineEnd);
            for (int i = index; i < stop; i++)
                if (spaces.ContainsKey(i)) { stop = i; break; }
            return new TextCharacters(text.AsMemory(index, stop - index), ToRunProperties(seg.Style));
        }
    }

    private List<(Control Child, Rect Slot)> _arranged = new();

    protected override Size ArrangeOverride(Size finalSize)
    {
        _layer.Arrange(new Rect(finalSize));
        foreach (var (child, reserved) in _arranged)
        {
            // The room reserved in the layout may be less than the control's height: it is arranged at its full size.
            var slot = GetLayoutHeight(child) is var layoutHeight && !double.IsNaN(layoutHeight) && child.DesiredSize.Height > reserved.Height
                ? new Rect(reserved.X, reserved.Y, reserved.Width, child.DesiredSize.Height) : reserved;
            if (!_scrolling && !_vScrolling) { child.Arrange(slot); continue; }
            bool hidden = _scrolling
                ? slot.Right <= _offsetX || slot.X >= _offsetX + _viewWidth
                : slot.Bottom <= _offsetY || slot.Y >= _offsetY + _viewHeight;
            if (hidden)
                child.Arrange(new Rect(-100000 - slot.Width, slot.Y, slot.Width, slot.Height)); // outside the viewport
            else
                child.Arrange(slot.Translate(new Vector(-_offsetX, -_offsetY)));
        }
        if (_tailRect is { } tail)
        {
            bool scrolling = _scrolling || _vScrolling;
            bool hidden = _scrolling ? tail.Right <= _offsetX || tail.X >= _offsetX + _viewWidth
                : _vScrolling && (tail.Bottom <= _offsetY || tail.Y >= _offsetY + _viewHeight);
            _tail.Arrange(hidden ? new Rect(-100000, 0, tail.Width, tail.Height)
                : scrolling ? tail.Translate(new Vector(-_offsetX, -_offsetY)) : tail);
        }
        if (_scrollBar.IsVisible)
            _scrollBar.Arrange(new Rect(0, Math.Max(0, finalSize.Height - _barHeight), _viewWidth, _barHeight));
        if (_vBar.IsVisible)
            _vBar.Arrange(new Rect(Math.Max(0, finalSize.Width - _barWidth), 0, _barWidth, finalSize.Height));
        return finalSize;
    }

    /// <summary>Draws the already-laid-out lines. Panel.Render is sealed, so a child control does the painting.</summary>
    private sealed class TextLayer(BibleTextBlock owner) : Control
    {
        public override void Render(DrawingContext context)
        {
            // Only whole columns are visible: clip to them and slide the row of columns by the scroll offset.
            double width = owner._scrolling ? owner._viewWidth : Bounds.Width;
            using var clip = context.PushClip(new Rect(0, 0, width, Bounds.Height));
            using var shift = context.PushTransform(Matrix.CreateTranslation(-owner._offsetX, -owner._offsetY));
            owner.DrawSelection(context);
            foreach (var placed in owner._lines)
                if (owner.IsInView(placed)) placed.Line.Draw(context, placed.Origin);
            owner.DrawStrongsUnderlines(context);
        }
    }

    /// <param name="icon">The file of an icon in the Assets (for example "copy.svg"), shown before the text; none if null.</param>
    private static MenuItem CreateMenuItem(string header, Action action, string? icon = null)
    {
        var item = new MenuItem { Header = header, FontSize = UiScale.Size(18), MinHeight = UiScale.Size(52), Padding = new Thickness(12, 8) };
        // Not MenuItem.Icon: the theme's template squeezes that into a 16x16 box, whatever the size of the icon.
        if (icon is not null)
        {
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
            panel.Children.Add(new ThemedSvg { Path = "/Assets/" + icon, Size = UiScale.Size(24) });
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            item.Header = panel;
        }
        item.Click += (_, _) => action();
        return item;
    }

    private readonly TextLayer _layer;
    private readonly Popup _popup;

    private const double TailSize = 32;
    private readonly BusySpinner _tail; // after the lines flowed so far, while the rest is still being flowed
    private Rect? _tailRect;            // where it sits, in the coordinates of the text
    private List<NoteRange> _notes = new();
    private List<StrongsRange> _strongs = new(); // in the order of the text, so they can be searched by position

    /// <summary>Finds the word with Strong's numbers under <paramref name="point"/>, and the bounds of the word.</summary>
    private string? HitStrongs(Point point, out Rect bounds)
    {
        bounds = default;
        if (_strongs.Count == 0 || !StrongsDictionaries.Any) return null; // without a dictionary there is nothing to show
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y || point.Y >= pl.Origin.Y + line.Height) continue;
            double x = point.X - pl.Origin.X;
            if (x < 0 || x > line.WidthIncludingTrailingWhitespace) continue; // another column's line may match

            int index = HitFromDistance(pl, x).FirstCharacterIndex;
            int lo = 0, hi = _strongs.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                var word = _strongs[mid];
                if (index < word.Start) hi = mid - 1;
                else if (index >= word.End) lo = mid + 1;
                else
                {
                    // the part of the word on this line (a phrase may go on in the next one)
                    int start = Math.Max(word.Start, pl.Start);
                    int end = Math.Min(word.End, pl.End);
                    var rects = end > start ? pl.Bounds(start, end - start) : [];
                    bounds = rects.Count > 0
                        ? rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y))
                        : new Rect(point, new Size(1, line.Height));
                    return word.Strong;
                }
            }
            return null;
        }
        return null;
    }

    /// <summary>Finds the footnote marker under <paramref name="point"/>, and the marker's bounds.</summary>
    private Footnote? HitFootnote(Point point, out Rect bounds)
    {
        bounds = default;
        if (_notes.Count == 0) return null;
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y || point.Y >= pl.Origin.Y + line.Height) continue;
            double x = point.X - pl.Origin.X;
            if (x < 0 || x > line.WidthIncludingTrailingWhitespace) continue; // another column's line may match

            // Character indices from TextLine are absolute indices into the flattened text.
            int index = HitFromDistance(pl, x).FirstCharacterIndex;
            foreach (var n in _notes)
            {
                if (index < n.Start || index >= n.End) continue;
                var rects = pl.Bounds(n.Start, n.End - n.Start);
                bounds = rects.Count > 0
                    ? rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y))
                    : new Rect(point, new Size(1, line.Height));
                return NoteOf(n);
            }
        }
        return null;
    }

    /// <summary>How far (in device-independent pixels) from a footnote marker a finger still counts as hitting it.</summary>
    private const double TouchSlop = 24;

    /// <summary>
    /// Like <see cref="HitFootnote"/>, but with a fat finger: the marker's bounds are grown by
    /// <see cref="TouchSlop"/> on every side (about a 48 dp target), and when several markers are in reach the one
    /// whose centre is nearest wins.
    /// </summary>
    private Footnote? HitFootnoteNear(Point point, out Rect bounds)
    {
        bounds = default;
        if (_notes.Count == 0) return null;
        NoteRange? best = null;
        double bestDistance = double.MaxValue;
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y - TouchSlop || point.Y >= pl.Origin.Y + line.Height + TouchSlop) continue;
            int first = pl.Start, end = pl.End;
            foreach (var n in _notes)
            {
                int start = Math.Max(n.Start, first), stop = Math.Min(n.End, end);
                if (stop <= start) continue; // the marker is not on this line
                var rects = pl.Bounds(start, stop - start);
                if (rects.Count == 0) continue;
                var rect = rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y));
                if (!rect.Inflate(TouchSlop).Contains(point)) continue;
                double distance = Math.Abs(rect.Center.X - point.X) + Math.Abs(rect.Center.Y - point.Y);
                if (distance >= bestDistance) continue;
                best = n;
                bestDistance = distance;
                bounds = rect;
            }
        }
        return best is { } hit ? NoteOf(hit) : null;
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    // ---- Text selection -------------------------------------------------------------------------------
    // SelectableTextBlock selects through its own TextLayout, which this control doesn't use (it formats lines
    // itself), so selection is implemented here on top of the laid-out lines.

    private string _text = string.Empty;

    private bool _appliedVertical; // how the lines now in _lines are arranged
    private double _appliedWidth;  // and how wide their column is

    /// <summary>Text index of the line at the top of the view (the first column shown), in the layout now applied.</summary>
    private int? VisibleStartIndex()
    {
        if (_pitch <= 0) return null;
        if (_appliedVertical)
        {
            foreach (var pl in _lines)
                if (pl.Origin.Y + pl.Line.Height > _offsetY + 0.5) return pl.Start;
            return null;
        }
        int column = _scrolling ? _firstColumn : 0;
        foreach (var pl in _lines)
            if (LogicalColumn(pl) >= column) return pl.Start;
        return null;
    }
    private int _anchor, _caret; // selection is [min, max) in flattened-text indices
    private bool _selecting;

    // Selecting by touch: a long press selects the word, dragging the finger on extends the selection by words.
    // Also while Strong's words are marked: a tap on one opens its entry, a long press (350 ms) selects, and the lift after a long press is no tap (see OnTapped).
    private ScrollGestureRecognizer? _scrollRecognizer;
    private bool _touchSelectingValue;

    /// <summary>
    /// A finger is selecting. While it is, the scroll recognizer must not take the finger over (it would after a few pixels, and the moves
    /// would only be seen as scroll gestures): its scrolling is switched off for as long as the finger selects.
    /// </summary>
    private bool _touchSelecting
    {
        get => _touchSelectingValue;
        set
        {
            _touchSelectingValue = value;
            if (_scrollRecognizer is { } recognizer) recognizer.CanVerticallyScroll = recognizer.CanHorizontallyScroll = !value;
        }
    }
    private int _touchWordStart, _touchWordEnd;

    /// <summary>
    /// A finger is selecting text, or has just stopped: its drag is not a swipe (the bar takes a mostly horizontal drag on the text for a step
    /// in the history), and a drag that selects must not turn into one.
    /// </summary>
    public bool IsSelectingByTouch => _touchSelecting || Environment.TickCount64 - _touchSelectionEnded < 600;
    private ulong _lastTouchMoveTimestamp; // the timestamp of the last move that extended the selection (it is seen by two routes)
    private long _touchSelectionEnded; // Environment.TickCount64 of the lift of the finger that selected (or of the start of the selection)

    // The long press is timed here, from the pointer events: the Holding event of Avalonia depends on the platform (and its base class
    // answers it with the context menu before this control can see it).
    private static readonly TimeSpan LongPressTime = TimeSpan.FromMilliseconds(350);
    private const double LongPressSlop = 10; // the finger may move this far (pixels) and still be a long press
    private DispatcherTimer? _holdTimer;
    private Point _touchPressPoint;
    private IPointer? _touchPointer;

    private void StartHoldTimer(Point position)
    {
        StopHoldTimer();
        if (OverScrollBar(position)) return;
        _touchPressPoint = position;
        _holdTimer = new DispatcherTimer { Interval = LongPressTime };
        _holdTimer.Tick += OnHoldTimer;
        _holdTimer.Start();
    }

    private void StopHoldTimer()
    {
        if (_holdTimer is null) return;
        _holdTimer.Stop();
        _holdTimer.Tick -= OnHoldTimer;
        _holdTimer = null;
    }

    /// <summary>The finger has stayed on the text: select the word, and the finger now extends the selection.</summary>
    private void OnHoldTimer(object? sender, EventArgs e)
    {
        StopHoldTimer();
        if (!_touchDown || _selecting || _panning) return;
        var point = ToContent(_touchPressPoint);
        if (!IsOverText(point) || IndexAt(point) is not { } index) return;
        Focus();
        SelectWordAt(index);
        if (!HasSelection) return;
        _touchWordStart = SelStart;
        _touchWordEnd = SelEnd;
        _touchSelecting = true;
        _touchSelectionEnded = Environment.TickCount64;
        _popup.IsOpen = false;
        _touchPointer?.Capture(this); // the moves and the lift of this finger come here, whatever else (a menu) takes the pointer
    }

    /// <summary>
    /// A long press with a finger also asks for the context menu (Avalonia, or Windows as a right click): not while the long press
    /// selects or is being timed, nor just after. The menu of a touch selection opens when the selection is tapped (see <see cref="OnTapped"/>).
    /// </summary>
    // The recognizer that scrolls marks the moves of a finger as handled once it has begun to scroll, and the ordinary handlers do not see
    // handled events: the selection by touch is driven from handlers that do.
    private void OnTouchSelectMoved(object? sender, PointerEventArgs e)
    {
        if (!_touchSelecting || e.Pointer.Type != PointerType.Touch) return;
        // Listened to in both phases (the scroll recognizer marks moves as handled; the handler sees handled events too): an event is used once.
        if (e.Timestamp == _lastTouchMoveTimestamp) return;
        _lastTouchMoveTimestamp = e.Timestamp;
        var position = e.GetPosition(this);
        if (IndexAt(ToContent(position)) is { } at) ExtendTouchSelection(at);
        UpdateAutoScroll(position);
        e.Handled = true;
    }

    private void OnTouchSelectEnded(object? sender, PointerEventArgs e)
    {
        if (!_touchSelecting || e.Pointer.Type != PointerType.Touch) return;
        _touchSelecting = false;
        _touchSelectionEnded = Environment.TickCount64;
    }

    private void OnContextRequestedTunnel(object? sender, ContextRequestedEventArgs e)
    {
        // The finger lifted (its release does not always arrive). Not when the selection began a moment ago: the Holding gesture of Avalonia asks for the menu
        // about 50 ms after the long press that selected the word, with the finger still down, and ending the selection then handed the finger to the scroll.
        if (_touchSelecting && Environment.TickCount64 - _touchSelectionEnded > 400) { _touchSelecting = false; _touchSelectionEnded = Environment.TickCount64; }
        // subscribed to the tunnel phase: the context menu itself answers in the bubble phase, which a handled event does not reach
        if (_touchSelecting || _holdTimer is not null || Environment.TickCount64 - _touchSelectionEnded < 1000) e.Handled = true;
    }

    /// <summary>The selection from the pressed word to the word at <paramref name="index"/>, whichever side of it that is.</summary>
    private void ExtendTouchSelection(int index) =>
        SetSelection(Math.Min(_touchWordStart, SelectionStart(index)), Math.Max(_touchWordEnd, SelectionEnd(index)));

    private static readonly IBrush SelectionBrush = new SolidColorBrush(Color.FromArgb(110, 0x33, 0x8F, 0xFF));
    private static readonly Cursor IBeamCursor = new(StandardCursorType.Ibeam);

    private int SelStart => Math.Min(_anchor, _caret);
    private int SelEnd => Math.Max(_anchor, _caret);
    public bool HasSelection => SelEnd > SelStart;

    /// <summary>Caret index (0..text length) nearest to <paramref name="point"/>, or null when there is no text.</summary>
    private int? IndexAt(Point point)
    {
        PlacedLine? best = null;
        double bestDistance = double.MaxValue;
        foreach (var pl in _lines)
        {
            double w = pl.Line.WidthIncludingTrailingWhitespace, h = pl.Line.Height;
            double dy = point.Y < pl.Origin.Y ? pl.Origin.Y - point.Y : point.Y >= pl.Origin.Y + h ? point.Y - (pl.Origin.Y + h) : 0;
            double dx = point.X < pl.Origin.X ? pl.Origin.X - point.X : point.X > pl.Origin.X + w ? point.X - (pl.Origin.X + w) : 0;
            double d = dy * dy + dx * dx;
            if (d < bestDistance) { bestDistance = d; best = pl; }
        }
        if (best is not { } b) return null;

        var line = b.Line;
        double x = Math.Clamp(point.X - b.Origin.X, 0, line.WidthIncludingTrailingWhitespace);
        var hit = HitFromDistance(b, x);
        int index = hit.FirstCharacterIndex + hit.TrailingLength;
        int first = b.Start;
        return Math.Clamp(index, first, first + line.Length - line.NewLineLength);
    }

    /// <summary>
    /// The character hit of a distance from the start of the line, found from the bounds of the characters. Avalonia's own
    /// TextLine.GetCharacterHitFromDistance is wrong for a right-to-left line that has non-text runs (the gaps of a justified line): it adds
    /// no width for such a run and goes through runs again, so the pointer is mapped to the wrong character (a selection began a word
    /// back). The hit is on the character whose bounds hold the distance (the nearest one beyond the line); its trailing edge is the one
    /// at the end in the direction of the text, as with Avalonia.
    /// </summary>
    private CharacterHit HitFromDistance(PlacedLine placed, double x)
    {
        var line = placed.Line;
        int first = placed.Start, last = first + line.Length - line.NewLineLength;
        int best = -1, bestLength = 1;
        bool bestTrailing = false;
        double bestDistance = double.MaxValue;
        for (int i = first; i < last && i < _text.Length; i++)
        {
            if (IsPartOfCharacter(_text[i])) continue; // part of the character before it: a mark or the second half of a surrogate pair
            int length = 1;
            while (i + length < last && i + length < _text.Length && IsPartOfCharacter(_text[i + length])) length++;
            foreach (var box in placed.Bounds(i, length))
            {
                var rect = box.Rectangle;
                double distance = x < rect.X ? rect.X - x : x > rect.Right ? x - rect.Right : 0;
                if (distance >= bestDistance) continue;
                bool rtl = box.FlowDirection == FlowDirection.RightToLeft;
                (best, bestLength, bestDistance) = (i, length, distance);
                bestTrailing = rtl ? x < rect.X + rect.Width / 2 : x >= rect.X + rect.Width / 2;
            }
            if (bestDistance == 0) break;
        }
        return best < 0 ? new CharacterHit(first) : new CharacterHit(best, bestTrailing ? bestLength : 0);
    }

    private static bool IsPartOfCharacter(char c) =>
        char.IsLowSurrogate(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark;

    /// <summary>
    /// The point is on the selection or just beside it (<see cref="SelectionTapSlop"/>): the nearest caret position is inside the selection and the nearest line is
    /// close. A finger is not exact, and a tap in the gap beside a short last line, or between the lines, would otherwise put the selection away instead
    /// of offering Copy and Select all.
    /// </summary>
    private bool IsOnSelection(Point point)
    {
        if (!HasSelection || IndexAt(point) is not { } at || at < SelStart || at > SelEnd) return false;
        foreach (var pl in _lines)
        {
            double w = pl.Line.WidthIncludingTrailingWhitespace, h = pl.Line.Height;
            double dy = point.Y < pl.Origin.Y ? pl.Origin.Y - point.Y : point.Y >= pl.Origin.Y + h ? point.Y - (pl.Origin.Y + h) : 0;
            double dx = point.X < pl.Origin.X ? pl.Origin.X - point.X : point.X > pl.Origin.X + w ? point.X - (pl.Origin.X + w) : 0;
            if (dx <= SelectionTapSlop && dy <= SelectionTapSlop) return true;
        }
        return false;
    }

    private const double SelectionTapSlop = 14; // device independent pixels

    private bool IsOverText(Point point) =>
        _lines.Any(pl => point.Y >= pl.Origin.Y && point.Y < pl.Origin.Y + pl.Line.Height
                         && point.X >= pl.Origin.X && point.X <= pl.Origin.X + pl.Line.WidthIncludingTrailingWhitespace);

    private void SetSelection(int anchor, int caret)
    {
        int max = _text.Length;
        (_anchor, _caret) = (Math.Clamp(anchor, 0, max), Math.Clamp(caret, 0, max));
        _layer.InvalidateVisual(); // only the highlight is drawn differently: no layout
    }

    // ---- Whole words ------------------------------------------------------------------------------------

    private static bool InWord(char c) => char.IsLetterOrDigit(c) || Hyphenation.IsMark(c) || c == '\'';

    /// <summary>The start of the word that <paramref name="index"/> is in or at the end of (the index itself between words).</summary>
    private int WordStart(int index) => WordStartIn(_text, index);

    internal static int WordStartIn(string text, int index)
    {
        int start = Math.Min(index, text.Length);
        while (start > 0 && InWord(text[start - 1])) start--;
        return start;
    }

    /// <summary>The end of the word that <paramref name="index"/> is in or at the start of (the index itself between words).</summary>
    private int WordEnd(int index) => WordEndIn(_text, index);

    internal static int WordEndIn(string text, int index)
    {
        int end = Math.Min(index, text.Length);
        while (end < text.Length && InWord(text[end])) end++;
        return end;
    }

    /// <summary>Punctuation that belongs to the word before it: a comma, a period, a closing quote or bracket, a dash.</summary>
    private static bool TrailingPunctuation(char c) =>
        char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.OtherPunctuation or System.Globalization.UnicodeCategory.ClosePunctuation
            or System.Globalization.UnicodeCategory.FinalQuotePunctuation or System.Globalization.UnicodeCategory.DashPunctuation;

    /// <summary>
    /// The end of a selection that reaches the word at <paramref name="index"/>: the end of the word and the punctuation marks that stand
    /// right behind it ("word," or "word.)"), so a selection does not leave them out.
    /// </summary>
    private int SelectionEnd(int index) => SelectionEndIn(_text, index);

    internal static int SelectionEndIn(string text, int index)
    {
        int end = WordEndIn(text, index);
        while (end < text.Length && TrailingPunctuation(text[end])) end++;
        return end;
    }

    /// <summary>Punctuation that belongs to the word after it: the inverted question and exclamation marks of Spanish, an opening quote or bracket.</summary>
    private static bool LeadingPunctuation(char c) =>
        c is '\u00BF' or '\u00A1' or '"'
        || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.OpenPunctuation or System.Globalization.UnicodeCategory.InitialQuotePunctuation;

    /// <summary>
    /// The start of a selection that reaches the word at <paramref name="index"/>: the start of the word and the punctuation marks that stand
    /// right in front of it ("¿word" or "(word"), unless they are stuck to the word before them ("word"next").
    /// </summary>
    private int SelectionStart(int index) => SelectionStartIn(_text, index);

    internal static int SelectionStartIn(string text, int index)
    {
        int start = WordStartIn(text, index), i = start;
        while (i > 0 && LeadingPunctuation(text[i - 1])) i--;
        return i < start && (i == 0 || !InWord(text[i - 1])) ? i : start;
    }

    private void SelectWordAt(int index) => SetSelection(SelectionStart(index), SelectionEnd(index));

    /// <summary>
    /// The selection of a drag that started at <paramref name="anchorIndex"/> and is now at <paramref name="index"/>: from the start of the
    /// word of the one to the end of the word of the other, so a selection never has part of a word. Without a move it is only the caret.
    /// </summary>
    private void SelectWordsFrom(int anchorIndex, int index)
    {
        if (index == anchorIndex) SetSelection(anchorIndex, anchorIndex);
        else if (index > anchorIndex) SetSelection(SelectionStart(anchorIndex), SelectionEnd(index));
        else SetSelection(SelectionEnd(anchorIndex), SelectionStart(index));
    }

    private int _mouseAnchor; // where the mouse button went down

    // ---- Scrolling while selecting ----------------------------------------------------------------------

    private const double AutoScrollEdge = 48;    // the finger or the mouse this near the edge (or beyond it) scrolls
    private const double AutoScrollSpeed = 700;  // pixels per second, at the edge: the deeper in the edge, the faster (up to twice)
    private const long AutoPageMilliseconds = 700; // in columns: a column this often
    private DispatcherTimer? _autoScrollTimer;
    private readonly System.Diagnostics.Stopwatch _autoScrollClock = new();
    private Point _selectPoint;   // where the finger or the mouse is while selecting, in the coordinates of the control
    private long _lastAutoPage;

    /// <summary>The direction (-1 up or back, 1 down or on) and how deep in the edge (0 to 2) the selecting finger or mouse is.</summary>
    private (int Direction, double Depth) AutoScrollZone()
    {
        if (_vScrolling)
        {
            if (_selectPoint.Y < AutoScrollEdge) return (-1, Math.Min(2, (AutoScrollEdge - _selectPoint.Y) / AutoScrollEdge));
            if (_selectPoint.Y > _viewHeight - AutoScrollEdge) return (1, Math.Min(2, (_selectPoint.Y - (_viewHeight - AutoScrollEdge)) / AutoScrollEdge));
        }
        else if (_scrolling)
        {
            if (_selectPoint.X < AutoScrollEdge) return (_rtl ? 1 : -1, 1); // the left edge goes on in a text from right to left
            if (_selectPoint.X > _viewWidth - AutoScrollEdge) return (_rtl ? -1 : 1, 1);
        }
        return (0, 0);
    }

    /// <summary>The selecting finger or mouse moved: start scrolling when it is at an edge, stop when it is not.</summary>
    private void UpdateAutoScroll(Point position)
    {
        _selectPoint = position;
        if (AutoScrollZone().Direction == 0) { _autoScrollTimer?.Stop(); return; }
        if (_autoScrollTimer is { IsEnabled: true }) return;
        _autoScrollClock.Restart();
        _autoScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AutoScrollTick());
        _autoScrollTimer.Start();
    }

    private void AutoScrollTick()
    {
        var (direction, depth) = AutoScrollZone();
        if (direction == 0 || (!_selecting && !_touchSelecting)) { _autoScrollTimer?.Stop(); return; }
        double seconds = _autoScrollClock.Elapsed.TotalSeconds;
        _autoScrollClock.Restart();
        if (_vScrolling)
            SetOffsetY(_offsetY + direction * AutoScrollSpeed * depth * seconds);
        else if (_scrolling && Environment.TickCount64 - _lastAutoPage >= AutoPageMilliseconds)
        {
            int target = _firstColumn + direction; // never past the first or last column: that would go on to another book
            if (target >= 0 && target <= (int)_scrollBar.Maximum) { _lastAutoPage = Environment.TickCount64; ApplyColumn(target); }
        }
        // the finger has not moved, but the text has: the selection reaches the word that is under it now
        if (IndexAt(ToContent(_selectPoint)) is not { } index) return;
        if (_touchSelecting) ExtendTouchSelection(index);
        else SelectWordsFrom(_mouseAnchor, index);
    }

    /// <summary>The selected text as plain text: soft hyphens and footnote markers are left out.</summary>
    public string GetSelectedText()
    {
        var sb = new StringBuilder();
        for (int i = SelStart; i < SelEnd && i < _text.Length; i++)
        {
            if (Hyphenation.IsMark(_text[i]) || _notes.Any(n => i >= n.Start && i < n.End)) continue;
            sb.Append(_text[i]);
        }
        return sb.ToString();
    }

    public void SelectAll() => SetSelection(0, _text.Length);

    public async void Copy()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && HasSelection)
            await clipboard.SetTextAsync(GetSelectedText());
    }

    /// <summary>
    /// The dotted underline of the words with Strong's numbers, in the foreground of their span, while a Strong's dictionary
    /// is switched on. It is drawn from the ranges of the words, below the baseline of each line they are on.
    /// </summary>
    private void DrawStrongsUnderlines(DrawingContext context)
    {
        if (_strongs.Count == 0 || !StrongsDictionaries.Any) return;
        foreach (var pl in _lines)
        {
            if (!IsInView(pl)) continue;
            var line = pl.Line;
            int first = pl.Start, last = first + line.Length - line.NewLineLength;

            // the first word that ends after the start of the line (the words are in order and do not overlap)
            int lo = 0, hi = _strongs.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_strongs[mid].End <= first) lo = mid + 1; else hi = mid;
            }

            double descent = line.Height - line.Baseline;
            double y = pl.Origin.Y + line.Baseline + Math.Min(StrongsUnderlineThickness + 0.5, descent / 2);
            for (int i = lo; i < _strongs.Count && _strongs[i].Start < last; i++)
            {
                int start = Math.Max(_strongs[i].Start, first), end = Math.Min(_strongs[i].End, last);
                if (end <= start) continue;
                var wordPen = StrongsPenFor(_strongs[i].Foreground);
                foreach (var box in pl.Bounds(start, end - start))
                {
                    var r = box.Rectangle;
                    context.DrawLine(wordPen, new Point(pl.Origin.X + r.Left, y), new Point(pl.Origin.X + r.Right, y));
                }
            }
        }
    }

    private void DrawSelection(DrawingContext context)
    {
        if (!HasSelection) return;
        var fill = SelectionFill();
        foreach (var pl in _lines)
        {
            if (!IsInView(pl)) continue;
            int first = pl.Start;
            int start = Math.Max(SelStart, first), end = Math.Min(SelEnd, first + pl.Line.Length - pl.Line.NewLineLength);
            if (end <= start) continue;
            foreach (var box in pl.Bounds(start, end - start))
                context.DrawRectangle(fill, null, box.Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y)));
        }
    }

    /// <summary>
    /// The colour of a selection: the one that the theme resource has, which the constructor sets to the light blue (so the text block, the popups and the other
    /// text of the application select in the same colour). The blue itself is the fallback.
    /// </summary>
    private IBrush SelectionFill()
    {
        if (this.TryFindResource("TextControlSelectionHighlightColor", ActualThemeVariant, out var found))
        {
            if (found is IBrush brush) return brush;
            if (found is Color color) return new SolidColorBrush(color);
        }
        return SelectionBrush;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers == KeyModifiers.None && NavigateByKey(e.Key)) { e.Handled = true; return; }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (e.Key == Key.A) { SelectAll(); e.Handled = true; }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (FromPopup(e)) return;
        var position = e.GetPosition(this);
        if (_holdTimer is not null && Math.Abs(position.X - _touchPressPoint.X) + Math.Abs(position.Y - _touchPressPoint.Y) > LongPressSlop) StopHoldTimer(); // the finger moved: a drag, not a long press
        if (_touchSelecting) { e.Handled = true; return; } // the finger moves the end of the selection: see OnTouchSelectMoved
        if (OverScrollBar(position)) { Cursor = null; return; }
        var point = ToContent(position);
        if (_selecting)
        {
            if (IndexAt(point) is { } index) SelectWordsFrom(_mouseAnchor, index);
            UpdateAutoScroll(position);
            return;
        }
        Cursor = HitFootnote(point, out _) is not null || HitStrongs(point, out _) is not null
            || HitChapterNumber(point, 0, out _) is not null || HitVerseNumber(point, VerseSlopMouse, out _) is not null ? HandCursor : IsOverText(point) ? IBeamCursor : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (FromPopup(e)) return;
        _touchDown = false;
        StopHoldTimer();
        if (_touchSelecting)
        {
            _touchSelecting = false;
            _touchSelectionEnded = Environment.TickCount64;
            e.Pointer.Capture(null);
            return;
        }
        var pressedStrong = _pressedStrong;
        _pressedStrong = null;
        if (_pressedInSelection)
        {
            _pressedInSelection = false;
            if (ContextMenu is { IsOpen: false } menu) menu.Open(this);
            return;
        }
        if (!_selecting) return;
        _selecting = false;
        e.Pointer.Capture(null);
        // A click on a word with Strong's numbers (not a drag that selected text) shows its dictionary entries.
        if (pressedStrong is { } word && _anchor == _caret)
            _ = ShowStrongsAsync(word.Strong, word.Bounds.Translate(new Vector(-_offsetX, -_offsetY)));
    }

    // Light dismiss closes a popup that opens during the very press that is being handled, so the popup is
    // shown from a posted callback that runs after this press has been fully processed.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (FromPopup(e)) return; // a press in the popup is not a press on the text under it (where there may be a footnote or a verse number)
        if (e.Pointer.Type == PointerType.Touch) { _touchDown = true; _inertia = false; _touchSelecting = false; _touchPointer = e.Pointer; StartHoldTimer(e.GetPosition(this)); return; } // touch pans (and taps open footnotes); it does not select
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var position = e.GetPosition(this);
        if (OverScrollBar(position)) return; // the scroll bar is a child; its presses bubble up to here
        var point = ToContent(position);
        // A click on the selection offers Copy and Select all, as a tap does (see OnTapped): the menu opens when the button is lifted. Not a footnote, a
        // Strong's entry or the start of a new selection then: the selection stays, to be copied.
        if (HasSelection && e.ClickCount == 1 && IsOnSelection(point))
        {
            _pressedInSelection = true;
            Focus();
            e.Handled = true;
            return;
        }
        if (HitFootnote(point, out var bounds) is { } note)
        {
            bounds = bounds.Translate(new Vector(-_offsetX, -_offsetY)); // popup placement uses control coordinates
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowFootnote(note, bounds), DispatcherPriority.Background);
            return;
        }
        if (TryOpenNumbers(point, touch: false)) { e.Handled = true; return; }

        _pressedStrong = e.ClickCount == 1 && HitStrongs(point, out var wordBounds) is { } strong ? (strong, wordBounds) : null;

        // Start a selection: click collapses it, double-click selects a word, dragging extends it.
        Focus();
        if (IndexAt(point) is not { } index) return;
        if (e.ClickCount == 2)
        {
            SelectWordAt(index);
        }
        else
        {
            _mouseAnchor = index;
            SetSelection(index, index);
            _selecting = true;
            e.Pointer.Capture(this);
        }
        e.Handled = true;
    }

    private (string Strong, Rect Bounds)? _pressedStrong;
    private bool _pressedInSelection; // the mouse button went down on the selection

    /// <summary>The popup's text is this much smaller than the text of the reader.</summary>
    private const double FootnoteScale = 0.85;

    private void ShowFootnote(Footnote note, Rect bounds) => ShowPopup(note.CloneContent(FootnoteScale), bounds);

    /// <summary>
    /// Shows the entries of the Strong's dictionaries that are switched on for the numbers of a word
    /// (<see cref="StrongsDictionaries.Lookup"/>), as markdown, in the popup of the footnotes.
    /// </summary>
    private async Task ShowStrongsAsync(string strong, Rect bounds)
    {
        string? markdown;
        try { markdown = await Task.Run(() => StrongsDictionaries.Lookup(strong)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or LiteDB.LiteException)
        {
            markdown = null; // a dictionary whose database cannot be read has no entry
        }
        if (string.IsNullOrWhiteSpace(markdown))
            markdown = Localizer.Format("NoStrongsEntry", strong);
 
        if (Environment.NewLine != "\r\n") markdown = markdown.Replace("\r\n", Environment.NewLine);

        // The markdown of the dictionaries is the same Bible Markdown as the text: bold, italic and paragraphs.
        var runs = BookText.ParseText(markdown, new BookText.VersePosition());
        var inlines = ToInlines(runs, FontSize * FootnoteScale, upperCase: false).ToList();
        LinkStrongsNumbers(inlines, strong, bounds);
        ShowPopup(inlines, bounds);
    }

    // A number of four digits or more in an entry ("G1234", or "1234" with the prefix of the word that was clicked)
    // is the number of another entry.
    private static readonly Regex StrongsReference = new(@"(?<![\p{L}\p{N}])(?<prefix>[GgHh])?(?<number>[0-9]{4,})(?![0-9])", RegexOptions.Compiled);

    /// <summary>
    /// Makes the numbers of four digits or more in the text of a dictionary entry links: clicking one closes the popup and
    /// shows the entry of that number in a new one, in the same place. A number without G or H gets the prefix of the
    /// number that was looked up (<paramref name="current"/>).
    /// </summary>
    private void LinkStrongsNumbers(IList<Inline> inlines, string current, Rect bounds)
    {
        string? defaultPrefix = current.TrimStart().Length > 0 && char.ToUpperInvariant(current.TrimStart()[0]) is 'G' or 'H'
            ? char.ToUpperInvariant(current.TrimStart()[0]).ToString() : null;
        for (int i = 0; i < inlines.Count; i++)
        {
            switch (inlines[i])
            {
                case Avalonia.Controls.Documents.Run { Text: { Length: > 0 } text } when StrongsReference.IsMatch(text):
                    var parts = new List<Inline>();
                    int last = 0;
                    foreach (Match m in StrongsReference.Matches(text))
                    {
                        string? prefix = m.Groups["prefix"].Success ? m.Groups["prefix"].Value.ToUpperInvariant() : defaultPrefix;
                        if (prefix is null || !int.TryParse(m.Groups["number"].Value, out int number) || number == 0) continue;
                        if (m.Index > last) parts.Add(new Avalonia.Controls.Documents.Run(text[last..m.Index]));
                        parts.Add(new InlineUIContainer(StrongsLink(m.Value, prefix + number, bounds)));
                        last = m.Index + m.Length;
                    }
                    if (parts.Count == 0) break;
                    if (last < text.Length) parts.Add(new Avalonia.Controls.Documents.Run(text[last..]));
                    inlines.RemoveAt(i);
                    foreach (var part in parts) inlines.Insert(i++, part);
                    i--; // the loop moves on to the inline after the last part
                    break;
                case Span { Inlines: { } children }:
                    LinkStrongsNumbers(children, current, bounds);
                    break;
            }
        }
    }

    private Control StrongsLink(string text, string key, Rect bounds)
    {
        var link = new TextBlock
        {
            Text = text,
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline,
            FontSize = FontSize * FootnoteScale,
            FontFamily = FontFamily,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        link.PointerPressed += (_, e) =>
        {
            e.Handled = true; // it is not the start of a selection of the text around it
            _popup.IsOpen = false;
            // Shown from a posted callback: a popup that opens during the press that closed another is light-dismissed by it.
            Dispatcher.UIThread.Post(() => _ = ShowStrongsAsync(key, bounds), DispatcherPriority.Background);
        };
        return link;
    }

    /// <summary>The inlines of a text in Bible Markdown (bold, italic, small caps, ...), to show it outside the text block.</summary>
    internal static IEnumerable<Inline> MarkdownInlines(string markdown, double size)
    {
        if (Environment.NewLine != "\r\n") markdown = markdown.Replace("\r\n", Environment.NewLine);
        return ToInlines(BookText.ParseText(markdown, new BookText.VersePosition()), size, upperCase: false);
    }
    /// <summary>The plain text of inlines (their runs, also in spans).</summary>
    internal static string PlainText(IEnumerable<Inline> inlines, List<string>? linkTexts = null)
    {
        var sb = new StringBuilder();
        void Walk(IEnumerable<Inline> items)
        {
            foreach (var inline in items)
            {
                if (inline is Avalonia.Controls.Documents.Run run) sb.Append(run.Text);
                else if (inline is Avalonia.Controls.Documents.LineBreak) sb.Append(Environment.NewLine); // as many characters as the text layout counts for it (two on Windows: "\r\n"): the positions of a selection count them
                else if (inline is Avalonia.Controls.Documents.InlineUIContainer container)
                {
                    sb.Append('￼'); // a control in the text (a link) is one character as well
                    linkTexts?.Add(container.Child is TextBlock link ? link.Text ?? string.Empty : string.Empty); // what the reader sees there, for copying
                }
                else if (inline is Span { Inlines: { } children }) Walk(children);
            }
        }
        Walk(inlines);
        return sb.ToString();
    }

    private void ShowPopup(IEnumerable<Inline> content, Rect bounds)
    {
        _popup.IsOpen = false;
        _popup.PlacementRect = bounds;
        var items = content.ToList();
        // Theme resources (not fixed colours) so the popup follows light/dark mode, including live theme switches.
        var text = new SelectableTextBlock // selectable so the text can be copied
        {
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(10, 8),
            Background = Brushes.Transparent, // a control without a background is not hit between its letters: a press there selected nothing
            FontSize = FontSize * FootnoteScale,
            FontFamily = FontFamily, // a popup is its own visual root: it would not inherit the font from this control
            // Nor the direction: a footnote of a Hebrew or Arabic text is written from right to left. It is decided by the text of the
            // popup itself (its first letter), so an entry of a dictionary in another language, opened from such a text, is not turned around.
            FlowDirection = StartsRightToLeft(PlainText(items)) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        foreach (var inline in items) text.Inlines!.Add(inline);
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseHighBrush");
        var linkTexts = new List<string>();
        var popupText = PlainText(items, linkTexts); // the text as the layout has it: the positions of a selection count into it
        // The text does not take the pointer (a control that selects takes every touch: a swipe selected instead of scrolling the popup, and holding the
        // events back to stop that lost the inertia of the scroll). A transparent host takes it, and the selection is made here: a swipe is then the scroll
        // viewer's own. A mouse selects by pressing and dragging (a double click selects a word), a finger by a long press (the word under it, which the
        // finger then extends).
        text.IsHitTestVisible = false;
        var host = new Border { Background = Brushes.Transparent, Child = text };
        // Not attached to the host (ContextMenu property): the long press of a finger asks for such a menu, and opening or even refusing it let go of the finger.
        var contextMenu = new ContextMenu
        {
            ItemsSource = new[]
            {
                CreateMenuItem("Copy", () => CopySelection(), "copy.svg"),
                CreateMenuItem("Select all", text.SelectAll, "select-all.svg"),
            },
        };
        // The menu opens from a right click and from a tap on the selection (below); not from the long press that selects a word by touch.
        bool menuWanted = false, rightPressed = false;
        contextMenu.Opening += (_, e) => { if (!menuWanted && !rightPressed) e.Cancel = true; };
        void OpenMenu()
        {
            menuWanted = true;
            try { contextMenu.Open(host); }
            finally { menuWanted = false; }
        }
        int TextLength() => popupText.Length;
        // The selected text for the clipboard. A link (a Strong's number in an entry) is a control in the text, which the layout counts as one character, U+FFFC,
        // and which the control would copy as that character: its own text takes its place.
        string SelectedText()
        {
            int low = Math.Min(text.SelectionStart, text.SelectionEnd), high = Math.Min(Math.Max(text.SelectionStart, text.SelectionEnd), popupText.Length);
            if (high <= low) return string.Empty;
            int link = popupText.Take(low).Count(c => c == '￼');
            var sb = new StringBuilder();
            for (int i = low; i < high; i++)
            {
                if (popupText[i] != '￼') sb.Append(popupText[i]);
                else sb.Append(link < linkTexts.Count ? linkTexts[link++] : string.Empty);
            }
            return sb.ToString();
        }
        async void CopySelection()
        {
            var clipboard = TopLevel.GetTopLevel(host)?.Clipboard ?? TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && SelectedText() is { Length: > 0 } selected) await clipboard.SetTextAsync(selected);
        }
        text.CopyingToClipboard += (_, e) => { e.Handled = true; CopySelection(); }; // Ctrl+C, when the text has the keys
        int IndexAt(Point point) => Math.Clamp(text.TextLayout.HitTestPoint(point - new Point(text.Padding.Left, text.Padding.Top)).TextPosition, 0, TextLength());
        // Whole words, by the functions of the text of the Bible (WordStartIn, SelectionStartIn ...: the punctuation that stands right behind or in front of a
        // word belongs to it). The selection of a drag from the place where it began (the word of it) to the word of the place where it is now.
        void SelectWords(int anchorIndex, int index, bool caretWithoutMove)
        {
            if (caretWithoutMove && index == anchorIndex) { SetRange(anchorIndex, anchorIndex); return; }
            if (index >= anchorIndex) SetRange(SelectionStartIn(popupText, anchorIndex), SelectionEndIn(popupText, index));
            else SetRange(SelectionStartIn(popupText, index), SelectionEndIn(popupText, anchorIndex));
        }
        // The two ends are written in an order that keeps the start before the end at every step: the control
        // keeps the start from going past the end, so writing a start that is beyond the old end first left it at the old end.
        void SetRange(int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            if (high >= text.SelectionEnd) { text.SelectionEnd = high; text.SelectionStart = low; }
            else { text.SelectionStart = low; text.SelectionEnd = high; }
        }
        // A finger that selects must not scroll the popup as well: the scroll gesture is switched off for as long as it selects.
        var lockedRecognizers = new List<(ScrollGestureRecognizer Recognizer, bool Vertical, bool Horizontal)>();
        void LockScroll(bool locked)
        {
            // The scroll recognizer of this control gets the touches of the popup too, and takes the finger for itself (and with it all its events, which then reach
            // nobody else) once the finger moves: switched off as the text of the Bible does it for its own long press (see _touchSelecting).
            if (_scrollRecognizer is { } own && !_touchSelecting) own.CanVerticallyScroll = own.CanHorizontallyScroll = !locked;
            if (locked)
            {
                if (Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(host).OfType<ScrollViewer>().FirstOrDefault() is not { } viewer) return;
                foreach (var visual in new Visual[] { viewer }.Concat(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(viewer)))
                    if (visual is InputElement input)
                        foreach (var recognizer in input.GestureRecognizers.OfType<ScrollGestureRecognizer>())
                        {
                            lockedRecognizers.Add((recognizer, recognizer.CanVerticallyScroll, recognizer.CanHorizontallyScroll));
                            recognizer.CanVerticallyScroll = recognizer.CanHorizontallyScroll = false;
                        }
                return;
            }
            foreach (var (recognizer, vertical, horizontal) in lockedRecognizers) { recognizer.CanVerticallyScroll = vertical; recognizer.CanHorizontallyScroll = horizontal; }
            lockedRecognizers.Clear();
        }

        bool selecting = false, touchSelecting = false, touchDown = false, moved = false, pressedOnSelection = false;
        bool longPressed = false; // this touch selected a word (kept when the capture is lost before the finger lifts: the order of the two events is not fixed)
        ulong lastMove = 0; // the timestamp of the last move that was used (it comes by two routes)
        int anchor = 0; // where the selection began (the place of the press)
        Point pressedAt = default;
        IPointer? touchPointer = null;
        DispatcherTimer? menuTimer = null, touchTimer = null;
        void StopMenuTimer() { menuTimer?.Stop(); menuTimer = null; }
        void StopTouchTimer() { touchTimer?.Stop(); touchTimer = null; }

        host.PointerPressed += (_, e) =>
        {
            StopMenuTimer();
            StopTouchTimer();
            if (touchSelecting) LockScroll(false);
            selecting = touchSelecting = moved = pressedOnSelection = longPressed = false;
            var properties = e.GetCurrentPoint(host).Properties;
            rightPressed = e.Pointer.Type == PointerType.Mouse && properties.IsRightButtonPressed;
            if (rightPressed) { OpenMenu(); e.Handled = true; return; }
            // A finger does not report a button: it is always the primary press.
            if (e.Pointer.Type == PointerType.Mouse && !properties.IsLeftButtonPressed) return;
            pressedAt = e.GetPosition(text);
            int at = IndexAt(pressedAt);
            int low = Math.Min(text.SelectionStart, text.SelectionEnd), high = Math.Max(text.SelectionStart, text.SelectionEnd);
            bool onSelection = low != high && at >= low && at <= high;
            if (e.Pointer.Type == PointerType.Touch)
            {
                touchDown = true;
                touchPointer = e.Pointer;
                pressedOnSelection = onSelection;
                var timer = touchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                timer.Tick += (_, _) =>
                {
                    StopTouchTimer();
                    if (!touchDown || moved) return;
                    // A long press selects the word, and the finger goes on from it.
                    touchSelecting = longPressed = true;
                    pressedOnSelection = false;
                    anchor = at;
                    SelectWords(anchor, at, caretWithoutMove: false);
                    LockScroll(true);
                    touchPointer?.Capture(host); // as the text of the Bible does for its long press: the moves and the lift of this finger come to the host
                };
                timer.Start();
                return; // not handled: a swipe is the scroll viewer's
            }
            if (e.ClickCount >= 2) { SelectWords(at, at, caretWithoutMove: false); e.Handled = true; return; }
            if (onSelection) { pressedOnSelection = true; e.Handled = true; return; } // the menu opens when the button is lifted
            anchor = at;
            SetRange(at, at);
            selecting = true;
            e.Pointer.Capture(host);
            e.Handled = true;
        };
        // Listened to with handled events too, in both phases (as the text of the Bible does): the scroll recognizer marks the moves of a finger handled once it
        // has the finger, and then the ordinary handlers do not see them; an event is used once.
        void OnMove(PointerEventArgs e)
        {
            if (e.Timestamp == lastMove) return;
            lastMove = e.Timestamp;
            var now = e.GetPosition(text);
            if (!moved && (touchDown || pressedOnSelection) && Math.Abs(now.X - pressedAt.X) + Math.Abs(now.Y - pressedAt.Y) > 10)
            {
                moved = true; // a swipe, or a drag: not a tap
                if (!touchSelecting) StopTouchTimer();
            }
            if (!selecting && !touchSelecting) return;
            // A finger that selected by a long press has its word already (also without a move); the mouse has only a caret until it moves.
            SelectWords(anchor, IndexAt(now), caretWithoutMove: selecting);
            e.Handled = true;
        }
        host.AddHandler(InputElement.PointerMovedEvent, (_, e) => OnMove(e), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        void OnRelease(PointerEventArgs e)
        {
            StopTouchTimer();
            bool wasSelecting = selecting || touchSelecting || longPressed;
            if (touchSelecting) LockScroll(false);
            selecting = touchSelecting = touchDown = false;
            if (e.Pointer.Captured == host) e.Pointer.Capture(null);
            if (!wasSelecting && !moved)
            {
                if (pressedOnSelection)
                {
                    // The menu waits a moment: a second press (a double click that selects a word) must not meet it.
                    StopMenuTimer();
                    var timer = menuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                    timer.Tick += (_, _) =>
                    {
                        StopMenuTimer();
                        if (text.SelectionStart != text.SelectionEnd && !contextMenu.IsOpen) OpenMenu();
                    };
                    timer.Start();
                }
                else if (e.Pointer.Type == PointerType.Touch && text.SelectionStart != text.SelectionEnd) SetRange(0, 0); // a tap elsewhere puts the selection away
            }
            pressedOnSelection = false;
        }
        host.PointerReleased += (_, e) => OnRelease(e);
        host.PointerCaptureLost += (_, _) =>
        {
            // The scroll recognizer took the finger before the long press: a swipe, which never selects. After the long press the capture goes too (switching
            // the scroll gesture off lets go of it), and the selection goes on: the release ends it.
            if (touchSelecting) return;
            StopTouchTimer();
            selecting = touchDown = false;
        };
        ShowCard(host, bounds);
    }

    // On Windows a popup is a window of its own, and Windows runs its press and hold (a long press is a right click, with a square that closes in) on it
    // as on any window, which gets in the way of the long press that selects text. The main window turns that off (see MainWindow in the desktop
    // project); a popup window is asked the same way, once for each window.
    private const uint WM_TABLET_QUERYSYSTEMGESTURESTATUS = 0x02CC;
    private const int TabletDisableGestures = 0x00000001 | 0x00000008 | 0x00000010; // press and hold, pen tap feedback, pen barrel feedback
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, object> GestureFreePopups = new();

    private void OnPopupOpened(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows() || TopLevel.GetTopLevel(_popup.Child) is not { } root || GestureFreePopups.TryGetValue(root, out _)) return;
        GestureFreePopups.Add(root, root);
        Win32Properties.AddWndProcHookCallback(root, TabletGestureHook);
    }

    private static IntPtr TabletGestureHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_TABLET_QUERYSYSTEMGESTURESTATUS) return IntPtr.Zero;
        handled = true;
        return (IntPtr)TabletDisableGestures;
    }

    /// <summary>Shows <paramref name="content"/> in the popup (a card that scrolls when it is tall), next to <paramref name="bounds"/>.</summary>
    private void ShowCard(Control content, Rect bounds)
    {
        _popup.IsOpen = false;
        _popup.PlacementRect = bounds;
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            // The text has the padding itself (and a background to be hit by), so a press on the margin of the card starts a selection too.
            Padding = content is Border ? new Thickness(0) : new Thickness(10, 8),
            MaxWidth = 320,
            Child = new ScrollViewer { MaxHeight = 360, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = content },
            // The drop shadow is drawn outside the card, so the popup needs room around it (and a transparent window).
            Margin = new Thickness(12),
            // A black shadow is invisible on a dark background: in dark mode the shadow is white. The popup is built
            // each time it is shown, so a change of theme is picked up by the next one.
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = 3, Blur = 12, Spread = 0,
                Color = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? Color.FromArgb(0x70, 255, 255, 255) : Color.FromArgb(0x60, 0, 0, 0),
            }),
        };
        card[!Border.BackgroundProperty] = new DynamicResourceExtension("SystemControlBackgroundAltHighBrush");
        card[!Border.BorderBrushProperty] = new DynamicResourceExtension("SystemControlForegroundBaseLowBrush");
        // A scroll that reaches the end of the card's own scroll viewer is not passed on (the events of a popup bubble up to this control, whose text
        // would scroll and put the popup away): the viewer handles what it can, the card swallows the rest.
        card.AddHandler(PointerWheelChangedEvent, (_, e) => e.Handled = true);
        // Nor does a request for a context menu (a long press): it would reach the context menu of this control, which acts on the text of the Bible.
        card.AddHandler(Control.ContextRequestedEvent, (_, e) => e.Handled = true);
        _popup.Child = card;
        _popup.IsOpen = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StrongsDictionaries.AnyChanged += OnStrongsAnyChanged;
        _layer.InvalidateVisual(); // it may have changed while the control was away
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StrongsDictionaries.AnyChanged -= OnStrongsAnyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    // The underline is painted by the text layer: paint again, no layout is needed.
    private void OnStrongsAnyChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(_layer.InvalidateVisual);

    public BibleTextBlock()
    {
        // The light blue is the colour of every selection (also of the text blocks and text boxes of the other pages): it takes the place of the accent colour that the
        // theme has for it. Set where the application's resources are, so that it is found from everywhere; the same brush each time.
        if (Application.Current is { } application) application.Resources["TextControlSelectionHighlightColor"] = SelectionBrush;
        _layer = new TextLayer(this) { IsHitTestVisible = false };
        ActualThemeVariantChanged += (_, _) => _layer.InvalidateVisual(); // the underline of Strong's words follows the theme
        Children.Add(_layer);
        _popup = new Popup
        {
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            PlacementTarget = this,
            WindowManagerAddShadowHint = false, // the card draws its own shadow, not a rectangular one of the window manager
        };
        _popup.Opened += OnPopupOpened;
        Children.Add(_popup);
        _tail = new BusySpinner { IsVisible = false, IsHitTestVisible = false, Thickness = 3 };
        _tail[!BusySpinner.BrushProperty] = new DynamicResourceExtension("SystemControlHighlightAccentBrush");
        Children.Add(_tail);
        _scrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false, AllowAutoHide = false };
        _scrollBar.ValueChanged += (_, e) =>
        {
            if (!_updatingBar) ApplyColumn((int)Math.Round(e.NewValue)); // snap to whole columns
        };
        // The bar can't move past its ends, so pressing an end button while already there asks for the next/previous book.
        _scrollBar.AddHandler(PointerPressedEvent, (_, e) =>
        {
            double x = e.GetPosition(_scrollBar).X, width = _scrollBar.Bounds.Width;
            if (x > width - 24 && _scrollBar.Value >= _scrollBar.Maximum - 0.5) RaiseBoundary(atEnd: true);
            else if (x < 24 && _scrollBar.Value <= 0.5) RaiseBoundary(atEnd: false);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Children.Add(_scrollBar);
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _vBar.ValueChanged += (_, e) => { if (!_updatingBar) SetOffsetY(e.NewValue); };
        Children.Add(_vBar);
        // Inertia is off by default in the recognizer (a ScrollViewer switches it on): without it the scrolling stops when the finger lifts.
        _scrollRecognizer = new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = true, IsScrollInertiaEnabled = true };
        GestureRecognizers.Add(_scrollRecognizer);
        // Where a press began, whatever else handles it: in the popup or not (see _gestureFromPopup).
        AddHandler(PointerPressedEvent, (_, e) => _gestureFromPopup = FromPopup(e), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.ScrollGestureEvent, OnScrollGesture);
        AddHandler(InputElement.ScrollGestureInertiaStartingEvent, OnScrollInertiaStarting);
        AddHandler(InputElement.ScrollGestureEndedEvent, OnScrollGestureEnded);
        AddHandler(ContextRequestedEvent, OnContextRequestedTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTouchSelectMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnTouchSelectEnded, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, (s, e) => { if (_touchSelecting && e.Pointer.Type == PointerType.Touch) { _touchSelecting = false; _touchSelectionEnded = Environment.TickCount64; } },
            Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        Tapped += OnTapped;
        ClipToBounds = true;
        Focusable = true;
        ContextMenu = new ContextMenu
        {
            ItemsSource = new[]
            {
                CreateMenuItem("Copy", () => Copy(), "copy.svg"),
                CreateMenuItem("Select all", SelectAll, "select-all.svg"),
            },
        };
        Background = Brushes.Transparent; // a Panel only receives pointer input where it has a background
        Inlines = new InlineCollection();
        Inlines.CollectionChanged += (_, _) => OnInlinesChanged();
    }

    /// <summary>
    /// Rich text content: <c>&lt;Run&gt;</c>, <c>&lt;Span&gt;</c>, <c>&lt;LineBreak/&gt;</c>. Controls placed among
    /// them are wrapped in an <c>InlineUIContainer</c> by Avalonia and become floats.
    /// </summary>
    [Content]
    public InlineCollection Inlines { get; }

    bool NewChapter = false;
    private readonly HashSet<Control> _ownedFloats = new();

    private int _inlinesVersion; // counts the changes of the inlines: what was made from them is kept until it changes (see FlattenCached)

    private void OnInlinesChanged()
    {
        _inlinesVersion++;
        _flat = null; // the content was replaced by inlines
        var current = new HashSet<Control>();
        foreach (var inline in Inlines) Collect(inline);

        void Collect(Inline inline)
        {
            switch (inline)
            {
                case FloatRun fr: current.Add(fr.Visual); break;
                case InlineUIContainer { Child: { } child }: current.Add(child); break;
                case Span span: foreach (var inner in span.Inlines ?? []) Collect(inner); break;
            }
        }

        // Floats whose inline was removed stay in Children for now: the layout that is shown (and keeps being shown
        // while the new lines are flowed on a worker) still has them placed, so removing them here made the chapter
        // numbers vanish until the flow was done. They go when the new layout replaces it (RemoveStaleFloats).
        _currentFloats = current;
        foreach (var c in current)
            if (_ownedFloats.Add(c) && !Children.Contains(c))
                Children.Add(c);
        MarkDirty();
    }

    private HashSet<Control> _currentFloats = new();

    private void RemoveStaleFloats()
    {
        foreach (var stale in _ownedFloats.Where(c => !_currentFloats.Contains(c)).ToList())
        {
            Children.Remove(stale);
            _ownedFloats.Remove(stale);
        }
    }

    // ---- Parsing BibleText runs ---------------------------------------------------------------------

    private static readonly IBrush WordsOfJesusBrush = new SolidColorBrush(Color.Parse("#B22222"));
    private static bool _buildWordsOfJesus = true;
    private static readonly IBrush VerseNumberBrush = new SolidColorBrush(Color.Parse("#2E8B57"));
    private static bool _buildGreenVerses;
    private static double _buildChapterHeight = double.NaN;

    /// <summary>
    /// The height of a chapter number: two lines of the text, less the gap that is kept under a float. The number is
    /// much larger than the text, so its line box would be three lines tall, but digits stay above the baseline and
    /// two lines are enough. NaN (the height of the text itself) when the line height is not known.
    /// </summary>
    private double ChapterFloatHeight()
    {
        var probe = new TextBlock { Text = "Ag", FontFamily = FontFamily, FontSize = FontSize, FontWeight = FontWeight.Normal };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lineHeight = probe.DesiredSize.Height;
        if (lineHeight <= 0) return double.NaN;
        // The float takes its height plus the gap (see PlaceFloat); a little less keeps the third line free.
        return Math.Max(1, 2 * lineHeight - FloatSpacing - 0.5);
    }

    /// <summary>
    /// Builds the books of the window again, e.g. after <see cref="FontSize"/> or <see cref="ShowWordsOfJesus"/>
    /// changed (the inlines carry the size and colours they were built with). The reader stays where they are.
    /// </summary>
    public async Task RebuildAsync()
    {
        if (Books is not { } books || _loadingBook) return;
        // The new content starts at the top of the window unless a target is pending: ask for the verse the reader is
        // at. It is resolved when the new text is laid out (which may only happen once the control is visible again),
        // and Position stays as it is until then. Assigning Position to its own value would do nothing.
        if (Position is { Book: not null } position)
        {
            _pendingPosition = position;
            _keepPosition = true;
        }
        await SwitchWindowAsync(books, BookIndex, ChapterRequest.ByIndex(ChapterIndex), null, _windowSlots);
    }

    /// <summary>Replaces the content with the text made from the runs of the window (see <see cref="BuildWindow"/>).</summary>
    private void SetContent(FlatWindow content)
    {
        // A window that is made larger or smaller around the reader (_readingAnchor) must not stop what the reader is doing: a finger that pans, a flick that
        // carries on, or an ease to a column go on (the offset is moved with the text when the new lines are laid out). Stopping a flick here left the
        // columns between two columns, and they then snapped back to the one they came from: the swipe moved a little and went back.
        if (_readingAnchor is null)
        {
            StopAnimation();
            _panning = false;
            _inertia = false;
            _touchSelecting = false;
            StopHoldTimer();
        }
        _overscroll = 0;
        // When a position is already known (the next or previous chapter, or a verse is wanted) the shown layout stays exactly
        // where it is until the new lines are flowed, and the position is then set from the anchor. Only a plain start from the top
        // resets the scrolling right away.
        if (_pendingAnchor is null && _pendingPosition is null && _readingAnchor is null)
        {
            _firstColumn = 0;
            _offsetX = _offsetY = 0;
        }
        _anchor = _caret = 0;
        Inlines.Clear(); // (a change notification: it forgets the content that was made from runs)
        _flat = content;
        AttachFloats(content);
        _inlinesVersion++;
        MarkDirty();
    }

    /// <summary>
    /// The inlines of the chapters <paramref name="first"/> to <paramref name="last"/> of a book: the first chapter of the book comes with its
    /// title (and its start mark), the others are the chapters alone. A text that is not a Bible book, and has no chapters, is shown whole,
    /// with its title.
    /// </summary>
    private static IEnumerable<Inline> ToInlines(BookText.Book book, int first, int last, double size, bool upperCase)
    {
        var chapters = ChaptersOf(book);
        if (first <= 0 || chapters.Count == 0)
        {
            // Mark the book start (chapter 0), so Position names the new book as soon as its heading is in view.
            yield return new VerseRun { Position = new BookText.VersePosition { Book = book, Chapter = 0, Verse = 0 } };
            yield return new Run(book.Name)
            {
                FontSize = size * BookTitleScale,
                FontWeight = FontWeight.Bold,
            };
            yield return new LineBreak();
            yield return new LineBreak();
        }
        if (chapters.Count == 0)
        {
            foreach (var inline in ToInlines(book.Runs, size, upperCase)) yield return inline;
            yield break;
        }
        first = Math.Clamp(first, 0, chapters.Count - 1);
        last = Math.Clamp(last, first, chapters.Count - 1);
        // Each chapter is the same text wherever it is in the window, which is what lets its lines be kept when the window is made again (see
        // GetChunkedFlow): it ends with a line break (also the last of the window), and the line breaks that a title brings in front of it are
        // reduced by the one that ends the chapter before. (A chapter that ends with a break of its own gets none added.)
        for (int i = first; i <= last; i++)
        {
            var inlines = ToInlines(chapters[i], size, upperCase).ToList();
            if (i > 0 && inlines.Count > 1 && inlines[0] is VerseRun && inlines[1] is LineBreak) inlines.RemoveAt(1);
            if (inlines.Count == 0 || inlines[^1] is not LineBreak) inlines.Add(new LineBreak());
            foreach (var inline in inlines) yield return inline;
        }
    }

    private static IEnumerable<Inline> ToInlines(IEnumerable<BookText.Run>? runs, double size, bool upperCase, bool breakBeforeFirst = true)
    {
        int lineBreaks = breakBeforeFirst ? 0 : 1; // how many line breaks the text so far ends with (as if one came before, to start without one)
        foreach (var run in runs ?? [])
        {
            // A chapter starts a new line (a paragraph, without a blank line before it), unless the text before it ends with a line break
            // already. (The first chapter of a book follows the title of the book; a chapter that starts with a title brings its breaks.)
            if (run.Class == BookText.Run.Classes.Chapter && run.Chapter > 1 && run.Runs?.FirstOrDefault()?.Class != BookText.Run.Classes.Title && lineBreaks == 0)
            {
                yield return new LineBreak();
                lineBreaks = 1;
            }
            foreach (var inline in ToInlines(run, size, upperCase))
            {
                lineBreaks = inline is LineBreak ? lineBreaks + 1 : 0;
                yield return inline;
            }
        }
    }

    private static IEnumerable<Inline> ToInlines(BookText.Run run, double size, bool upperCase)
    {
        var firstRun = run.Runs?.FirstOrDefault();
        switch (run.Class)
        {
            case BookText.Run.Classes.Footnote:
            {
                // The popup content keeps its structure, so links/references inside a footnote stay styled.
                var note = new Footnote();
                    var runs = run.Runs;
                    if (runs?.Any() == true)
                    {
                        if (firstRun?.Class == BookText.Run.Classes.Bold &&
                            firstRun.Text == $"{run.Position.Chapter}:{run.Position.Verse}")
                        {
                            foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase))
                                note.Inlines!.Add(child);
                        }
                        else 
                        {
                            if (firstRun.Text.StartsWith($"{run.Position.Chapter}:{run.Position.Verse}"))
                            {
                                firstRun.Text = Regex.Replace(firstRun.Text, $@"{run.Position.Chapter}:{run.Position.Verse}\s*", "");
                            }
                            if (string.IsNullOrWhiteSpace(firstRun.Text))
                            {
                                foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase))
                                    note.Inlines!.Add(child);
                            }
                            else
                            {
                                foreach (var child in ToInlines(run.Runs, size, upperCase))
                                    note.Inlines!.Add(child);
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(run.Text))
                        note.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
                yield return note;
                yield break;
            }

            case BookText.Run.Classes.Chapter:
                if (run.Position is not null) yield return new VerseRun { Position = run.Position }; // chapter start (verse 0)
                if (run.Runs.Any() && firstRun?.Class == BookText.Run.Classes.Title)
                { // Swap chapter & first title
                    foreach (var child in ToInlines(firstRun, size, upperCase)) yield return child;
                    yield return new FloatRun(run.Chapter.ToString())
                    {
                        Side = FloatSide.Left,
                        FontSize = size * 2.4,
                        FontWeight = FontWeight.Bold,
                        FloatHeight = _buildChapterHeight, // two lines of the text, not three
                    };
                    // The chapter number is already shown as a left float, so don't repeat it in the text.
                    foreach (var child in ToInlines(run.Runs.Skip(1), size, upperCase)) yield return child;
                    yield break;
                }
                else
                {
                    yield return new FloatRun(run.Chapter.ToString())
                    {
                        Side = FloatSide.Left,
                        FontSize = size * 2.4,
                        FontWeight = FontWeight.Bold,
                        FloatHeight = _buildChapterHeight, // two lines of the text, not three
                    };
                    if (run.Runs?.Any() == true)
                    {
                        foreach (var child in ToInlines(run.Runs, size, upperCase)) yield return child;
                    }
                }
                yield break;

            case BookText.Run.Classes.Verse:
                yield return new VerseRun(run.Position?.Verse.ToString() ?? "")
                {
                    Position = run.Position,
                    FontWeight = FontWeight.Bold,
                    Foreground = _buildGreenVerses ? VerseNumberBrush
                        : run.MarkerColor.A != 0 ? new SolidColorBrush(run.MarkerColor) : null,
                };
                if (run.Runs?.Any() == true)
                    foreach (var child in ToInlines(run.Runs, size, upperCase)) yield return child;
                // A marker parsed from the text ("@12 ") has only the number as its text, which the VerseRun shows already.
                else if (!string.IsNullOrEmpty(run.Text))
                    yield return new Avalonia.Controls.Documents.Run(run.Text);

                yield break;

            case BookText.Run.Classes.Paragraph:
                yield return new LineBreak();
                yield return new LineBreak();
                yield break;
            case BookText.Run.Classes.LineBreak: // a backslash at the end of a line of the markdown: the next line of a poem
                yield return new LineBreak();
                yield break;
            case BookText.Run.Classes.Title:
                if (!run.First)
                {
                    yield return new LineBreak();
                    yield return new LineBreak();
                }
                break;
        }

        // Everything else is a styled span around the run's text or its parsed children.
        var span = run.Class == BookText.Run.Classes.WordWithStrong && !string.IsNullOrWhiteSpace(run.StrongNumber)
            ? new StrongsSpan { Strong = run.StrongNumber }
            : new Span();
        bool lineAfter = false;
        switch (run.Class)
        {
            case BookText.Run.Classes.Bold: span.FontWeight = FontWeight.Bold; break;
            case BookText.Run.Classes.Italic: span.FontStyle = FontStyle.Italic; break;
            case BookText.Run.Classes.Greek: span.FontFamily = GreekFont; break;
            case BookText.Run.Classes.Hebrew:
                span.FontFamily = HebrewFont;
                span.FontSize = size * HebrewScale; // Frank Ruehl CLM looks small next to the Latin fonts
                break;
            case BookText.Run.Classes.WordsOfJesus: if (_buildWordsOfJesus) span.Foreground = WordsOfJesusBrush; break;
            case BookText.Run.Classes.SmallCaps: upperCase = true; break; // the size is reduced per letter, see SmallCapsRuns
            case BookText.Run.Classes.Link:
                span.TextDecorations = TextDecorations.Underline;
                span.Foreground = LinkBrush;
                break;
            case BookText.Run.Classes.Title:
            case BookText.Run.Classes.Book:
                size *= 1.25;
                span.FontSize = size;
                span.FontWeight = FontWeight.Bold;
                lineAfter = true;
                break;
        }

        bool hasChildren = run.Runs?.Any() == true;
        if (hasChildren)
        {
            foreach (var child in ToInlines(run.Runs, size, upperCase))
                span.Inlines!.Add(child);
        }
        else if (!string.IsNullOrEmpty(run.Text))
        {
            if (upperCase) foreach (var part in SmallCapsRuns(run.Text, size)) span.Inlines!.Add(part);
            else span.Inlines!.Add(new Avalonia.Controls.Documents.Run(run.Text));
        }

        if (span.Inlines!.Count > 0) yield return span;
        if (lineAfter) yield return new LineBreak();
    }

    /// <summary>The title of a book is this much larger than the text: large enough to be the heading, small enough for a long name (DEUTERONOMIO) to fit a line of a phone without being split.</summary>
    private const double BookTitleScale = 1.5;

    private const double SmallCapsScale = 0.85;

    // The dotted underline of words that have a Strong's number is drawn by the text layer (see DrawStrongsUnderlines),
    // not made a decoration of the text: it then does not belong to the laid-out lines, and switching it on or off, or
    // changing the theme, only paints again.
    private const double StrongsUnderlineThickness = 1.2;

    // The underline is in the foreground of the word's span (see StrongsRange.Foreground): one pen per brush, made when it is first needed.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<IBrush, Pen> StrongsPens = new();
    private static Pen StrongsPenFor(IBrush brush) => StrongsPens.GetOrAdd(brush, StrongsPenOf);

    // A dash is as long as the thickness times its number: 1 on, 2 off with round caps are dots.
    private static Pen StrongsPenOf(IBrush brush) =>
        new(brush, StrongsUnderlineThickness, new DashStyle(new double[] { 1, 2 }, 0), PenLineCap.Round);

    // Greek and Hebrew text is set in the fonts chosen for them in the settings (Cardo and Frank Ruehl CLM until then),
    // whatever font the reader chose for the rest.
    private static FontFamily GreekFont => CrossBible.Library.UI.SettingsView.GreekFamily;
    private static FontFamily HebrewFont => CrossBible.Library.UI.SettingsView.HebrewFamily;
    private const double HebrewScale = 1.5;

    /// <summary>
    /// Small capitals: the first letter of every word is a capital of the regular size, the other letters are
    /// capitals of a smaller size ("Lord" becomes L + ORD).
    /// </summary>
    private static IEnumerable<Avalonia.Controls.Documents.Run> SmallCapsRuns(string text, double size)
    {
        bool wordStart = true;
        var small = new StringBuilder();
        foreach (var ch in text)
        {
            if (wordStart && !char.IsWhiteSpace(ch))
            {
                if (small.Length > 0) { yield return SmallRun(small.ToString()); small.Clear(); }
                yield return new Avalonia.Controls.Documents.Run(char.ToUpperInvariant(ch).ToString());
                wordStart = false;
            }
            else
            {
                small.Append(char.IsWhiteSpace(ch) ? ch : char.ToUpperInvariant(ch));
                wordStart = char.IsWhiteSpace(ch);
            }
        }
        if (small.Length > 0) yield return SmallRun(small.ToString());

        Avalonia.Controls.Documents.Run SmallRun(string t) => new(t) { FontSize = size * SmallCapsScale };
    }

    /// <summary>The run's text without markup: its children's text when it has any, else its own.</summary>
    private static string PlainText(BookText.Run run) =>
        run.Runs?.Any() == true ? string.Concat(run.Runs.Select(PlainText)) : run.Text ?? string.Empty;

    private readonly record struct RunStyle(FontFamily Family, double Size, FontStyle Style, FontWeight Weight,
        IBrush? Foreground, TextDecorationCollection? Decorations,
        BaselineAlignment Baseline = BaselineAlignment.Baseline);

    /// <summary>1 → "a", 26 → "z", 27 → "aa", …</summary>
    private static string Letters(int number)
    {
        var sb = new StringBuilder();
        for (; number > 0; number = (number - 1) / 26)
            sb.Insert(0, (char)('a' + (number - 1) % 26));
        return sb.ToString();
    }

    private readonly record struct NoteRange(int Start, int End, Footnote? Note, BookText.Run? Source = null);

    /// <summary>Where a word with Strong's numbers is in the flattened text, and the foreground of its span, which its underline is drawn in.</summary>
    private readonly record struct StrongsRange(int Start, int End, string Strong, IBrush Foreground);

    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#1A6FD1"));

    private readonly record struct Segment(int Start, int End, RunStyle Style);

    private RunStyle BaseStyle() => new(FontFamily, FontSize, FontStyle.Normal, FontWeight.Normal,
        Foreground ?? Brushes.Black, null);

    private static GenericTextRunProperties ToRunProperties(RunStyle s) =>
        new(new Typeface(s.Family, s.Style, s.Weight), s.Size, s.Decorations, s.Foreground,
            baselineAlignment: s.Baseline);

    /// <summary>Inherit from <paramref name="parent"/>, overriding only what the inline sets locally.</summary>
    private static RunStyle Derive(Inline inline, RunStyle parent)
    {
        var r = parent;
        if (inline.IsSet(TextElement.FontFamilyProperty)) r = r with { Family = inline.FontFamily };
        if (inline.IsSet(TextElement.FontSizeProperty)) r = r with { Size = inline.FontSize };
        if (inline.IsSet(TextElement.FontStyleProperty)) r = r with { Style = inline.FontStyle };
        if (inline.IsSet(TextElement.FontWeightProperty)) r = r with { Weight = inline.FontWeight };
        if (inline.IsSet(TextElement.ForegroundProperty) && inline.Foreground is { } fg) r = r with { Foreground = fg };
        if (inline.TextDecorations is { } d) r = r with { Decorations = d };
        if (inline.IsSet(Inline.BaselineAlignmentProperty)) r = r with { Baseline = inline.BaselineAlignment };
        return r;
    }

    // The result of Flatten, with what it was made from: a layout is made several times for one text (when the lines have been flowed, when a
    // scroll bar appears), and each pass allocated the same 0.5-0.7 MB again.
    private (int Version, double Size, FontFamily Family, IBrush? Foreground, string? Hyphenation, string? Text) _flatKey;
    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes,
        HashSet<int> Breaks, List<(int Index, BookText.VersePosition Pos)> Markers, List<StrongsRange> Strongs)? _flatResult;

    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes,
        HashSet<int> Breaks, List<(int Index, BookText.VersePosition Pos)> Markers, List<StrongsRange> Strongs) FlattenCached()
    {
        var key = (_inlinesVersion, FontSize, FontFamily, Foreground, HyphenationLanguage, Text);
        if (_flatResult is { } kept && _flatKey.Equals(key)) return kept;
        var result = Flatten();
        _flatKey = key;
        _flatResult = result;
        return result;
    }

    /// <summary>
    /// Concatenates <see cref="Inlines"/> (or <see cref="Text"/> when there are none) into one string plus
    /// per-run styles. Whitespace is collapsed like HTML so indented XAML text doesn't show its indentation.
    /// </summary>
    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes,
        HashSet<int> Breaks, List<(int Index, BookText.VersePosition Pos)> Markers, List<StrongsRange> Strongs) Flatten(IEnumerable<Inline>? source = null)
    {
        if (source is null && _flat is { } window && Inlines.Count == 0) return FlattenWindow(window); // the window was made from runs
        var sb = new StringBuilder();
        var segs = new List<Segment>();
        var anchors = new Dictionary<Control, int>();
        var notes = new List<NoteRange>();
        var strongs = new List<StrongsRange>();
        var breaks = new HashSet<int>();
        var markers = new List<(int Index, BookText.VersePosition Pos)>();
        int noteNumber = 0;
        bool lastSpace = true; // trims leading whitespace
        var root = BaseStyle();

        string? hyphenation = HyphenationLanguage; // soft hyphens mark where a word may be split at the end of a line
        void Append(string raw, RunStyle style, bool verbatim = false)
        {
            if (!verbatim && hyphenation is not null) raw = Hyphenation.Apply(raw, hyphenation);
            int start = sb.Length;
            foreach (char c in raw)
            {
                if (!verbatim && char.IsWhiteSpace(c))
                {
                    if (lastSpace) continue;
                    sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = verbatim && c == '\n';
                }
            }
            if (sb.Length > start) segs.Add(new Segment(start, sb.Length, style));
        }

        // Avalonia turns the line break between XAML elements into a one-character whitespace Run. Next to a
        // footnote that would be a (justify-stretched) gap, so it is dropped: the marker belongs to its word.
        bool afterFootnote = false;
        int verseNumberEnd = -1; // where the text of the last verse number ends

        void TrimTrailingSpace()
        {
            if (sb.Length == 0 || sb[^1] != ' ') return;
            sb.Length--;
            if (segs.Count > 0)
            {
                var last = segs[^1];
                if (last.End - 1 <= last.Start) segs.RemoveAt(segs.Count - 1);
                else segs[^1] = last with { End = last.End - 1 };
            }
            lastSpace = false;
        }

        void Walk(IEnumerable<Inline> inlines, RunStyle parent)
        {
            foreach (var inline in inlines)
            {
                var style = Derive(inline, parent);
                if (inline is VerseRun { Position: { } versePosition }) markers.Add((sb.Length, versePosition));
                if (inline is Run { Text: { } t } && inline is not FloatRun && inline is not Footnote
                    && afterFootnote && string.IsNullOrWhiteSpace(t))
                    continue; // formatting whitespace right after a footnote marker
                if (inline is not FloatRun && inline is not Footnote) afterFootnote = false;
                switch (inline)
                {
                    case FloatRun fr: anchors[fr.Visual] = sb.Length; break; // floated, not flowed
                    case InlineUIContainer { Child: { } child }: anchors[child] = sb.Length; break;
                    case Footnote fn:
                    {
                        string marker = fn.Marker ?? Letters(++noteNumber);
                        var markerStyle = parent with
                        {
                            Size = parent.Size * 0.7,
                            Baseline = BaselineAlignment.Superscript,
                            Foreground = fn.IsSet(TextElement.ForegroundProperty) ? fn.Foreground : LinkBrush,
                            Decorations = null,
                        };
                        // no gap between the word and its marker, but the space behind a verse number stays: the marker is the first of its verse
                        if (verseNumberEnd != sb.Length - 1) TrimTrailingSpace();
                        int markerStart = sb.Length;
                        Append(marker, markerStyle, verbatim: true);
                        if (sb.Length > markerStart) notes.Add(new NoteRange(markerStart, sb.Length, fn));
                        afterFootnote = true;
                        break;
                    }
                    case Run run:
                        Append(run.Text ?? string.Empty, style);
                        if (inline is VerseRun) verseNumberEnd = sb.Length;
                        break;
                    case PageBreak pageBreak:
                        Append("\n", style, verbatim: true);
                        breaks.Add(sb.Length); // the next line starts a column
                        break;
                    case LineBreak: Append("\n", style, verbatim: true); break;
                    case StrongsSpan word when word.Inlines is { } children:
                    {
                        int wordStart = sb.Length;
                        Walk(children, style);
                        if (sb.Length > wordStart) strongs.Add(new StrongsRange(wordStart, sb.Length, word.Strong, style.Foreground));
                        break;
                    }
                    case Span span when span.Inlines is { } children: Walk(children, style); break;
                }
            }
        }

        if (source is not null) Walk(source, root);
        else if (Inlines.Count > 0) Walk(Inlines, root);
        else if (!string.IsNullOrEmpty(Text)) Append(Text, root);

        // Trim one trailing collapsed space.
        if (sb.Length > 0 && sb[^1] == ' ' && segs.Count > 0)
        {
            sb.Length--;
            var last = segs[^1];
            if (last.End - 1 <= last.Start) segs.RemoveAt(segs.Count - 1);
            else segs[^1] = last with { End = last.End - 1 };
        }
        // The caller stores breaks, split and markers together with the lines they belong to (see MeasureCore).
        return (sb.ToString(), segs, anchors, notes, breaks, markers, strongs);
    }

    /// <summary>Index of the segment containing <paramref name="index"/> (segments are contiguous and sorted), or -1.</summary>
    private static int FindSegment(List<Segment> segments, int index)
    {
        int lo = 0, hi = segments.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var seg = segments[mid];
            if (index < seg.Start) hi = mid - 1;
            else if (index >= seg.End) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    /// <summary>
    /// Serves the flattened text to the formatter one styled run at a time. <see cref="Limit"/> makes the text
    /// appear to end early: the formatter fetches runs up to the end of the paragraph for every line it breaks,
    /// which is quadratic for a long paragraph, so each line is formatted from a window just past its end.
    /// </summary>
    /// <summary>Like <see cref="RichTextSource"/> for one line, which ends at a soft hyphen that is shown as a hyphen.</summary>
    private sealed class HyphenSource(string text, List<Segment> segments, int lineEnd) : ITextSource
    {
        public TextRun? GetTextRun(int index)
        {
            if (index >= lineEnd) return null;
            int found = FindSegment(segments, index);
            if (found < 0) return null;
            var seg = segments[found];
            var properties = ToRunProperties(seg.Style);
            if (index == lineEnd - 1) return new TextCharacters("-".AsMemory(), properties);
            return new TextCharacters(text.AsMemory(index, Math.Min(seg.End, lineEnd - 1) - index), properties);
        }
    }

    private sealed class RichTextSource(string text, List<Segment> segments) : ITextSource
    {
        public int Limit { get; set; } = int.MaxValue;

        public TextRun? GetTextRun(int textSourceIndex)
        {
            int end = Math.Min(text.Length, Limit);
            if (textSourceIndex >= end) return null;
            int i = FindSegment(segments, textSourceIndex);
            if (i < 0) return null;
            var seg = segments[i];
            return new TextCharacters(text.AsMemory(textSourceIndex, Math.Min(seg.End, end) - textSourceIndex), ToRunProperties(seg.Style));
        }
    }
}
