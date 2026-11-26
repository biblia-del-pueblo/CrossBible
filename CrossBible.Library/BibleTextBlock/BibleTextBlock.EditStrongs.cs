using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace CrossBible.Library.UI;

/// <summary>
/// The Strong's numbers in edit mode: the numbers of a word (<see cref="BookText.Run.StrongNumber"/>) are shown very small and blue below its end
/// (see FlatText), and a right click on the word opens a popup to change them.
/// </summary>
public partial class BibleTextBlock
{
    private Popup? _strongPopup;

    private bool FromStrongPopup(Avalonia.Interactivity.RoutedEventArgs e) =>
        _strongPopup is { IsOpen: true, Child: Visual child } && e.Source is Visual source
        && (ReferenceEquals(source, child) || Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(source).Contains(child));

    /// <summary>A right click while editing: the numbers of the word under the pointer can be changed. True when the click was handled (it was on such a word).</summary>
    /// <summary>
    /// The height of a line while a text with Strong's numbers is edited (0: as tall as its letters): the numbers of the words are drawn below them
    /// (see <see cref="DrawStrongNumbers"/>), in the room that the lines have extra.
    /// </summary>
    private double EditLineHeight => IsEditing && BookText?.HasStrongs == true ? FontSize * 2 : 0;

    /// <summary>The Strong's numbers of the editable words, very small and blue below each word. They are only drawn: they are not in the text, so they have no caret and cannot be selected or edited.</summary>
    private void DrawStrongNumbers(DrawingContext context)
    {
        if (!IsEditing || _flat is not { } window || window.StrongWords.Count == 0) return;
        double size = FontSize * 0.55;
        var typeface = new Typeface(FontFamily);
        foreach (var pl in _lines)
        {
            if (!IsInView(pl)) continue;
            int first = pl.Start, last = first + pl.Line.Length - pl.Line.NewLineLength;
            foreach (var word in window.StrongWords)
            {
                // below the end of the word: on the line that it ends on
                if (word.End <= word.Start || word.End - 1 < first || word.End - 1 >= last) continue;
                var numbers = string.Join(' ', (word.Run.StrongNumber ?? "").Split(new[] { ' ', ',', '\t', (char)160 }, StringSplitOptions.RemoveEmptyEntries));
                if (numbers.Length == 0) continue;
                int from = Math.Max(word.Start, first);
                var boxes = pl.Bounds(from, word.End - from);
                if (boxes.Count == 0) continue;
                double left = boxes.Min(b => b.Rectangle.Left), right = boxes.Max(b => b.Rectangle.Right);
                var text = new FormattedText(numbers, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, LinkBrush);
                double x = pl.Origin.X + (left + right - text.Width) / 2;
                double y = Math.Min(pl.Origin.Y + pl.Line.Baseline + 2, pl.Origin.Y + pl.Line.Height - text.Height);
                context.DrawText(text, new Point(x, y));
            }
        }
    }

    private bool OpenStrongEditor(Point position)
    {
        if (_flat is not { } window || _lines.Count == 0 || IndexAt(ToContent(position)) is not { } index) return false;

        // The word whose range holds the place, the smallest when a phrase holds a word with numbers of its own.
        BookText.Run? run = null;
        int best = int.MaxValue, start = 0, end = 0;
        foreach (var r in window.StrongWords)
            if (r.Run.IsEditable && index >= r.Start && index <= r.End && r.End - r.Start < best)
                (run, best, start, end) = (r.Run, r.End - r.Start, r.Start, r.End);
        if (run is null) return false;

        string word = window.Text[start..end];
        var box = new TextBox { Text = string.Join(' ', (run.StrongNumber ?? "").Split([' ', ',', ' '], StringSplitOptions.RemoveEmptyEntries)), MinWidth = 220, Watermark = "G1234 H7225" };
        var apply = new Button { Content = Localizer.Get("StrongsApply") };
        var remove = new Button { Content = Localizer.Get("StrongsRemove") };
        var popup = _strongPopup ??= new Popup { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true, PlacementTarget = this };
        if (!Children.Contains(popup)) Children.Add(popup);
        popup.IsOpen = false;
        popup.PlacementRect = new Rect(position.X, position.Y, 1, 1);

        var edited = run;
        void Apply(string numbers)
        {
            popup.IsOpen = false;
            SetStrongNumbers(edited, numbers);
            Focus();
        }
        apply.Click += (_, _) => Apply(box.Text ?? "");
        remove.Click += (_, _) => Apply("");
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Apply(box.Text ?? ""); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; Focus(); e.Handled = true; }
        };

        var card = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = word.Replace(' ', ' '), FontWeight = FontWeight.SemiBold, MaxWidth = 300, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = Localizer.Get("StrongsNumbers"), Opacity = 0.7, FontSize = 12 },
                    box,
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { apply, remove } },
                },
            },
        };
        card[!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SystemControlBackgroundAltHighBrush");
        card[!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SystemControlForegroundBaseLowBrush");
        popup.Child = card;
        popup.IsOpen = true;
        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
        return true;
    }

    /// <summary>Sets the Strong's numbers of a word (separated by spaces or commas); none takes them away.</summary>
    private void SetStrongNumbers(BookText.Run run, string input)
    {
        var numbers = string.Join(' ', input.Split([' ', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries).Select(n => n.ToUpperInvariant()));
        if ((run.StrongNumber ?? "") == numbers) return;
        PushUndo([new RunState(run, null, run.StrongNumber, true)]);
        run.StrongNumber = numbers.Length == 0 ? null : numbers;
        RefreshEdited([run]);
    }
}
