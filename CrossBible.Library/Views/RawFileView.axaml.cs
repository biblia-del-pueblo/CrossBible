using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace CrossBible.Library.UI;

/// <summary>
/// The markdown source of a book (<see cref="BookText.Book.Markdown"/>) in a text box, which can be edited and saved when the Bible is editable
/// (<see cref="BookText.IsEditable"/>); it replaces the text while "Show raw file" in the menu of the pen button is chosen (see <see cref="BibleToolbar.ShowRawFile"/>).
/// The text is read when the view is shown, and when the book changes while it is.
/// </summary>
public partial class RawFileView : UserControl
{
    /// <summary>The book whose source is shown (bound to the book of the position of the toolbar).</summary>
    public static readonly StyledProperty<BookText.Book?> BookProperty =
        AvaloniaProperty.Register<RawFileView, BookText.Book?>(nameof(Book));

    public BookText.Book? Book { get => GetValue(BookProperty); set => SetValue(BookProperty, value); }

    /// <summary>Raised when the close button is clicked, so the host can show the text again.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised after the book was saved (its file written, its text parsed again), so the host can show the new text.</summary>
    public event EventHandler? Saved;

    private string _loaded = ""; // the text as it was read or saved: what the text box is compared with
    private bool _loading, _saving;

    public RawFileView()
    {
        InitializeComponent();
        FlowDirection = Localizer.FlowDirection; // mirrored for Arabic and Hebrew
        RawBox.FlowDirection = Avalonia.Media.FlowDirection.LeftToRight; // the markup is read from the left
        PropertyChanged += (_, e) =>
        {
            if ((e.Property == IsVisibleProperty || e.Property == BookProperty) && IsVisible) Load();
            else if (e.Property == IsVisibleProperty)
            {
                _loading = true;
                RawBox.Text = null; // the text of a book is large: not kept while the view is away
                _loading = false;
            }
        };
    }

    private void SetStatus(string? text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void UpdateSave() =>
        SaveButton.IsEnabled = !_saving && !_loading && Book?.BookText?.IsEditable == true && !string.Equals(RawBox.Text ?? "", _loaded, StringComparison.Ordinal);

    private void Load()
    {
        var book = Book;
        TitleText.Text = book?.Name;
        FileText.Text = book?.File;
        SetStatus(null);
        _loading = true;
        try
        {
            _loaded = book?.Markdown ?? "";
            RawBox.Text = _loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _loaded = "";
            RawBox.Text = ex.Message;
        }
        finally { _loading = false; }
        RawBox.IsReadOnly = book?.BookText?.IsEditable != true; // only the source of an editable Bible is edited
        RawBox.CaretIndex = 0;
        UpdateSave();
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) => UpdateSave();

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Writes the text to the file of the book (the file as it was is kept once, as <c>.bak</c>), parses the book again, and tells the host. If the text does not
    /// parse, the file is not written.
    /// </summary>
    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Book is not { } book || book.BookText?.IsEditable != true) return;
        var text = RawBox.Text ?? "";
        var original = book.Markdown;
        _saving = true;
        UpdateSave();
        try
        {
            book.Markdown = text;
            var runs = await Task.Run(() =>
            {
                var parsed = BookText.ParseBibleBook(book).ToList(); // first: a text that does not parse is not written
                if (!string.IsNullOrEmpty(book.File))
                {
                    try { if (!File.Exists(book.File + ".bak")) File.Copy(book.File, book.File + ".bak"); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not back up {book.File}: {ex}"); }
                    File.WriteAllText(book.File, text, new System.Text.UTF8Encoding(false));
                }
                return parsed;
            });
            book.Runs = runs;
            _loaded = text;
            SetStatus(Localizer.Get("TrSaved"));
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            book.Markdown = original;
            SetStatus(ex.Message);
        }
        finally
        {
            _saving = false;
            UpdateSave();
        }
    }
}
