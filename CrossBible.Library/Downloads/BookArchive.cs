using Avalonia.Controls.Shapes;
using BibleMarkdown;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using IO = System.IO;

namespace CrossBible.Library.Downloads
{
    public class BookArchive: InstallableBook
    {
        public string File { get; set; }
        public override string Origin => "Local File";
        
        public BookArchive() { } // for reading the settings
        public BookArchive(string file) { this.File = file; Language = ""; LanguageEnglishName = ""; }
        public BookArchive(BookArchive source): base(source)
        {
            File = source.File;
        }

        public override InstallableBook Upgrade() => Class == Classes.Dictionary ? new BibliaLibreDictionary(this) : this;

        // Words in capitals (LORD, GOD) become small caps. A word with Strong's attributes is matched as a whole, so its
        // attributes are not mistaken for words in capitals (the morphology "HR/Ncmsa" has capitals): either as
        // "LORD_H3068" (the small caps go around the whole word: "[Lord_H3068]{.smallcaps}") or as
        // [LORD]{strong="H3068" x-morph="HR/Ncmsa"} (the small caps go around the span: "[[Lord]{strong=...}]{.smallcaps}").
        // The reader reads both as a small caps run that holds a word with a Strong's number.
        static readonly Regex SmallCapsPattern = new(
            @"(?<!(^|\r?\n)#[^\r\n]*?)(?:" +
            @"(?<span>\[(?<spantext>[^\[\]]+)\]\{(?<spanattr>" + BookText.StrongsExtendedAttributes + @")\})|" +
            @"(?<strongs>" + BookText.StrongsWord + @")|\p{Lu}{2,})",
            RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex StrongsAttributePart = new(@"^[GH][0-9]+$", RegexOptions.Compiled);

        static bool IsRomanNumberOrIsbn(string word) => Regex.IsMatch(word, "^(?:[IVXCD]+|ISBN|BC|AC)$", RegexOptions.Singleline);

        /// <summary>The first letter of the word and the rest in lower case: "LORD" becomes "Lord".</summary>
        static string Capitalize(string word) => word.Length < 2 ? word : word[0] + word[1..].ToLowerInvariant();

        /// <summary>
        /// A word with Strong's attributes: when all its words are in capitals (and at least one is not a roman number)
        /// it is returned as small caps around the whole word, else unchanged.
        /// </summary>
        static string SmallCapsStrongsWord(string token)
        {
            var parts = token.Split('_');
            var words = parts.Where(p => !StrongsAttributePart.IsMatch(p)).ToList();
            bool hasCapitals = words.Any(w => Regex.IsMatch(w, @"^\p{Lu}{2,}$") && !IsRomanNumberOrIsbn(w));
            if (!hasCapitals || words.Any(w => w.Any(char.IsLower))) return token; // no capitals, or mixed: not all small caps
            return "[" + string.Join('_', parts.Select(p => StrongsAttributePart.IsMatch(p) ? p : Capitalize(p))) + "]{.smallcaps}";
        }

        /// <summary>A span with Strong's attributes: when all its words are in capitals, small caps go around the span, else unchanged.</summary>
        static string SmallCapsStrongsSpan(Match m)
        {
            var text = m.Groups["spantext"].Value;
            var words = Regex.Matches(text, @"[\p{L}\p{M}]+").Select(w => w.Value).ToList();
            bool hasCapitals = words.Any(w => Regex.IsMatch(w, @"^\p{Lu}{2,}$") && !IsRomanNumberOrIsbn(w));
            if (!hasCapitals || words.Any(w => w.Any(char.IsLower))) return m.Value;
            var capitalized = Regex.Replace(text, @"[\p{L}\p{M}]+", w => Capitalize(w.Value));
            return $"[[{capitalized}]{{{m.Groups["spanattr"].Value}}}]{{.smallcaps}}";
        }

        public static string PostprocessMarkdownSmallCaps(string text)
        {
            return SmallCapsPattern.Replace(text, m =>
            {
                if (m.Groups["span"].Success) return SmallCapsStrongsSpan(m);
                if (m.Groups["strongs"].Success) return SmallCapsStrongsWord(m.Value);

                if (IsRomanNumberOrIsbn(m.Value))
                {
                    // is roman number
                    return m.Value;
                }

                var str = new StringBuilder("[");
                str.Append(m.Value[0]);
                for (int i = 1; i < m.Value.Length; i++) str.Append(Char.ToLower(m.Value[i]));
                str.Append("]{.smallcaps}");
                return str.ToString();
            });
        }

        public override async Task Install()
        {
            if (string.IsNullOrEmpty(File)) throw new InvalidOperationException("File is not set.");

            var name = IO.Path.GetFileNameWithoutExtension(File);
            EnglishName = Name = name;
            var path = ConfigPath;

            var src = IO.Path.Combine(path, "src");
            Directory.CreateDirectory(src);

            var ext = IO.Path.GetExtension(File).ToLower();
            if (ext == ".zip" || ext == ".7z")
            {
                Directory.Delete(src, true);
                Zip.UnzipZipFile(File, src);
            }
            else IO.File.Copy(File, IO.Path.Combine(src, IO.Path.GetFileName(File)), true);
            
            try {
                await Task.Run(() => {
                    var transform = new Transform(path, src);
                    transform.Import.Postprocess = PostprocessMarkdownSmallCaps;
                    transform.Import.All();
                    try
                    {
                        var info = transform.Import.ReadInfo(path);
                        if (info != null)
                        {
                            var oldpath = ConfigPath;
                            LanguageCode = info.LanguageCode;
                            Language = info.Language;
                            LanguageEnglishName = info.LanguageEnglishName;
                            Class = (CrossBible.Library.InstallableBook.Classes)info.Class;
                            CountryCode = info.CountryCode;
                            CountryName = info.CountryName;
                            EnglishName = info.EnglishName;
                            Name = info.Name ?? "";
                            var newpath = ConfigPath;
                            if (oldpath != newpath) Directory.Move(oldpath, newpath);
                        }
                    } catch (Exception ex)
                    {
                        Debug.WriteLine($"Cannot read info.json: {ex}");
                    }
                    transform.Index.Create(LanguageCode);
                    Directory.Delete(src, true);
                });
            }
            finally { }
        }
    }

    public class BookArchiveDictionary: BookArchive, IStrongDictionary
    {
        public override Classes Class => Classes.Dictionary;
        public BookArchiveDictionary() { } // for reading the settings
        public BookArchiveDictionary(BookArchive source) : base(source) { }
        string DatabaseFile => IO.Path.Combine(ConfigPath, StrongsDatabase.FileName);
        StrongsDatabase database = null;
        StrongsDatabase Database
        {
            get => database ??= StrongsDatabase.Open(DatabaseFile);
            set => database = value;
        }
        public bool IsEnabled { get; set; }
        protected bool IsImported => IO.File.Exists(DatabaseFile);

        public override async Task Install()
        {
            await base.Install();
            IsEnabled = true; // an installed dictionary is used at once; it is switched off in the Strongs list
        }

        public string? LookupStrong(string strong) => Database.Get(strong);
        public override async Task Uninstall()
        {
            IsEnabled = false; // a dictionary that is removed is switched off first, so Any is told
            Dispose();
            await base.Uninstall();
        }

        public void Dispose() => database?.Dispose();
    }
}
