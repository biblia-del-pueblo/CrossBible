using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using BibleMarkdown;
using Microsoft.VisualBasic;
using Svg.FilterEffects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace CrossBible.Library;

// partial: the regular expressions of the parsing are made at build time ([GeneratedRegex]). They are ordinary C# then, which the
// ReadyToRun compiler precompiles, instead of being built (and for the Compiled ones, compiled) when the first book is read.
// The Bibles in the settings are of different kinds: each is written with its kind ("$type") and read as that kind.
[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BookText), "BookText")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(InstallableBook), "InstallableBook")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CrossWireBook), "CrossWireBook")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CrossWireDictionary), "CrossWireDictionary")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(eBible), "eBible")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BibliaLibreBible), "BibliaLibreBible")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(BibliaLibreDictionary), "BibliaLibreDictionary")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CrossBible.Library.Downloads.BookArchive), "BookArchive")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(CrossBible.Library.Downloads.BookArchiveDictionary), "BookArchiveDictionary")]
public partial class BookText : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>Raised when <see cref="HasStrongs"/> changes (possibly on the thread that reads a book).</summary>
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Whether the text has Strong's numbers; found out when a book that has them is read.</summary>
    public bool HasStrongs
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(HasStrongs)));
        }
    } = false;

    public bool IsBible => Books.Any(book => book.IsBible);
    internal static readonly Dictionary<int, Book.Classes> ClassByIndex = new Dictionary<int, Book.Classes>()
    {
        {0, Book.Classes.Prefix },
        {1, Book.Classes.Pentateuch },
        {2, Book.Classes.Pentateuch },
        {3, Book.Classes.Pentateuch },
        {4, Book.Classes.Pentateuch },
        {5, Book.Classes.Pentateuch },
        {6, Book.Classes.Historical },
        {7, Book.Classes.Historical },
        {8, Book.Classes.Historical },
        {9, Book.Classes.Historical },
        {10, Book.Classes.Historical },
        {11, Book.Classes.Historical },
        {12, Book.Classes.Historical },
        {13, Book.Classes.Historical },
        {14, Book.Classes.Historical },
        {15, Book.Classes.Historical },
        {16, Book.Classes.Historical },
        {17, Book.Classes.Historical },
        {18, Book.Classes.PoetryAndWisdom },
        {19, Book.Classes.PoetryAndWisdom },
        {20, Book.Classes.PoetryAndWisdom },
        {21, Book.Classes.PoetryAndWisdom },
        {22, Book.Classes.PoetryAndWisdom },
        {23, Book.Classes.MajorProphets },
        {24, Book.Classes.MajorProphets },
        {25, Book.Classes.MajorProphets },
        {26, Book.Classes.MajorProphets },
        {27, Book.Classes.MajorProphets },
        {28, Book.Classes.MinorProphets },
        {29, Book.Classes.MinorProphets },
        {30, Book.Classes.MinorProphets },
        {31, Book.Classes.MinorProphets },
        {32, Book.Classes.MinorProphets },
        {33, Book.Classes.MinorProphets },
        {34, Book.Classes.MinorProphets },
        {35, Book.Classes.MinorProphets },
        {36, Book.Classes.MinorProphets },
        {37, Book.Classes.MinorProphets },
        {38, Book.Classes.MinorProphets },
        {39, Book.Classes.MinorProphets },
        {40, Book.Classes.Gospels },
        {41, Book.Classes.Gospels },
        {42, Book.Classes.Gospels },
        {43, Book.Classes.Gospels },
        {44, Book.Classes.Acts },
        {45, Book.Classes.Epistles },
        {46, Book.Classes.Epistles },
        {47, Book.Classes.Epistles },
        {48, Book.Classes.Epistles },
        {49, Book.Classes.Epistles },
        {50, Book.Classes.Epistles },
        {51, Book.Classes.Epistles },
        {52, Book.Classes.Epistles },
        {53, Book.Classes.Epistles },
        {54, Book.Classes.Epistles },
        {55, Book.Classes.Epistles },
        {56, Book.Classes.Epistles },
        {57, Book.Classes.Epistles },
        {58, Book.Classes.Epistles },
        {59, Book.Classes.Epistles },
        {60, Book.Classes.Epistles },
        {61, Book.Classes.Epistles },
        {62, Book.Classes.Epistles },
        {63, Book.Classes.Epistles },
        {64, Book.Classes.Epistles },
        {65, Book.Classes.Epistles },
        {66, Book.Classes.Revelation }
    };
    internal static readonly Dictionary<string, Book.Classes> ClassById = new Dictionary<string, Book.Classes>()
    {
        {"Gen", Book.Classes.Pentateuch },
        {"Exod", Book.Classes.Pentateuch },
        {"Lev", Book.Classes.Pentateuch },
        {"Num", Book.Classes.Pentateuch },
        {"Deut", Book.Classes.Pentateuch },
        {"Josh", Book.Classes.Historical },
        {"Judg", Book.Classes.Historical },
        {"Ruth", Book.Classes.Historical },
        {"1Sam", Book.Classes.Historical },
        {"2Sam", Book.Classes.Historical },
        {"1Kgs", Book.Classes.Historical },
        {"2Kgs", Book.Classes.Historical },
        {"1Chr", Book.Classes.Historical },
        {"2Chr", Book.Classes.Historical },
        {"Ezra", Book.Classes.Historical },
        {"Neh", Book.Classes.Historical },
        {"Esth", Book.Classes.Historical },
        {"Job", Book.Classes.PoetryAndWisdom },
        {"Ps", Book.Classes.PoetryAndWisdom },
        {"Prov", Book.Classes.PoetryAndWisdom },
        {"Eccl", Book.Classes.PoetryAndWisdom },
        {"Song", Book.Classes.PoetryAndWisdom },
        {"Isa", Book.Classes.MajorProphets },
        {"Jer", Book.Classes.MajorProphets },
        {"Lam", Book.Classes.MajorProphets },
        {"Ez", Book.Classes.MajorProphets },
        {"Dan", Book.Classes.MajorProphets },
        {"Hos", Book.Classes.MinorProphets },
        {"Joel", Book.Classes.MinorProphets },
        {"Amos", Book.Classes.MinorProphets },
        {"Obad", Book.Classes.MinorProphets },
        {"Jonah", Book.Classes.MinorProphets },
        {"Mic", Book.Classes.MinorProphets },
        {"Nah", Book.Classes.MinorProphets },
        {"Hab", Book.Classes.MinorProphets },
        {"Zeph", Book.Classes.MinorProphets },
        {"Hag", Book.Classes.MinorProphets },
        {"Zech", Book.Classes.MinorProphets },
        {"Mal", Book.Classes.MinorProphets },
        {"Matt", Book.Classes.Gospels },
        {"Mark", Book.Classes.Gospels },
        {"Luke", Book.Classes.Gospels },
        {"John", Book.Classes.Gospels },
        {"Acts", Book.Classes.Acts },
        {"Rom", Book.Classes.Epistles },
        {"1Cor", Book.Classes.Epistles },
        {"2Cor", Book.Classes.Epistles },
        {"Gal", Book.Classes.Epistles },
        {"Eph", Book.Classes.Epistles },
        {"Phil", Book.Classes.Epistles },
        {"Col", Book.Classes.Epistles },
        {"1Thess", Book.Classes.Epistles },
        {"2Thess", Book.Classes.Epistles },
        {"1Tim", Book.Classes.Epistles },
        {"2Tim", Book.Classes.Epistles },
        {"Titus", Book.Classes.Epistles },
        {"Phlm", Book.Classes.Epistles },
        {"Heb", Book.Classes.Epistles },
        {"Jas", Book.Classes.Epistles },
        {"1Pet", Book.Classes.Epistles },
        {"2Pet", Book.Classes.Epistles },
        {"1John", Book.Classes.Epistles },
        {"2John", Book.Classes.Epistles },
        {"3John", Book.Classes.Epistles },
        {"Jude", Book.Classes.Epistles },
        {"Rev", Book.Classes.Revelation },
        {"Jdt", Book.Classes.Apocrypha },
        {"Wis", Book.Classes.Apocrypha },
        {"Tob", Book.Classes.Apocrypha },
        {"Sir", Book.Classes.Apocrypha },
        {"Bar", Book.Classes.Apocrypha },
        {"1Macc", Book.Classes.Apocrypha },
        {"2Macc", Book.Classes.Apocrypha },
        {"AddDan", Book.Classes.Apocrypha },
        {"AddEsth", Book.Classes.Apocrypha },
        {"PrMan", Book.Classes.Apocrypha },
        {"3Macc", Book.Classes.Apocrypha },
        {"4Macc", Book.Classes.Apocrypha },
        {"EpJer", Book.Classes.Apocrypha },
        {"1Esd", Book.Classes.Apocrypha },
        {"2Esd", Book.Classes.Apocrypha },
        {"OdesSol", Book.Classes.Apocrypha },
        {"PssSol", Book.Classes.Apocrypha },
        {"Lao", Book.Classes.Apocrypha },
        {"1En", Book.Classes.Apocrypha },
        {"Jub", Book.Classes.Apocrypha },
        {"Sus", Book.Classes.Apocrypha },
        {"Bel", Book.Classes.Apocrypha }
    };

    public class Book
    {
        public enum Classes
        {
            Prefix,
            Pentateuch,
            Historical,
            PoetryAndWisdom,
            MajorProphets,
            MinorProphets,
            Gospels,
            Acts,
            Epistles,
            Revelation,
            Apocrypha,
            Appendix
        }
        public BookText BookText { get; set; }
        public Book(BookText bible) { BookText = bible; } 
        public string Name { get; set; }
        public string Abbrevation { get; set; }
        public int Index { get; set; }
        public Classes Class { get; set; }
        public string Id { get; set; }
        public string Description { get; set; }
        public string File { get; set; }
        public string Markdown {
            get
            {
                if (field != null) return field;
                field = System.IO.File.ReadAllText(File, System.Text.Encoding.UTF8);
                if (Environment.NewLine != "\r\n") field = field.Replace("\r\n", Environment.NewLine);
                return field;
            }
            set => field = value;
        }
        bool? isBible = null;
        public bool IsBible => isBible ??= BookText.IsBibleRegex().IsMatch(Markdown);
        private List<Run>? _runs;
        private readonly object _parseLock = new();
        /// <summary>The parsed text. Parsed on first use, once: a second thread that asks while it is parsed (the reader caught up with the preload) waits for it.</summary>
        public List<Run> Runs {
            get
            {
                if (_runs is { } parsed) return parsed;
                lock (_parseLock) return _runs ??= BookText.ParseBibleBook(this).ToList();
            }
            set { _runs = value; _chapters = null; }
        }
        private List<Run>? _chapters;
        /// <summary>The chapters of the book (its runs of the class chapter); none for a text that is not a Bible book.</summary>
        public List<Run> Chapters => _chapters ??= Runs.Where(r => r.Class == Run.Classes.Chapter).ToList();
    }
    public class VersePosition
    {
        public Book Book { get; set; }
        public int Chapter { get; set; }
        public Run Title { get; set; }
        public int TitleIndex { get; set; }
        public int Verse { get; set; }

        public VersePosition Clone => new VersePosition()
        {
            Book = Book,
            Chapter = Chapter,
            Title = Title,
            Verse = Verse
        };
    }

    public class Run
    {
        public enum Classes
        {
            Text,
            Italic,
            Bold,
            SmallCaps,
            WordWithStrong,
            WordsOfJesus,
            Greek,
            Hebrew,
            Arabic,
            Link,
            Title,
            Book,
            Chapter,
            Verse,
            Footnote,
            Paragraph,
            LineBreak
        }
        public Classes Class { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
        public IEnumerable<Run> Runs { get; set; }
        /// <summary>The Strong's numbers of a <see cref="Classes.WordWithStrong"/> run, separated by spaces ("H7225 G746").</summary>
        public string? StrongNumber { get; set; }
        /// <summary>The lemma of a <see cref="Classes.WordWithStrong"/> run, if the text gives one.</summary>
        public string? Lemma { get; set; }
        /// <summary>The morphology code of a <see cref="Classes.WordWithStrong"/> run, if the text gives one.</summary>
        public string? Morph { get; set; }
        public VersePosition Position { get; set; }
        public VersePosition Reference { get; set; }
        public Color MarkerColor { get; set; }
        public int Chapter { get; set; }
        public Run Parent { get; private set; }
        public bool First { get; set; }
        public int TitleLevel { get; set; }
        IEnumerable<Run> Append(IEnumerable<Run> children)
        {
            yield return this;
            foreach (var run in children)
            {
                if (run.Parent == null) run.Parent = this;
                yield return run;
            }
        }
        public IEnumerable<Run> All => Runs
            .SelectMany(run => run.Append(run.All));
    }
    
    // Footnotes ^[...], small caps [...]{.smallcaps}, words of Jesus [...]{.wj} and links [...](url) may contain
    // nested [ and ]: a balancing group counts them (a "[" pushes onto the stack c, a "]" pops, and the construct only
    // ends at the "]" that finds the stack empty).
    // A backslash escapes the next character (\[ \] \* \^ \\, as the importers write them): such a pair is text, not markup.
    const string Escaped = @"\\[\[\]*^\\]";
    const string Balanced = @"(?>\\[\s\S]|\[(?<c>)|[^\[\]\\]+|\](?<-c>)|\\)*(?(c)(?!))";
    const string SmallCapsEnd = @"\]\{\.smallcaps\}";
    const string WordsOfJesusEnd = @"\]\{\.wj\}";
    const string StrongsEnd = @"\]\{" + StrongsExtendedAttributes + @"\}";
    const string LinkEnd = @"\]\([a-zA-Z]+://.*?\)";
    // Strong's numbers in the USFM style of Creator.UsfmStrongs: a word followed by "_G1234" / "_H7225"
    // (Strong's number) also joins the words connected with _ that has one number at the end:
    // "In_the_beginning_H7225". Words with a "_" but no such attribute are plain text.
    internal const string StrongsAttribute = @"_(?<snumber>[GH][0-9]+)";
    // key="value" pairs, e.g. strong="G1234" lemma="..." x-morph="...", with or without spaces after the "{" (values have no quotes inside).
    internal const string StrongsExtendedAttributes = @"(?:\s*(?:z-|x-)?(?:strongs?|morph|lemma)\s*=\s*""[^""]*"")+\s*";
    // The atomic group (?>...) keeps a "[text]" that is not followed by attributes from backtracking exponentially.
    internal const string StrongsExtendedWord = @"\[(?<secontent>(?>[^\[\]]+|(?<open>\[)|(?<-open>\]))*(?(open)(?!)))\]\{(?<seattr>" + StrongsExtendedAttributes + @")\}";

    internal const string StrongsWord = @"(?<![\p{L}\p{M}_])(?<sword>[\p{L}\p{M}]+(?:_[\p{L}\p{M}]+)*)" + StrongsAttribute;
    // A verse marker inside a text: "@" and a number, after a space or newline (or the start) and followed by a space.
    const string VerseMarker = @"(?<![^ \n])@[0-9]+(?=[ \t])";
    // A title inside a text: one or more "#" at the start of a line, a space and the text up to the end of the line.
    const string TitleStart = @"(?<![^\n])#+ ";
    // The line and paragraph breaks of Pandoc's markdown: a backslash at the end of a line, or two or more spaces, make a line break (poetry has
    // one for each of its lines), and an empty line makes a new paragraph; a single line ending is only a space. The spaces around a break
    // belong to it. A backslash that is escaped (\\) is a pair of the text (and no break), which Escaped takes before this is looked at.
    const string HardBreak = @"(?:[ \t]*\\(?:\r?\n|\z)|[ \t]{2,}\r?\n)[ \t]*";
    const string ParagraphBreak = @"[ \t]*(?:\r?\n[ \t]*){2,}";
    // The plain text ends at the true end of the text (\z, not $: $ also matches in front of a final line break, which would then be lost, and with
    // it the only space between a verse and the number of the next one in a file with LF line breaks, as the phone writes them).
    const string TextPartsPattern =
        @"(?<![^ \n])@(?<verse>[0-9]+)(?=[ \t])|" +
        @"(?<![^\n])(?<hashes>#+) (?<title>[^\r\n]*)(?:\r?\n|$)|" +
        "(?<paragraph>" + ParagraphBreak + ")|(?<linebreak>" + HardBreak + ")|" +
        @"\*\*(?<bold>.*?)\*\*|\*(?<italic>.*?)\*|" +
        @"\^\[(?<footnote>" + Balanced + @")\]|" +
        @"\[(?<smallcaps>" + Balanced + @")" + SmallCapsEnd + "|" +
        @"\[(?<wordsofjesus>" + Balanced + @")" + WordsOfJesusEnd + "|" +
        @"\[(?<link>" + Balanced + @")\]\((?<linkurl>[a-zA-Z]+://.*?)\)|" +
        "(?<strongs>" + StrongsWord + "|" + StrongsExtendedWord + ")|" +
        @"(?<normal>(?:" + Escaped + @"|.)*?(?=" + VerseMarker + "|" + TitleStart + "|" + ParagraphBreak + "|" + HardBreak + "|" + StrongsWord + @"|" + StrongsExtendedWord + @"|\*\*.*?\*\*|\*.*?\*|\^\[" + Balanced + @"\]|" +
        @"\[" + Balanced + SmallCapsEnd + @"|\[" + Balanced + WordsOfJesusEnd + @"|\[" + Balanced + LinkEnd + @"|\z))";

    [GeneratedRegex(TextPartsPattern, RegexOptions.Singleline)]
    private static partial Regex TextParts();

    [GeneratedRegex(@"\\([\[\]*^\\])")]
    private static partial Regex EscapedCharacter();

    /// <summary>Drops the backslash of an escaped <c>[ ] * ^ \</c>.</summary>
    static string Unescape(string text) => text.Contains('\\') ? EscapedCharacter().Replace(text, "$1") : text;

    public static IEnumerable<Run> ParseText(string text, VersePosition position)
    {
        int titleIndex = 1;
        foreach (Match m in TextParts().Matches(text))
        {
            if (m.Value == "") continue;

            if (m.Groups["verse"].Success)
            {
                position = position.Clone;
                position.Verse = int.Parse(m.Groups["verse"].Value);
                yield return new Run
                {
                    Class = Run.Classes.Verse,
                    Text = "",
                    Position = position,
                    Runs = Enumerable.Empty<Run>()
                };
            }
            else if (m.Groups["paragraph"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Paragraph,
                    Text = "",
                    Position = position,
                    Runs = Enumerable.Empty<Run>()
                };
            else if (m.Groups["linebreak"].Success)
                yield return new Run
                {
                    Class = Run.Classes.LineBreak,
                    Text = "",
                    Position = position,
                    Runs = Enumerable.Empty<Run>()
                };
            else if (m.Groups["title"].Success)
            {
                position = position.Clone;
                position.TitleIndex = titleIndex++;
                var title = new Run
                {
                    Class = Run.Classes.Title,
                    Text = m.Groups["title"].Value,
                    TitleLevel = m.Groups["hashes"].Length,
                    Position = position,
                    Runs = ParseText(m.Groups["title"].Value, position).ToList()
                };
                position.Title = title;
                yield return title;
            }
            else if (m.Groups["bold"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Bold,
                    Text = m.Groups["bold"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["bold"].Value, position).ToList()
                };
            else if (m.Groups["italic"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Italic,
                    Text = m.Groups["italic"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["italic"].Value, position).ToList()
                };
            else if (m.Groups["footnote"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Footnote,
                    Text = m.Groups["footnote"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["footnote"].Value, position).ToList()
                };
            else if (m.Groups["smallcaps"].Success)
                yield return new Run
                {
                    Class = Run.Classes.SmallCaps,
                    Text = m.Groups["smallcaps"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["smallcaps"].Value, position).ToList()
                };
            else if (m.Groups["link"].Success)
                yield return new Run
                {
                    Class = Run.Classes.Link,
                    Text = m.Groups["link"].Value,
                    Url = m.Groups["linkurl"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["link"].Value, position).ToList()
                };
            else if (m.Groups["wordsofjesus"].Success)
                yield return new Run
                {
                    Class = Run.Classes.WordsOfJesus,
                    Text = m.Groups["wordsofjesus"].Value,
                    Position = position,
                    Runs = ParseText(m.Groups["wordsofjesus"].Value, position).ToList()
                };
            else if (m.Groups["strongs"].Success)
                yield return ParseStrongsWord(m.Groups["strongs"].Value, position, m);
            else if (m.Groups["normal"].Success)
                foreach (var (part, partClass) in SplitScripts(Unescape(m.Groups["normal"].Value)))
                    yield return new Run
                    {
                        Class = partClass,
                        Text = part,
                        Position = position,
                        Runs = Enumerable.Empty<Run>()
                    };
        }
    }

    [GeneratedRegex(@"(?<param>(?:z-|x-)?(?<sparam>strongs?|morph|lemma))\s*=\s*""(?<value>[^""]*)""")]
    private static partial Regex StrongsAttributeToken();

    const string StrongsExtendedWordPattern = StrongsWord + "|" + StrongsExtendedWord;

    [GeneratedRegex(StrongsExtendedWordPattern, RegexOptions.Singleline)]
    private static partial Regex StrongsExtendedWordRegex();

    /// <summary>
    /// A word (or phrase) with Strong's attributes, e.g. <c>beginning_H7225_&amp;lemma_%morph</c>: the run's text is the
    /// word without the attributes (other "_" become spaces), the numbers, lemma and morphology are kept on the run.
    /// </summary>
    static Run ParseStrongsWord(string word, VersePosition position, Match m = null)
    {
        if (position?.Book?.BookText is { } booktext) booktext.HasStrongs = true;
        var tokens = m ?? StrongsExtendedWordRegex().Match(word);
        var text = m.Groups["sword"].Success ?
            tokens.Groups["sword"].Value.Replace('_', ' ') :
            tokens.Groups["secontent"].Value;
        var numgroup = m.Groups["snumber"];
        string? numbers = null, lemma = null, morph = null;
        if (numgroup.Success) numbers = numgroup.Value;
        else
        {
            var seattr = m.Groups["seattr"].Value;
            var parameters = StrongsAttributeToken().Matches(seattr).OfType<Match>();
            foreach (var par in parameters)
            {
                var val = par.Groups["value"].Value;
                switch (par.Groups["sparam"].Value)
                {
                    case "strong" or "strongs": numbers = val.Replace(',', ' '); break;
                    case "lemma": lemma = val; break;
                    case "morph": morph = val; break;
                }
            }
        }
        return new Run
        {
            Class = Run.Classes.WordWithStrong,
            Text = text,
            StrongNumber = numbers,
            Lemma = lemma,
            Morph = morph,
            Position = position,
            // The word itself may be Greek or Hebrew: it keeps its font like any other text.
            Runs = SplitScripts(text).Select(part => new Run
            {
                Class = part.Class,
                Text = part.Text,
                Position = position,
                Runs = Enumerable.Empty<Run>()
            }).ToList()
        };
    }

    // Greek and Coptic (U+0370-03FF), Greek Extended / polytonic (U+1F00-1FFF); Hebrew (U+0590-05FF), Hebrew
    // presentation forms (U+FB1D-FB4F).
    static Run.Classes ScriptClassOf(char ch) => ch switch
    {
        >= 'Ͱ' and <= 'Ͽ' or >= 'ἀ' and <= '῿' => Run.Classes.Greek,
        >= '֐' and <= '׿' or >= 'יִ' and <= 'ﭏ' => Run.Classes.Hebrew,
        _ => Run.Classes.Text,
    };

    static bool IsCombiningMark(char ch) =>
        char.GetUnicodeCategory(ch) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark;

    /// <summary>
    /// Splits plain text into pieces of <see cref="Run.Classes.Text"/>, <see cref="Run.Classes.Greek"/> and
    /// <see cref="Run.Classes.Hebrew"/>. Only whole words are Greek or Hebrew: a piece holds the letters (with their
    /// accents and vowel points) of one word, never a space, punctuation or a digit. Everything else, and every letter
    /// of another script, is <see cref="Run.Classes.Text"/>. Text without Greek or Hebrew comes back as one piece.
    /// </summary>
    public static IEnumerable<(string Text, Run.Classes Class)> SplitScripts(string text)
    {
        var current = Run.Classes.Text;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var kind = ScriptClassOf(ch);
            // Only letters count as Greek/Hebrew (the blocks also hold punctuation such as the Greek question mark
            // and the Hebrew maqaf); combining marks stay with the letter they sit on, in whatever piece that is.
            if (IsCombiningMark(ch)) continue;
            if (kind != Run.Classes.Text && !char.IsLetter(ch)) kind = Run.Classes.Text;
            if (kind == current) continue;
            if (i > start) yield return (text[start..i], current);
            start = i;
            current = kind;
        }
        if (start < text.Length || text.Length == 0) yield return (text[start..], current);
    }
    static IEnumerable<Run> ParseVerse(Match verse, VersePosition position)
    {
        if (verse.Groups["title"].Success)
        {
            var titleText = verse.Groups["title"].Value;
            yield return new Run
            {
                Class = Run.Classes.Title,
                Text = titleText,
                Position = position,
                Runs = ParseText(titleText, position).ToList()
            };
        }
        else
        {
            var verseText = verse.Groups["versebody"].Value;
            int verseNumber = int.Parse(verse.Groups["verseno"].Value);
            position = new VersePosition { Book = position.Book, Chapter = position.Chapter, Verse = verseNumber };
            var verseRun = new Run
            {
                Class = Run.Classes.Verse,
                Text = verseText,
                Position = position,
                Runs = ParseText(verseText, position).ToList()
            };
            yield return verseRun;
        }

    }
    // A text with verses (and at least one "@1"), and no heading of its own: a Bible book and not a plain text.
    [GeneratedRegex(@"\A(?=[\s\S]*@\d)(?![\s\S]*^#[ \t]+(?!\d+[ \t]*\r?$))", RegexOptions.Multiline)]
    private static partial Regex IsBibleRegex();

    // The titles and the verses of a chapter.
    [GeneratedRegex(@"((?<=^|\n)##\s+(?<title>.*?)\r?\n)|@(?<verseno>[0-9]+)(?=\s)(?<versebody>.*?)(?=@[0-9]+\s+|\r?\n#|$)", RegexOptions.Singleline)]
    private static partial Regex ChapterParts();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComments();

    [GeneratedRegex(@"(?<!:)//.*?\r?\n", RegexOptions.Singleline)]
    private static partial Regex LineComments();

    // ^a^ text ^a^[footnote]: the footnote comes in front of the text it belongs to (the balanced brackets are the footnote's).
    [GeneratedRegex(@"\^(?<mark>[a-zA-Z]+)\^(?!\[)(?<text>.*?)(?:(?<own>^)?\^\k<mark>(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\]))[ \t]*(?(own)\r?\n?)", RegexOptions.Singleline)]
    private static partial Regex Footnotes();

    [GeneratedRegex(@"(^|\n)#\s+(?<chno>[0-9]+)\s*\r?\n(?<chbody>.*?)(?=\n#\s+[0-9]+\s*\r?\n|$)", RegexOptions.Singleline)]
    private static partial Regex Chapters();

    static IEnumerable<Run> ParseChapter(Match chapter, VersePosition position)
    {
        var chapterText = chapter.Groups["chbody"].Value;
        var chapterNumber = int.Parse(chapter.Groups["chno"].Value);
        position = new VersePosition { Book = position.Book, Chapter = chapterNumber, Verse = 0 };
        yield return new Run
        {
            Class = Run.Classes.Chapter,
            Chapter = chapterNumber,
            Position = position,
            Text = chapterText,
            Runs = ChapterParts().Matches(chapterText)
                .SelectMany(verse => ParseVerse(verse, position))
                .ToList()
        };
    }

    /// <summary>
    /// Parses a text that is not a Bible (a book, a dictionary entry): <see cref="ParseText"/>, with the first title
    /// marked as <see cref="Run.First"/>, so it is shown without the blank lines a title gets before it.
    /// </summary>
    public static IEnumerable<Run> ParseNormalText(string text, VersePosition position)
    {
        bool first = true;
        foreach (var run in ParseText(text, position))
        {
            if (first && run.Class == Run.Classes.Title)
            {
                first = false;
                run.First = true;
            }
            yield return run;
        }
    }

    public static IEnumerable<Run> ParseNormalBook(Book book)
    {
        var position = new VersePosition()
        {
            Book = book,
            Title = null,
            Chapter = 1,
            Verse = 1
        };
        return ParseNormalText(book.Markdown, position);
    }
    public static IEnumerable<Run> ParseBibleBook(Book book)
    {
        var markdown = book.Markdown;
        var first = true;
        // strip comments
        markdown = BlockComments().Replace(markdown, ""); // comments
        markdown = LineComments().Replace(markdown, ""); // single line comments
        // Replace footnotes
        bool replaced;
        do
        {
            replaced = false;
            markdown = Footnotes().Replace(markdown, m =>
            {
                replaced = true;
                return $"{m.Groups["footnote"].Value}{m.Groups["text"].Value}";
            });// ^^ footnotes
        } while (replaced);

        bool isBible = book.IsBible;
        if (isBible)
        {
            var chapters = Chapters().Matches(markdown)
                .OfType<Match>();
            foreach (var chapter in chapters.SelectMany(chapter => ParseChapter(chapter, new VersePosition { Book = book })))
            {
                var title = chapter.Runs.FirstOrDefault(r => r.Class == Run.Classes.Title);
                if (title != null)
                {
                    title.First = first;
                    first = false;
                }
                yield return chapter;
            }
        } else
        {
            foreach (var run in ParseNormalBook(book)) yield return run;
        }
    }

    public string Name { get; set; } = "";
    public string? EnglishName { get; set; }
    public virtual string Origin => GetType().Name;
    public string Id => WebUtility.UrlEncode(Origin + "." + (!string.IsNullOrEmpty(EnglishName) ? EnglishName : Name));
    [System.Text.Json.Serialization.JsonIgnore] // not saved with the settings: the books are read from the folder
    public List<Book> Books { get; set; } = new List<Book>();
    public string ConfigPath
    {
        get
        {
            if (field == null)
            {
                field = Path.Combine(Settings.Current.ConfigPath, "Books", Id);
            }
            return field;
        }
    }

    public void Open()
    {
        var bible = OpenFrom(ConfigPath);
        Name = bible.Name;
        EnglishName = bible.EnglishName;
        Books = bible.Books;
    }
    public static BookText OpenFrom(string path)
    {
        var bible = new BookText();
        bible.Name = WebUtility.UrlDecode(Path.GetFileName(path));
        var dir = new DirectoryInfo(path);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Directory not found: {path}");
        var files = dir.EnumerateFiles("*.md", SearchOption.TopDirectoryOnly)
            .Where(file => Regex.IsMatch(file.Name, @"^[0-9.]+-"))
            .OrderBy(file => file.Name);
        bible.Books.AddRange(files.Select(file => new Book(bible)
        {
            Name = Regex.Replace(file.Name, @"^[0-9.]+-(.*)\.md$", "$1"),
            Index = int.Parse(Regex.Match(file.Name, "^[0-9]+").Value),
            File = file.FullName,
        }));
        var booknamesFile = Path.Combine(path, "booknames.xml");
        if (!File.Exists(booknamesFile)) booknamesFile = Path.Combine(path, "src", "booknames.xml");
        if (File.Exists(booknamesFile))
        {
            var xml = System.Xml.Linq.XDocument.Load(booknamesFile);
            var enid = xml.Root.Elements("ID")
                .FirstOrDefault(e => e.Attribute("descr")?.Value.Equals("english", StringComparison.OrdinalIgnoreCase) == true)
                ?.Elements("BOOK");
            var id = xml.Root.Elements("ID")
                .Select(id => {
                    var xbooks = id.Elements("BOOK");
                    return xbooks
                        .Join(bible.Books, book => book.Value, book => book.Name, (id, book) => new { Id = id, Book = book })
                        .Join(enid, book => book.Id.Attribute("bnumber")?.Value, id => id.Attribute("bnumber")?.Value, (b, enid) => new
                        {
                            b.Id, b.Book, EnId = enid
                        })
                        .ToList();
                })
                .MaxBy(list => list.Count);
            foreach (var book in id)
            {
                book.Book.Abbrevation = book.Id.Attribute("bshort")?.Value;
                book.Book.Description = book.Id.Attribute("desc")?.Value;
                var bid = book.Id.Attribute("id")?.Value ?? book.EnId.Attribute("bshort")?.Value ?? book.EnId.Attribute("abbr")?.Value;
                book.Book.Id = bid;
                if (string.IsNullOrEmpty(bid))
                {
                    if (BookText.ClassByIndex.TryGetValue(book.Book.Index, out var @class)) book.Book.Class = @class;
                    else book.Book.Class = Book.Classes.Apocrypha;
                } else
                {
                    if (BookText.ClassById.TryGetValue(bid, out var @class)) book.Book.Class = @class;
                    else if(BookText.ClassByIndex.TryGetValue(book.Book.Index, out var classbyindex)) book.Book.Class = classbyindex;
                    else book.Book.Class = Book.Classes.Apocrypha;
                }
            }
        }
        return bible;
    }   
}