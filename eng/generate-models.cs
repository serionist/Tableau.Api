#:property RestorePackagesWithLockFile=false
#:property PublishAot=false
// The model generator: dotnet run eng/generate-models.cs -- [--check]
//
// It writes the 2.0 models of Tableau's REST API into src/Tableau.Api/Rest/Models/Generated/ from Tableau's schema,
// eng/schema/ts-api_3_29.xsd, compiled with System.Xml.Schema: one file per type, ordered by name, LF line endings, and
// a header naming the schema and its SHA-256, so every run on any OS writes the same bytes. With --check it writes
// nothing, and exits 1 when the folder holds anything but what it would write; the gate's quick lane runs it that way,
// so CI proves the models are current. Nothing in the folder is edited by hand: an addition goes in a partial
// declaration in src/Tableau.Api/Rest/Models/.
//
// The mapping (docs/plans/2026-10-28-modernization.md, Models and the generator):
// - A named complex type, workbookType, is a public sealed partial record, Workbook, with init properties; an anonymous
//   one is named after its owner and its element, ViewUsage. tsRequest and tsResponse are internal records: Tableau's
//   JSON sends their child at the top level, with no wrapper.
// - Every attribute and element is a nullable property, required or not, with the schema's name in [JsonPropertyName].
// - xs:string is string, xs:boolean bool, xs:int int, xs:long and the xs:integer family long, xs:decimal decimal,
//   xs:dateTime DateTimeOffset, xs:time TimeOnly. A union of integers is long, any other union string.
// - An enumeration of strings is a struct that keeps its text, the known values its static properties, so a value
//   Tableau adds later still reads. An enumeration of numbers is the number.
// - A type with one repeated element and nothing else is a list wrapper: a property of that type is an IReadOnlyList,
//   read and written inside its wrapper object, {"tag":[...]}, by a converter generated for it. A repeated element beside
//   others is an IReadOnlyList of its own. Both read a single object as a list of one.
// - xs:choice is one property per alternative. xs:extension takes the base's members; a type that others extend takes
//   theirs too, since JSON carries no xsi:type to tell them apart. Text beside attributes is the property Value.
// Anything else stops it with an error naming the construct, so a new schema can't drop a type or a member silently.
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Schema;

const string SchemaFile = "eng/schema/ts-api_3_29.xsd";
const string OutputFolder = "src/Tableau.Api/Rest/Models/Generated";
const string Usage = "Usage: dotnet run eng/generate-models.cs -- [--check]";

if (args is not ([] or ["--check"]))
{
    Console.Error.WriteLine(Usage);
    return 2;
}
var check = args.Length == 1;
var root = Repo.Root();
var output = Path.Combine(root, OutputFolder);

Dictionary<string, string> files;
string summary;
try
{
    var bytes = File.ReadAllBytes(Path.Combine(root, SchemaFile));
    var model = new Reader().Read(bytes);
    files = Emitter.Files(model, SchemaFile, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    summary = $"{model.Records.Count(record => record.IsPublic)} records, {model.Enums.Count} enumerations and "
        + $"{model.Wrappers.Count} list converters from {SchemaFile}";
}
catch (SchemaException e)
{
    Console.Error.WriteLine($"generate-models: {e.Message}");
    return 1;
}

var present = Directory.Exists(output)
    ? Directory.GetFiles(output).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
    : [];
if (check)
{
    var problems = new List<string>();
    foreach (var (name, text) in files)
    {
        var path = Path.Combine(output, name);
        if (!File.Exists(path)) problems.Add($"{name} is missing");
        else if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(Repo.Utf8.GetBytes(text))) problems.Add($"{name} differs");
    }
    problems.AddRange(present.Where(name => !files.ContainsKey(name)).Select(name => $"{name} is not one the generator writes"));
    if (problems.Count == 0)
    {
        Console.WriteLine($"generate-models: {OutputFolder} is current: {files.Count} files, {summary}");
        return 0;
    }
    foreach (var problem in problems) Console.Error.WriteLine($"generate-models: {OutputFolder}/{problem}");
    Console.Error.WriteLine($"generate-models: {OutputFolder} is not what {SchemaFile} generates: run "
        + "`dotnet run eng/generate-models.cs` and commit the result");
    return 1;
}

var foreign = present.Where(name => !name.EndsWith(".g.cs", StringComparison.Ordinal)).ToList();
if (foreign.Count > 0)
{
    Console.Error.WriteLine($"generate-models: {OutputFolder} holds only generated files; move {string.Join(", ", foreign)} "
        + "to src/Tableau.Api/Rest/Models/");
    return 1;
}
Directory.CreateDirectory(output);
foreach (var stale in present.Where(name => !files.ContainsKey(name))) File.Delete(Path.Combine(output, stale));
foreach (var (name, text) in files) File.WriteAllText(Path.Combine(output, name), text, Repo.Utf8);
Console.WriteLine($"generate-models: wrote {files.Count} files to {OutputFolder}: {summary}");
return 0;

/// <summary>Where the repo is, and how its files are written.</summary>
internal static class Repo
{
    public static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The folder above eng/, wherever the command runs from.</summary>
    public static string Root([CallerFilePath] string source = "") => Path.GetFullPath(Path.Combine(
        AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetDirectoryName(source)!, ".."));
}

/// <summary>A construct the generator doesn't map, or a name it can't give.</summary>
internal sealed class SchemaException(string message)
    : Exception(message.Replace("<c>", "`", StringComparison.Ordinal).Replace("</c>", "`", StringComparison.Ordinal));

/// <summary>How a property holds a list, if it does.</summary>
internal enum ListKind
{
    None,
    Direct,
    Wrapped,
}

/// <summary>A C# type a property holds, by its name in the generated code.</summary>
internal sealed record TypeRef(string Name)
{
    public static readonly TypeRef String = new("string");
    public static readonly TypeRef Bool = new("bool");
    public static readonly TypeRef Int = new("int");
    public static readonly TypeRef Long = new("long");
    public static readonly TypeRef Decimal = new("decimal");
    public static readonly TypeRef DateTimeOffset = new("DateTimeOffset");
    public static readonly TypeRef TimeOnly = new("TimeOnly");
}

internal sealed record Property(string JsonName, string Name, TypeRef Type, ListKind List, string? Converter, string Doc);

internal sealed record RecordModel(string Name, string Doc, bool IsPublic, List<Property> Properties);

internal sealed record EnumModel(string Name, string Doc, List<(string Member, string Value)> Values);

internal sealed record WrapperModel(string Name, string Doc, string ItemName, TypeRef Item);

internal sealed record SchemaModel(List<RecordModel> Records, List<EnumModel> Enums, List<WrapperModel> Wrappers);

/// <summary>Reads the compiled schema into the models, naming each type once.</summary>
internal sealed class Reader
{
    // Names that would hide a type a C# file imports by default: Task would hide System.Threading.Tasks.Task in every
    // caller's file that also imports the models' namespace, which an async one does.
    private static readonly Dictionary<string, string> _renames = new(StringComparer.Ordinal)
    {
        ["taskType"] = "TableauTask",
    };

    // The namespaces a C# file imports by default (implicit usings) and the one every generated file imports: a model
    // with the name of a type in them would hide it, or be hidden.
    private static readonly HashSet<string> _importedNamespaces = new(StringComparer.Ordinal)
    {
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading",
        "System.Threading.Tasks", "System.Text.Json.Serialization",
    };

    // Names the generated code itself declares or inherits: the hand-written types beside the models, and the members
    // of a record, of an enumeration's struct and of the serializer context.
    // The context names a property after each type, beside its own members.
    private static readonly HashSet<string> _reserved = new(StringComparer.Ordinal)
    {
        "TableauJsonContext", "TableauListConverter", "TableauWrappedListConverter", "TableauLists", "TableauEnumConverter",
        "ITableauEnum",
        "Options", "Default", "GeneratedSerializerOptions", "GetTypeInfo",
    };

    private static readonly HashSet<string> _recordMembers = new(StringComparer.Ordinal)
    {
        "EqualityContract", "Equals", "GetHashCode", "ToString", "PrintMembers", "GetType", "MemberwiseClone", "Finalize",
        "Deconstruct",
    };

    private static readonly HashSet<string> _enumMembers = new(_recordMembers, StringComparer.Ordinal) { "Value", "Create" };

    private readonly Dictionary<string, string> _taken = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<XmlSchemaComplexType, string> _names = [];
    private readonly Dictionary<XmlSchemaComplexType, TypeRef> _records = [];
    private readonly Dictionary<XmlSchemaComplexType, WrapperModel> _wrappers = [];
    private readonly Dictionary<XmlSchemaSimpleType, TypeRef> _simple = [];
    private readonly Queue<(XmlSchemaComplexType Type, string Name, string Doc, bool IsPublic)> _pending = [];
    private readonly Dictionary<XmlSchemaComplexType, List<XmlSchemaComplexType>> _extensions = [];
    private readonly List<RecordModel> _recordModels = [];
    private readonly List<EnumModel> _enumModels = [];

    public SchemaModel Read(byte[] bytes)
    {
        var schema = Compile(bytes);
        var types = schema.SchemaTypes.Values.Cast<XmlSchemaType>()
            .OrderBy(type => type.QualifiedName.Name, StringComparer.Ordinal).ToList();
        foreach (var type in types.OfType<XmlSchemaComplexType>())
        {
            if (type.DerivedBy == XmlSchemaDerivationMethod.Extension && type.BaseXmlSchemaType is XmlSchemaComplexType { QualifiedName.IsEmpty: false } baseType)
            {
                if (!_extensions.TryGetValue(baseType, out var derived)) _extensions[baseType] = derived = [];
                derived.Add(type);
            }
        }

        foreach (var element in schema.Elements.Values.Cast<XmlSchemaElement>().OrderBy(element => element.QualifiedName.Name, StringComparer.Ordinal))
        {
            if (element.ElementSchemaType is not XmlSchemaComplexType { QualifiedName.IsEmpty: true } envelope)
                throw new SchemaException($"the global element {element.QualifiedName.Name} has a named or simple type; only envelopes with a type of their own, such as tsRequest and tsResponse, are mapped");
            var name = Pascal(element.QualifiedName.Name);
            _names[envelope] = name;
            Enqueue(envelope, name, $"The schema's <c>{element.QualifiedName.Name}</c> element. Tableau's JSON sends its child at the top level, with no <c>{element.QualifiedName.Name}</c> wrapper.", isPublic: false);
        }

        foreach (var type in types)
        {
            switch (type)
            {
                case XmlSchemaComplexType complex when IsWrapper(complex, out var item):
                    Wrapper(complex, item);
                    break;
                case XmlSchemaComplexType complex:
                    Record(complex);
                    break;
                case XmlSchemaSimpleType simple:
                    Simple(simple);
                    break;
            }
        }

        while (_pending.TryDequeue(out var next)) Build(next.Type, next.Name, next.Doc, next.IsPublic);

        CheckImportedNames();
        return new SchemaModel(
            [.. _recordModels.OrderBy(record => record.Name, StringComparer.Ordinal)],
            [.. _enumModels.OrderBy(model => model.Name, StringComparer.Ordinal)],
            [.. _wrappers.Values.OrderBy(wrapper => wrapper.Name, StringComparer.Ordinal)]);
    }

    private static XmlSchema Compile(byte[] bytes)
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        var errors = new List<string>();
        set.ValidationEventHandler += (_, e) => errors.Add($"line {e.Exception.LineNumber}: {e.Message}");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var stream = new MemoryStream(bytes))
        using (var reader = XmlReader.Create(stream, settings))
            set.Add(null, reader);
        set.Compile();
        if (errors.Count > 0) throw new SchemaException($"the schema doesn't compile: {errors[0]}");
        return set.Schemas().Cast<XmlSchema>().Single();
    }

    // A record's members: its text or its elements in schema order, its attributes by name, then the members of the
    // types that extend it.
    private void Build(XmlSchemaComplexType type, string name, string doc, bool isPublic)
    {
        var properties = new List<Property>();
        AddMembers(type, name, properties);
        if (_extensions.TryGetValue(type, out var derived))
        {
            foreach (var extension in derived) AddMembers(extension, name, properties);
            doc += " It also holds the members of the types that extend it, "
                + string.Join(", ", derived.Select(extension => $"<c>{extension.QualifiedName.Name}</c>"))
                + ", since JSON doesn't say which one an object is.";
        }
        _recordModels.Add(new RecordModel(name, doc, isPublic, properties));
    }

    private void AddMembers(XmlSchemaComplexType type, string owner, List<Property> properties)
    {
        var where = Describe(type);
        if (type.IsMixed || type.ContentType == XmlSchemaContentType.Mixed) throw new SchemaException($"{where}: mixed content isn't mapped");
        if (type.AttributeWildcard is not null) throw new SchemaException($"{where}: xs:anyAttribute isn't mapped");
        switch (type.ContentModel)
        {
            case XmlSchemaComplexContent { Content: XmlSchemaComplexContentRestriction }:
                throw new SchemaException($"{where}: a complexContent restriction isn't mapped");
            case XmlSchemaSimpleContent { Content: XmlSchemaSimpleContentRestriction }:
                throw new SchemaException($"{where}: a simpleContent restriction isn't mapped");
            case XmlSchemaSimpleContent { Content: XmlSchemaSimpleContentExtension }:
                if (type.BaseXmlSchemaType is not XmlSchemaSimpleType text)
                    throw new SchemaException($"{where}: simpleContent extends a complex type, which isn't mapped");
                Add(properties, owner, where, new Property("value", "Value", Simple(text), ListKind.None, null, "The element's text."));
                break;
        }

        if (type.ContentModel is XmlSchemaSimpleContent && type.AttributeUses.Contains(new XmlQualifiedName("value")))
            throw new SchemaException($"{where}: an attribute named value would take the name of the element's text");
        if (type.ContentType == XmlSchemaContentType.ElementOnly)
        {
            foreach (var element in Elements(type.ContentTypeParticle, where))
                Add(properties, owner, where, ElementProperty(element, where));
        }

        var attributes = type.AttributeUses.Values.Cast<XmlSchemaAttribute>()
            .Where(attribute => attribute.Use != XmlSchemaUse.Prohibited)
            .OrderBy(attribute => attribute.QualifiedName.Name, StringComparer.Ordinal);
        foreach (var attribute in attributes)
        {
            var json = attribute.QualifiedName.Name;
            Add(properties, owner, where, new Property(json, Member(json, where), Simple(attribute.AttributeSchemaType!), ListKind.None, null,
                $"The <c>{Xml(json)}</c> attribute."));
        }
    }

    // One property per JSON name: the same member reached twice, through a choice's branches or an extension, is kept
    // once; the same name with another shape can't be told apart in JSON.
    private static void Add(List<Property> properties, string owner, string where, Property property)
    {
        if (property.Name == owner) throw new SchemaException($"{where}: the member {property.JsonName} would have its type's name, {owner}");
        var same = properties.FirstOrDefault(existing => existing.JsonName == property.JsonName);
        if (same is not null)
        {
            if (same.Type != property.Type || same.List != property.List || same.Converter != property.Converter)
                throw new SchemaException($"{where}: {property.JsonName} appears twice, as {Shape(same)} and as {Shape(property)}");
            return;
        }
        if (properties.Any(existing => string.Equals(existing.Name, property.Name, StringComparison.OrdinalIgnoreCase)))
            throw new SchemaException($"{where}: two members would be named {property.Name}");
        properties.Add(property);
    }

    private static string Shape(Property property) => property.List == ListKind.None ? property.Type.Name : $"a list of {property.Type.Name}";

    private Property ElementProperty(XmlSchemaElement element, string where)
    {
        var json = element.QualifiedName.Name;
        var name = Member(json, where);
        var repeated = element.MaxOccurs > 1;
        switch (element.ElementSchemaType)
        {
            case XmlSchemaSimpleType simple:
                return new Property(json, name, Simple(simple), repeated ? ListKind.Direct : ListKind.None, null,
                    repeated ? $"The <c>{Xml(json)}</c> elements, as a list." : $"The <c>{Xml(json)}</c> element.");
            case XmlSchemaComplexType complex when IsWrapper(complex, out var item):
                if (repeated) throw new SchemaException($"{where}: the element {json} repeats a list wrapper, a list of lists, which isn't mapped");
                var wrapper = Wrapper(complex, item);
                return new Property(json, name, wrapper.Item, ListKind.Wrapped, wrapper.Name,
                    $"The <c>{Xml(json)}</c> element: its <c>{Xml(wrapper.ItemName)}</c> elements, as a list.");
            case XmlSchemaComplexType complex:
                return new Property(json, name, Record(complex), repeated ? ListKind.Direct : ListKind.None, null,
                    repeated ? $"The <c>{Xml(json)}</c> elements, as a list." : $"The <c>{Xml(json)}</c> element.");
            default:
                throw new SchemaException($"{where}: the element {json} has no type");
        }
    }

    // The elements of a content model, in schema order, through sequences, choices and group references.
    private static List<XmlSchemaElement> Elements(XmlSchemaParticle particle, string where)
    {
        var elements = new List<XmlSchemaElement>();
        Walk(particle);
        return elements;

        void Walk(XmlSchemaParticle part)
        {
            switch (part)
            {
                case XmlSchemaElement element:
                    elements.Add(element);
                    break;
                case XmlSchemaSequence or XmlSchemaChoice:
                    if (part.MaxOccurs > 1) throw new SchemaException($"{where}: a repeated {(part is XmlSchemaChoice ? "choice" : "sequence")} isn't mapped");
                    foreach (var item in ((XmlSchemaGroupBase)part).Items.Cast<XmlSchemaParticle>()) Walk(item);
                    break;
                case XmlSchemaGroupRef group:
                    if (part.MaxOccurs > 1) throw new SchemaException($"{where}: a repeated group isn't mapped");
                    Walk(group.Particle ?? throw new SchemaException($"{where}: a group reference with no group"));
                    break;
                case XmlSchemaAll:
                    throw new SchemaException($"{where}: xs:all isn't mapped");
                case XmlSchemaAny:
                    throw new SchemaException($"{where}: xs:any isn't mapped");
                default:
                    // The compiled form of empty content is an internal particle type with no members to read.
                    if (part.GetType().Name != "EmptyParticle") throw new SchemaException($"{where}: {part.GetType().Name} isn't mapped");
                    break;
            }
        }
    }

    // A list wrapper: one repeated element, no attributes, nothing extending it.
    private bool IsWrapper(XmlSchemaComplexType type, out XmlSchemaElement item)
    {
        item = null!;
        if (type.IsAbstract || type.ContentType != XmlSchemaContentType.ElementOnly || type.AttributeUses.Count > 0
            || type.AttributeWildcard is not null || type.DerivedBy == XmlSchemaDerivationMethod.Extension
            || _extensions.ContainsKey(type))
            return false;
        var elements = Elements(type.ContentTypeParticle, Describe(type));
        if (elements is not [{ MaxOccurs: > 1 } only]) return false;
        item = only;
        return true;
    }

    private WrapperModel Wrapper(XmlSchemaComplexType type, XmlSchemaElement item)
    {
        if (_wrappers.TryGetValue(type, out var known)) return known;
        var where = Describe(type);
        var name = Take(NameOf(type) + "Converter", where);
        var itemName = item.QualifiedName.Name;
        var itemType = item.ElementSchemaType switch
        {
            XmlSchemaSimpleType simple => Simple(simple),
            XmlSchemaComplexType complex when IsWrapper(complex, out _) => throw new SchemaException($"{where}: its {itemName} elements are list wrappers, a list of lists, which isn't mapped"),
            XmlSchemaComplexType complex => Record(complex),
            _ => throw new SchemaException($"{where}: the element {itemName} has no type"),
        };
        var wrapper = new WrapperModel(name, $"Reads and writes {(type.QualifiedName.IsEmpty ? "the type of " : "the schema's ")}{where}: its <c>{Xml(itemName)}</c> elements, as a list, inside the wrapper object.", itemName, itemType);
        _wrappers[type] = wrapper;
        return wrapper;
    }

    private TypeRef Record(XmlSchemaComplexType type)
    {
        if (_records.TryGetValue(type, out var known)) return known;
        if (type.QualifiedName == new XmlQualifiedName("anyType", XmlSchema.Namespace)) throw new SchemaException("xs:anyType isn't mapped");
        var where = Describe(type);
        var name = Take(NameOf(type), where);
        var reference = new TypeRef(name);
        _records[type] = reference;
        Enqueue(type, name, type.QualifiedName.IsEmpty ? $"The type of {where}." : $"The schema's {where}.", isPublic: true);
        return reference;
    }

    private void Enqueue(XmlSchemaComplexType type, string name, string doc, bool isPublic)
    {
        if (!isPublic)
        {
            Take(name, Describe(type));
            _records[type] = new TypeRef(name);
        }
        _pending.Enqueue((type, name, doc, isPublic));
    }

    // A simple type's C# type, and its enumeration's struct when it is one of strings.
    private TypeRef Simple(XmlSchemaSimpleType type)
    {
        if (_simple.TryGetValue(type, out var known)) return known;
        TypeRef result;
        if (type.QualifiedName.Namespace == XmlSchema.Namespace) result = Builtin(type);
        else
        {
            switch (type.Content)
            {
                case XmlSchemaSimpleTypeRestriction restriction:
                    var values = restriction.Facets.OfType<XmlSchemaEnumerationFacet>().Select(facet => facet.Value!).ToList();
                    var baseType = type.BaseXmlSchemaType as XmlSchemaSimpleType
                        ?? throw new SchemaException($"{Describe(type)}: a restriction of a complex type isn't mapped");
                    var mapped = Simple(baseType);
                    result = values.Count > 0 && mapped == TypeRef.String ? Enumeration(type, values) : mapped;
                    break;
                case XmlSchemaSimpleTypeUnion union:
                    // Only the members' kind matters: a union with a member that isn't an integer can hold any text.
                    result = union.BaseMemberTypes!.All(member => IsInteger(member.Datatype!.TypeCode)) ? TypeRef.Long : TypeRef.String;
                    break;
                default:
                    throw new SchemaException($"{Describe(type)}: {type.Content?.GetType().Name ?? "a simple type with no content"} isn't mapped");
            }
        }
        _simple[type] = result;
        return result;
    }

    private static TypeRef Builtin(XmlSchemaSimpleType type) => type.Datatype!.TypeCode switch
    {
        XmlTypeCode.String or XmlTypeCode.NormalizedString or XmlTypeCode.Token or XmlTypeCode.AnyUri
            or XmlTypeCode.AnyAtomicType or XmlTypeCode.Language or XmlTypeCode.Name or XmlTypeCode.NCName => TypeRef.String,
        XmlTypeCode.Boolean => TypeRef.Bool,
        XmlTypeCode.Int => TypeRef.Int,
        var code when IsInteger(code) => TypeRef.Long,
        XmlTypeCode.Decimal => TypeRef.Decimal,
        XmlTypeCode.DateTime => TypeRef.DateTimeOffset,
        XmlTypeCode.Time => TypeRef.TimeOnly,
        _ => throw new SchemaException($"xs:{type.QualifiedName.Name} isn't mapped"),
    };

    private static bool IsInteger(XmlTypeCode code) => code is XmlTypeCode.Int or XmlTypeCode.Long or XmlTypeCode.Integer
        or XmlTypeCode.NonNegativeInteger or XmlTypeCode.PositiveInteger or XmlTypeCode.NonPositiveInteger
        or XmlTypeCode.NegativeInteger or XmlTypeCode.UnsignedInt;

    private TypeRef Enumeration(XmlSchemaSimpleType type, List<string> values)
    {
        var where = Describe(type);
        var name = Take(type.QualifiedName.IsEmpty ? AnonymousSimpleName(type) : TypeName(type.QualifiedName.Name), where);
        var members = new List<(string Member, string Value)>();
        foreach (var value in values)
        {
            var member = EnumMember(value, where);
            if (member == name || _enumMembers.Contains(member))
                throw new SchemaException($"{where}: the value {value} would be named {member}, which its struct already uses");
            if (members.Any(known => string.Equals(known.Member, member, StringComparison.OrdinalIgnoreCase)))
                throw new SchemaException($"{where}: two values would be named {member}");
            members.Add((member, value));
        }
        _enumModels.Add(new EnumModel(name, $"The values of {where}. A value Tableau adds later reads too, and keeps its text.", members));
        return new TypeRef(name);
    }

    // A complex type's C# name: its schema name without the Type suffix; for an anonymous one, its owner's name and its
    // element's.
    private string NameOf(XmlSchemaComplexType type)
    {
        if (_names.TryGetValue(type, out var known)) return known;
        string name;
        if (!type.QualifiedName.IsEmpty) name = TypeName(type.QualifiedName.Name);
        else if (type.Parent is XmlSchemaElement element && Owner(element) is { } owner)
            name = NameOf(owner) + Pascal(element.QualifiedName.Name);
        else throw new SchemaException($"can't name {Describe(type)}");
        _names[type] = name;
        return name;
    }

    private string AnonymousSimpleName(XmlSchemaSimpleType type) => type.Parent switch
    {
        XmlSchemaAttribute attribute when Owner(attribute) is { } owner => NameOf(owner) + Pascal(attribute.QualifiedName.Name),
        XmlSchemaElement element when Owner(element) is { } owner => NameOf(owner) + Pascal(element.QualifiedName.Name),
        _ => throw new SchemaException($"can't name {Describe(type)}"),
    };

    // The complex type an attribute or a local element is declared in.
    private static XmlSchemaComplexType? Owner(XmlSchemaObject declared)
    {
        for (var parent = declared.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is XmlSchemaComplexType type) return type;
        }
        return null;
    }

    private static string TypeName(string schemaName) => _renames.TryGetValue(schemaName, out var renamed)
        ? renamed
        : Pascal(schemaName.Length > 4 && schemaName.EndsWith("Type", StringComparison.Ordinal) ? schemaName[..^4] : schemaName);

    private static string Member(string schemaName, string where)
    {
        var name = Pascal(schemaName);
        return _recordMembers.Contains(name)
            ? throw new SchemaException($"{where}: the member {schemaName} would be named {name}, which a record already has")
            : name;
    }

    private string Take(string name, string what)
    {
        if (_reserved.Contains(name)) throw new SchemaException($"{what} would be named {name}, a type the library already has beside the models");
        if (!_taken.TryAdd(name, what)) throw new SchemaException($"{what} and {_taken[name]} would both be named {name}");
        return name;
    }

    private void CheckImportedNames()
    {
        var imported = new[]
            {
                typeof(object), typeof(Enumerable), typeof(HttpClient), typeof(LinkedList<>), typeof(JsonConverter), typeof(Uri),
                typeof(FileSystemWatcher),
            }
            .Select(type => type.Assembly).Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.IsNested && type.Namespace is { } ns && _importedNamespaces.Contains(ns))
            .ToLookup(type => type.Name, StringComparer.Ordinal);
        foreach (var (name, what) in _taken.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (imported[name].FirstOrDefault() is { } clash)
                throw new SchemaException($"{what} would be named {name}, as {clash.FullName} is: give it another name in the generator's renames");
        }
    }

    private static string Describe(XmlSchemaType type) => type switch
    {
        { QualifiedName.IsEmpty: false } => $"<c>{type.QualifiedName.Name}</c>",
        { Parent: XmlSchemaElement element } when Owner(element) is { } owner => $"the <c>{element.QualifiedName.Name}</c> element of {Describe(owner)}",
        { Parent: XmlSchemaElement element } => $"the <c>{element.QualifiedName.Name}</c> element",
        { Parent: XmlSchemaAttribute attribute } when Owner(attribute) is { } owner => $"the <c>{attribute.QualifiedName.Name}</c> attribute of {Describe(owner)}",
        _ => "an anonymous type",
    };

    /// <summary>An identifier from a schema name: each run of letters and digits, its first letter in upper case.</summary>
    internal static string Pascal(string text)
    {
        var name = string.Concat(Parts(text).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        return name.Length == 0 || char.IsDigit(name[0]) ? throw new SchemaException($"can't make an identifier of {text}") : name;
    }

    /// <summary>
    /// An enumeration value's identifier: as <see cref="Pascal"/>, with a part in capitals written as a word, so
    /// ADD_SITE_ROLE is AddSiteRole and SAML Saml, while OpenID stays OpenID.
    /// </summary>
    internal static string EnumMember(string value, string where)
    {
        var name = string.Concat(Parts(value).Select(part => part.Length > 1 && part.All(c => !char.IsLower(c))
            ? char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()
            : char.ToUpperInvariant(part[0]) + part[1..]));
        return name.Length == 0 || char.IsDigit(name[0]) ? throw new SchemaException($"{where}: can't make an identifier of the value {value}") : name;
    }

    private static List<string> Parts(string text)
    {
        var parts = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inPart = i < text.Length && char.IsAsciiLetterOrDigit(text[i]);
            if (inPart && start < 0) start = i;
            else if (!inPart && start >= 0)
            {
                parts.Add(text[start..i]);
                start = -1;
            }
        }
        return parts;
    }

    internal static string Xml(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>Writes the models as C# files, keyed by file name.</summary>
internal static class Emitter
{
    private const string Namespace = "Tableau.Api.Rest.Models";

    public static Dictionary<string, string> Files(SchemaModel model, string schemaFile, string hash)
    {
        var header = "// <auto-generated/>\n"
            + $"// Generated by eng/generate-models.cs from {schemaFile},\n"
            + $"// SHA-256 {hash}.\n"
            + "// Don't edit it: change the schema or the generator, then run `dotnet run eng/generate-models.cs`.\n"
            + "#nullable enable\n";
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in model.Records) files.Add($"{record.Name}.g.cs", header + RecordFile(record));
        foreach (var enumeration in model.Enums) files.Add($"{enumeration.Name}.g.cs", header + EnumFile(enumeration));
        files.Add("ListConverters.g.cs", header + ConvertersFile(model.Wrappers));
        files.Add("TableauJsonContext.g.cs", header + ContextFile(model));
        return files.OrderBy(file => file.Key, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);
    }

    private static string RecordFile(RecordModel record)
    {
        var text = new StringBuilder();
        var usings = new SortedSet<string>(StringComparer.Ordinal) { "System.Text.Json.Serialization" };
        if (record.Properties.Any(property => property.List != ListKind.None)) usings.Add("System.Collections.Generic");
        if (record.Properties.Any(property => property.Type == TypeRef.DateTimeOffset || property.Type == TypeRef.TimeOnly)) usings.Add("System");
        Usings(text, usings);
        Summary(text, "", record.Doc);
        text.Append(CultureInfo.InvariantCulture, $"{(record.IsPublic ? "public" : "internal")} sealed partial record {record.Name}\n{{\n");
        for (var i = 0; i < record.Properties.Count; i++)
        {
            var property = record.Properties[i];
            if (i > 0) text.Append('\n');
            Summary(text, "    ", property.Doc);
            text.Append(CultureInfo.InvariantCulture, $"    [JsonPropertyName(\"{property.JsonName}\")]\n");
            var type = property.List switch
            {
                ListKind.None => $"{property.Type.Name}?",
                _ => $"IReadOnlyList<{property.Type.Name}>?",
            };
            if (property.List == ListKind.Direct) text.Append(CultureInfo.InvariantCulture, $"    [JsonConverter(typeof(TableauListConverter<{property.Type.Name}>))]\n");
            if (property.List == ListKind.Wrapped) text.Append(CultureInfo.InvariantCulture, $"    [JsonConverter(typeof({property.Converter}))]\n");
            text.Append(CultureInfo.InvariantCulture, $"    public {type} {property.Name} {{ get; init; }}\n");
        }
        return text.Append("}\n").ToString();
    }

    private static string EnumFile(EnumModel model)
    {
        var text = new StringBuilder();
        Usings(text, ["System.Text.Json.Serialization"]);
        Summary(text, "", model.Doc);
        text.Append("/// <param name=\"Value\">The value's text, as Tableau sends it.</param>\n")
            .Append(CultureInfo.InvariantCulture, $"[JsonConverter(typeof(TableauEnumConverter<{model.Name}>))]\n")
            .Append(CultureInfo.InvariantCulture, $"public readonly partial record struct {model.Name}(string Value) : ITableauEnum<{model.Name}>\n{{\n");
        foreach (var (member, value) in model.Values)
        {
            Summary(text, "    ", $"<c>{Reader.Xml(value)}</c>.");
            text.Append(CultureInfo.InvariantCulture, $"    public static {model.Name} {member} {{ get; }} = new(\"{value}\");\n\n");
        }
        Summary(text, "    ", "The value's text.");
        text.Append("    public override string ToString() => Value ?? \"\";\n\n")
            .Append(CultureInfo.InvariantCulture, $"    static {model.Name} ITableauEnum<{model.Name}>.Create(string value) => new(value);\n")
            .Append("}\n");
        return text.ToString();
    }

    private static string ConvertersFile(List<WrapperModel> wrappers)
    {
        var text = new StringBuilder();
        Usings(text, []);
        for (var i = 0; i < wrappers.Count; i++)
        {
            var wrapper = wrappers[i];
            if (i > 0) text.Append('\n');
            Summary(text, "", wrapper.Doc);
            text.Append(CultureInfo.InvariantCulture, $"internal sealed class {wrapper.Name}() : TableauWrappedListConverter<{wrapper.Item.Name}>(\"{wrapper.ItemName}\");\n");
        }
        return text.ToString();
    }

    // Every type the models use, so the context holds the metadata of each: the converters of the lists ask it for
    // their items' metadata by type.
    private static string ContextFile(SchemaModel model)
    {
        var text = new StringBuilder();
        Usings(text, ["System.Text.Json.Serialization"]);
        var types = model.Records.Select(record => record.Name)
            .Concat(model.Enums.Select(enumeration => enumeration.Name))
            .Concat(model.Wrappers.Select(wrapper => wrapper.Item.Name)
                .Concat(model.Records.SelectMany(record => record.Properties).Where(property => property.List != ListKind.None).Select(property => property.Type.Name)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        foreach (var type in types) text.Append(CultureInfo.InvariantCulture, $"[JsonSerializable(typeof({type}))]\n");
        return text.Append("internal sealed partial class TableauJsonContext;\n").ToString();
    }

    private static void Usings(StringBuilder text, IEnumerable<string> usings)
    {
        text.Append('\n');
        var any = false;
        foreach (var name in usings)
        {
            text.Append(CultureInfo.InvariantCulture, $"using {name};\n");
            any = true;
        }
        if (any) text.Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"namespace {Namespace};\n\n");
    }

    private static void Summary(StringBuilder text, string indent, string doc) =>
        text.Append(CultureInfo.InvariantCulture, $"{indent}/// <summary>{doc}</summary>\n");
}
