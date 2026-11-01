using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tableau.Api.Tests;

/// <summary>
/// <see cref="TableauContent"/> and <see cref="PermissionGrantee"/>: each factory makes its kind from a LUID and refuses
/// anything else, and two values are equal when they name the same content or grantee, the LUIDs' case aside.
/// </summary>
[TestClass]
public sealed class TableauContentTests
{
    private const string Id = "6c3d4e5f-0001-4a7b-8c9d-0e1f2a3b4c5d";
    private const string TableId = "33333333-4444-4555-8666-777777777777";

    public static IEnumerable<object[]> Factories =>
    [
        [TableauContentKind.Project, (Func<string, TableauContent>)TableauContent.Project],
        [TableauContentKind.Workbook, (Func<string, TableauContent>)TableauContent.Workbook],
        [TableauContentKind.View, (Func<string, TableauContent>)TableauContent.View],
        [TableauContentKind.DataSource, (Func<string, TableauContent>)TableauContent.DataSource],
        [TableauContentKind.Flow, (Func<string, TableauContent>)TableauContent.Flow],
        [TableauContentKind.VirtualConnection, (Func<string, TableauContent>)TableauContent.VirtualConnection],
        [TableauContentKind.Collection, (Func<string, TableauContent>)TableauContent.Collection],
        [TableauContentKind.Database, (Func<string, TableauContent>)TableauContent.Database],
        [TableauContentKind.Table, (Func<string, TableauContent>)TableauContent.Table],
        [TableauContentKind.Column, (Func<string, TableauContent>)(id => TableauContent.Column(TableId, id))],
    ];

    [TestMethod]
    [DynamicData(nameof(Factories))]
    public void EachFactoryMakesItsKindFromALuidAndRefusesAnythingElse(TableauContentKind kind, Func<string, TableauContent> make)
    {
        var content = make(Id);

        Assert.AreEqual((kind, Id), (content.Kind, content.Id));
        Assert.AreEqual(kind == TableauContentKind.Column ? TableId : null, content.TableId);
        var thrown = Assert.ThrowsExactly<ArgumentException>(() => make("Superstore"));
        Assert.AreEqual(kind == TableauContentKind.Column ? "columnId" : "id", thrown.ParamName);
        Assert.ThrowsExactly<ArgumentNullException>(() => make(null!));
        // Whitespace around a LUID would go into a path.
        Assert.ThrowsExactly<ArgumentException>(() => make(" " + Id));
    }

    [TestMethod]
    public void AColumnsTableMustBeALuidToo()
    {
        Assert.AreEqual("tableId", Assert.ThrowsExactly<ArgumentException>(() => TableauContent.Column("Orders", Id)).ParamName);
        Assert.AreEqual("tableId", Assert.ThrowsExactly<ArgumentNullException>(() => TableauContent.Column(null!, Id)).ParamName);
    }

    [TestMethod]
    public void ContentIsEqualByItsKindAndItsLuidsCaseAside()
    {
        var workbook = TableauContent.Workbook(Id);
        var same = TableauContent.Workbook(Id.ToUpperInvariant());

        Assert.AreEqual(workbook, same);
        Assert.IsTrue(workbook.Equals((object)same));
        Assert.AreEqual(workbook.GetHashCode(), same.GetHashCode());
        Assert.AreNotEqual(workbook, TableauContent.View(Id));
        Assert.AreNotEqual(workbook, TableauContent.Workbook(TableId));
        Assert.IsFalse(workbook.Equals(null));
        Assert.IsFalse(workbook.Equals((object)Id));
        Assert.AreEqual(TableauContent.Column(TableId, Id), TableauContent.Column(TableId.ToUpperInvariant(), Id));
        Assert.AreNotEqual(TableauContent.Column(TableId, Id), TableauContent.Column(Id, Id));
        Assert.AreEqual(
            TableauContent.Column(TableId, Id).GetHashCode(), TableauContent.Column(TableId.ToUpperInvariant(), Id.ToUpperInvariant()).GetHashCode());
        Assert.HasCount(2, new HashSet<TableauContent> { workbook, same, TableauContent.Table(Id) });
    }

    [TestMethod]
    public void ContentPrintsItsKindAndItsLuids()
    {
        Assert.AreEqual($"Workbook {Id}", TableauContent.Workbook(Id).ToString());
        Assert.AreEqual($"Column {Id} of table {TableId}", TableauContent.Column(TableId, Id).ToString());
    }

    [TestMethod]
    public void AGranteeIsAUserAGroupOrAGroupSetByItsLuid()
    {
        var group = PermissionGrantee.Group(Id);
        var user = PermissionGrantee.User(Id);
        var set = PermissionGrantee.GroupSet(Id);

        Assert.AreEqual((PermissionGranteeKind.Group, PermissionGranteeKind.User, PermissionGranteeKind.GroupSet), (group.Kind, user.Kind, set.Kind));
        Assert.AreEqual(("groups", "users", "groupSets"), (group.Segment, user.Segment, set.Segment));
        Assert.AreEqual((Id, Id, Id), (group.Id, user.Id, set.Id));
        Assert.AreEqual(group, PermissionGrantee.Group(Id.ToUpperInvariant()));
        Assert.AreEqual(group.GetHashCode(), PermissionGrantee.Group(Id.ToUpperInvariant()).GetHashCode());
        Assert.IsTrue(group.Equals((object)PermissionGrantee.Group(Id)));
        Assert.AreNotEqual(group, user);
        Assert.AreNotEqual(group, set);
        Assert.IsFalse(group.Equals(null));
        Assert.AreEqual($"group {Id}", group.ToString());
        Assert.AreEqual($"user {Id}", user.ToString());
        Assert.AreEqual($"group set {Id}", set.ToString());
        Assert.AreEqual("id", Assert.ThrowsExactly<ArgumentException>(() => PermissionGrantee.User("ada")).ParamName);
        Assert.AreEqual("id", Assert.ThrowsExactly<ArgumentException>(() => PermissionGrantee.Group("Finance")).ParamName);
        Assert.AreEqual("id", Assert.ThrowsExactly<ArgumentException>(() => PermissionGrantee.GroupSet("Analysts")).ParamName);
    }

    [TestMethod]
    public void EqualityOperatorsCompareByValue()
    {
        TableauContent? none = null;
        PermissionGrantee? nobody = null;

        Assert.IsTrue(TableauContent.Workbook(Id) == TableauContent.Workbook(Id.ToUpperInvariant()));
        Assert.IsFalse(TableauContent.Workbook(Id) != TableauContent.Workbook(Id.ToUpperInvariant()));
        Assert.IsTrue(TableauContent.Workbook(Id) != TableauContent.View(Id));
        Assert.IsTrue(TableauContent.Workbook(Id) != none);
        Assert.IsTrue(none != TableauContent.Workbook(Id));
        Assert.IsTrue(none == null);
        Assert.IsTrue(PermissionGrantee.Group(Id) == PermissionGrantee.Group(Id.ToUpperInvariant()));
        Assert.IsTrue(PermissionGrantee.Group(Id) != PermissionGrantee.GroupSet(Id));
        Assert.IsTrue(PermissionGrantee.User(Id) != nobody);
        Assert.IsTrue(nobody != PermissionGrantee.User(Id));
        Assert.IsTrue(nobody == null);
    }

    [TestMethod]
    public void EveryKindHasItsWords()
    {
        var words = Enum.GetValues<TableauContentKind>().Select(TableauContent.Words).ToList();

        CollectionAssert.AreEqual(new[]
        {
            "project", "workbook", "view", "data source", "flow", "virtual connection", "collection", "database", "table", "column",
        }, words);
        Assert.AreEqual("42", TableauContent.Words((TableauContentKind)42));
    }
}
