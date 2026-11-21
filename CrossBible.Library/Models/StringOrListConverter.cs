using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrossBible.Library;

/// <summary>
/// Reads a list of strings from a JSON array, or from a single JSON string (a list of one), and always writes an array. For a setting that was one string
/// and became a list (<see cref="Settings.CurrentBible"/>): the settings files that are written already have the string, and a file that cannot be read
/// would make the reader start with none of its settings.
/// </summary>
public sealed class StringOrListConverter : JsonConverter<List<string>>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                if (reader.GetString() is { Length: > 0 } single) list.Add(single);
                return list;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.String) throw new JsonException("A list of strings was expected.");
                    if (reader.GetString() is { Length: > 0 } item) list.Add(item);
                }
                return list;
            default:
                throw new JsonException("A string or a list of strings was expected.");
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}
