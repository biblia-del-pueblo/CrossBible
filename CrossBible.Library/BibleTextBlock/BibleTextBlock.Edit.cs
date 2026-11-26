using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace CrossBible.Library.UI;

/// <summary>
/// Edit mode: while <see cref="IsEditing"/> is set, the text is made from the same runs as the reading text (see FlatText), and the runs that have
/// <see cref="BookText.Run.IsEditable"/> can be edited in place: typing, deleting, the arrow keys, selecting with the mouse, copying, cutting, pasting
/// and undo. What is typed changes the <c>Text</c> of the run it is in; leaving the mode writes the books that were changed back as markdown
/// (see <see cref="BookText.ToMarkdown(IEnumerable{BookText.Run})"/>), and shows the reading text again where the reader was.
/// </summary>
public partial class BibleTextBlock
{
    /// <summary>True while the editable runs are edited. Set by the pen button of <see cref="BibleToolbar"/>.</summary>
    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<BibleTextBlock, bool>(nameof(IsEditing));

    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }

    /// <summary>What a run was before a step: its text, or (for a word with Strong's numbers) its numbers.</summary>
    private readonly record struct RunState(BookText.Run Run, string? Text, string? Strong, bool IsStrong);

    private readonly List<(List<RunState> Changes, int Caret)> _undo = new();
    private const int UndoLimit = 100;
    private readonly HashSet<BookText.Book> _dirtyBooks = new(); // the books with a run that was edited
    private bool _caretActive;                                    // the reader clicked or typed: the caret is shown (before that it is nowhere)

    /// <summary>Whether the text at <see cref="Position"/> has anything to edit (<see cref="BookText.HasEditableText"/>: in a text of several Bibles read together, the verses of the editable ones can be edited).</summary>
    public bool CanEdit => Position?.Book?.BookText is { HasEditableText: true };

    private void OnIsEditingChanged(bool editing)
    {
        if (editing) BeginEdit();
        else _ = EndEditAsync();
    }

    private void BeginEdit()
    {
        if (!CanEdit)
        {
            SetCurrentValue(IsEditingProperty, false);
            return;
        }
        _dirtyBooks.Clear();
        _undo.Clear();
        _caretActive = false;
        _anchor = _caret = 0;
        _ = RebuildAsync(); // the text is made again for editing (the editable runs as they are, no hyphens), where the reader is
        Focus();
    }

    private async Task EndEditAsync()
    {
        if (_strongPopup is not null) _strongPopup.IsOpen = false;
        var dirty = _dirtyBooks.ToList();
        _dirtyBooks.Clear();
        _undo.Clear();
        _caretActive = false;
        _anchor = _caret = 0;

        foreach (var book in dirty)
        {
            string original = book.Markdown;
            try
            {
                var markdown = BookText.ToMarkdown(book.Runs);
                if (original.Contains("\r\n")) markdown = markdown.Replace("\n", "\r\n");
                book.Markdown = markdown;
                // Parsing again (to be sure that the markdown makes the text that was edited) and writing the file are work for a worker thread.
                var runs = await Task.Run(() =>
                {
                    var parsed = BookText.ParseBibleBook(book).ToList();
                    if (!string.IsNullOrEmpty(book.File))
                    {
                        // The markdown is made from the runs: what the runs do not keep is lost, so the file as it was is kept once.
                        try { if (!System.IO.File.Exists(book.File + ".bak")) System.IO.File.Copy(book.File, book.File + ".bak"); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not back up {book.File}: {ex}"); }
                        System.IO.File.WriteAllText(book.File, markdown, new System.Text.UTF8Encoding(false));
                    }
                    return parsed;
                });
                book.Runs = runs;
            }
            catch (Exception ex)
            {
                book.Markdown = original;
                System.Diagnostics.Debug.WriteLine($"Could not save {book.File}: {ex}");
            }
        }
        // The books of a combined text copy the runs that were replaced: they are made again from the new ones (so that the next edit changes the runs that are written).
        if (BookText?.Sources is { Count: > 0 })
            foreach (var combined in BookText.Books.Where(b => dirty.Any(d => d.Index == b.Index))) combined.ResetRuns();
        if (dirty.Count > 0) lock (_pieceCache) _pieceCache.Clear(); // the pieces were made from the runs that are replaced
        // The reading text again, where the reader was.
        _inlinesVersion++;
        await RebuildAsync();
    }

    /// <summary>The runs of a book were replaced (its source was saved): the text that was made from the old ones is forgotten and made again, where the reader is.</summary>
    public async Task ReloadAfterSourceChangedAsync()
    {
        lock (_pieceCache) _pieceCache.Clear();
        _inlinesVersion++;
        await RebuildAsync();
    }

    // ---- The caret, as a place in a run (the window of chapters changes under it) ----------------------

    private (BookText.Run Run, int Offset)? CaretRun()
    {
        if (!IsEditing || !_caretActive || _flat is not { } window) return null;
        foreach (var r in window.Edits)
            if (r.Start <= _caret && _caret <= r.End) return (r.Run, _caret - r.Start);
        return null;
    }

    private void RestoreCaret((BookText.Run Run, int Offset)? place)
    {
        if (place is not { } p || _flat is not { } window) return;
        foreach (var r in window.Edits)
            if (ReferenceEquals(r.Run, p.Run)) { _anchor = _caret = Math.Min(r.Start + p.Offset, r.End); return; }
        _caretActive = false;
    }

    // ---- Changing the runs ----------------------------------------------------------------------------

    /// <summary>
    /// The text of the books of the window changed (the runs in <paramref name="runs"/>): the books are marked as changed, and the chapters that the runs are in
    /// are made again (the others come from the cache) and flowed again; the lines of the other chapters are kept.
    /// </summary>
    private void RefreshEdited(IReadOnlyCollection<BookText.Run> runs)
    {
        foreach (var run in runs)
        {
            // A run of a combined text is a copy: the run it was copied from is what is written, so it gets the change, and its book is the one that changed.
            if (run.Source is { } source)
            {
                source.Text = run.Text;
                source.StrongNumber = run.StrongNumber;
            }
            if ((run.Source ?? run).Position?.Book is { } book) _dirtyBooks.Add(book);
        }
        if (_flat is not { } window || Books is not { } books) return;
        lock (_pieceCache)
            foreach (var run in runs)
            {
                if (run.Position?.Book is not { } b || books.FirstOrDefault(x => SameBook(x, b)) is not { } book) continue;
                int i = ChaptersOf(book).FindIndex(c => c.Chapter == run.Position.Chapter);
                if (i >= 0) _pieceCache.Remove((book, i));
            }
        var rebuilt = BuildWindow(MakeFlatContext(), window.Books, window.FirstBook, window.First, window.LastBook, window.Last);
        _flat = rebuilt;
        AttachFloats(rebuilt);
        _inlinesVersion++;
        MarkDirty();
        _layer.InvalidateVisual();
    }

    private void PushUndo(IEnumerable<RunState> changes)
    {
        _undo.Add((changes.ToList(), _caret));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
    }

    /// <summary>
    /// Replaces <c>[start, end)</c> of the text by <paramref name="insert"/>, with the caret behind it. Only the editable runs change: the part of the range that is in
    /// an editable run is deleted, the new text goes into the run that holds <c>start</c> (nothing happens where there is none).
    /// </summary>
    private void Replace(int start, int end, string insert)
    {
        if (_flat is not { } window) return;
        insert = insert.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '); // a verse is one line of the text: a line break is a space
        start = Math.Clamp(start, 0, window.Text.Length);
        end = Math.Clamp(end, start, window.Text.Length);

        // The run that takes the new text: the one that holds the start (at the end of a run, that run).
        RunRange? target = null;
        foreach (var r in window.Edits)
            if (r.Start <= start && start <= r.End && (end == start || r.End > start)) { target = r; break; }
        var affected = end > start ? window.Edits.Where(r => r.End > start && r.Start < end).ToList() : new List<RunRange>();
        if (affected.Count == 0 && (target is null || insert.Length == 0)) return;

        var touched = affected.Select(r => r.Run).Concat(target is { } t0 ? [t0.Run] : []).Distinct().ToList();
        PushUndo(touched.Select(run => new RunState(run, run.Text, null, false)));

        int insertAt = target is { } t1 ? start - t1.Start : 0;
        foreach (var r in affected)
        {
            var text = r.Run.Text ?? "";
            int cutEnd = Math.Min(Math.Min(end, r.End) - r.Start, text.Length), cutStart = Math.Min(Math.Max(start, r.Start) - r.Start, cutEnd);
            r.Run.Text = text.Remove(cutStart, cutEnd - cutStart);
            if (target is { } t2 && ReferenceEquals(t2.Run, r.Run))
                insertAt = insertAt > cutEnd ? insertAt - (cutEnd - cutStart) : Math.Min(insertAt, cutStart);
        }
        if (target is { } t3 && insert.Length > 0)
        {
            var text = t3.Run.Text ?? "";
            t3.Run.Text = text.Insert(Math.Clamp(insertAt, 0, text.Length), insert);
        }
        _anchor = _caret = target is not null ? start + insert.Length : start;
        RefreshEdited(touched);
    }

    private void Undo()
    {
        if (_undo.Count == 0) return;
        var (changes, caret) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        foreach (var state in changes)
            if (state.IsStrong) state.Run.StrongNumber = state.Strong; else state.Run.Text = state.Text;
        _anchor = _caret = caret;
        RefreshEdited(changes.Select(c => c.Run).ToList());
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!IsEditing || string.IsNullOrEmpty(e.Text) || FromPopup(e)) return;
        _caretActive = true;
        Replace(SelStart, SelEnd, e.Text);
        e.Handled = true;
    }

    /// <summary>Handles a key while editing; true when it was one the editor uses.</summary>
    private bool EditKey(KeyEventArgs e)
    {
        if (_flat is not { } window) return false;
        var text = window.Text;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _caretActive = true;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.A: SetSelection(0, text.Length); return true;
                case Key.C: Copy(); return true;
                case Key.X: Cut(); return true;
                case Key.V: _ = PasteAsync(); return true;
                case Key.Z: Undo(); return true;
            }
        }

        switch (e.Key)
        {
            case Key.Back:
                if (HasSelection) Replace(SelStart, SelEnd, "");
                else if (_caret > 0) Replace(StepBack(text, _caret), _caret, "");
                return true;
            case Key.Delete:
                if (HasSelection) Replace(SelStart, SelEnd, "");
                else if (_caret < text.Length) Replace(_caret, StepForward(text, _caret), "");
                return true;
            case Key.Enter: return true; // the structure of the text (verses, paragraphs) is not edited
            case Key.Left: MoveCaret(ctrl ? WordStartBefore(text, _caret) : StepBack(text, _caret), shift); return true;
            case Key.Right: MoveCaret(ctrl ? WordEndAfter(text, _caret) : StepForward(text, _caret), shift); return true;
            case Key.Up: MoveCaret(IndexOnNeighbourLine(-1), shift); return true;
            case Key.Down: MoveCaret(IndexOnNeighbourLine(1), shift); return true;
            case Key.Home: MoveCaret(ctrl ? 0 : LineStartOf(_caret), shift); return true;
            case Key.End: MoveCaret(ctrl ? text.Length : LineEndOf(_caret), shift); return true;
        }
        return false;
    }

    private void MoveCaret(int index, bool extend)
    {
        index = Math.Clamp(index, 0, _flat?.Text.Length ?? 0);
        SetSelection(extend ? _anchor : index, index);
        EnsureCaretVisible();
    }

    private static int StepBack(string text, int index) =>
        index <= 0 ? 0 : index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]) ? index - 2 : index - 1;

    private static int StepForward(string text, int index) =>
        index >= text.Length ? text.Length : index + 1 < text.Length && char.IsHighSurrogate(text[index]) && char.IsLowSurrogate(text[index + 1]) ? index + 2 : index + 1;

    private static int WordStartBefore(string text, int index)
    {
        while (index > 0 && !InWord(text[index - 1])) index--;
        return WordStartIn(text, index);
    }

    private static int WordEndAfter(string text, int index)
    {
        while (index < text.Length && !InWord(text[index])) index++;
        return WordEndIn(text, index);
    }

    private async void Cut()
    {
        if (!HasSelection || _flat is not { } window) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        int start = SelStart, end = SelEnd;
        await clipboard.SetTextAsync(window.Text[start..end].Replace("\n", Environment.NewLine));
        Replace(start, end, "");
    }

    private async Task PasteAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        var pasted = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(pasted)) Replace(SelStart, SelEnd, pasted);
    }

    // ---- The caret, on the lines that are laid out ---------------------------------------------------

    /// <summary>The line the caret at <paramref name="index"/> is on, and its distance from the start of the line (the lines of the layout that is shown).</summary>
    private (int Line, double X)? CaretPlace(int index)
    {
        if (_lines.Count == 0) return null;
        int found = -1;
        for (int i = 0; i < _lines.Count; i++)
            if (index < _lines[i].End) { found = i; break; }
        if (found < 0) found = _lines.Count - 1; // the end of the text
        var pl = _lines[found];
        if (index < pl.Start || index > pl.End) return null; // the layout is not of this text yet
        double x = 0;
        if (index > pl.Start)
        {
            var boxes = pl.Bounds(index - 1, 1);
            if (boxes.Count > 0) x = boxes[^1].Rectangle.Right;
        }
        return (found, x);
    }

    /// <summary>The caret one line up or down, as near as it can be to where it is across.</summary>
    private int IndexOnNeighbourLine(int step)
    {
        if (CaretPlace(_caret) is not var (line, x)) return _caret;
        int other = line + step;
        if (other < 0) return 0;
        if (other >= _lines.Count) return _flat?.Text.Length ?? _caret;
        var target = _lines[other];
        return IndexAt(new Point(target.Origin.X + x, target.Origin.Y + target.Line.Height / 2)) ?? _caret;
    }

    private int LineStartOf(int index) => CaretPlace(index) is var (line, _) ? _lines[line].Start : index;

    private int LineEndOf(int index)
    {
        if (CaretPlace(index) is not var (line, _)) return index;
        var pl = _lines[line];
        return pl.End - pl.Line.NewLineLength;
    }

    /// <summary>Scrolls to the column (or the place) that the caret is in, when it is not shown.</summary>
    private void EnsureCaretVisible()
    {
        if (!IsEditing || !_caretActive || CaretPlace(_caret) is not var (index, _)) return;
        var pl = _lines[index];
        if (Orientation == Orientation.Vertical)
        {
            if (!_vScrolling) return;
            if (pl.Origin.Y < _offsetY) SetOffsetY(pl.Origin.Y);
            else if (pl.Origin.Y + pl.Line.Height > _offsetY + _viewHeight) SetOffsetY(pl.Origin.Y + pl.Line.Height - _viewHeight);
        }
        else if (_scrolling && _pitch > 0)
        {
            int column = LogicalColumn(pl);
            if (column < _firstColumn || column >= _firstColumn + Math.Max(1, _visibleColumns)) ScrollToColumn(column);
        }
        _layer.InvalidateVisual();
    }

    private static readonly Pen CaretPen = new(Brushes.DodgerBlue, 1.5);

    /// <summary>Draws the caret (when nothing is selected) on its line.</summary>
    private void DrawCaret(DrawingContext context)
    {
        if (!IsEditing || !_caretActive || HasSelection || CaretPlace(_caret) is not var (index, x)) return;
        var pl = _lines[index];
        if (!IsInView(pl)) return;
        double px = pl.Origin.X + x;
        context.DrawLine(CaretPen, new Point(px, pl.Origin.Y), new Point(px, pl.Origin.Y + pl.Line.Height));
    }
}
