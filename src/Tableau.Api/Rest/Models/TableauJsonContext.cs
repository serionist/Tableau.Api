using System.Text.Json.Serialization;

namespace Tableau.Api.Rest.Models;

/// <summary>
/// The source-generated JSON metadata of every model, for Tableau's REST API. Its other half,
/// <c>Generated/TableauJsonContext.g.cs</c>, lists the types.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Metadata only: deserialising needs it, and one path for both directions is one path to test.</item>
/// <item>A number may arrive as a string, since Tableau sends the schema's <c>xs:integer</c> values so.</item>
/// <item>A property left null is not written, so a request carries only what its caller set.</item>
/// <item>Each property names its JSON member itself, with the schema's name, so no naming policy applies.</item>
/// </list>
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class TableauJsonContext : JsonSerializerContext;
