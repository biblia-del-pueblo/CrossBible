using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CrossBible.Library;

// An interlinear text: the verses of several texts one after another.
public partial class BookText
{
    /// <summary>The texts that this one was made of by <see cref="Combine"/> (the first is the one the titles are from); null for a text that is not combined.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<BookText>? Sources { get; private set; }

    /// <summary>The <see cref="Id"/>s that are saved as the Bible that is open (<see cref="Settings.CurrentBible"/>): its own, or for a combined text those of the texts it is made of.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> PersistedIds => Sources is { Count: > 0 } sources ? sources.Select(s => s.Id).ToList() : new[] { Id };

    /// <summary>Reads the books from the folder of the text when it has none yet (an installed Bible is not read until it is needed). The books belong to this text.</summary>
    internal async Task EnsureBooksAsync()
    {
        if (Books.Count > 0) return;
        var loaded = await Task.Run(() => OpenFrom(ConfigPath));
        foreach (var book in loaded.Books) book.BookText = this;
        Books = loaded.Books;
    }

    /// <summary>
    /// Combines texts into an interlinear text. The books, chapters, titles and verses are those of the first text; after each of its verses come the verses with
    /// the same number of the other texts (in the order they are given), each verse starting on a new line. The titles are the first text's only.
    /// <para>
    /// The books are matched by their <see cref="Book.Index"/>, the chapters by their number and the verses by theirs. A text that lacks a book, a chapter or a
    /// verse just has nothing there, and the verses of a text that the first text does not have are left out. A book that is not a Bible book (an introduction,
    /// a dictionary) is the first text's, as it is.
    /// </para>
    /// <para>
    /// The runs are copies that belong to the books of the combined text (so the places of the verses are those of the combined text, which is what the
    /// navigation needs); the texts that are combined are not changed. The books are made when they are first used, like a text that is read from files,
    /// not all at once.
    /// </para>
    /// </summary>
    public static BookText Combine(params IEnumerable<BookText> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var all = texts.Where(t => t is not null).ToList();
        if (all.Count == 0) throw new ArgumentException("There must be at least one text to combine.", nameof(texts));

        var combined = new BookText
        {
            Name = string.Join(" / ", all.Select(t => t.Name).Where(name => !string.IsNullOrEmpty(name))),
            HasStrongs = all.Any(t => t.HasStrongs),
            Sources = all,
        };
        foreach (var first in all[0].Books)
        {
            var book = new Book(combined)
            {
                Name = first.Name,
                Abbrevation = first.Abbrevation,
                Index = first.Index,
                Class = first.Class,
                Id = first.Id,
                Description = first.Description,
                File = first.File, // only so that Markdown has something to read: it is the first text's
                IsBible = first.IsBible,
            };
            // The same book in each text, the first text's own first (a text that has no such book is not in the list).
            var parts = new List<Book> { first };
            foreach (var other in all.Skip(1))
                if (other.Books.FirstOrDefault(b => b.Index == first.Index) is { } match) parts.Add(match);
            book.RunsFactory = () => CombineRuns(book, parts);
            combined.Books.Add(book);
        }
        return combined;
    }

    private static List<Run> CombineRuns(Book target, List<Book> parts)
    {
        var first = parts[0];
        var result = new List<Run>();
        foreach (var run in first.Runs)
            result.Add(run.Class == Run.Classes.Chapter && first.IsBible ? CombineChapter(run, parts.Skip(1), target) : CloneRun(run, target));
        return result;
    }

    private static Run CombineChapter(Run chapter, IEnumerable<Book> others, Book target)
    {
        // The verses of the other texts in this chapter, by their number (the first one, if a number is there twice).
        var otherVerses = others
            .Select(book => book.Chapters.FirstOrDefault(c => c.Chapter == chapter.Chapter)?.Runs?
                .Where(r => r.Class == Run.Classes.Verse && r.Position is not null)
                .GroupBy(r => r.Position.Verse)
                .ToDictionary(g => g.Key, g => g.First()))
            .Where(verses => verses is not null)
            .ToList();

        var runs = new List<Run>();
        bool verseOnLine = false; // a verse comes before on the line: the next one starts a new line (a title ends its line itself)
        int endBreak = 0;         // what the verses of the group end with: 0 nothing, 1 a line break, 2 a paragraph break
        Run NewBreak(Run.Classes kind, Run verse) => new() { Class = kind, Text = "", Position = ClonePosition(verse.Position, target), Runs = Enumerable.Empty<Run>() };
        void AddVerse(Run verse)
        {
            if (verseOnLine) runs.Add(NewBreak(Run.Classes.LineBreak, verse));
            var copy = CloneRun(verse, target);
            // A break at the end of a verse belongs to the end of the verses of all the texts, not to the end of this one: it is taken off here, and the group
            // gets one (see EndGroup). A paragraph break is more than a line break, so it is the one that counts when the texts differ.
            if (copy.Runs is List<Run> children)
                while (children.Count > 0 && children[^1].Class is Run.Classes.Paragraph or Run.Classes.LineBreak)
                {
                    endBreak = Math.Max(endBreak, children[^1].Class == Run.Classes.Paragraph ? 2 : 1);
                    children.RemoveAt(children.Count - 1);
                }
            runs.Add(copy);
            verseOnLine = true;
        }
        // The verses of all the texts are in; the break that they ended with (once, however many there were) comes now, and the next verse starts after it.
        void EndGroup(Run verse)
        {
            if (endBreak == 0) return;
            runs.Add(NewBreak(endBreak == 2 ? Run.Classes.Paragraph : Run.Classes.LineBreak, verse));
            verseOnLine = false;
            endBreak = 0;
        }
        foreach (var run in chapter.Runs ?? Enumerable.Empty<Run>())
        {
            if (run.Class != Run.Classes.Verse || run.Position is null)
            {
                runs.Add(CloneRun(run, target));
                verseOnLine = false;
                continue;
            }
            AddVerse(run);
            foreach (var verses in otherVerses)
                if (verses!.TryGetValue(run.Position.Verse, out var other)) AddVerse(other);
            EndGroup(run);
        }

        return new Run
        {
            Class = Run.Classes.Chapter,
            Chapter = chapter.Chapter,
            Position = ClonePosition(chapter.Position, target),
            Text = chapter.Text,
            Runs = runs,
        };
    }

    /// <summary>A copy of a run (and of the runs in it) whose places belong to <paramref name="target"/>.</summary>
    private static Run CloneRun(Run run, Book target) => new()
    {
        Class = run.Class,
        Text = run.Text,
        Url = run.Url,
        StrongNumber = run.StrongNumber,
        Lemma = run.Lemma,
        Morph = run.Morph,
        Position = ClonePosition(run.Position, target),
        Reference = run.Reference, // where a link leads: not a place in this text
        MarkerColor = run.MarkerColor,
        Chapter = run.Chapter,
        First = run.First,
        TitleLevel = run.TitleLevel,
        Runs = run.Runs?.Select(child => CloneRun(child, target)).ToList()!,
    };

    private static VersePosition ClonePosition(VersePosition? position, Book target) => position is null ? null! : new VersePosition
    {
        Book = target,
        Chapter = position.Chapter,
        Title = position.Title,
        TitleIndex = position.TitleIndex,
        Verse = position.Verse,
    };
}
