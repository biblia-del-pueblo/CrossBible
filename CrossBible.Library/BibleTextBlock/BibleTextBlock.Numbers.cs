using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CrossBible.Library.UI;

/// <summary>The place that the reader picked in the popup of a chapter number or of a verse number.</summary>
public sealed class PlaceRequestedEventArgs(BookText.VersePosition place) : EventArgs
{
    public BookText.VersePosition Place { get; } = place;
}

// Chapter numbers and verse numbers are buttons: one opens a popup with the numbers of all chapters of the book (or all verses of the chapter),
// and the number that is picked is scrolled to.
public partial class BibleTextBlock
{
    /// <summary>How far (in device-independent pixels) from a verse number a mouse pointer and a finger still count as hitting it.</summary>
    private const double VerseSlopMouse = 2, VerseSlopTouch = 12;

    /// <summary>Opens the popup of the chapter number or of the verse number at <paramref name="point"/> (a point of the text), if there is one.</summary>
    private bool TryOpenNumbers(Point point, bool touch)
    {
        if (HitChapterNumber(point, touch ? 8 : 0, out var chapterBounds) is { } book)
        {
            var bounds = chapterBounds.Translate(new Vector(-_offsetX, -_offsetY)); // popup placement uses control coordinates
            Dispatcher.UIThread.Post(() => ShowChapters(book, bounds), DispatcherPriority.Background);
            return true;
        }
        if (HitVerseNumber(point, touch ? VerseSlopTouch : VerseSlopMouse, out var verseBounds) is { } position)
        {
            var bounds = verseBounds.Translate(new Vector(-_offsetX, -_offsetY));
            Dispatcher.UIThread.Post(() => ShowVerses(position, bounds), DispatcherPriority.Background);
            return true;
        }
        return false;
    }

    /// <summary>The book whose chapter number (the big numeral at the start of a chapter) is at <paramref name="point"/>, and where the numeral is.</summary>
    private BookText.Book? HitChapterNumber(Point point, double slop, out Rect bounds)
    {
        bounds = default;
        if (_flat is not { } flat) return null;
        foreach (var (anchor, spec) in flat.Floats)
        {
            if (spec.Control is not { } control) continue;
            int slot = _arranged.FindIndex(a => ReferenceEquals(a.Child, control));
            if (slot < 0) continue;
            // Where the numeral is drawn, not the room that the layout reserved for it: the reserved height is only two lines of the text, which the numeral
            // is taller than (see ArrangeOverride, which arranges it at its full size). Its second line is part of it too.
            var (_, reserved) = _arranged[slot];
            var rect = GetLayoutHeight(control) is var layoutHeight && !double.IsNaN(layoutHeight) && control.DesiredSize.Height > reserved.Height
                ? new Rect(reserved.X, reserved.Y, reserved.Width, control.DesiredSize.Height) : reserved;
            if (!rect.Inflate(slop).Contains(point)) continue;
            // The chapter's own mark (verse 0) is the last mark before the place where its numeral floats. It is at that very place when the chapter has no
            // title; a title comes between them (the numeral floats behind the title, see EmitRun), so the mark is not looked for at the anchor alone.
            // A mark of a verse in between would be that of another chapter: the numeral is not of the mark before it then. The mark of the first verse is at the
            // anchor itself (the numeral floats where the verses begin), but it comes after the numeral: only a mark of verse 0 counts at the anchor.
            BookText.VersePosition? last = null;
            foreach (var (index, pos) in _markers)
            {
                if (index > anchor || (index == anchor && pos.Verse != 0)) break;
                last = pos;
            }
            if (last is { Verse: 0, Chapter: >= 1, Book: { } book }) { bounds = rect; return book; }
        }
        return null;
    }

    /// <summary>
    /// The place of the verse number at <paramref name="point"/> (the number of a verse is a button), and where the number is. Not when a footnote marker
    /// comes right before it: the marker is a button of its own, and the two would be too near to tell apart.
    /// </summary>
    private BookText.VersePosition? HitVerseNumber(Point point, double slop, out Rect bounds)
    {
        bounds = default;
        if (_markers.Count == 0) return null;
        BookText.VersePosition? best = null;
        double bestDistance = double.MaxValue;
        foreach (var pl in _lines)
        {
            var line = pl.Line;
            if (point.Y < pl.Origin.Y - slop || point.Y >= pl.Origin.Y + line.Height + slop) continue;
            // The first mark that is on this line (the marks are in the order of the text).
            int lo = 0, hi = _markers.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_markers[mid].Index < pl.Start) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < _markers.Count && _markers[i].Index < pl.End; i++)
            {
                var (index, pos) = _markers[i];
                if (pos.Verse < 1 || pos.Book is null) continue; // not a verse number (the start of a chapter or of a book)
                int length = pos.Verse.ToString().Length;
                if (index + length > pl.End || FootnoteBefore(index)) continue;
                var rects = pl.Bounds(index, length);
                if (rects.Count == 0) continue;
                var rect = rects[0].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y));
                for (int r = 1; r < rects.Count; r++) rect = rect.Union(rects[r].Rectangle.Translate(new Vector(pl.Origin.X, pl.Origin.Y)));
                if (!rect.Inflate(slop).Contains(point)) continue;
                double distance = Math.Abs(rect.Center.X - point.X) + Math.Abs(rect.Center.Y - point.Y);
                if (distance >= bestDistance) continue;
                best = pos;
                bestDistance = distance;
                bounds = rect;
            }
        }
        return best;
    }

    /// <summary>Whether a footnote marker ends at <paramref name="index"/> (or one space before it).</summary>
    private bool FootnoteBefore(int index)
    {
        foreach (var n in _notes)
        {
            if (n.Start >= index) break;
            if (n.End == index || n.End == index - 1) return true;
        }
        return false;
    }

    /// <summary>
    /// The reader picked a chapter or a verse in the popup of a number. The owner (the toolbar) takes the reader there, so that the place is noted in its
    /// history and in its position indicator; without a handler the text goes there by itself.
    /// </summary>
    public event EventHandler<PlaceRequestedEventArgs>? PlaceRequested;

    private void GoToPlace(BookText.VersePosition place)
    {
        if (PlaceRequested is { } handler) handler(this, new PlaceRequestedEventArgs(place));
        else ScrollToPosition(place);
    }

    private void ShowChapters(BookText.Book book, Rect bounds)
    {
        var numbers = ChaptersOf(book).Where(c => c.Chapter >= 1).Select(c => c.Chapter).Distinct().ToList();
        int current = Position?.Book is { } shown && ReferenceEquals(shown, book) ? Position.Chapter : -1;
        ShowNumbers(numbers, current, bounds, n => GoToPlace(new BookText.VersePosition { Book = book, Chapter = n, Verse = 0 }));
    }

    private void ShowVerses(BookText.VersePosition at, Rect bounds)
    {
        var numbers = _markers
            .Where(m => SameBook(m.Pos.Book, at.Book) && m.Pos.Chapter == at.Chapter && m.Pos.Verse >= 1)
            .Select(m => m.Pos.Verse).Distinct().OrderBy(v => v).ToList();
        ShowNumbers(numbers, at.Verse, bounds, n => GoToPlace(new BookText.VersePosition { Book = at.Book, Chapter = at.Chapter, Verse = n }));
    }

    /// <summary>A popup with a button for each number; a click closes it and calls <paramref name="pick"/>. The number <paramref name="current"/> is in bold.</summary>
    private void ShowNumbers(IReadOnlyList<int> numbers, int current, Rect bounds, Action<int> pick)
    {
        if (numbers.Count == 0) return;
        var panel = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, MaxWidth = 296 };
        foreach (int n in numbers)
        {
            int number = n;
            var button = new Button
            {
                Content = number.ToString(),
                MinWidth = UiScale.Size(48),
                MinHeight = UiScale.Size(40),
                Margin = new Thickness(2),
                Padding = new Thickness(6, 4),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontFamily = FontFamily, // a popup is its own visual root: it would not inherit the font from this control
                FontSize = FontSize * FootnoteScale,
                FontWeight = number == current ? FontWeight.Bold : FontWeight.Normal,
            };
            button.Click += (_, _) =>
            {
                _popup.IsOpen = false;
                pick(number);
            };
            panel.Children.Add(button);
        }
        ShowCard(panel, bounds);
    }
}
