using System;

namespace Tableau.Api;

/// <summary>
/// A field of one kind of resource that a list can filter and sort on, such as <see cref="ProjectField.Name"/>: one of
/// the fields Tableau's reference lists for that resource. A list refuses a field of another resource before it sends
/// anything.
/// </summary>
/// <remarks>
/// For a field this version doesn't list, write the expression yourself with <see cref="TableauFilter.FromString"/>.
/// </remarks>
public sealed class TableauField
{
    internal TableauField(string resource, string owner, string member, string name)
    {
        Resource = resource;
        Owner = owner;
        Member = member;
        Name = name;
    }

    /// <summary>The field's name as Tableau's filter and sort expressions write it, such as <c>siteRole</c>.</summary>
    public string Name { get; }

    /// <summary>What the field belongs to, as a list names it: <c>projects</c>, <c>users</c>, <c>groups</c>.</summary>
    internal string Resource { get; }

    /// <summary>The class that lists the field, for a message: <c>UserField</c>.</summary>
    internal string Owner { get; }

    /// <summary>The field's member in that class, for a message: <c>SiteRole</c>.</summary>
    internal string Member { get; }

    /// <summary>The field's name, as an expression writes it.</summary>
    public override string ToString() => Name;

    /// <summary>
    /// Throws <see cref="ArgumentException"/> naming <paramref name="parameter"/> when <paramref name="field"/> isn't
    /// one of <paramref name="resource"/>'s fields.
    /// </summary>
    internal static void Of(TableauField field, string resource, string parameter)
    {
        if (!string.Equals(field.Resource, resource, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{field.Owner}.{field.Member} is a field of {field.Resource}, and a list of {resource} takes "
                + "its own resource's fields alone.", parameter);
        }
    }
}

/// <summary>
/// The fields Query Projects filters and sorts on, as Tableau's reference lists them; each field says the operators the
/// reference gives it.
/// </summary>
public static class ProjectField
{
    private const string Projects = "projects";

    /// <summary><c>createdAt</c>, when the project was made: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField CreatedAt { get; } = new(Projects, nameof(ProjectField), nameof(CreatedAt), "createdAt");

    /// <summary><c>name</c>, the project's name: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Name { get; } = new(Projects, nameof(ProjectField), nameof(Name), "name");

    /// <summary><c>ownerDomain</c>, the domain of the project's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerDomain { get; } = new(Projects, nameof(ProjectField), nameof(OwnerDomain), "ownerDomain");

    /// <summary><c>ownerEmail</c>, the email address of the project's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerEmail { get; } = new(Projects, nameof(ProjectField), nameof(OwnerEmail), "ownerEmail");

    /// <summary><c>ownerName</c>, the user name of the project's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerName { get; } = new(Projects, nameof(ProjectField), nameof(OwnerName), "ownerName");

    /// <summary>
    /// <c>parentProjectId</c>, the LUID of the project the project is in: <c>eq</c>, <c>in</c>. Tableau Server 2025.1
    /// refuses it in a sort.
    /// </summary>
    public static TableauField ParentProjectId { get; } = new(Projects, nameof(ProjectField), nameof(ParentProjectId), "parentProjectId");

    /// <summary><c>topLevelProject</c>, whether the project is in no other project: <c>eq</c>.</summary>
    public static TableauField TopLevelProject { get; } = new(Projects, nameof(ProjectField), nameof(TopLevelProject), "topLevelProject");

    /// <summary><c>updatedAt</c>, when the project last changed: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField UpdatedAt { get; } = new(Projects, nameof(ProjectField), nameof(UpdatedAt), "updatedAt");
}

/// <summary>
/// The fields Get Users on Site filters and sorts on, as Tableau's reference lists them; each field says the operators
/// the reference gives it.
/// </summary>
public static class UserField
{
    private const string Users = "users";

    /// <summary><c>domainName</c>, the user's domain, <c>local</c> for a user Tableau holds itself: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField DomainName { get; } = new(Users, nameof(UserField), nameof(DomainName), "domainName");

    /// <summary><c>friendlyName</c>, the user's display name: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField FriendlyName { get; } = new(Users, nameof(UserField), nameof(FriendlyName), "friendlyName");

    /// <summary>
    /// <c>isLocal</c>, whether Tableau holds the user itself: <c>eq</c>. Tableau Server 2025.1 refuses it, though the
    /// reference lists it.
    /// </summary>
    public static TableauField IsLocal { get; } = new(Users, nameof(UserField), nameof(IsLocal), "isLocal");

    /// <summary><c>lastLogin</c>, when the user last signed in: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField LastLogin { get; } = new(Users, nameof(UserField), nameof(LastLogin), "lastLogin");

    /// <summary><c>luid</c>, the user's LUID: <c>eq</c>, <c>in</c>. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Luid { get; } = new(Users, nameof(UserField), nameof(Luid), "luid");

    /// <summary><c>name</c>, the user's name, which they sign in with: <c>cieq</c>, <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Name { get; } = new(Users, nameof(UserField), nameof(Name), "name");

    /// <summary><c>siteRole</c>, the user's site role, such as <c>Viewer</c>: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField SiteRole { get; } = new(Users, nameof(UserField), nameof(SiteRole), "siteRole");
}

/// <summary>
/// The fields Query Groups filters and sorts on, as Tableau's reference lists them; each field says the operators the
/// reference gives it.
/// </summary>
public static class GroupField
{
    private const string Groups = "groups";

    /// <summary><c>domainName</c>, the group's domain, <c>local</c> for a group Tableau holds itself: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField DomainName { get; } = new(Groups, nameof(GroupField), nameof(DomainName), "domainName");

    /// <summary><c>domainNickname</c>, the nickname of the group's domain: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField DomainNickname { get; } = new(Groups, nameof(GroupField), nameof(DomainNickname), "domainNickname");

    /// <summary>
    /// <c>isExternalUserEnabled</c>, whether the group has on-demand access: <c>eq</c>; Tableau Cloud, from REST API 3.21.
    /// </summary>
    public static TableauField IsExternalUserEnabled { get; } =
        new(Groups, nameof(GroupField), nameof(IsExternalUserEnabled), "isExternalUserEnabled");

    /// <summary><c>isLocal</c>, whether Tableau holds the group itself: <c>eq</c>.</summary>
    public static TableauField IsLocal { get; } = new(Groups, nameof(GroupField), nameof(IsLocal), "isLocal");

    /// <summary>
    /// <c>luid</c>, the group's LUID: <c>eq</c>, <c>in</c>; Tableau Cloud, from REST API 3.22. Tableau Server 2025.1
    /// refuses it in a sort.
    /// </summary>
    public static TableauField Luid { get; } = new(Groups, nameof(GroupField), nameof(Luid), "luid");

    /// <summary><c>minimumSiteRole</c>, the site role the group grants its members: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField MinimumSiteRole { get; } = new(Groups, nameof(GroupField), nameof(MinimumSiteRole), "minimumSiteRole");

    /// <summary>
    /// <c>name</c>, the group's name: <c>cieq</c>, <c>eq</c>, <c>in</c>, and <c>like</c>, which
    /// <see cref="TableauFilter.FromString"/> writes.
    /// </summary>
    public static TableauField Name { get; } = new(Groups, nameof(GroupField), nameof(Name), "name");

    /// <summary><c>userCount</c>, how many users the group has: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField UserCount { get; } = new(Groups, nameof(GroupField), nameof(UserCount), "userCount");
}
