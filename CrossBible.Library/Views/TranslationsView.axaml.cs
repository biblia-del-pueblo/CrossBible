using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CrossBible.Library.Downloads;

namespace CrossBible.Library.UI;

/// <summary>
/// The page of the translations; it replaces the text while the toolbar menu's "Translate…" is chosen (see <see cref="BibleToolbar.ShowTranslations"/>).
/// A list at the top has the translations that exist (<see cref="BookTranslation.Translations"/>) and "New Translation"; below it are the fields of the info.json of
/// the chosen translation. An existing translation is saved; a new one is made empty ("New") or from the books of an installed Bible ("Create from",
/// see <see cref="BookTranslation.CreateFrom"/>).
/// </summary>
public partial class TranslationsView : UserControl
{
    /// <summary>An entry of the list of translations: the folder of the translation, or none for "New Translation".</summary>
    private sealed record Choice(string? Folder, string Text)
    {
        public override string ToString() => Text;
    }

    private const string InfoFile = "info.json";

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    private bool _loading;
    private bool _busy;
    private List<InstallableBook> _sources = new();

    public TranslationsView()
    {
        InitializeComponent();
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        AttachedToVisualTree += (_, _) => Reload(null);
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) Reload(SelectedFolder); };
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private string? SelectedFolder => (TranslationBox.SelectedItem as Choice)?.Folder;

    private static List<string> ExistingFolders()
    {
        try
        {
            Directory.CreateDirectory(BookTranslation.RootPath);
            return BookTranslation.Translations.Select(t => t.FolderName).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cannot list the translations: {ex}");
            return new List<string>();
        }
    }

    /// <summary>Fills the lists (the translations, the Bibles to create from) and chooses <paramref name="select"/> (the new translation when there is none).</summary>
    private void Reload(string? select)
    {
        _loading = true;
        try
        {
            var choices = new List<Choice> { new(null, Localizer.Get("TrNewTranslation")) };
            choices.AddRange(ExistingFolders().Select(folder => new Choice(folder, folder)));
            TranslationBox.ItemsSource = choices;
            TranslationBox.SelectedItem = choices.FirstOrDefault(c => c.Folder is not null && c.Folder == select) ?? choices[0];

            // The Bibles that are installed (not the Strong's dictionaries, and not the translations), by language and name.
            _sources = (Settings.Current.Books ?? new List<BookText>())
                .OfType<InstallableBook>()
                .Where(b => !string.IsNullOrEmpty(b.EnglishName) && b is not IStrongDictionary && Directory.Exists(b.ConfigPath))
                .OrderBy(b => b.LanguageCode ?? "").ThenBy(b => b.Name ?? b.EnglishName ?? "")
                .ToList();
            SourceBox.ItemsSource = _sources.Select(b => b.Name ?? b.EnglishName).ToList();
            SourceBox.SelectedIndex = _sources.Count > 0 ? 0 : -1;
        }
        finally { _loading = false; }
        LoadFields();
    }

    private void OnTranslationChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loading) LoadFields();
    }

    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        // For a new translation the language of the source is the one to begin with (the name is for the reader to give).
        if (_loading || SelectedFolder is not null || SourceBox.SelectedIndex < 0) return;
        var source = _sources[SourceBox.SelectedIndex];
        LanguageBox.Text = source.Language;
        LanguageEnglishNameBox.Text = source.LanguageEnglishName;
        LanguageCodeBox.Text = source.LanguageCode;
        CountryCodeBox.Text = source.CountryCode;
        CountryNameBox.Text = source.CountryName;
        VersificationBox.Text = source.Versification;
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void UpdateButtons()
    {
        bool isNew = SelectedFolder is null;
        ExistingPanel.IsVisible = !isNew;
        NewPanel.IsVisible = isNew;
        SaveButton.IsEnabled = CoverButton.IsEnabled = DeleteButton.IsEnabled = NewButton.IsEnabled = !_busy;
        CreateFromButton.IsEnabled = !_busy && SourceBox.SelectedIndex >= 0;
    }

    /// <summary>Shows the info.json of the chosen translation in the fields (empty fields for a new one).</summary>
    private void LoadFields()
    {
        SetStatus(null);
        InstallableBook? info = null;
        if (SelectedFolder is { } folder)
        {
            try
            {
                var file = Path.Combine(new BookTranslation { FolderName = folder }.ConfigPath, InfoFile);
                if (File.Exists(file)) info = JsonSerializer.Deserialize(File.ReadAllText(file, System.Text.Encoding.UTF8), JsonContext.Plain.InstallableBook);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                SetStatus(ex.Message);
            }
        }
        _deleteArmed = false;
        DeleteButton.Content = Localizer.Get("TrDelete");
        ShowCover();
        NameBox.Text = info?.Name;
        EnglishNameBox.Text = info?.EnglishName;
        LanguageBox.Text = info?.Language;
        LanguageEnglishNameBox.Text = info?.LanguageEnglishName;
        LanguageCodeBox.Text = info?.LanguageCode;
        CountryCodeBox.Text = info?.CountryCode;
        CountryNameBox.Text = info?.CountryName;
        VersificationBox.Text = info?.Versification;
        UpdateButtons();
    }

    /// <summary>The fields as the properties of the info.json (the versification only when there is one).</summary>
    private List<(string Property, string Value)> FieldValues(bool isNew)
    {
        var values = new List<(string, string)>();
        if (isNew) values.Add((nameof(InstallableBook.Class), InstallableBook.Classes.Bible.ToString()));
        values.Add((nameof(InstallableBook.Name), NameBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.EnglishName), EnglishNameBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.Language), LanguageBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.LanguageEnglishName), LanguageEnglishNameBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.LanguageCode), LanguageCodeBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.CountryCode), CountryCodeBox.Text?.Trim() ?? ""));
        values.Add((nameof(InstallableBook.CountryName), CountryNameBox.Text?.Trim() ?? ""));
        if (!string.IsNullOrWhiteSpace(VersificationBox.Text)) values.Add((nameof(InstallableBook.Versification), VersificationBox.Text.Trim()));
        return values;
    }

    /// <summary>The name of the folder of a new translation: its name (else its English name) without the characters that a file name cannot have.</summary>
    private string NewFolderName()
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? EnglishNameBox.Text : NameBox.Text;
        return new string((name ?? "").Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim().TrimEnd('.');
    }

    /// <summary>
    /// Puts the translation in <see cref="Settings.Books"/> (its entry is updated from the fields when it is there already), and saves the settings, so that it is
    /// one of the Bibles that can be read. The names and the language are those of the fields.
    /// </summary>
    private void Register(string folder)
    {
        var books = Settings.Current.Books ??= new List<BookText>();
        var translation = books.OfType<BookTranslation>().FirstOrDefault(t => string.Equals(t.FolderName, folder, StringComparison.OrdinalIgnoreCase));
        bool added = translation is null;
        translation ??= new BookTranslation { FolderName = folder };

        string name = NameBox.Text?.Trim() ?? "";
        string englishName = EnglishNameBox.Text?.Trim() ?? "";
        translation.Class = InstallableBook.Classes.Bible;
        translation.Name = name.Length > 0 ? name : englishName.Length > 0 ? englishName : folder;
        translation.EnglishName = englishName.Length > 0 ? englishName : translation.Name; // the list of Bibles is made of the entries that have an English name
        translation.Language = LanguageBox.Text?.Trim() ?? "";
        translation.LanguageEnglishName = LanguageEnglishNameBox.Text?.Trim() ?? "";
        translation.LanguageCode = LanguageCodeBox.Text?.Trim() ?? "";
        translation.CountryCode = CountryCodeBox.Text?.Trim() ?? "";
        translation.CountryName = CountryNameBox.Text?.Trim() ?? "";
        translation.Versification = string.IsNullOrWhiteSpace(VersificationBox.Text) ? null : VersificationBox.Text.Trim();
        if (added) books.Add(translation);
        SaveSettings();
    }

    private static async void SaveSettings()
    {
        try { await Settings.Current.SaveAsync(); } // the file is written off the UI thread
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
    }

    // ---- Cover image and delete ------------------------------------------------------------------------

    private bool _deleteArmed; // "Delete" was clicked once: the next click deletes

    /// <summary>The cover image of the chosen translation (<c>cover.*</c> in its folder), or none.</summary>
    private static string? CoverFile(string folder)
    {
        var path = new BookTranslation { FolderName = folder }.ConfigPath;
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, "cover.*").FirstOrDefault() : null;
    }

    private void ShowCover()
    {
        var old = CoverImage.Source as Bitmap;
        CoverImage.Source = null;
        old?.Dispose();
        CoverImage.IsVisible = false;
        if (SelectedFolder is not { } folder || CoverFile(folder) is not { } file) return;
        try
        {
            using var stream = File.OpenRead(file);
            CoverImage.Source = new Bitmap(stream);
            CoverImage.IsVisible = true;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Cannot show {file}: {ex.Message}"); }
    }

    /// <summary>Chooses an image and stores it as <c>cover.ext</c> (the extension of the file that was chosen) in the folder of the translation.</summary>
    private async void OnCoverClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedFolder is not { } folder || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            });
            if (files.Count == 0) return;
            var chosen = files[0];

            // Read through the stream (a file of the phone has no path), and check that it is an image before the translation is touched.
            using var data = new MemoryStream();
            await using (var source = await chosen.OpenReadAsync()) await source.CopyToAsync(data);
            data.Position = 0;
            try { using var test = new Bitmap(data); }
            catch (Exception) { SetStatus(Localizer.Get("TrNotImage")); return; }

            var extension = Path.GetExtension(chosen.Name).ToLowerInvariant();
            var path = new BookTranslation { FolderName = folder }.ConfigPath;
            Directory.CreateDirectory(path);
            foreach (var old in Directory.EnumerateFiles(path, "cover.*")) File.Delete(old); // one cover only, whatever its extension
            await File.WriteAllBytesAsync(Path.Combine(path, "cover" + extension), data.ToArray());
            ShowCover();
            SetStatus(Localizer.Get("TrCoverSet"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message);
        }
    }

    /// <summary>The first click asks "Really delete?", the second deletes the folder of the translation and takes it out of the settings.</summary>
    private void OnDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedFolder is not { } folder) return;
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteButton.Content = Localizer.Get("TrConfirmDelete");
            return;
        }
        try
        {
            var path = new BookTranslation { FolderName = folder }.ConfigPath;
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Settings.Current.Books?.RemoveAll(b => b is BookTranslation t && string.Equals(t.FolderName, folder, StringComparison.OrdinalIgnoreCase));
            SaveSettings();
            Reload(null);
            SetStatus(Localizer.Get("TrDeleted"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _deleteArmed = false;
            DeleteButton.Content = Localizer.Get("TrDelete");
            SetStatus(ex.Message);
        }
    }

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SelectedFolder is not { } folder) return;
        try
        {
            BibleMarkdown.Importer.WriteInfoValues(new BookTranslation { FolderName = folder }.ConfigPath, FieldValues(false), onlyMissing: false);
            Register(folder);
            SetStatus(Localizer.Get("TrSaved"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message);
        }
    }

    /// <summary>Checks the name of a new translation; the translation, or null (the status says why).</summary>
    private BookTranslation? NewTranslation()
    {
        var folder = NewFolderName();
        if (folder.Length == 0) { SetStatus(Localizer.Get("TrNameRequired")); return null; }
        var translation = new BookTranslation { FolderName = folder };
        if (Directory.Exists(translation.ConfigPath)) { SetStatus(Localizer.Format("TrExists", folder)); return null; }
        return translation;
    }

    private void OnNewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (NewTranslation() is not { } translation) return;
        try
        {
            Directory.CreateDirectory(translation.ConfigPath);
            BibleMarkdown.Importer.WriteInfoValues(translation.ConfigPath, FieldValues(true), onlyMissing: false);
            Register(translation.FolderName);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus(ex.Message);
            return;
        }
        Reload(translation.FolderName);
        SetStatus(Localizer.Get("TrCreated"));
    }

    private async void OnCreateFromClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SourceBox.SelectedIndex < 0 || NewTranslation() is not { } translation) return;
        var source = _sources[SourceBox.SelectedIndex];
        var values = FieldValues(false);
        _busy = true;
        UpdateButtons();
        SetStatus(Localizer.Get("TrCopying"));
        try
        {
            // Copying the books of a Bible is work for a worker thread; the info.json of the source comes with them, and the fields are put into it.
            await Task.Run(() =>
            {
                Directory.CreateDirectory(translation.ConfigPath);
                translation.CreateFrom(source);
                BibleMarkdown.Importer.WriteInfoValues(translation.ConfigPath, values, onlyMissing: false);
            });
            _busy = false;
            Register(translation.FolderName);
            Reload(translation.FolderName);
            SetStatus(Localizer.Get("TrCreated"));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _busy = false;
            UpdateButtons();
            SetStatus(ex.Message);
        }
    }
}
