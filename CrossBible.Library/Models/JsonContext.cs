using BibleMarkdown;
using System.Text.Json.Serialization;

namespace CrossBible.Library;

/// <summary>
/// The types that the reader reads and writes as JSON (source generation: no reflection, so that the code can be compiled ahead of time). The settings have
/// Bibles of several kinds, which are written with their names (see the attributes of <see cref="BookText"/>).
/// </summary>
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(InstallableBook))]
[JsonSerializable(typeof(string[]))]
internal partial class JsonContext : JsonSerializerContext
{
    public static readonly JsonContext Plain = new(JsonOptions.Create(indented: false));
    public static readonly JsonContext Indented = new(JsonOptions.Create(indented: true));
}
