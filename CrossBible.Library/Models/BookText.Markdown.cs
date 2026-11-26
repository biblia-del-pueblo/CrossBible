using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CrossBible.Library;

/// <summary>Writes the runs of a book (as <see cref="ParseBibleBook"/> makes them) back as markdown.</summary>
public partial class BookText
{
    private static readonly Regex UsfmWord = new(@"^[\p{L}\p{M}]+(?: [\p{L}\p{M}]+)*$", RegexOptions.Compiled);
    private static readonly Regex UsfmNumber = new(@"^[GH][0-9]+$", RegexOptions.Compiled);

    /// <summary>
    /// The markdown of the runs of a book: the chapters (<c># 1</c>) with their titles (<c>##</c>) and verses (<c>@1 text</c>, each on a line of its own),
    /// the words in bold, italics, small caps, words of Jesus, links, footnotes and words with Strong's numbers. What the parser does not keep
    /// (comments, the line breaks inside a verse, the way a footnote was written) is not written.
    /// </summary>
    public static string ToMarkdown(IEnumerable<Run> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs) Write(sb, run);
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
        return sb.ToString().Replace("\n", Environment.NewLine);
    }

    private static string Escape(string? text) =>
        string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, @"([\[\]*^\\])", @"\$1");

    private static void EnsureLineStart(StringBuilder sb)
    {
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
    }

    private static void WriteChildren(StringBuilder sb, Run run)
    {
        if (run.Runs?.Any() == true) foreach (var child in run.Runs) Write(sb, child);
        else sb.Append(Escape(run.Text));
    }

    private static string PlainText(Run run) =>
        run.Runs?.Any() == true ? string.Concat(run.Runs.Select(PlainText)) : run.Text ?? "";

    private static void Write(StringBuilder sb, Run run)
    {
        switch (run.Class)
        {
            case Run.Classes.Chapter:
                EnsureLineStart(sb);
                sb.Append("# ").Append(run.Chapter).Append('\n');
                WriteChildren(sb, run);
                EnsureLineStart(sb);
                break;
            case Run.Classes.Title:
                EnsureLineStart(sb);
                sb.Append('#', run.TitleLevel > 0 ? run.TitleLevel : 2).Append(' ');
                WriteChildren(sb, run);
                sb.Append('\n');
                break;
            case Run.Classes.Verse:
                EnsureLineStart(sb);
                sb.Append('@').Append(run.Position?.Verse ?? 0);
                if (run.Runs?.Any() == true)
                {
                    foreach (var child in run.Runs) Write(sb, child);
                    EnsureLineStart(sb); // a verse of a chapter ends its line
                }
                break;
            case Run.Classes.Paragraph:
                sb.Append("\n\n");
                break;
            case Run.Classes.LineBreak:
                sb.Append("\\\n");
                break;
            case Run.Classes.Bold:
                sb.Append("**"); WriteChildren(sb, run); sb.Append("**");
                break;
            case Run.Classes.Italic:
                sb.Append('*'); WriteChildren(sb, run); sb.Append('*');
                break;
            case Run.Classes.Footnote:
                sb.Append("^["); WriteChildren(sb, run); sb.Append(']');
                break;
            case Run.Classes.SmallCaps:
                sb.Append('['); WriteChildren(sb, run); sb.Append("]{.smallcaps}");
                break;
            case Run.Classes.WordsOfJesus:
                sb.Append('['); WriteChildren(sb, run); sb.Append("]{.wj}");
                break;
            case Run.Classes.Link:
                sb.Append('['); WriteChildren(sb, run); sb.Append("](").Append(run.Url).Append(')');
                break;
            case Run.Classes.WordWithStrong:
                WriteStrongsWord(sb, run);
                break;
            default:
                WriteChildren(sb, run);
                break;
        }
    }

    private static void WriteStrongsWord(StringBuilder sb, Run run)
    {
        var numbers = string.Join(' ', (run.StrongNumber ?? "").Split([' ', ',', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        if (numbers.Length == 0) { WriteChildren(sb, run); return; } // the numbers were taken away: a plain word
        var word = PlainText(run);
        if (run.Lemma is null && run.Morph is null && UsfmWord.IsMatch(word) && UsfmNumber.IsMatch(numbers))
        {
            sb.Append(word.Replace(' ', '_')).Append('_').Append(numbers); // the USFM style: word_G1234
            return;
        }
        sb.Append('['); WriteChildren(sb, run); sb.Append("]{strong=\"").Append(numbers).Append('"');
        if (run.Lemma is not null) sb.Append(" lemma=\"").Append(run.Lemma).Append('"');
        if (run.Morph is not null) sb.Append(" x-morph=\"").Append(run.Morph).Append('"');
        sb.Append('}');
    }
}
