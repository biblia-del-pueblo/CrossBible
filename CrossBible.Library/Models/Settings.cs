using EstrellasDeEsperanza.AsyncLock;
using BibleMarkdown;
using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace CrossBible.Library
{
    public class Settings
    {
        const string SettingsFile = "settings.json";
        public List<BookText> Books { get; set; }
        /// <summary>The <see cref="BookText.EnglishName"/> of the Bible that was open last; it is opened again at the start.</summary>
        public string? CurrentBible { get; set; }
        /// <summary>Where the reader was in <see cref="CurrentBible"/>: the number of the book (<c>Book.Index</c>), the chapter and the verse.</summary>
        public int CurrentBookIndex { get; set; }
        public int CurrentChapter { get; set; }
        public int CurrentVerse { get; set; }
        /// <summary>"System" (or null), "Light" or "Dark".</summary>
        public string? Theme { get; set; }
        /// <summary>A place in the history of positions (the back and forward buttons of the toolbar).</summary>
        public class HistoryEntry
        {
            public int BookIndex { get; set; }
            public int Chapter { get; set; }
            public int Verse { get; set; }
        }
        /// <summary>The positions the reader has been at, the oldest first. Kept in memory, saved by <see cref="SaveOnExit"/>.</summary>
        public List<HistoryEntry>? History { get; set; }
        /// <summary>The entry of <see cref="History"/> that the reader is at (earlier ones are back, later ones forward).</summary>
        public int HistoryIndex { get; set; } = -1;
        public bool? ShowWordsOfJesus { get; set; }
        /// <summary>
        /// Whether the words with Strong's numbers are marked and can be clicked (the Strongs button of the toolbar and the "All" box of its
        /// list switch it). It does not change which dictionaries are used: that is the <see cref="CrossBible.Library.Downloads.IStrongDictionary.IsEnabled"/>
        /// of each dictionary. On by default.
        /// </summary>
        public bool ShowStrongs
        {
            get => field;
            set
            {
                if (field == value) return;
                field = value;
                CrossBible.Library.Downloads.StrongsDictionaries.Refresh();
            }
        } = true;
        /// <summary>
        /// Whether there is no persecution of Christians in the country of the user: null while the question was not answered (the online Bibles view asks it once),
        /// true when the user confirmed it, false when there is persecution. Only when it is true the online Bibles view uses the internet (it lists and downloads Bibles).
        /// </summary>
        public bool? InMyCountryThereIsNoPersecution { get; set; }
        /// <summary>The language of the texts: "en", "de" ...; null for the language of the device. It is taken when the app starts.</summary>
        public string? Language { get; set; }
        /// <summary>The size of the text of the interface as a factor (1.25 is 25% larger); null for the size of the system (see <see cref="UiScale"/>).</summary>
        public double? UiSize { get; set; }
        /// <summary>Show the verse numbers in green (off when null).</summary>
        public bool? ShowVerseNumbersInGreen { get; set; } = true;
        /// <summary>Split words at the end of a line (see <see cref="Hyphenation"/>); on when null.</summary>
        public bool? Hyphenate { get; set; }
        public double? FontSize { get; set; }
        /// <summary>The opacity of the text of the Bible in percent (30 to 100); null for the default of the theme (see <see cref="TextColor"/>).</summary>
        public double? TextOpacity { get; set; }
        /// <summary>The size of the main window of the desktop app when it was closed (not maximized), in device-independent units; null before the first close.</summary>
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public string FontFamily { get; set; } = "Bible Roman";
        /// <summary>The font of Greek words in the text and of the Greek Strong's letters.</summary>
        public string GreekFontFamily { get; set; } = "Cardo";
        /// <summary>The font of Hebrew words in the text and of the Hebrew Strong's letters.</summary>
        public string HebrewFontFamily { get; set; } = "Frank Ruehl CLM";
        public string ConfigPath
        {
            get
            {
                if (field == null)
                {
                    field = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrossBible");
                    Directory.CreateDirectory(field);
                }
                return field;
            }
            set
            {
                field = value;
                Directory.CreateDirectory(field);
            }
        }
        public string BookCoversPath => Path.Combine(ConfigPath, "Book-Covers");

        [JsonIgnore]
        public static Settings Current { get; private set; } = new Settings();

        private static int _requested, _written;

        AsyncLock SaveLock = new AsyncLock();

        public void Save()
        {
            var json = Serialize();
            int number = ++_requested;
            var file = Path.Combine(ConfigPath, SettingsFile);
            using (SaveLock.Lock())
            {
                if (number < _written) return;
                _written = number;
                File.WriteAllText(file, json);
            }
        }

        /// <summary>
        /// Saves without waiting for the disk. The settings are turned into text here, on the calling thread (so nothing
        /// changes them meanwhile); the file is written asynchronously, off that thread. Writes never overlap, and a newer
        /// one is not overwritten by an older one that gets there later.
        /// </summary>
        public Task SaveAsync()
        {
            var json = Serialize();
            int number = ++_requested;
            var file = Path.Combine(ConfigPath, SettingsFile);
            return Task.Run(async () =>
            {
                using (await SaveLock.LockAsync())
                {
                    if (number < _written) return; // a newer state was already written
                    _written = number;
                    await File.WriteAllTextAsync(file, json);
                }
            });
        }

        /// <summary>The history is cut to its last this many entries when the settings are saved.</summary>
        public const int MaxHistoryOnSave = 250;

        private string Serialize()
        {
            TrimHistory();
            // The kinds of Bible in Books are written with their names ("$type", see the attributes of BookText).
            return JsonSerializer.Serialize(this, JsonContext.Indented.Settings);
        }

        /// <summary>Drops the oldest entries beyond <see cref="MaxHistoryOnSave"/>; <see cref="HistoryIndex"/> keeps pointing at the same entry.</summary>
        private void TrimHistory()
        {
            if (History is not { } history || history.Count <= MaxHistoryOnSave) return;
            int surplus = history.Count - MaxHistoryOnSave;
            history.RemoveRange(0, surplus);
            HistoryIndex = Math.Max(-1, HistoryIndex - surplus);
        }

        private static bool _saveOnExit, _loaded;

        /// <summary>
        /// Saves the settings when the app ends or crashes, so that things that change often (the history) need not be
        /// saved with every change. Never saves what was not read: a file that failed to load must not be overwritten.
        /// </summary>
        private static void SaveOnExit()
        {
            if (_saveOnExit) return;
            _saveOnExit = true;
            void Save()
            {
                if (!_loaded) return;
                try { Current.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
            }
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Save();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Save(); // the process ends after this
        }

        public void Load()
        {
            SaveOnExit();
            var file = Path.Combine(ConfigPath, SettingsFile);
            if (!File.Exists(file)) { _loaded = true; Localizer.Apply(Language); return; }
            try
            {
                var json = File.ReadAllText(file);
                var settings = JsonSerializer.Deserialize(json, JsonContext.Indented.Settings)
                    ?? throw new JsonException("The settings file is empty.");
                Books = settings.Books;
                CurrentBible = settings.CurrentBible;
                CurrentBookIndex = settings.CurrentBookIndex;
                CurrentChapter = settings.CurrentChapter;
                CurrentVerse = settings.CurrentVerse;
                Theme = settings.Theme;
                History = settings.History;
                HistoryIndex = settings.HistoryIndex;
                ShowWordsOfJesus = settings.ShowWordsOfJesus;
                ShowStrongs = settings.ShowStrongs;
                InMyCountryThereIsNoPersecution = settings.InMyCountryThereIsNoPersecution;
                ShowVerseNumbersInGreen = settings.ShowVerseNumbersInGreen;
                Hyphenate = settings.Hyphenate;
                FontSize = settings.FontSize;
                TextOpacity = settings.TextOpacity;
                FontFamily = settings.FontFamily;
                GreekFontFamily = settings.GreekFontFamily;
                HebrewFontFamily = settings.HebrewFontFamily;
                ConfigPath = settings.ConfigPath;
                Language = settings.Language;
                UiSize = settings.UiSize;
                WindowWidth = settings.WindowWidth;
                WindowHeight = settings.WindowHeight;
                Localizer.Apply(Language);
                // The dictionaries were switched on or off while the file was read, before the list was replaced.
                CrossBible.Library.Downloads.StrongsDictionaries.Refresh();
                _loaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex}");
            }
        }
    }
}
