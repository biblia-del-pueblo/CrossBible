using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Platform;
using CrossBible.Library.Downloads;
using NHyphenator;
using NHyphenator.Loaders;

namespace CrossBible.Library;

/// <summary>
/// Hyphenation of the text: finds where the words may be split at the end of a line (Liang's algorithm, by NHyphenator)
/// and marks these places with a soft hyphen (U+00AD). The text block breaks lines there and draws the hyphen at the end
/// of a line that was broken at one (see BibleTextBlock). The patterns are the ones of TeX (hyph-utf8) in
/// Assets/Hyphenation; there is one set per language, and a language without one is not hyphenated.
/// </summary>
public static class Hyphenation
{
    /// <summary>The soft hyphen: invisible unless a line is broken at it.</summary>
    public const char SoftHyphen = '­';

    /// <summary>
    /// The languages that have patterns (ISO 639-1 codes; "sr-cyrl" is Serbian in the Cyrillic script, the patterns are not for the Latin one: see <see cref="CodeOf"/>).
    /// The patterns of a language are Assets/Hyphenation/hyph-&lt;code&gt;.pat.txt, and its exceptions hyph-&lt;code&gt;.hyp.txt, if there are.
    /// </summary>
    public static readonly string[] Languages = { "de", "en", "es", "fr", "pt", "ru", "tr", "el", "it", "nl", "pl", "da", "sv", "nb", "fi",
        "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml", "pa", "or", "th", "hy", "id", "mn", "ne", "sr-cyrl", "sh-latn", "sr" };

    /// <summary>
    /// The Indian languages, and Thai. Their words have combining vowel signs, which are not letters for NHyphenator, so they are
    /// hyphenated by <see cref="LiangPatterns"/>. They are not written with a hyphen at the end of a line: the line is
    /// broken at a zero width space (<see cref="BreakOpportunity"/>) and nothing is drawn.
    /// </summary>
    public static readonly HashSet<string> IndicLanguages = new() { "hi", "bn", "ta", "te", "mr", "gu", "kn", "ml", "pa", "or", "th", "ne" };

    /// <summary>
    /// Languages that are hyphenated by <see cref="LiangPatterns"/> too, but with a soft hyphen, so a hyphen is written at the end of a line: Serbian in the Cyrillic script.
    /// NHyphenator finds hardly any place in a Serbian word with its patterns (its settings and the way it reads the patterns do not fit them), where the algorithm
    /// itself, applied to the same file, finds all of them (<c>\u0441\u0432\u0435-\u0442\u0438-\u043B\u0438-\u0448\u0442\u0435</c>, <c>\u043F\u0440\u0430-\u0432\u043E-\u0441\u043B\u0430-\u0432\u0459\u0435</c>).
    /// </summary>
    public static readonly HashSet<string> LiangLanguages = new() { "sr-cyrl", "sh-latn", "sr" };

    /// <summary>The patterns for the script of a word, for the languages of <see cref="LiangLanguages"/> that are written in two (plain "sr", where the script of the Bible is not known).</summary>
    private static string PatternsOf(string language, string word) =>
        language == "sr" ? (word[0] is >= 'Ѐ' and <= 'ӿ' ? "sr-cyrl" : "sh-latn") : language;

    /// <summary>An invisible place where a line may be broken, without a hyphen (used for the Indian languages).</summary>
    public const char BreakOpportunity = '\u200B';

    /// <summary>Whether the character only marks a place where a word may be split (it is not part of the text that is read or copied).</summary>
    public static bool IsMark(char c) => c is SoftHyphen or BreakOpportunity;

    /// <summary>
    /// Whether a line that ends with the mark at <paramref name="index"/> shows a hyphen there. A soft hyphen does; so does a <see cref="BreakOpportunity"/> behind a
    /// Cyrillic or Latin letter (Serbian: the Cyrillic letters come from another font than the soft hyphen, and the line is not broken at a soft hyphen between them, while
    /// the invisible break works); the Indian languages have no hyphen.
    /// </summary>
    public static bool ShowsHyphen(string text, int index) =>
        index >= 0 && index < text.Length
        && (text[index] == SoftHyphen || (text[index] == BreakOpportunity && index > 0 && text[index - 1] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= 'À' and <= 'ɏ') or (>= 'Ѐ' and <= 'ӿ')));

    // Languages whose patterns must be sorted before use (see NHyphenator).
    private static readonly HashSet<string> SortedLanguages = new() { "de", "pt" };

    private static readonly ConcurrentDictionary<string, Hyphenator?> Hyphenators = new();
    private static readonly ConcurrentDictionary<(string Language, string Word), string> Words = new();

    // The words are worked out on a worker thread too (see Warm), while the UI thread may need others: one at a time, as the
    // hyphenators were only ever used from one thread.
    private static readonly object HyphenateLock = new();

    private static Hyphenator? HyphenatorOf(string language) => Hyphenators.GetOrAdd(language, code =>
    {
        if (!Languages.Contains(code)) return null;
        try { return new Hyphenator(new AssetPatternsLoader(code), SoftHyphen.ToString(), minWordLength: 6, minLetterCount: 3, hyphenateLastWord: true, sortPatterns: SortedLanguages.Contains(code)); }
        catch (Exception ex) when (ex is IOException or ArgumentException or KeyNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"No hyphenation patterns for {code}: {ex.Message}");
            return null;
        }
    });

    private static readonly ConcurrentDictionary<string, LiangPatterns?> LiangPatternSets = new();

    /// <summary>The patterns (and the exceptions, if there are) of a language that <see cref="LiangPatterns"/> hyphenates: the Indian languages and <see cref="LiangLanguages"/>, with an invisible break (see <see cref="ShowsHyphen"/> for the hyphen).</summary>
    private static LiangPatterns? LiangPatternsOf(string language) => LiangPatternSets.GetOrAdd(language, code =>
    {
        try
        {
            var patterns = LiangPatterns.Parse(AssetPatternsLoader.Read(AssetPatternsLoader.UriOf(code, "pat")) ?? "", BreakOpportunity);
            if (AssetPatternsLoader.Read(AssetPatternsLoader.UriOf(code, "hyp")) is { } exceptions) patterns.AddExceptions(exceptions);
            return patterns;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"No hyphenation patterns for {code}: {ex.Message}");
            return null;
        }
    });

    /// <summary>Letters, and for the Indian languages also the vowel signs and joiners that belong to them.</summary>
    private static bool IsWordChar(char c, bool indic) =>
        char.IsLetter(c) || (indic && (c is '\u200C' or '\u200D'
            || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark));

    /// <summary>
    /// The text with a mark at every place where a word may be split: a soft hyphen (<see cref="SoftHyphen"/>), or for
    /// the Indian languages a <see cref="BreakOpportunity"/>. Short words stay as they are. The same word is only worked out once.
    /// </summary>
    public static string Apply(string text, string language)
    {
        if (text.Length == 0) return text;
        bool indic = IndicLanguages.Contains(language);
        Func<string, string>? hyphenate = null;
        if (language == "sr") hyphenate = word => LiangPatternsOf(PatternsOf(language, word))?.Hyphenate(word) ?? word;
        else if (indic || LiangLanguages.Contains(language)) { if (LiangPatternsOf(language) is { } patterns) hyphenate = patterns.Hyphenate; }
        else if (HyphenatorOf(language) is { } hyphenator) hyphenate = hyphenator.HyphenateText;
        if (hyphenate is null) return text;

        StringBuilder? result = null;
        int wordStart = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool inWord = i < text.Length && IsWordChar(text[i], indic);
            if (inWord) { if (wordStart < 0) wordStart = i; continue; }
            if (wordStart >= 0)
            {
                string word = text[wordStart..i];
                string hyphenated = word.Length < 6 ? word : Words.GetOrAdd((language, word), key =>
                {
                    lock (HyphenateLock)
                    {
                        return hyphenate(key.Word);
                    }
                });
                if (hyphenated != word)
                {
                    result ??= new StringBuilder(text.Length + 16).Append(text, 0, wordStart);
                    result.Append(hyphenated);
                }
                else result?.Append(word);
                wordStart = -1;
            }
            if (i < text.Length) result?.Append(text[i]);
        }
        return result?.ToString() ?? text;
    }

    /// <summary>
    /// Works out the words of the runs and of their children, so they are in the cache when the text is flattened on the UI thread, where the
    /// first time a word is seen is the slow part. Meant for a worker thread.
    /// </summary>
    public static void Warm(IEnumerable<BookText.Run>? runs, string language)
    {
        if (runs is null) return;
        foreach (var run in runs)
        {
            if (!string.IsNullOrEmpty(run.Text)) Apply(run.Text, language);
            Warm(run.Runs, language);
        }
    }

    /// <summary>The language code (<c>"de"</c>) of the installed Bible of this name, or null if it is not known or has no patterns.</summary>
    public static string? LanguageOf(BookText? bible)
    {
        if (bible is null) return null;
        // A combined text (see BookText.Combine) is hyphenated when all the texts it is made of are of the language of one set of patterns: the words of
        // another language would be broken by the rules of the first.
        if (bible.Sources is { Count: > 0 } sources)
        {
            var codes = sources.Select(LanguageOf).Distinct().ToList();
            return codes.Count == 1 ? codes[0] : null;
        }
        var installed = (Settings.Current.Books ?? new List<BookText>()).OfType<InstallableBook>()
            .FirstOrDefault(b => b.EnglishName == bible.EnglishName || b.EnglishName == bible.Name);
        if (installed is null) return null;
        // The language code of the Bible (InstallableBook.LanguageCode: "de", "pt-BR"), not its Language, which is a name ("Deutsch"): what a name is made of differs
        // from system to system (on Linux a name that is no culture is made a custom culture, with the name as its code), the code does not.
        return CodeOf(installed.LanguageCode);
    }

    // The code of the language of "de" or "pt-BR" ("pt"); null when it has no patterns.
    private static string? CodeOf(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return null;
        string[] parts = languageCode.Trim().Split('-', '_');
        string code = parts[0].ToLowerInvariant();
        if (code is "no" or "nn") code = "nb"; // Norwegian, both written forms, share the patterns of Bokmål
        // Serbian is written in two scripts, with patterns of their own (the Latin ones are those of Serbo-Croatian, "sh-latn"); with no script in the code ("sr"), they are chosen word by word.
        if (code == "sr")
        {
            if (parts.Skip(1).Any(p => p.Equals("Latn", StringComparison.OrdinalIgnoreCase))) code = "sh-latn";
            else if (parts.Skip(1).Any(p => p.Equals("Cyrl", StringComparison.OrdinalIgnoreCase))) code = "sr-cyrl";
        }
        return Languages.Contains(code) ? code : null;
    }

    /// <summary>Reads the patterns (and the exceptions, if there are) of a language from the assets of this assembly.</summary>
    private sealed class AssetPatternsLoader(string code) : IHyphenatePatternsLoader
    {
        // Nepali is written in Devanagari like Hindi and uses its patterns.
        internal static Uri UriOf(string code, string kind) => new($"avares://CrossBible.Library/Assets/Hyphenation/hyph-{(code == "ne" ? "hi" : code)}.{kind}.txt");

        internal static string? Read(Uri uri)
        {
            if (!AssetLoader.Exists(uri)) return null;
            using var reader = new StreamReader(AssetLoader.Open(uri), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        public string LoadPatterns() => Read(UriOf(code, "pat")) ?? "";
        public string? LoadExceptions() => Read(UriOf(code, "hyp"));
    }

    /// <summary>
    /// Liang's algorithm for patterns of TeX (<c>1क</c>, <c>ा1</c>, <c>2्2</c>: a digit between letters is the priority of
    /// a break there; the highest one wins, odd means a break). NHyphenator cannot be used for the Indian languages,
    /// because it ends a word at every vowel sign.
    /// </summary>
    public sealed class LiangPatterns
    {
        private readonly Dictionary<string, byte[]> patterns = new();
        private int longest;
        private char mark;

        public static LiangPatterns Parse(string text, char mark)
        {
            var result = new LiangPatterns { mark = mark };
            foreach (var line in text.Split(new[] { '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var letters = new StringBuilder();
                var values = new List<byte>();
                byte pending = 0;
                foreach (char c in line)
                {
                    if (c is >= '0' and <= '9') { pending = (byte)(c - '0'); continue; }
                    letters.Append(c);
                    values.Add(pending);
                    pending = 0;
                }
                values.Add(pending);
                if (letters.Length == 0) continue;
                result.patterns[letters.ToString()] = values.ToArray();
                result.longest = Math.Max(result.longest, letters.Length);
            }
            if (result.patterns.Count == 0) throw new ArgumentException("Patterns must contain at least one pattern");
            return result;
        }

        // The words that have their own places to be split (hyph-<code>.hyp.txt, one word a line: "на-тка-ти"), by the word in lower case, as the indexes of the
        // letters that a split is in front of.
        private readonly Dictionary<string, int[]> exceptions = new();

        /// <summary>Takes the exceptions of the patterns: words in which the places to split are marked by hyphens (на-тка-ти). They are used instead of the patterns.</summary>
        public void AddExceptions(string text)
        {
            foreach (var line in text.Split(new[] { '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var breaks = new List<int>();
                var word = new StringBuilder();
                foreach (char c in line)
                {
                    if (c == '-') breaks.Add(word.Length);
                    else word.Append(c);
                }
                if (word.Length > 0) exceptions[word.ToString().ToLowerInvariant()] = breaks.ToArray();
            }
        }

        /// <summary>The word with the mark at every place where it may be split, at least two characters from both ends.</summary>
        public string Hyphenate(string word)
        {
            const int minimum = 2;
            if (word.Length < 2 * minimum) return word;
            if (exceptions.Count > 0 && exceptions.TryGetValue(word.ToLowerInvariant(), out var fixedBreaks))
            {
                var spelled = new StringBuilder(word.Length + fixedBreaks.Length);
                for (int k = 0; k < word.Length; k++)
                {
                    if (Array.IndexOf(fixedBreaks, k) >= 0) spelled.Append(mark);
                    spelled.Append(word[k]);
                }
                return spelled.ToString();
            }
            string dotted = "." + word.ToLowerInvariant() + ".";
            var levels = new byte[dotted.Length + 1];
            for (int start = 0; start < dotted.Length; start++)
                for (int length = 1; length <= longest && start + length <= dotted.Length; length++)
                    if (patterns.TryGetValue(dotted.Substring(start, length), out var values))
                        for (int j = 0; j < values.Length; j++)
                            if (values[j] > levels[start + j]) levels[start + j] = values[j];

            var result = new StringBuilder(word.Length + 4);
            for (int k = 0; k < word.Length; k++)
            {
                // the level before the character word[k] is levels[k + 1] (the dotted word starts with a dot)
                if (k >= minimum && word.Length - k >= minimum && levels[k + 1] % 2 == 1) result.Append(mark);
                result.Append(word[k]);
            }
            return result.ToString();
        }
    }
}
