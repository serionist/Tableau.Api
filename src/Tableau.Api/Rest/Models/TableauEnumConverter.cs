using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tableau.Api.Rest.Models;

/// <summary>An enumeration of the schema: a struct that keeps any text, the values it knows among them.</summary>
/// <typeparam name="TSelf">The struct.</typeparam>
internal interface ITableauEnum<TSelf> where TSelf : struct, ITableauEnum<TSelf>
{
    /// <summary>The value's text, as Tableau sends it.</summary>
    string Value { get; }

    /// <summary>The value with this text, known or not.</summary>
    static abstract TSelf Create(string value);
}

/// <summary>
/// Reads an enumeration's value from its JSON string, whether this version knows it or not, and writes its text back,
/// so a value Tableau adds later never fails a response.
/// </summary>
/// <typeparam name="T">The enumeration's struct.</typeparam>
internal sealed class TableauEnumConverter<T> : JsonConverter<T> where T : struct, ITableauEnum<T>
{
    /// <inheritdoc/>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? T.Create(reader.GetString()!)
            : throw new JsonException($"Expected a string for {typeof(T).Name}, found {reader.TokenType}.");

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}
