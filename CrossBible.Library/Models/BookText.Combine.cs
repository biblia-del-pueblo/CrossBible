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
    /// Combines texts into an interlinear text. The books, titles and the order of the verses are those of the first text; after each of its verses come the verses with
    /// the same number of the other texts (in the order they are given), each verse starting on a new line. The titles are the first text's only.
    /// <para>
    /// The books are matched by their <see cref="Book.Index"/>, the chapters by their number and the verses by theirs. A text that lacks a book, a chapter or a
    /// verse just has nothing there. The chapters and the verses that only a later text has are included, in between the others by number: after the ones with a
    /// lower number, a verse before the titles that come in front of the next verse. The titles of a chapter that the first text does not have are those of the first
    /// text that has it. A book that only a later text has is included, by its number among the others, made from the first text that has it. A book that is not a Bible book (an introduction, a dictionary) is that text's, as it is.
    /// The numbers are what is compared, unless the texts have a versification (info.json, <see cref="InstallableBook.Versification"/>) that differs from the first
    /// text's (the first one that has the book): then the verses of the later texts are given the numbers of that text's versification where that is known (see <see cref="VerseMapper"/>: the
    /// headings of the Psalms that one versification counts as verses and the other does not). A text without a versification, or the first one, is matched by number.
    /// </para>
    /// <para>
    /// Only paragraph breaks are shown (the parser makes a space of a line break of the markdown, see <see cref="ParseText"/>). A paragraph break at the end of a
    /// verse comes once, after the verses of all the texts, when any of them ends with one.
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
        // The versification that the verses of a later text are adapted to is that of the first text that has the book (the first text, for the books that it has).
        var versifications = all.Select(VerseMapper.VersificationOf).ToList();

        // The books of the first text come in its order. A book that only a later text has is put in by its number, in front of the first book with a higher number (or at the end),
        // and is made from the first text that has it: that text gives its titles and the order of its verses, like the first text does for its own books.
        var entries = new List<(Book First, int Source)>();
        foreach (var first in all[0].Books) entries.Add((first, 0));
        var missing = all.Skip(1).SelectMany((t, i) => t.Books.Select(b => (Book: b, Source: i + 1)))
            .Where(x => all[0].Books.All(b => b.Index != x.Book.Index))
            .GroupBy(x => x.Book.Index).OrderBy(g => g.Key)
            .Select(g => g.First());
        foreach (var later in missing)
        {
            int at = entries.FindIndex(e => e.First.Index > later.Book.Index);
            entries.Insert(at < 0 ? entries.Count : at, (later.Book, later.Source));
        }

        foreach (var (first, source) in entries)
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
            // The same book in each text, the one that gives the book first (a text that has no such book is not in the list; the texts before it have none).
            var parts = new List<Book> { first };
            var partMappers = new List<VerseMapper?> { null };
            for (int i = source + 1; i < all.Count; i++)
                if (all[i].Books.FirstOrDefault(b => b.Index == first.Index) is { } match)
                {
                    parts.Add(match);
                    partMappers.Add(VerseMapper.Create(versifications[i], versifications[source]));
                }
            book.RunsFactory = () => CombineRuns(book, parts, partMappers);
            combined.Books.Add(book);
        }
        return combined;
    }

    private static List<Run> CombineRuns(Book target, List<Book> parts, List<VerseMapper?> mappers)
    {
        var first = parts[0];
        var result = new List<Run>();
        if (!first.IsBible)
        {
            foreach (var run in first.Runs) result.Add(CloneRun(run, target));
            return result;
        }

        // The chapters of each text by number. The chapters of the first text come in its order; the ones that only a later text has go in between them, by number (after the
        // ones with a lower number): such a chapter is made from the first text that has it, which gives its titles.
        var chaptersOf = parts.Select(p => p.Chapters.GroupBy(c => c.Chapter).ToDictionary(g => g.Key, g => g.First())).ToList();

        // The verses of the later texts by the chapter and the number they have in the first text's versification (a text without a mapper keeps its own), in their order.
        var versesOf = new List<Dictionary<int, List<(int Verse, Run Run)>>>();
        for (int i = 1; i < parts.Count; i++)
        {
            var byChapter = new Dictionary<int, List<(int Verse, Run Run)>>();
            foreach (var chapter in chaptersOf[i].Values.OrderBy(c => c.Chapter))
                foreach (var verse in (chapter.Runs ?? Enumerable.Empty<Run>()).Where(r => r.Class == Run.Classes.Verse && r.Position is not null))
                {
                    var (mappedChapter, mappedVerse) = mappers[i] is { } mapper ? mapper.Map(first.Index, chapter.Chapter, verse.Position.Verse) : (chapter.Chapter, verse.Position.Verse);
                    if (!byChapter.TryGetValue(mappedChapter, out var list)) byChapter[mappedChapter] = list = new List<(int, Run)>();
                    list.Add((mappedVerse, verse));
                }
            versesOf.Add(byChapter);
        }

        var onlyLater = new SortedSet<int>(versesOf.SelectMany(d => d.Keys).Concat(chaptersOf.Skip(1).SelectMany(d => d.Keys)).Except(chaptersOf[0].Keys));
        // The chapter of a number: the verses that the later texts have in it, and the chapter that gives its titles (the first text's, else the first later text's own chapter of that
        // number when it is not mapped, else one without titles).
        Run ChapterNumbered(int number, Run? firstChapter, out List<List<(int Verse, Run Run)>> others)
        {
            others = versesOf.Select(d => d.TryGetValue(number, out var list) ? list : new List<(int Verse, Run Run)>()).ToList();
            if (firstChapter != null) return firstChapter;
            for (int i = 1; i < parts.Count; i++)
                if (mappers[i] == null && chaptersOf[i].TryGetValue(number, out var own)) return own;
            return new Run { Class = Run.Classes.Chapter, Chapter = number, Text = number.ToString(), Position = null!, Runs = new List<Run>() };
        }
        void AddChaptersBefore(int number)
        {
            while (onlyLater.Count > 0 && onlyLater.Min < number)
            {
                int next = onlyLater.Min;
                onlyLater.Remove(next);
                var skeleton = ChapterNumbered(next, null, out var others);
                if (others.All(list => list.Count == 0) && skeleton.Runs?.Any() != true) continue;
                result.Add(CombineChapter(skeleton, others, target));
            }
        }

        foreach (var run in first.Runs)
        {
            if (run.Class != Run.Classes.Chapter) { result.Add(CloneRun(run, target)); continue; }
            AddChaptersBefore(run.Chapter);
            var skeleton = ChapterNumbered(run.Chapter, run, out var others);
            result.Add(CombineChapter(skeleton, others, target));
        }
        AddChaptersBefore(int.MaxValue);
        return result;
    }

    /// <summary>
    /// The chapter of the combined text: the chapter <paramref name="chapter"/> that gives its titles and the order of its verses, with the verses that the later texts have in it
    /// (<paramref name="others"/>, one list per text, as (verse number in the first text's versification, the verse)). They come after the verse with their number;
    /// the ones that the first text does not have go in between, by number.
    /// Verse 0 is the heading of the chapter (a verse that the first text does not count): it comes in front of the first verse, after the titles.
    /// </summary>
    private static Run CombineChapter(Run chapter, List<List<(int Verse, Run Run)>> others, Book target)
    {
        static bool IsVerse(Run r) => r.Class == Run.Classes.Verse && r.Position is not null;
        var items = (chapter.Runs ?? Enumerable.Empty<Run>()).ToList();

        // The verses of the other texts in this chapter, by their number in the first text (all that have it, in their order).
        var otherVerses = others
            .Select(list => list.GroupBy(v => v.Verse).ToDictionary(g => g.Key, g => g.Select(v => v.Run).ToList()))
            .ToList();
        // The verses of the other texts that are the heading of the chapter in the first text, in the order of the texts.
        var headings = otherVerses.SelectMany(d => d.TryGetValue(0, out var list) ? list : new List<Run>()).ToList();

        // The verses that the first text does not have but later ones do, by number, each with its verses in the order of the texts.
        var own = new HashSet<int>(items.Where(IsVerse).Select(r => r.Position.Verse));
        var extra = new SortedDictionary<int, List<Run>>();
        foreach (var verses in otherVerses)
            foreach (var (number, list) in verses)
            {
                if (own.Contains(number) || number == 0) continue; // 0: the heading, which has its place (see AddHeadings)
                if (!extra.TryGetValue(number, out var group)) extra[number] = group = new List<Run>();
                group.AddRange(list);
            }

        var runs = new List<Run>();
        bool verseOnLine = false; // a verse comes before on the line: the next one starts a new line (a title ends its line itself)
        bool endParagraph = false; // one of the verses of the group ends with a paragraph break
        Run NewBreak(Run.Classes kind, Run verse) => new() { Class = kind, Text = "", Position = ClonePosition(verse.Position, target), Runs = Enumerable.Empty<Run>() };
        void AddVerse(Run verse)
        {
            if (verseOnLine) runs.Add(NewBreak(Run.Classes.LineBreak, verse));
            var copy = CloneRun(verse, target);
            if (copy.Position != null) copy.Position.Chapter = chapter.Chapter; // a verse that is mapped to another chapter is in this one now
            if (copy.Runs is List<Run> children)
            {
                // A paragraph break at the end of a verse belongs to the end of the verses of all the texts, not to the end of this one: it is taken off here,
                // and the group gets one (see EndGroup).
                while (children.Count > 0 && children[^1].Class == Run.Classes.Paragraph)
                {
                    endParagraph = true;
                    children.RemoveAt(children.Count - 1);
                }
            }
            runs.Add(copy);
            verseOnLine = true;
        }
        // The verses of all the texts are in; the paragraph break that they ended with (once, however many there were) comes now, and the next verse starts after it.
        void EndGroup(Run verse)
        {
            if (!endParagraph) return;
            runs.Add(NewBreak(Run.Classes.Paragraph, verse));
            verseOnLine = false;
            endParagraph = false;
        }
        // The verses that only later texts have, with a number lower than `number`, are put in here (the lowest first).
        void AddExtraBefore(int number)
        {
            while (extra.Count > 0 && extra.Keys.First() < number)
            {
                int next = extra.Keys.First();
                var group = extra[next];
                extra.Remove(next);
                foreach (var verse in group) AddVerse(verse);
                EndGroup(group[0]);
            }
        }

        // The verses of the other texts that are the heading of the chapter in the first text come in front of its first verse (after its titles), once.
        void AddHeadings()
        {
            if (headings.Count == 0) return;
            var group = headings.ToList();
            headings.Clear();
            foreach (var verse in group) AddVerse(verse);
            EndGroup(group[0]);
        }

        // The number of the verse that is next at or after each run: a verse that only a later text has goes before the titles that come in front of the verse that follows it
        // (they are the beginning of that verse's part), not between them and it.
        var nextVerse = new int[items.Count + 1];
        nextVerse[items.Count] = int.MaxValue;
        for (int i = items.Count - 1; i >= 0; i--) nextVerse[i] = IsVerse(items[i]) ? items[i].Position.Verse : nextVerse[i + 1];

        for (int i = 0; i < items.Count; i++)
        {
            var run = items[i];
            if (i == 0 || IsVerse(items[i - 1])) AddExtraBefore(nextVerse[i]); // the start of a verse, or of the titles in front of one
            if (!IsVerse(run))
            {
                runs.Add(CloneRun(run, target));
                verseOnLine = false;
                continue;
            }
            AddHeadings();
            AddVerse(run);
            foreach (var verses in otherVerses)
                if (verses.TryGetValue(run.Position.Verse, out var other)) foreach (var verse in other) AddVerse(verse);
            EndGroup(run);
        }
        AddExtraBefore(int.MaxValue); // the verses after the last one of the first text
        AddHeadings();                // a chapter of the first text without verses

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
