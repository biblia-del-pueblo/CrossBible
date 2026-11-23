using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CrossBible.Library.UI;

/// <summary>
/// Edit mode: while <see cref="IsEditing"/> is set, a text box with the markdown of the book that is read covers the text.
/// Leaving the mode saves the markdown (to the book's file too), parses the book again and shows the changed text where the reader was.
/// </summary>
public partial class BibleTextBlock
{
    /// <summary>True while the markdown of the current book is edited. Set by the pen button of <see cref="BibleToolbar"/>.</summary>
    public static readonly StyledProperty<bool> IsEditingProperty =
        AvaloniaProperty.Register<BibleTextBlock, bool>(nameof(IsEditing));

    public bool IsEditing { get => GetValue(IsEditingProperty); set => SetValue(IsEditingProperty, value); }

    private TextBox? _editor;
    private string _editedMarkdown = "";

    /// <summary>The book whose markdown is being edited (null when not editing).</summary>
    public BookText.Book? EditedBook { get; private set; }

    /// <summary>Whether the book at <see cref="Position"/> can be edited: it has a markdown of its own (a text of several Bibles read together has not).</summary>
    public bool CanEdit => Position?.Book is { } book && book.BookText?.Sources is not { Count: > 1 };

    private void OnIsEditingChanged(bool editing)
    {
        if (editing) BeginEdit();
        else _ = EndEditAsync();
    }

    private void BeginEdit()
    {
        if (!CanEdit || Position?.Book is not { } book)
        {
            SetCurrentValue(IsEditingProperty, false);
            return;
        }
        EditedBook = book;
        _editedMarkdown = book.Markdown;
        _editor = new TextBox
        {
            Text = _editedMarkdown,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, Monospace"),
            FontSize = FontSize,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        _editor[!BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SystemControlBackgroundAltHighBrush");
        Children.Add(_editor);
        InvalidateArrange();
        var editor = _editor;
        Dispatcher.UIThread.Post(() => editor.Focus(), DispatcherPriority.Input);
    }

    private async Task EndEditAsync()
    {
        var editor = _editor;
        var book = EditedBook;
        _editor = null;
        EditedBook = null;
        if (editor is null || book is null) return;
        var text = editor.Text ?? "";
        Children.Remove(editor);
        InvalidateArrange();
        Focus();
        if (text == _editedMarkdown) return;

        try
        {
            book.Markdown = text;
            // Parsing and writing the file are work for a worker thread.
            var runs = await Task.Run(() =>
            {
                var parsed = BookText.ParseBibleBook(book).ToList();
                if (!string.IsNullOrEmpty(book.File))
                {
                    try { System.IO.File.WriteAllText(book.File, text, new System.Text.UTF8Encoding(false)); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not save {book.File}: {ex}"); }
                }
                return parsed;
            });
            book.Runs = runs;
            lock (_pieceCache) _pieceCache.Clear(); // the pieces were made from the old runs
            await RebuildAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not apply the edited markdown: {ex}");
        }
    }
}
