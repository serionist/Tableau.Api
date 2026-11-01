using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Rest.Models;

namespace Tableau.Api.Tests;

/// <summary>
/// Every generated model, found by reflection, written and read through the source-generated context with each of its
/// members set: each member reaches JSON under the schema's name and reads back the same. Every value of every
/// enumeration writes its text and reads back the same value, and a default one prints no text. So a model, a
/// converter or a context entry that the fixtures don't reach still can't be wrong unnoticed.
/// </summary>
[TestClass]
public sealed class EveryModelTests
{
    private static readonly List<Type> _models = [.. typeof(TableauJsonContext).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(TableauJsonContext).Namespace && !type.IsNested && !type.IsAbstract && !type.IsGenericTypeDefinition)
        .OrderBy(type => type.Name, StringComparer.Ordinal)];

    private static readonly List<Type> _records = [.. _models.Where(type => type.IsClass && type.GetCustomAttribute<JsonConverterAttribute>() is null
        && !typeof(JsonConverter).IsAssignableFrom(type) && type != typeof(TableauJsonContext))];

    private static readonly List<Type> _enums = [.. _models.Where(type => type.IsValueType)];

    [TestMethod]
    public void TheModelsAreAllHere()
    {
        Assert.HasCount(200, _records, "197 generated public records, the column written by hand while the schema lacks it, and the two envelopes");
        Assert.HasCount(50, _enums);
    }

    [TestMethod]
    public void EveryRecordWritesEveryMemberAndReadsItBack()
    {
        var failures = new List<string>();
        foreach (var type in _records)
        {
            var info = TableauJsonContext.Default.GetTypeInfo(type);
            if (info is null)
            {
                failures.Add($"{type.Name} is not in the context");
                continue;
            }
            var written = JsonSerializer.Serialize(Sample(type, 0), info);
            var names = JsonNode.Parse(written)!.AsObject().Select(member => member.Key).Order(StringComparer.Ordinal).ToList();
            var expected = Members(type).Select(member => member.Json).Order(StringComparer.Ordinal).ToList();
            var again = JsonSerializer.Serialize(JsonSerializer.Deserialize(written, info), info);
            if (!names.SequenceEqual(expected)) failures.Add($"{type.Name} wrote {string.Join(",", names)}, not {string.Join(",", expected)}");
            else if (again != written) failures.Add($"{type.Name} read back otherwise: {written} became {again}");
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [TestMethod]
    public void EveryEnumerationValueWritesItsTextAndReadsBack()
    {
        var failures = new List<string>();
        var values = 0;
        foreach (var type in _enums)
        {
            var info = TableauJsonContext.Default.GetTypeInfo(type)!;
            if (Activator.CreateInstance(type)!.ToString() != "") failures.Add($"{type.Name}'s default value prints text");
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(property => property.PropertyType == type))
            {
                var value = property.GetValue(null)!;
                var text = (string)type.GetProperty("Value")!.GetValue(value)!;
                var written = JsonSerializer.Serialize(value, info);
                values++;
                if (written != JsonSerializer.Serialize(text)) failures.Add($"{type.Name}.{property.Name} wrote {written}");
                else if (!value.Equals(JsonSerializer.Deserialize(written, info))) failures.Add($"{type.Name}.{property.Name} read back otherwise");
                else if (value.ToString() != text) failures.Add($"{type.Name}.{property.Name} prints {value}, not {text}");
            }
        }

        Assert.IsEmpty(failures, string.Join("\n", failures));
        Assert.IsGreaterThan(150, values);
    }

    private static IEnumerable<(PropertyInfo Property, string Json)> Members(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property, property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
            .Where(member => member.Name is not null)
            .Select(member => (member.property, member.Name!));

    // A value for each member, two records deep, so a recursive type ends; a deeper record is written empty.
    private static object Sample(Type type, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) return "text";
        if (type == typeof(bool)) return true;
        if (type == typeof(int)) return 7;
        if (type == typeof(long)) return 9007199254740993L;
        if (type == typeof(decimal)) return 0.25m;
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 10, 31, 12, 30, 0, TimeSpan.Zero);
        if (type == typeof(TimeOnly)) return new TimeOnly(18, 30);
        if (type.IsValueType) return type.GetProperties(BindingFlags.Public | BindingFlags.Static).First(property => property.PropertyType == type).GetValue(null)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            var item = type.GetGenericArguments()[0];
            var list = Array.CreateInstance(item, 2);
            list.SetValue(Sample(item, depth), 0);
            list.SetValue(Sample(item, depth), 1);
            return list;
        }
        var record = Activator.CreateInstance(type, nonPublic: true)!;
        if (depth < 2)
        {
            foreach (var (property, _) in Members(type)) property.SetValue(record, Sample(property.PropertyType, depth + 1));
        }
        return record;
    }
}
