using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrossBible.Library.Downloads;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrossBible.Library.UI;

/// <summary>One row of <see cref="OnlineBiblesView"/>: an eBible plus its install state.</summary>
public partial class OnlineBibleItem : ObservableObject
{
    public OnlineBibleItem(InstallableBook bible, OnlineBiblesView view)
    {
        Bible = bible;
        IsInstalled = CheckInstalled(bible);
        View = view;
    }

    OnlineBiblesView View { get; set; }
    public InstallableBook Bible { get; private set; }
    public void Upgrade() => Bible = Bible.Upgrade();
    private WeakReference<Avalonia.Media.IImage>? _coverRef; // the cover, as long as a row on the screen holds it
    private bool _hasNoCover;
    private static bool _fontsAdded;

    /// <summary>
    /// Gives the three files of the Bible Roman font to the renderer of the SVG: it looks fonts up in the system only,
    /// so without this the generated covers (<see cref="CoverFallback"/>) show a serif of the system unless the font is installed.
    /// </summary>
    private static void AddFonts()
    {
        if (_fontsAdded) return;
        _fontsAdded = true;
        foreach (var file in new[] { "BibleRoman", "BibleRomanBold", "BibleRomanItalic" })
        {
            try
            {
                using var asset = Avalonia.Platform.AssetLoader.Open(new Uri($"avares://CrossBible.Library/Assets/{file}.ttf"));
                var data = new MemoryStream();
                asset.CopyTo(data);
                data.Position = 0;
                Avalonia.Svg.Skia.SvgSource.s_skiaModel.Settings.TypefaceProviders.Insert(0,
                    new Svg.Skia.TypefaceProviders.CustomTypefaceProvider(data, 0));
            }
            catch (Exception) { /* no such font file: the serif of the system is used */ }
        }
    }

    /// <summary>The width a cover is decoded to for the row, in pixels: a little more than the 80 units it is shown at.</summary>
    private const int RowCoverWidth = 160;
    /// <summary>The width of the larger cover of the popup (three times the row).</summary>
    private const int PopupCoverWidth = 360;

    private static Avalonia.Media.IImage? LoadCover(InstallableBook bible, int width)
    {
        try
        {
            var image = bible.Image;
            if (CoverFallback.IsGenerated(image))
            {
                // Made now, and not stored: it is always the same for the same Bible. A long address from the settings of the first
                // version is replaced by the marker, so the settings file gets smaller the next time it is saved.
                bible.Image = CoverFallback.Marker;
                AddFonts();
                return new Avalonia.Svg.Skia.SvgImage { Source = Avalonia.Svg.Skia.SvgSource.LoadFromSvg(CoverFallback.Source(bible)) };
            }
            if (string.IsNullOrEmpty(image) || !File.Exists(image)) return null;
            if (Path.GetExtension(image).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                return new Avalonia.Svg.Skia.SvgImage { Source = Avalonia.Svg.Skia.SvgSource.Load(image, null) };
            using var stream = File.OpenRead(image);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception) { return null; /* a broken image file: no cover */ }
    }

    /// <summary>
    /// The cover of the Bible (<see cref="InstallableBook.Image"/> is the path of its file, or <see cref="CoverFallback.Marker"/> for one that is generated), a bitmap decoded small or an SVG
    /// (drawn as vectors, so sharp at any size); null if there is none. It is loaded when the row first asks for it and only held weakly: the
    /// row on the screen holds it, and when the row has been scrolled away it can be collected (a list of over a thousand Bibles
    /// with all their covers in memory took over a gigabyte on a phone).
    /// </summary>
    public Avalonia.Media.IImage? Cover
    {
        get
        {
            if (_hasNoCover) return null;
            if (_coverRef is not null && _coverRef.TryGetTarget(out var held)) return held;
            var cover = LoadCover(Bible, RowCoverWidth);
            if (cover is null) _hasNoCover = true;
            else _coverRef = new WeakReference<Avalonia.Media.IImage>(cover);
            return cover;
        }
    }

    /// <summary>The cover for the popup, larger: made when the popup is shown, and not kept (see <see cref="LargeCoverImage"/>).</summary>
    public Avalonia.Media.IImage? LargeCover => _hasNoCover ? null : LoadCover(Bible, PopupCoverWidth);

    public string Description =>
        string.Join(" · ", new[] { Bible.LanguageEnglishName, Bible.Language, Bible.CountryName, Bible.Origin }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete)),
        NotifyPropertyChangedFor(nameof(IsInstalledAndWide))]
    private bool _isInstalled;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInstall)), NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private bool _isBusy;

    /// <summary>Delete was clicked once: the row asks for a confirmation before it removes the Bible's files.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanDelete)), NotifyPropertyChangedFor(nameof(IsConfirmingDelete))]
    private bool _confirmDelete;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool CanInstall => !IsInstalled && !IsBusy;
    public bool CanDelete => IsInstalled && !IsBusy && !ConfirmDelete;
    public bool IsConfirmingDelete => IsInstalled && !IsBusy && ConfirmDelete;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsInstalledAndWide => IsInstalled && View.IsWide;

    /// <summary>Called by the view when <see cref="OnlineBiblesView.IsWide"/> changed: the bindings read it again.</summary>
    public void NotifyWideChanged() => OnPropertyChanged(nameof(IsInstalledAndWide));

    /// <summary>Installed means the Bible's folder holds converted books (the folder alone is created on access).</summary>
    private static bool CheckInstalled(InstallableBook bible)
    {
        if (string.IsNullOrEmpty(bible.Name)) return false;
        var dir = bible.ConfigPath;
        if (!Directory.Exists(dir)) return false;
        // A Strong's dictionary has no books: it is installed when its database is.
        return bible is BookArchive || (bible is IStrongDictionary ? File.Exists(Path.Combine(dir, BibleMarkdown.StrongsDatabase.FileName))
            : Directory.EnumerateFiles(dir, "*.md").Any());
    }
}

/// <summary>The Bibles that are shown side by side in one row of the list (fewer than the columns in the last row).</summary>
public sealed record OnlineBibleRow(IReadOnlyList<OnlineBibleItem> Items);

/// <summary>
/// The larger cover in the popup of a cover: it is read when the popup is shown (it is attached to the screen then) and let go when the popup
/// closes, so the rows do not each hold a large bitmap.
/// </summary>
public class LargeCoverImage : Avalonia.Controls.Image
{
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Source = (DataContext as OnlineBibleItem)?.LargeCover;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (Source as IDisposable)?.Dispose();
        Source = null;
    }
}

/// <summary>
/// Lists the Bibles offered by ebible.org, filtered by language (and a name search), and installs the chosen
/// one with <see cref="eBible.Install"/>. The layout is in OnlineBiblesView.axaml.
/// </summary>
public partial class OnlineBiblesView : UserControl
{
    public const bool ShowExceptions = true;
    private static string AllLanguages => Localizer.Get("AllLanguages");

    private List<OnlineBibleItem> _items = new();
    private List<OnlineBibleItem> _installed = new(); // the rows of the installed Bibles expander
    private bool _loading;

    /// <summary>Raised after a Bible was installed, so the host can add it to its list.</summary>
    public event EventHandler<InstallableBook>? BibleInstalled;

    /// <summary>Raised after a Bible was deleted, so the host can remove it from its list.</summary>
    public event EventHandler<InstallableBook>? BibleDeleted;

    /// <summary>The width above which the rows show the "Install" caption next to the button's icon.</summary>
    private const double WideWidth = 500;

    /// <summary>True while the view is wider than <see cref="WideWidth"/>.</summary>
    public static readonly StyledProperty<bool> IsWideProperty =
        AvaloniaProperty.Register<OnlineBiblesView, bool>(nameof(IsWide));

    public bool IsWide { get => GetValue(IsWideProperty); private set => SetValue(IsWideProperty, value); }

    static OnlineBiblesView()
    {
        // The rows' "Installed" caption depends on it through IsInstalledAndWide, which has to be announced per row.
        IsWideProperty.Changed.AddClassHandler<OnlineBiblesView>((view, _) =>
        {
            foreach (var item in view._items.Concat(view._installed)) item.NotifyWideChanged();
        });
    }

    public OnlineBiblesView()
    {
        InitializeComponent();
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        DataContext = this;
        LanguageBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == ComboBox.TextProperty) ApplyFilter();
        };
        ClassPopup.PlacementTarget = ClassButton;
        SizeChanged += (_, e) =>
        {
            IsWide = e.NewSize.Width > WideWidth;
            SetLanguageColumns(LanguageColumnsFor(e.NewSize.Width));
            int columns = ColumnsFor(e.NewSize);
            if (columns == _columns) return;
            _columns = columns;
            SetList(List, _shown);
            SetList(InstalledList, _installedShown);
        };
        AttachedToVisualTree += (_, _) => OnShown();
        // The page is kept while it is hidden, so what is installed may have changed since it was last shown.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) OnShown(); };
    }

    // The same rule as the columns of the text (BibleView): when the shorter side of the view is at least 600, as many columns
    // of at least 350 with 32 between them as fit; narrower, one.
    private const double TwoColumnsMinSize = 600, MinColumnWidth = 350, ColumnGap = 32, ListMargin = 24;
    private int _columns = 1;
    private IReadOnlyList<OnlineBibleItem> _shown = Array.Empty<OnlineBibleItem>();          // the filtered online list
    private IReadOnlyList<OnlineBibleItem> _installedShown = Array.Empty<OnlineBibleItem>(); // the installed Bibles

    private static int ColumnsFor(Size size) =>
        size.Width >= TwoColumnsMinSize
            ?Math.Max(1, (int)Math.Floor((size.Width - ListMargin + ColumnGap) / (MinColumnWidth + ColumnGap)))
            : 1;

    /// <summary>
    /// Shows the Bibles in the list: one row each, or with several columns rows of that many. The list stays virtualised, which a
    /// wrapping panel would not (every row of over a thousand would be made at once).
    /// </summary>
    private void SetList(ListBox box, IReadOnlyList<OnlineBibleItem> items)
    {
        var bibleRow = (Avalonia.Controls.Templates.IDataTemplate)Resources["BibleRow"]!;
        _selectedItem = null; _selectedCell = null; // a list that is made again has no selection
        box.Classes.Set("columns", _columns >= 2); // the style that hides the selection of a whole row
        // Empty first: a list that keeps its row containers would go on showing the old number of columns after the template changed.
        box.ItemsSource = null;
        if (_columns < 2)
        {
            box.ItemTemplate = bibleRow;
            box.ItemsSource = items;
            return;
        }
        int columns = _columns;
        // A row of equal cells; the last row has fewer Bibles, which keep the width of the others (the grid has all the columns).
        // Made in code: the number of columns is not known in the AXAML.
        box.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<OnlineBibleRow>((row, _) =>
        {
            // Called without data while the list replaces its items (and for a container that is recycled): nothing to show then.
            if (row is null) return null;
            var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = columns };
            foreach (var item in row.Items)
            {
                var cell = new ContentControl { Content = item, ContentTemplate = bibleRow, Margin = new Thickness(8, 0) };
                cell.Classes.Add("cell"); // hover and selection of this Bible alone: the list selects the whole row (see the styles)
                if (ReferenceEquals(item, _selectedItem)) { cell.Classes.Add("selected"); _selectedCell = cell; } // made again as the list scrolled
                cell.PointerPressed += (_, _) => SelectItem(item, cell); // not handled: the buttons of the row still get their clicks
                grid.Children.Add(cell);
            }
            return grid;
        });
        box.ItemsSource = Enumerable.Range(0, (items.Count + columns - 1) / columns)
            .Select(r => new OnlineBibleRow(items.Skip(r * columns).Take(columns).ToList()))
            .ToList();
    }

    // The selected Bible of a list with several columns, and its cell on the screen (a cell is made again when its row is scrolled
    // back into view, and then marks itself).
    private int _languageColumns;

    /// <summary>The columns of the language drop-down: three where the view is wide, two on a narrower screen (a phone).</summary>
    private static int LanguageColumnsFor(double width) => width >= 540 ? 3 : 2;

    private void SetLanguageColumns(int columns)
    {
        if (columns == _languageColumns) return;
        _languageColumns = columns;
        // Cells of equal width, in rows: a plain grid instead of the list that is virtualised, which is fine for a few hundred languages.
        LanguageBox.ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Avalonia.Controls.Panel?>(
            () => new Avalonia.Controls.Primitives.UniformGrid { Columns = columns });
    }

    private OnlineBibleItem? _selectedItem;
    private ContentControl? _selectedCell;

    private void SelectItem(OnlineBibleItem item, ContentControl cell)
    {
        _selectedCell?.Classes.Set("selected", false);
        _selectedItem = item;
        _selectedCell = cell;
        cell.Classes.Set("selected", true);
    }

    private bool _loadStarted;

    /// <summary>
    /// Called when the view is attached or becomes visible. The view is created (and attached) hidden together with
    /// the reader, so the list is only fetched the first time it is actually shown.
    /// </summary>
    private async void OnShown()
    {
        if (!IsVisible || TopLevel.GetTopLevel(this) is null) return;
        UpdateInstalled();
        // The setting is changed in the settings page, so it is read again every time this view is shown.
        ApplyFilter(); // without the confirmation this shows the hint instead of a list
        if (!Settings.Current.InMyCountryThereIsNoPersecution && NoneInstalled && !_persecutionAsked)
        {
            // Nothing to read yet and the answer is not known: ask once, then the list is shown or not.
            _persecutionAsked = true;
            PersecutionPanel.IsVisible = true;
            return;
        }
        if (!Settings.Current.InMyCountryThereIsNoPersecution || _loadStarted || _items.Count > 0) return;
        _loadStarted = true;
        await LoadAsync();
    }

    private bool _persecutionAsked;

    /// <summary>True while no Bible is installed (the Bibles of the "Installed Bibles" list).</summary>
    public static bool NoneInstalled =>
        !(Settings.Current.Books ?? new List<BookText>()).OfType<InstallableBook>().Any(b => b is not IStrongDictionary && !string.IsNullOrEmpty(b.Name));

    /// <summary>
    /// Whether the close button is there: it goes back to the text, and without a Bible there is no text (the reader shows this list
    /// instead, and the settings too go back to it).
    /// </summary>
    public static readonly StyledProperty<bool> CanCloseProperty = AvaloniaProperty.Register<OnlineBiblesView, bool>(nameof(CanClose), true);

    public bool CanClose { get => GetValue(CanCloseProperty); private set => SetValue(CanCloseProperty, value); }

    /// <summary>"Yes, there is persecution": the online Bibles stay unlisted (the setting stays off).</summary>
    private void OnPersecutionYesClick(object? sender, RoutedEventArgs e) => AnswerPersecution(noPersecution: false);

    /// <summary>"No persecution": the setting is switched on and the list is loaded.</summary>
    private void OnPersecutionNoClick(object? sender, RoutedEventArgs e) => AnswerPersecution(noPersecution: true);

    private void AnswerPersecution(bool noPersecution)
    {
        PersecutionPanel.IsVisible = false;
        Settings.Current.InMyCountryThereIsNoPersecution = noPersecution;
        try { Settings.Current.Save(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save the settings: {ex}"); }
        OnShown(); // shows the hint, or loads the list
    }

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Fills the "Installed Bibles" expander from <see cref="Settings.Books"/>.</summary>
    private void UpdateInstalled()
    {
        // The same rows as the online list: an installed Bible can be deleted from here too.
        var items = (Settings.Current.Books ?? new List<BookText>())
            .OfType<InstallableBook>()
            .Where(b => !string.IsNullOrEmpty(b.Name))
            .OrderBy(b => string.IsNullOrWhiteSpace(b.Name) ? b.EnglishName : b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new OnlineBibleItem(b, this))
            .ToList();
        _installed = items;
        _installedShown = items;
        SetList(InstalledList, items);
        InstalledHeader.Text = Localizer.Format("InstalledBiblesCount", items.Count);
        CanClose = !NoneInstalled;
    }

    // ---- Install from a file ------------------------------------------------------------------------

    private bool _installingFromFile;
    private TaskCompletionSource<string?>? _nameRequest;

    /// <summary>
    /// Picks a zip, xml file, asks for a name and installs it as a <see cref="BookArchive"/>: the books are
    /// imported into the Bible's folder and the Bible is added to the settings like an online one.
    /// </summary>
    private async void OnInstallFromFileClick(object? sender, RoutedEventArgs e)
    {
        if (_installingFromFile || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        _installingFromFile = true;
        InstallFromFileButton.IsEnabled = false;
        string? temporary = null;
        BookArchive? bible = null;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localizer.Get("InstallBibleFromFileTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(Localizer.Get("BibleArchives")) { Patterns = new[] { "*.zip", "*.xml" } } },
            });
            if (files.Count == 0) return;
            var file = files[0];

            // Some platforms (Android) give a content address instead of a path: copy that to a temporary file.
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(file.Name));
                await using var source = await file.OpenReadAsync();
                await using var target = System.IO.File.Create(temporary);
                await source.CopyToAsync(target);
                path = temporary;
            }

            LoadSpinner.IsVisible = true;

            var name = Path.GetFileNameWithoutExtension(file.Name);
            StatusText.Text = Localizer.Format("Installing", name);
            bible = new BookArchive(path);
            await bible.Install();
            bible = bible.Upgrade() as BookArchive;
            if (string.IsNullOrEmpty(bible.Name) && string.IsNullOrEmpty(bible.EnglishName))
            {
                var oldpath = bible.ConfigPath;
                name = await AskNameAsync(name);
                if (name is null) {
                    await bible.Uninstall();
                    return;
                }
                if (string.IsNullOrEmpty(bible.Name)) bible.Name = name;
                if (string.IsNullOrEmpty(bible.EnglishName)) bible.EnglishName = name;
                var newpath = bible.ConfigPath;
                if (oldpath != newpath) Directory.Move(oldpath, newpath);
            }
            if (!Directory.EnumerateFiles(bible.ConfigPath, "*.md").Any() &&
                !Directory.EnumerateFiles(bible.ConfigPath, "*.db").Any())
                throw new InvalidDataException(Localizer.Get("NoBooksImported"));

            await BibliaLibreBible.GetImagesAsync(bible);

            var settings = Settings.Current;
            settings.Books ??= new List<BookText>();
            settings.Books.Add(bible);
            settings.Save();
            UpdateInstalled();
            InstalledExpander.IsExpanded = true;
            StatusText.Text = Localizer.Format("NameInstalled", name);
            BibleInstalled?.Invoke(this, bible);
            bible = null; // installed: keep its folder
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.Format("InstallFromFileFailed", ShowExceptions ? ex.ToString() : ex.Message);
        }
        finally
        {
            // A failed install leaves a half filled folder behind: the name was free, so the folder is new.
            if (bible is not null)
                try { if (Directory.Exists(bible.ConfigPath)) Directory.Delete(bible.ConfigPath, true); } catch { }
            if (temporary is not null) try { System.IO.File.Delete(temporary); } catch { }
            LoadSpinner.IsVisible = _loading;
            InstallFromFileButton.IsEnabled = true;
            _installingFromFile = false;
        }
    }

    /// <summary>Shows the name panel; completes with the name, or null when it is cancelled.</summary>
    private Task<string?> AskNameAsync(string suggestion)
    {
        NameBox.Text = suggestion;
        NameError.IsVisible = false;
        NamePanel.IsVisible = true;
        NameBox.Focus();
        NameBox.SelectAll();
        _nameRequest = new TaskCompletionSource<string?>();
        return _nameRequest.Task;
    }

    private void FinishName(string? name)
    {
        NamePanel.IsVisible = false;
        var request = _nameRequest;
        _nameRequest = null;
        request?.TrySetResult(name);
    }

    private void OnNameOkClick(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        string? error =
            string.IsNullOrEmpty(name) ? Localizer.Get("EnterName")
            : Settings.Current.Books?.Any(b => string.Equals(b.EnglishName, name, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(b.Name, name, StringComparison.CurrentCultureIgnoreCase)) == true
                ? Localizer.Get("NameExists")
            : null;
        NameError.Text = error;
        NameError.IsVisible = error is not null;
        if (error is null) FinishName(name);
    }

    private void OnNameCancelClick(object? sender, RoutedEventArgs e) => FinishName(null);

    private void OnNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter) OnNameOkClick(sender, e);
        else if (e.Key == Avalonia.Input.Key.Escape) FinishName(null);
    }

    private async void OnReloadClick(object? sender, RoutedEventArgs e) => await LoadAsync();

    public async Task LoadAsync()
    {
        if (_loading || !Settings.Current.InMyCountryThereIsNoPersecution) return;
        _loading = true;
        LoadSpinner.IsVisible = true;
        StatusText.Text = Localizer.Get("LoadingModules");
        try
        {
            var bibles = await Task.Run(LoadSourcesAsync);
            _items = bibles.Select(b => new OnlineBibleItem(b, this)).ToList();
            FillLanguages();
            FillClasses();
            ApplyFilter();
            PresetLanguage();
        }
        catch (Exception ex)
        {
            StatusText.Text = Localizer.Format("CouldNotLoadList", ShowExceptions ? ex.ToString() : ex.Message);
        }
        finally
        {
            _loading = false;
            LoadSpinner.IsVisible = false;
        }
    }

    /// <summary>
    /// The modules of all the sources, in one list sorted by language and name. A source that cannot be reached is
    /// left out (the others are still listed); only when none can be reached is that an error.
    /// </summary>
    private static async Task<List<InstallableBook>> LoadSourcesAsync()
    {
        var sources = new Func<Task<IEnumerable<InstallableBook>>>[]
        {
            async () => await eBible.GetBiblesAsync(),
            async () => await CrossWireBook.GetBiblesAsync(),
            async () => await BibliaLibreBible.GetBooksAsync(),
            async () => await CrossWireDictionary.GetStrongsDictionariesAsync(),
        };
        var results = await Task.WhenAll(sources.Select(async load =>
        {
            try { return (Books: await load(), Error: (Exception?)null); }
            catch (Exception ex) { return (Books: Enumerable.Empty<InstallableBook>(), Error: ex); }
        }));

        var books = results.SelectMany(r => r.Books)
            .OrderBy(b => b.LanguageEnglishName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(b => b.Name ?? b.EnglishName ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (books.Count == 0 && results.FirstOrDefault(r => r.Error != null).Error is { } error) throw error;

        await BibliaLibreBible.GetImagesAsync(books);

        return books;
    }

    private InstallableBook.Classes _classFilter = InstallableBook.Classes.All;
    private DateTime _classClosedAt = DateTime.MinValue;

    /// <summary>The kinds of modules (<see cref="InstallableBook.Classes"/>) that are in the list: a choice of one, or all.</summary>
    private void FillClasses()
    {
        var present = _items.Select(i => i.Bible.Class).Distinct().Where(c => c != InstallableBook.Classes.All).Order().ToList();
        if (_classFilter != InstallableBook.Classes.All && !present.Contains(_classFilter)) _classFilter = InstallableBook.Classes.All;

        // A list with one selected entry (radio buttons do not exclude each other while the popup is closed, which
        // left "All" checked for good).
        _fillingClasses = true;
        try
        {
            var entries = present.Prepend(InstallableBook.Classes.All).Select(kind => new ListBoxItem
            {
                Tag = kind,
                Content = kind == InstallableBook.Classes.All ? $"{Localizer.Get("All")} ({_items.Count})" : $"{Localizer.ClassName(kind)} ({_items.Count(i => i.Bible.Class == kind)})",
            }).ToList();
            ClassList.ItemsSource = entries;
            ClassList.SelectedItem = entries.First(entry => (InstallableBook.Classes)entry.Tag! == _classFilter);
        }
        finally { _fillingClasses = false; }
        UpdateClassButton();
    }

    private bool _fillingClasses;

    /// <summary>A kind was chosen; choosing the chosen one again (nothing selected) or "All" turns the filter off.</summary>
    private void OnClassSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_fillingClasses) return;
        var kind = (ClassList.SelectedItem as ListBoxItem)?.Tag is InstallableBook.Classes chosen ? chosen : InstallableBook.Classes.All;
        if (ClassList.SelectedItem is null) // toggled off: "All" is the entry that shows it
        {
            _fillingClasses = true;
            ClassList.SelectedIndex = 0;
            _fillingClasses = false;
        }
        ClassPopup.IsOpen = false;
        if (kind == _classFilter) return;
        _classFilter = kind;
        UpdateClassButton();
        ApplyFilter();
    }

    /// <summary>The filter icon is tinted while a kind is chosen, so a shortened list is not a surprise.</summary>
    private void UpdateClassButton()
    {
        bool active = _classFilter != InstallableBook.Classes.All;
        ClassButton.Background = active ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#664C8BF5")) : Avalonia.Media.Brushes.Transparent;
        ToolTip.SetTip(ClassButton, active ? Localizer.Format("ShowingKind", Localizer.ClassName(_classFilter)) : Localizer.Get("FilterByKind"));
    }

    private void OnClassFilterClick(object? sender, RoutedEventArgs e)
    {
        if (ClassPopup.IsOpen) { ClassPopup.IsOpen = false; return; }
        // Pressing the button already light-dismissed the open popup; don't reopen it with the same click.
        if ((DateTime.UtcNow - _classClosedAt).TotalMilliseconds < 300) return;
        ClassPopup.IsOpen = true;
    }

    private void OnClassPopupClosed(object? sender, EventArgs e) => _classClosedAt = DateTime.UtcNow;

    private void FillLanguages()
    {
        var languages = _items
            .Select(i => LanguageOf(i.Bible))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        int i = 0;
        // Remove duplicates
        while (i < languages.Count-1)
        {
            while (i < languages.Count - 1 && languages[i + 1].Contains(languages[i], StringComparison.OrdinalIgnoreCase)) languages.RemoveAt(i + 1);
            i++;
        }
        // A language typed before the list was there is only text: there was no item to select, so SelectedItem is null. Replacing the
        // items clears the text of the box, and it must not stay empty.
        var typed = LanguageBox.Text;
        var selected = LanguageBox.SelectedItem as string;
        LanguageBox.ItemsSource = new[] { AllLanguages }.Concat(languages).ToList();
        if (selected is not null && ((IEnumerable<string>)LanguageBox.ItemsSource).Contains(selected)) LanguageBox.SelectedItem = selected;
        else LanguageBox.Text = typed ?? "";
    }

    private bool _languagePreset;

    /// <summary>
    /// The first time the list is there, the language filter is the language of the interface ("de"; ApplyFilter turns the code into the names of the language). It is
    /// not set when the reader chose a language already, nor when no module is in that language (the list would be empty), and not again after a reload.
    /// </summary>
    private void PresetLanguage()
    {
        if (_languagePreset || _items.Count == 0) return;
        _languagePreset = true;
        if (!string.IsNullOrWhiteSpace(LanguageBox.Text)) return;
        var culture = Localizer.Culture;
        if (culture.Name.Length == 0) return; // the invariant culture has no language
        LanguageBox.Text = culture.TwoLetterISOLanguageName; // filters the list
        if (_shown.Count == 0) LanguageBox.Text = ""; // no module in this language: all of them
    }

    private static string LanguageOf(InstallableBook bible) =>
        string.IsNullOrWhiteSpace(bible.LanguageEnglishName) ? bible.Language : bible.LanguageEnglishName;

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (LanguageBox is null || SearchBox is null || StatusText is null) return; // during InitializeComponent
        if (!Settings.Current.InMyCountryThereIsNoPersecution)
        {
            _shown = Array.Empty<OnlineBibleItem>();
            SetList(List, _shown);
            StatusText.Text = Localizer.Format("PersecutionHint", Localizer.Get("NoPersecution"));
            return;
        }
        var language = LanguageBox.Text?.Trim();
        if (string.IsNullOrEmpty(language)) language = AllLanguages;
        string langenglish = null;
        if (Regex.IsMatch(language, "[a-z]{2,3}(?:-[A-Z]{2})?"))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(language);
                language = Regex.Replace(culture.NativeName, @"\s+|\(.*?\)", "", RegexOptions.Singleline);
                langenglish = Regex.Replace(culture.EnglishName, @"\s+|\(.*?\)", "", RegexOptions.Singleline);
            }
            catch { }
        }
        var text = SearchBox.Text?.Trim();
        var filtered = _items.Where(i =>
            (_classFilter == InstallableBook.Classes.All || i.Bible.Class == _classFilter) &&
            (language == AllLanguages || i.Bible.Language.Contains(language, StringComparison.CurrentCultureIgnoreCase) ||
            i.Bible.LanguageEnglishName.Contains(language, StringComparison.CurrentCultureIgnoreCase) ||
            langenglish != null && 
            (i.Bible.Language.Contains(langenglish, StringComparison.CurrentCultureIgnoreCase) ||
            i.Bible.LanguageEnglishName.Contains(langenglish, StringComparison.CurrentCultureIgnoreCase))) &&
            (string.IsNullOrEmpty(text) ||
             (i.Bible.Name?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
             (i.Bible.EnglishName?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false)))
            .ToList();
        _shown = filtered;
        SetList(List, filtered);
        if (_items.Count > 0) StatusText.Text = Localizer.Format("ModulesCount", filtered.Count, _items.Count);
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OnlineBibleItem { CanDelete: true } item) item.ConfirmDelete = true;
    }

    private void OnCancelDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is OnlineBibleItem item) item.ConfirmDelete = false;
    }

    /// <summary>Removes the Bible's folder (the converted books and the downloaded source) and its entry in the settings.</summary>
    private async void OnConfirmDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not OnlineBibleItem { IsConfirmingDelete: true } item) return;
        item.Error = null;
        item.IsBusy = true;
        try
        {
            await Task.Run(() => item.Bible.Uninstall());
            var settings = Settings.Current;
            settings.Books?.RemoveAll(b => b.Id == item.Bible.Id);
            settings.Save();
            StrongsDictionaries.Refresh();
            item.IsInstalled = false;
            // The Bible may be shown in both lists (the rows are separate objects): the online one is no longer installed.
            foreach (var other in _items.Where(o => o.Bible.Id == item.Bible.Id)) other.IsInstalled = false;
            UpdateInstalled();
            BibleDeleted?.Invoke(this, item.Bible);
        }
        catch (Exception ex)
        {
            item.Error = Localizer.Format("DeleteFailed", ShowExceptions ? ex.ToString() : ex.Message);
        }
        finally
        {
            item.ConfirmDelete = false;
            item.IsBusy = false;
        }
    }

    private Point? _coverPressedAt;

    // The press is only noted (not handled: the list still scrolls and selects). The Tapped event is not used: inside a list item it is
    // not raised reliably, so a tap is a release on the cover close to where the press began.
    private void OnCoverPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) =>
        _coverPressedAt = sender is Avalonia.Controls.Image image && e.GetCurrentPoint(image).Properties.IsLeftButtonPressed ? e.GetPosition(image) : null;

    /// <summary>
    /// Clicking or touching a cover shows it large (three times the size of the row, like the popup of a hover);
    /// a click or touch anywhere closes it. It is shown beside the cover, as the tooltip of a hover is. A flyout opens it: a bare Popup made in code is not part of the visual tree, and is not reliable.
    /// </summary>
    private void OnCoverReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Image { DataContext: OnlineBibleItem item } image || _coverPressedAt is not { } start) return;
        _coverPressedAt = null;
        var end = e.GetPosition(image);
        // A drag (the list scrolling) or a release off the cover is no tap.
        if (Math.Abs(end.X - start.X) > 10 || Math.Abs(end.Y - start.Y) > 10 || !new Rect(image.Bounds.Size).Contains(end)) return;
        e.Handled = true;
        var flyout = new Avalonia.Controls.Flyout
        {
            Placement = Avalonia.Controls.PlacementMode.Right, // next to the cover, like the tooltip of a hover
            ShowMode = Avalonia.Controls.FlyoutShowMode.Transient, // light dismiss: any click or touch outside closes it
            Content = new LargeCoverImage { DataContext = item, Width = 240, Height = 336, Stretch = Avalonia.Media.Stretch.Uniform },
        };
        flyout.ShowAt(image); // beside the small cover; the popup moves aside by itself where there is no room
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not OnlineBibleItem item || !item.CanInstall) return;
        item.Error = null;
        item.IsBusy = true;
        try
        {
            await item.Bible.Install();
            item.Upgrade();
            item.IsInstalled = true;
            var settings = Settings.Current;
            settings.Books ??= new List<BookText>();
            if (!settings.Books.OfType<InstallableBook>().Any(b => b.Id == item.Bible.Id))
                settings.Books.Add(item.Bible);
            settings.Save();
            StrongsDictionaries.Refresh(); // a dictionary that was installed counts only once it is in the list
            UpdateInstalled();
            BibleInstalled?.Invoke(this, item.Bible);
        }
        catch (Exception ex)
        {
            item.Error = Localizer.Format("InstallFailed", ShowExceptions ? ex.ToString() : ex.Message);
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
