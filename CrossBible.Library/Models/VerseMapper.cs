using BibleMarkdown;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace CrossBible.Library;

/// <summary>
/// Maps the verses of a text to the chapters and verse numbers of another versification (see <see cref="InstallableBook.Versification"/>), so that <see cref="BookText.Combine"/>
/// can line up verses that are numbered differently. It is the translation of verses of SWORD, through KJV (<see cref="SwordVersification.ToKjv"/> and
/// <see cref="SwordVersification.FromKjv"/>): its rules for NRSV, Synodal, Vulg, Calvin, DarbyFr and Segond, and for a versification without rules the headings of the
/// Psalms that are counted as verses (German, Luther, the Hebrew ones ...). What neither tells (the other versifications that SWORD has no rules for) stays matched by number.
/// </summary>
internal sealed class VerseMapper
{
    readonly SwordVersification from, to;

    VerseMapper(SwordVersification from, SwordVersification to) { this.from = from; this.to = to; }

    /// <summary>
    /// The mapper from the versification <paramref name="source"/> to <paramref name="target"/> (names as in info.json), or null when nothing is to be mapped: a name is
    /// missing or not known, or they are the same.
    /// </summary>
    public static VerseMapper? Create(string? source, string? target)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) return null;
        var from = SwordVersification.Find(source);
        var to = SwordVersification.Find(target);
        if (from == null || to == null || ReferenceEquals(from, to)) return null;
        return new VerseMapper(from, to);
    }

    /// <summary>
    /// The chapter and the verse that a verse of the source has in the target (the book by its number). Verse 0 is the heading of a chapter: what the source counts as a
    /// verse and the target does not (or the other way round: the verse that the source does not count, as the target has a verse for it).
    /// </summary>
    public (int Chapter, int Verse) Map(int book, int chapter, int verse)
    {
        var (kjvChapter, kjvVerse) = from.ToKjv(book, chapter, verse);
        return to.FromKjv(book, kjvChapter, kjvVerse);
    }

    /// <summary>
    /// The versification of a text: the one its entry has (an installed Bible), else the one in the info.json in its folder; null when it has none.
    /// </summary>
    public static string? VersificationOf(BookText text)
    {
        if (text is InstallableBook { Versification: { Length: > 0 } known }) return known;
        try
        {
            if (string.IsNullOrEmpty(text.Id)) return null;
            var file = Path.Combine(text.ConfigPath, "info.json");
            if (!File.Exists(file)) return null;
            var options = new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true };
            if (JsonNode.Parse(File.ReadAllText(file), documentOptions: options) is not JsonObject info) return null;
            return info.FirstOrDefault(p => string.Equals(p.Key, nameof(InstallableBook.Versification), StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
