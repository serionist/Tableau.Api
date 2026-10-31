using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tableau.Api.Rest.Models;

/// <summary>
/// A repeated element that sits beside others, such as <c>"granteeCapabilities":[...]</c>: read from an array, or from
/// a single object as a list of one; written as an array.
/// </summary>
/// <typeparam name="T">The item's type.</typeparam>
internal sealed class TableauListConverter<T> : JsonConverter<IReadOnlyList<T>>
{
    /// <inheritdoc/>
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TableauLists.ReadItems<T>(ref reader, options);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options) =>
        TableauLists.WriteItems(writer, value, options);
}

/// <summary>
/// A list inside its wrapper object, as Tableau's JSON keeps the XML's list elements: <c>"tags":{"tag":[...]}</c>. An
/// empty wrapper, <c>{}</c>, reads as an empty list and an empty list writes as one; a single object in place of the
/// array reads as a list of one. Each wrapper of the schema has a converter generated from this one, with its item's
/// name.
/// </summary>
/// <typeparam name="T">The item's type.</typeparam>
/// <param name="itemName">The name of the item's element inside the wrapper, such as <c>tag</c>.</param>
internal abstract class TableauWrappedListConverter<T>(string itemName) : JsonConverter<IReadOnlyList<T>>
{
    /// <inheritdoc/>
    public override IReadOnlyList<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected the {itemName} list's wrapper object, found {reader.TokenType}.");
        IReadOnlyList<T>? items = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isItem = reader.ValueTextEquals(itemName);
            reader.Read();
            // A member the schema doesn't name is skipped, as on any model.
            if (isItem) items = TableauLists.ReadItems<T>(ref reader, options);
            else reader.Skip();
        }
        return items ?? [];
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Count > 0)
        {
            writer.WritePropertyName(itemName);
            TableauLists.WriteItems(writer, value, options);
        }
        writer.WriteEndObject();
    }
}

/// <summary>What both list converters share: the items, read and written through the context's metadata.</summary>
internal static class TableauLists
{
    /// <summary>An array's items, or a single value as a list of one; a null item is dropped.</summary>
    public static IReadOnlyList<T> ReadItems<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return [];
        var info = Info<T>(options);
        var items = new List<T>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            Add(items, JsonSerializer.Deserialize(ref reader, info));
            return items.AsReadOnly();
        }
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            Add(items, JsonSerializer.Deserialize(ref reader, info));
        return items.AsReadOnly();
    }

    /// <summary>The items as an array, a list of one included, as Tableau sends them.</summary>
    public static void WriteItems<T>(Utf8JsonWriter writer, IReadOnlyList<T> items, JsonSerializerOptions options)
    {
        var info = Info<T>(options);
        writer.WriteStartArray();
        foreach (var item in items) JsonSerializer.Serialize(writer, item, info);
        writer.WriteEndArray();
    }

    private static void Add<T>(List<T> items, T? item)
    {
        if (item is not null) items.Add(item);
    }

    // The context lists every item type, so its metadata is there, and no reflection is needed.
    private static JsonTypeInfo<T> Info<T>(JsonSerializerOptions options) => (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
}
