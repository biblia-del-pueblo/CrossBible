using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace CrossBible.Library;

/// <summary>
/// The texts of the app in the language of the reader. They are in Resources/Strings.resx (English) and
/// Resources/Strings.de.resx (German); more languages are more files, Strings.fr.resx and so on. The language is
/// the one of the device unless the reader chose one in the settings (<see cref="Settings.Language"/>).
/// </summary>
public static class Localizer
{
    // The texts of every language are in this assembly (see the EmbeddedResource in CrossBible.Library.csproj), as Strings.resources for English and
    // Strings.de.resources and so on for the others, and they are read here, not by a ResourceManager: that one looks for the other languages in satellite
    // assemblies, which an app compiled ahead of time (native AOT) cannot load.
    static readonly Assembly Source = typeof(Localizer).Assembly;
    static readonly ConcurrentDictionary<string, Dictionary<string, string>?> Tables = new();

    /// <summary>The texts of one language (null when there is no such language), by name: "" is the English of Strings.resx, "de" the German.</summary>
    static Dictionary<string, string>? Table(string language) => Tables.GetOrAdd(language, name =>
    {
        using var stream = Source.GetManifestResourceStream("CrossBible.Library.Resources.Strings" + (name.Length == 0 ? "" : "." + name) + ".resources");
        if (stream is null) return null;
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new ResourceReader(stream);
        foreach (System.Collections.DictionaryEntry entry in reader)
            if (entry.Key is string key && entry.Value is string text) table[key] = text;
        return table;
    });

    /// <summary>The text for a key in a culture: the culture's own language (zh-Hans), then the language it belongs to (de for de-CH), then English.</summary>
    static string? Lookup(string key, CultureInfo culture)
    {
        for (var c = culture; c != null && !string.IsNullOrEmpty(c.Name); c = c.Parent)
            if (Table(c.Name) is { } table && table.TryGetValue(key, out var text)) return text;
        return Table("") is { } neutral && neutral.TryGetValue(key, out var english) ? english : null;
    }

    /// <summary>The culture of the texts. The device's language until <see cref="Apply"/> chooses another.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Right to left for the languages that are written so (Arabic, Hebrew), else left to right.</summary>
    public static Avalonia.Media.FlowDirection FlowDirection =>
        Culture.TextInfo.IsRightToLeft ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight;

    /// <summary>Takes the language that the reader chose: "en", "de" and so on, or null (or "System") for the language of the device.</summary>
    public static void Apply(string? language)
    {
        try { Culture = string.IsNullOrWhiteSpace(language) || language == "System" ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { Culture = CultureInfo.CurrentUICulture; }
    }

    /// <summary>
    /// Raised by <see cref="Change"/> after the language changed. The head of the app (the desktop app, the Android app)
    /// answers it by opening its main window or view again, which builds all the texts in the new language.
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// The reader chose another language: it is taken, and <see cref="LanguageChanged"/> is raised.
    /// </summary>
    /// <returns>False when nobody listens to <see cref="LanguageChanged"/>: the texts then change when the app is started again.</returns>
    public static bool Change(string? language)
    {
        Apply(language);
        var handlers = LanguageChanged;
        handlers?.Invoke(null, EventArgs.Empty);
        return handlers is not null;
    }

    /// <summary>The text for a key; the key itself when there is none, so a missing text is seen and does not crash the app.</summary>
    public static string Get(string key) => Lookup(key, Culture) ?? key;

    /// <summary>The text for a key with the arguments put in its {0}, {1} ...</summary>
    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);

    /// <summary>The name of a kind of module ("Bible", "Dictionary" ...).</summary>
    public static string ClassName(InstallableBook.Classes kind) => Get("Class_" + kind);
}
