using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// The JSON fixtures in Fixtures/, each a body Tableau sends or takes, with invented values; manifest.json says where
/// each comes from. They are read as the client will read them, through the source-generated context.
/// </summary>
internal static class ModelFixtures
{
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>Each fixture the manifest lists, and whether it is a request's body or a response's.</summary>
    public static IReadOnlyList<(string File, string Body)> Manifest()
    {
        using var manifest = JsonDocument.Parse(Text("manifest.json"));
        return
        [
            .. manifest.RootElement.GetProperty("fixtures").EnumerateArray()
                .Select(fixture => (fixture.GetProperty("file").GetString()!, fixture.GetProperty("body").GetString()!)),
        ];
    }

    public static string Text(string file) => File.ReadAllText(Path.Combine(Folder, file));

    public static TsResponse Response(string file) =>
        JsonSerializer.Deserialize(Text(file), TableauJsonContext.Default.TsResponse)
        ?? throw new InvalidDataException($"{file} is null");

    public static TsRequest Request(string file) =>
        JsonSerializer.Deserialize(Text(file), TableauJsonContext.Default.TsRequest)
        ?? throw new InvalidDataException($"{file} is null");

    public static string Write(TsResponse response) => JsonSerializer.Serialize(response, TableauJsonContext.Default.TsResponse);

    public static string Write(TsRequest request) => JsonSerializer.Serialize(request, TableauJsonContext.Default.TsRequest);

    /// <summary>Fails, naming the first difference, unless the two bodies say the same to Tableau.</summary>
    public static void AssertSameJson(string expected, string actual)
    {
        var difference = JsonEquivalence.Difference(expected, actual);
        Assert.IsNull(difference, $"{difference}\nwritten: {actual}");
    }
}
