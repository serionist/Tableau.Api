using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// Every public type of the library is either a 2.0 type, which the native AOT app keeps through
/// tests/Tableau.Api.Aot/roots.xml and so checks, or a type of the old client, listed in <see cref="OldPublicTypes"/>.
/// That list only shrinks: each step that ports an area deletes its old types and their lines, and lowers its count.
/// </summary>
[TestClass]
public sealed class ModelRootsTests
{
    private static readonly Assembly _library = typeof(TableauApiClient).Assembly;

    private static (HashSet<string> Namespaces, HashSet<string> Types) Roots()
    {
        using var stream = typeof(ModelRootsTests).Assembly.GetManifestResourceStream("roots.xml")!;
        var assembly = XDocument.Load(stream).Root!.Elements("assembly")
            .Single(element => (string?)element.Attribute("fullname") == _library.GetName().Name);
        return (
            new HashSet<string>(assembly.Elements("namespace").Select(element => (string)element.Attribute("fullname")!), StringComparer.Ordinal),
            new HashSet<string>(assembly.Elements("type").Select(element => (string)element.Attribute("fullname")!), StringComparer.Ordinal));
    }

    private static bool Rooted(Type type, (HashSet<string> Namespaces, HashSet<string> Types) roots) =>
        roots.Types.Contains(type.FullName!) || (type.Namespace is { } name && roots.Namespaces.Contains(name));

    [TestMethod]
    public void EveryPublicTypeIsRootedOrOld()
    {
        var roots = Roots();
        var old = new HashSet<string>(OldPublicTypes.Names, StringComparer.Ordinal);

        var forgotten = _library.GetExportedTypes()
            .Where(type => !Rooted(type, roots) && !old.Contains(type.FullName!))
            .Select(type => type.FullName!).Order(StringComparer.Ordinal).ToList();

        Assert.IsEmpty(forgotten, $"Add each 2.0 type to tests/Tableau.Api.Aot/roots.xml: {string.Join(", ", forgotten)}");
    }

    [TestMethod]
    public void TheOldTypesAreStillThereAndNotRooted()
    {
        var roots = Roots();
        var exported = _library.GetExportedTypes().ToDictionary(type => type.FullName!, StringComparer.Ordinal);

        var gone = OldPublicTypes.Names.Where(name => !exported.ContainsKey(name)).ToList();
        var rooted = OldPublicTypes.Names.Where(name => exported.TryGetValue(name, out var type) && Rooted(type, roots)).ToList();

        Assert.IsEmpty(gone, $"Take the deleted types out of OldPublicTypes: {string.Join(", ", gone)}");
        Assert.IsEmpty(rooted, $"A 2.0 type is not an old one; take it out of OldPublicTypes: {string.Join(", ", rooted)}");
        Assert.HasCount(OldPublicTypes.Names.Distinct(StringComparer.Ordinal).Count(), OldPublicTypes.Names);
    }

    [TestMethod]
    public void TheOldTypesListCountsItsTypes() =>
        Assert.AreEqual(OldPublicTypes.Most, OldPublicTypes.Names.Count,
            "Most is the count of the list, lowered with it as the old client loses types; the gate refuses it higher than on main.");

    // A caller who imports Tableau.Api and the models namespace in one file gets CS0104 on a name both have. The old
    // client's types clash on purpose, and go with it.
    [TestMethod]
    public void NoModelSharesItsNameWithAnotherPublicType()
    {
        var old = new HashSet<string>(OldPublicTypes.Names, StringComparer.Ordinal);
        var exported = _library.GetExportedTypes();
        var others = exported.Where(type => type.Namespace != "Tableau.Api.Rest.Models" && !old.Contains(type.FullName!))
            .ToLookup(type => type.Name, StringComparer.OrdinalIgnoreCase);

        var clashes = exported.Where(type => type.Namespace == "Tableau.Api.Rest.Models" && others[type.Name].Any())
            .Select(type => $"{type.FullName} and {string.Join(", ", others[type.Name].Select(other => other.FullName))}").ToList();

        Assert.IsEmpty(clashes, string.Join("; ", clashes));
    }

    [TestMethod]
    public void TheModelsAreRootedByTheirNamespace()
    {
        var roots = Roots();
        var models = _library.GetExportedTypes().Where(type => type.Namespace == "Tableau.Api.Rest.Models").ToList();

        Assert.IsGreaterThan(200, models.Count);
        Assert.IsTrue(models.All(type => Rooted(type, roots)));
    }
}
