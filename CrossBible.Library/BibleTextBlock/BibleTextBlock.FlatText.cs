using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Controls.Primitives;

namespace CrossBible.Library.UI;

// The text of the window made directly from the runs of the parsed book (BookText.Run), without Avalonia inlines. Inlines are heavy objects that
// only the UI thread may touch, so making them (and then walking them again in Flatten) was the work that was left on the UI thread whenever the
// window of chapters moved. The builder below produces what Flatten produced from the inlines: the text, its styled segments, the verse markers, the
// footnote and Strong's ranges and where the chapter numerals float; it is plain data and runs on a worker, once per chapter.
public partial class BibleTextBlock
{
    /// <summary>What the flat text of a chapter depends on besides the chapter: if it is another, the text is made again.</summary>
    private sealed record FlatContext(RunStyle Root, string? Hyphenation, bool GreenVerses, bool WordsOfJesus, double ChapterHeight,
        FontFamily Greek, FontFamily Hebrew);

    /// <summary>A chapter numeral that floats left: made into a control on the UI thread, once, so the lines that were flowed around it can be kept.</summary>
    private sealed class FloatSpec(string text, double size, double height)
    {
        public string Text { get; } = text;
        public double Size { get; } = size;
        public double Height { get; } = height;
        public Control? Control { get; set; }
    }

    /// <summary>The flat text of one chapter (or of the title of a book): its indices count from its own start.</summary>
    private sealed class FlatPiece(string text, List<Segment> segments, List<(int Index, BookText.VersePosition Pos)> markers,
        List<NoteRange> notes, List<StrongsRange> strongs, List<(int Anchor, FloatSpec Spec)> floats)
    {
        public string Text { get; } = text;
        public List<Segment> Segments { get; } = segments;
        public List<(int Index, BookText.VersePosition Pos)> Markers { get; } = markers;
        public List<NoteRange> Notes { get; } = notes;
        public List<StrongsRange> Strongs { get; } = strongs;
        public List<(int Anchor, FloatSpec Spec)> Floats { get; } = floats;
    }

    /// <summary>The flat text of the chapters of the window, put together.</summary>
    private sealed class FlatWindow(FlatContext context, IReadOnlyList<BookText.Book> books, int firstBook, int first, int lastBook, int last, string text, List<Segment> segments,
        List<(int Index, BookText.VersePosition Pos)> markers, List<NoteRange> notes, List<StrongsRange> strongs, List<(int Anchor, FloatSpec Spec)> floats)
    {
        public FlatContext Context { get; } = context;
        public IReadOnlyList<BookText.Book> Books { get; } = books;
        public int FirstBook { get; } = firstBook;
        public int First { get; } = first;
        public int LastBook { get; } = lastBook;
        public int Last { get; } = last;
        public string Text { get; } = text;
        public List<Segment> Segments { get; } = segments;
        public List<(int Index, BookText.VersePosition Pos)> Markers { get; } = markers;
        public List<NoteRange> Notes { get; } = notes;
        public List<StrongsRange> Strongs { get; } = strongs;
        public List<(int Anchor, FloatSpec Spec)> Floats { get; } = floats;
    }

    private FlatWindow? _flat; // the content of the window when it was made from runs (else the content is Inlines or Text)

    // The flat text of the chapters, kept while the window moves: the chapters that stay in it are the larger part of every new window.
    private readonly Dictionary<(BookText.Book Book, int Chapter), (FlatContext Context, FlatPiece Piece)> _pieceCache = new();
    private (IReadOnlyList<BookText.Book>? Books, int FirstBook, int First, int LastBook, int Last) _pieceCacheRange;

    private (double Size, FontFamily? Family, double Spacing, double Height)? _chapterHeightKey;

    /// <summary><see cref="ChapterFloatHeight"/>, measured once for a font and size.</summary>
    private double CachedChapterHeight()
    {
        if (_chapterHeightKey is { } key && key.Size == FontSize && Equals(key.Family, FontFamily) && key.Spacing == FloatSpacing) return key.Height;
        double height = ChapterFloatHeight();
        _chapterHeightKey = (FontSize, FontFamily, FloatSpacing, height);
        return height;
    }

    private FlatContext MakeFlatContext() =>
        new(BaseStyle(), HyphenationLanguage, ShowVerseNumbersInGreen, ShowWordsOfJesus, CachedChapterHeight(), GreekFont, HebrewFont);

    /// <summary>
    /// Builds the flat text of the chapters from <paramref name="first"/> of book <paramref name="firstBook"/> to <paramref name="last"/> of book <paramref name="lastBook"/>
    /// (the books between them whole); meant for a worker thread. A book that starts in the window comes with its title.
    /// </summary>
    private FlatWindow BuildWindow(FlatContext cx, IReadOnlyList<BookText.Book> books, int firstBook, int first, int lastBook, int last)
    {
        var pieces = new List<FlatPiece>();
        for (int b = firstBook; b <= lastBook; b++)
        {
            var book = books[b];
            var chapters = ChaptersOf(book);
            bool follows = pieces.Count > 0; // text of another book comes before: a blank line sets the title apart
            if (chapters.Count == 0) { pieces.Add(new FlatBuilder(cx).Body(book, follows)); continue; }
            int from = Math.Clamp(b == firstBook ? first : 0, 0, chapters.Count - 1);
            int to = Math.Clamp(b == lastBook ? last : chapters.Count - 1, from, chapters.Count - 1);
            if (from == 0) pieces.Add(new FlatBuilder(cx).Header(book, follows));
            for (int i = from; i <= to; i++)
            {
                FlatPiece? piece = null;
                lock (_pieceCache)
                    if (_pieceCache.TryGetValue((book, i), out var kept) && kept.Context.Equals(cx)) piece = kept.Piece;
                if (piece is null)
                {
                    piece = new FlatBuilder(cx).Chapter(chapters[i], dropLeadingBreak: i > 0);
                    lock (_pieceCache) _pieceCache[(book, i)] = (cx, piece);
                }
                pieces.Add(piece);
            }
        }
        return Compose(cx, books, firstBook, first, lastBook, last, pieces);
    }

    private static FlatWindow Compose(FlatContext cx, IReadOnlyList<BookText.Book> books, int firstBook, int first, int lastBook, int last, List<FlatPiece> pieces)
    {
        var text = pieces.Count == 1 ? pieces[0].Text : string.Concat(pieces.Select(p => p.Text));
        var segments = new List<Segment>(pieces.Sum(p => p.Segments.Count));
        var markers = new List<(int Index, BookText.VersePosition Pos)>();
        var notes = new List<NoteRange>();
        var strongs = new List<StrongsRange>();
        var floats = new List<(int Anchor, FloatSpec Spec)>();
        int offset = 0;
        foreach (var p in pieces)
        {
            foreach (var s in p.Segments) segments.Add(s with { Start = s.Start + offset, End = s.End + offset });
            foreach (var (index, pos) in p.Markers) markers.Add((index + offset, pos));
            foreach (var n in p.Notes) notes.Add(n with { Start = n.Start + offset, End = n.End + offset });
            foreach (var s in p.Strongs) strongs.Add(s with { Start = s.Start + offset, End = s.End + offset });
            foreach (var (anchor, spec) in p.Floats) floats.Add((anchor + offset, spec));
            offset += p.Text.Length;
        }
        return new FlatWindow(cx, books, firstBook, first, lastBook, last, text, segments, markers, notes, strongs, floats);
    }

    /// <summary>Keeps the pieces of this window and of the one before it (the window moves by a chapter or two, and goes back); the rest go.</summary>
    private void PrunePieceCache(IReadOnlyList<BookText.Book> books, int firstBook, int first, int lastBook, int last)
    {
        var keep = new HashSet<(BookText.Book, int)>();
        foreach (var (b, c) in SlotsOf(books, firstBook, first, lastBook, last)) keep.Add((books[b], c));
        if (_pieceCacheRange is { Books: { } before } range && ReferenceEquals(before, books))
            foreach (var (b, c) in SlotsOf(books, range.FirstBook, range.First, range.LastBook, range.Last)) keep.Add((books[b], c));
        lock (_pieceCache)
            foreach (var stale in _pieceCache.Keys.Where(k => !keep.Contains(k)).ToList())
                _pieceCache.Remove(stale);
        _pieceCacheRange = (books, firstBook, first, lastBook, last);
    }

    /// <summary>Makes the controls of the chapter numerals of a window (UI thread) and lets the panel own them, like the floats of inlines.</summary>
    private void AttachFloats(FlatWindow window)
    {
        var current = new HashSet<Control>();
        foreach (var (_, spec) in window.Floats)
        {
            spec.Control ??= MakeFloat(spec);
            current.Add(spec.Control);
        }
        _currentFloats = current;
        foreach (var c in current)
            if (_ownedFloats.Add(c) && !Children.Contains(c))
                Children.Add(c);
    }

    private static Control MakeFloat(FloatSpec spec)
    {
        // Not hit-testable: a touch that starts on the chapter number must pan the text (and a tap on it open a footnote near it) like a touch anywhere else.
        // As a control that selects text it took the touch, and a swipe that began on the number at a chapter start did not move the text.
        var visual = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Text = spec.Text, FontSize = spec.Size, FontWeight = FontWeight.Bold, IsHitTestVisible = false };
        SetLayoutHeight(visual, spec.Height);
        SetSide(visual, FloatSide.Left);
        return visual;
    }

    /// <summary>What <see cref="Flatten"/> gives for a window made from runs. The text is made again if the font, colours or settings it was made for changed.</summary>
    private (string Text, List<Segment> Segments, Dictionary<Control, int> Anchors, List<NoteRange> Notes,
        HashSet<int> Breaks, List<(int Index, BookText.VersePosition Pos)> Markers, List<StrongsRange> Strongs) FlattenWindow(FlatWindow window)
    {
        var cx = MakeFlatContext();
        if (!cx.Equals(window.Context))
        {
            window = BuildWindow(cx, window.Books, window.FirstBook, window.First, window.LastBook, window.Last);
            _flat = window;
            AttachFloats(window);
        }
        var anchors = new Dictionary<Control, int>();
        foreach (var (anchor, spec) in window.Floats) anchors[spec.Control!] = anchor;
        return (window.Text, window.Segments, anchors, window.Notes, new HashSet<int>(), window.Markers, window.Strongs);
    }

    /// <summary>The footnote of a marker: made from its run when it is wanted (the popup), not for every marker of the window.</summary>
    private Footnote? NoteOf(NoteRange note)
    {
        if (note.Note is not null || note.Source is null) return note.Note;
        _buildGreenVerses = ShowVerseNumbersInGreen;
        _buildWordsOfJesus = ShowWordsOfJesus;
        return ToInlines(note.Source, FontSize, upperCase: false).OfType<Footnote>().FirstOrDefault();
    }

    private static readonly ConcurrentDictionary<Color, IBrush> MarkerBrushes = new();

    /// <summary>
    /// Makes the flat text from runs the way <c>ToInlines</c> followed by <c>Flatten</c> make it from inlines (and must keep doing so: the comments
    /// name the rule of each of them). One builder makes one piece.
    /// </summary>
    private sealed class FlatBuilder(FlatContext cx)
    {
        /// <summary>The inlines that a run (or a list of them) brings to the level it belongs to, as ToInlines yields them: how many, and how many line breaks they end with.</summary>
        private struct Top
        {
            public int Count, Tail;
            public bool Drop;        // the line break that follows the start mark of a chapter is left out (a chapter after another one)
            public bool FirstIsVerse;
            public void Add(bool isBreak) { Count++; Tail = isBreak ? Tail + 1 : 0; }
        }

        private readonly StringBuilder _sb = new();
        private readonly List<Segment> _segs = new();
        private readonly List<(int Index, BookText.VersePosition Pos)> _markers = new();
        private readonly List<NoteRange> _notes = new();
        private readonly List<StrongsRange> _strongs = new();
        private readonly List<(int Anchor, FloatSpec Spec)> _floats = new();
        private int _noteNumber;
        private bool _lastSpace = true; // trims leading whitespace
        private bool _afterFootnote;
        private int _verseNumberEnd = -1;
        private readonly RunStyle _root = cx.Root;

        public FlatPiece Chapter(BookText.Run chapter, bool dropLeadingBreak)
        {
            var top = new Top { Drop = dropLeadingBreak };
            EmitRun(chapter, _root, _root.Size, false, ref top);
            if (top.Count == 0 || top.Tail == 0) AppendBreak(_root); // every chapter ends with a line break
            return Finish();
        }

        public FlatPiece Header(BookText.Book book, bool leadingBreak)
        {
            var top = new Top();
            EmitHeader(book, leadingBreak, ref top);
            return Finish();
        }

        /// <summary>A text that is not a Bible book (it has no chapters): the title and the runs.</summary>
        public FlatPiece Body(BookText.Book book, bool leadingBreak)
        {
            var top = new Top();
            EmitHeader(book, leadingBreak, ref top);
            EmitRuns(book.Runs, _root, _root.Size, false, true, ref top);
            return Finish();
        }

        private void EmitHeader(BookText.Book book, bool leadingBreak, ref Top top)
        {
            // Mark the book start (chapter 0), so Position names the new book as soon as its heading is in view.
            EmitVerseRun(null, new BookText.VersePosition { Book = book, Chapter = 0, Verse = 0 }, _root, ref top);
            if (leadingBreak) EmitBreak(_root, ref top); // after the text of the book before: a blank line above the title
            EmitText(book.Name, _root with { Size = _root.Size * BookTitleScale, Weight = FontWeight.Bold }, ref top);
            EmitBreak(_root, ref top);
            EmitBreak(_root, ref top);
        }

        private FlatPiece Finish()
        {
            // Trim one trailing collapsed space.
            if (_sb.Length > 0 && _sb[^1] == ' ' && _segs.Count > 0)
            {
                _sb.Length--;
                var last = _segs[^1];
                if (last.End - 1 <= last.Start) _segs.RemoveAt(_segs.Count - 1);
                else _segs[^1] = last with { End = last.End - 1 };
            }
            return new FlatPiece(_sb.ToString(), _segs, _markers, _notes, _strongs, _floats);
        }

        // ---- The text (what Flatten does with the inlines) ----

        private void Append(string raw, RunStyle style, bool verbatim = false)
        {
            if (!verbatim && cx.Hyphenation is not null) raw = Hyphenation.Apply(raw, cx.Hyphenation); // soft hyphens mark where a word may be split
            int start = _sb.Length;
            foreach (char c in raw)
            {
                if (!verbatim && char.IsWhiteSpace(c))
                {
                    if (_lastSpace) continue;
                    _sb.Append(' ');
                    _lastSpace = true;
                }
                else
                {
                    _sb.Append(c);
                    _lastSpace = verbatim && c == '\n';
                }
            }
            if (_sb.Length > start) _segs.Add(new Segment(start, _sb.Length, style));
        }

        private void TrimTrailingSpace()
        {
            if (_sb.Length == 0 || _sb[^1] != ' ') return;
            _sb.Length--;
            if (_segs.Count > 0)
            {
                var last = _segs[^1];
                if (last.End - 1 <= last.Start) _segs.RemoveAt(_segs.Count - 1);
                else _segs[^1] = last with { End = last.End - 1 };
            }
            _lastSpace = false;
        }

        private void AppendBreak(RunStyle style)
        {
            _afterFootnote = false;
            Append("\n", style, verbatim: true);
        }

        // ---- The inlines of ToInlines ----

        private void EmitText(string? text, RunStyle style, ref Top top)
        {
            if (string.IsNullOrEmpty(text)) return;
            top.Add(false);
            // Formatting whitespace right after a footnote marker is dropped: the marker belongs to its word, and a gap would be stretched by the justification.
            if (_afterFootnote && string.IsNullOrWhiteSpace(text)) return;
            _afterFootnote = false;
            Append(text, style);
        }

        private void EmitBreak(RunStyle style, ref Top top)
        {
            if (top.Drop && top.Count == 1 && top.FirstIsVerse) { top.Drop = false; return; }
            top.Add(true);
            AppendBreak(style);
        }

        private void EmitVerseRun(string? text, BookText.VersePosition? position, RunStyle style, ref Top top)
        {
            top.Add(false);
            if (top.Count == 1) top.FirstIsVerse = true;
            if (position is not null) _markers.Add((_sb.Length, position));
            if (text is not null && _afterFootnote && string.IsNullOrWhiteSpace(text)) return;
            _afterFootnote = false;
            Append(text ?? string.Empty, style);
            _verseNumberEnd = _sb.Length;
        }

        private void EmitFootnote(BookText.Run run, RunStyle parent, ref Top top)
        {
            top.Add(false);
            string marker = Letters(++_noteNumber);
            var markerStyle = parent with { Size = parent.Size * 0.7, Baseline = BaselineAlignment.Superscript, Foreground = LinkBrush, Decorations = null };
            // No gap between the word and its marker, but the space behind a verse number stays: the marker is the first of its verse.
            if (_verseNumberEnd != _sb.Length - 1) TrimTrailingSpace();
            int markerStart = _sb.Length;
            Append(marker, markerStyle, verbatim: true);
            if (_sb.Length > markerStart) _notes.Add(new NoteRange(markerStart, _sb.Length, null, run)); // the popup's content is made when it is wanted
            _afterFootnote = true;
        }

        private void EmitRuns(IEnumerable<BookText.Run>? runs, RunStyle parent, double size, bool upper, bool breakBeforeFirst, ref Top top)
        {
            int lineBreaks = breakBeforeFirst ? 0 : 1; // how many line breaks the text so far ends with (as if one came before, to start without one)
            foreach (var run in runs ?? [])
            {
                // A chapter starts a new line (a paragraph, without a blank line before it), unless the text before it ends with a line break
                // already. (The first chapter of a book follows the title of the book; a chapter that starts with a title brings its breaks.)
                if (run.Class == BookText.Run.Classes.Chapter && run.Chapter > 1 && run.Runs?.FirstOrDefault()?.Class != BookText.Run.Classes.Title && lineBreaks == 0)
                {
                    EmitBreak(parent, ref top);
                    lineBreaks = 1;
                }
                int before = top.Count;
                EmitRun(run, parent, size, upper, ref top);
                int emitted = top.Count - before;
                if (emitted > 0)
                {
                    int tail = Math.Min(top.Tail, emitted);
                    lineBreaks = tail == emitted ? lineBreaks + tail : tail;
                }
            }
        }

        /// <summary>Whether <c>ToInlines(run)</c> brings at least one inline.</summary>
        private static bool Yields(BookText.Run run) => run.Class switch
        {
            BookText.Run.Classes.Footnote or BookText.Run.Classes.Chapter or BookText.Run.Classes.Verse
                or BookText.Run.Classes.Paragraph or BookText.Run.Classes.LineBreak
                or BookText.Run.Classes.Title or BookText.Run.Classes.Book => true, // a title and a book end with a line break
            _ => SpanHasInlines(run),
        };

        private static bool SpanHasInlines(BookText.Run run) =>
            run.Runs?.Any() == true ? run.Runs.Any(Yields) : !string.IsNullOrEmpty(run.Text);

        private void EmitRun(BookText.Run run, RunStyle parent, double size, bool upper, ref Top top)
        {
            var firstRun = run.Runs?.FirstOrDefault();
            switch (run.Class)
            {
                case BookText.Run.Classes.Footnote:
                    EmitFootnote(run, parent, ref top);
                    return;

                case BookText.Run.Classes.Chapter:
                    if (run.Position is not null) EmitVerseRun(null, run.Position, parent, ref top); // chapter start (verse 0)
                    if (run.Runs?.Any() == true && firstRun?.Class == BookText.Run.Classes.Title)
                    {   // swap chapter & first title
                        EmitRun(firstRun, parent, size, upper, ref top);
                        EmitFloat(run.Chapter.ToString(), size, ref top);
                        // The chapter number is already shown as a left float, so it is not repeated in the text.
                        EmitRuns(run.Runs.Skip(1), parent, size, upper, true, ref top);
                    }
                    else
                    {
                        EmitFloat(run.Chapter.ToString(), size, ref top);
                        if (run.Runs?.Any() == true) EmitRuns(run.Runs, parent, size, upper, true, ref top);
                    }
                    return;

                case BookText.Run.Classes.Verse:
                {
                    var verseStyle = parent with { Weight = FontWeight.Bold };
                    if (cx.GreenVerses) verseStyle = verseStyle with { Foreground = VerseNumberBrush };
                    else if (run.MarkerColor.A != 0) verseStyle = verseStyle with { Foreground = MarkerBrushes.GetOrAdd(run.MarkerColor, c => new ImmutableSolidColorBrush(c)) };
                    EmitVerseRun(run.Position?.Verse.ToString() ?? "", run.Position, verseStyle, ref top);
                    if (run.Runs?.Any() == true) EmitRuns(run.Runs, parent, size, upper, true, ref top);
                    // A marker parsed from the text ("@12 ") has only the number as its text, which the verse number shows already.
                    else EmitText(run.Text, parent, ref top);
                    return;
                }

                case BookText.Run.Classes.Paragraph:
                    EmitBreak(parent, ref top);
                    EmitBreak(parent, ref top);
                    return;
                case BookText.Run.Classes.LineBreak: // a backslash at the end of a line of the markdown: the next line of a poem
                    EmitBreak(parent, ref top);
                    return;
                case BookText.Run.Classes.Title:
                    if (!run.First)
                    {
                        EmitBreak(parent, ref top);
                        EmitBreak(parent, ref top);
                    }
                    break;
            }

            // Everything else is a styled span around the run's text or its parsed children.
            var style = parent;
            bool strongs = run.Class == BookText.Run.Classes.WordWithStrong && !string.IsNullOrWhiteSpace(run.StrongNumber);
            bool lineAfter = false;
            switch (run.Class)
            {
                case BookText.Run.Classes.Bold: style = style with { Weight = FontWeight.Bold }; break;
                case BookText.Run.Classes.Italic: style = style with { Style = FontStyle.Italic }; break;
                case BookText.Run.Classes.Greek: style = style with { Family = cx.Greek }; break;
                case BookText.Run.Classes.Hebrew: style = style with { Family = cx.Hebrew, Size = size * HebrewScale }; break; // Frank Ruehl CLM looks small next to the Latin fonts
                case BookText.Run.Classes.WordsOfJesus: if (cx.WordsOfJesus) style = style with { Foreground = WordsOfJesusBrush }; break;
                case BookText.Run.Classes.SmallCaps: upper = true; break; // the size is reduced per letter
                case BookText.Run.Classes.Link: style = style with { Decorations = TextDecorations.Underline, Foreground = LinkBrush }; break;
                case BookText.Run.Classes.Title:
                case BookText.Run.Classes.Book:
                    size *= TitleScale;
                    style = style with { Size = size, Weight = FontWeight.Bold };
                    lineAfter = true;
                    break;
            }

            bool hasChildren = run.Runs?.Any() == true;
            if (hasChildren ? run.Runs!.Any(Yields) : !string.IsNullOrEmpty(run.Text))
            {
                top.Add(false); // the span
                _afterFootnote = false;
                int wordStart = _sb.Length;
                var inner = new Top();
                if (hasChildren) EmitRuns(run.Runs, style, size, upper, true, ref inner);
                else if (upper) EmitSmallCaps(run.Text!, size, style, ref inner);
                else EmitText(run.Text, style, ref inner);
                if (strongs && _sb.Length > wordStart) _strongs.Add(new StrongsRange(wordStart, _sb.Length, run.StrongNumber!, style.Foreground!));
            }
            if (lineAfter) EmitBreak(parent, ref top);
        }

        private void EmitFloat(string text, double size, ref Top top)
        {
            top.Add(false);
            _floats.Add((_sb.Length, new FloatSpec(text, size * 2.4, cx.ChapterHeight))); // floated, not flowed
        }

        /// <summary>
        /// Small capitals: the first letter of every word is a capital of the regular size, the other letters are capitals of a smaller size
        /// ("Lord" becomes L + ORD). The same as <see cref="SmallCapsRuns"/>.
        /// </summary>
        private void EmitSmallCaps(string text, double size, RunStyle style, ref Top top)
        {
            bool wordStart = true;
            var small = new StringBuilder();
            var smallStyle = style with { Size = size * SmallCapsScale };
            foreach (var ch in text)
            {
                if (wordStart && !char.IsWhiteSpace(ch))
                {
                    if (small.Length > 0) { EmitText(small.ToString(), smallStyle, ref top); small.Clear(); }
                    EmitText(char.ToUpperInvariant(ch).ToString(), style, ref top);
                    wordStart = false;
                }
                else
                {
                    small.Append(char.IsWhiteSpace(ch) ? ch : char.ToUpperInvariant(ch));
                    wordStart = char.IsWhiteSpace(ch);
                }
            }
            if (small.Length > 0) EmitText(small.ToString(), smallStyle, ref top);
        }
    }
}
