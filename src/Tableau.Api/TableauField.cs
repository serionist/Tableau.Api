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
    internal TableauField(string resource, string owner, string name)
    {
        Resource = resource;
        Owner = owner;
        Name = name;
    }

    /// <summary>The field's name as Tableau's filter and sort expressions write it, such as <c>siteRole</c>.</summary>
    public string Name { get; }

    /// <summary>What the field belongs to, as a list names it: <c>projects</c>, <c>users</c>, <c>groups</c>.</summary>
    internal string Resource { get; }

    /// <summary>The class that lists the field, for a message: <c>UserField</c>.</summary>
    internal string Owner { get; }

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
            throw new ArgumentException($"{field.Owner}.{field.Name} is a field of {field.Resource}, and a list of {resource} takes "
                + "its own resource's fields alone.", parameter);
        }
    }
}

/// <summary>
/// The fields Query Projects filters and sorts on, as Tableau's reference lists them. Each takes <c>eq</c>; the names
/// and the owner's fields take <c>in</c> too, and the dates <c>gt</c>, <c>gte</c>, <c>lt</c> and <c>lte</c>.
/// </summary>
public static class ProjectField
{
    private const string Projects = "projects";

    /// <summary><c>createdAt</c>: when the project was made.</summary>
    public static TableauField CreatedAt { get; } = new(Projects, nameof(ProjectField), "createdAt");

    /// <summary><c>name</c>: the project's name.</summary>
    public static TableauField Name { get; } = new(Projects, nameof(ProjectField), "name");

    /// <summary><c>ownerDomain</c>: the domain of the project's owner.</summary>
    public static TableauField OwnerDomain { get; } = new(Projects, nameof(ProjectField), "ownerDomain");

    /// <summary><c>ownerEmail</c>: the email address of the project's owner.</summary>
    public static TableauField OwnerEmail { get; } = new(Projects, nameof(ProjectField), "ownerEmail");

    /// <summary><c>ownerName</c>: the user name of the project's owner.</summary>
    public static TableauField OwnerName { get; } = new(Projects, nameof(ProjectField), "ownerName");

    /// <summary><c>parentProjectId</c>: the LUID of the project the project is in. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField ParentProjectId { get; } = new(Projects, nameof(ProjectField), "parentProjectId");

    /// <summary><c>topLevelProject</c>: whether the project is in no other project.</summary>
    public static TableauField TopLevelProject { get; } = new(Projects, nameof(ProjectField), "topLevelProject");

    /// <summary><c>updatedAt</c>: when the project last changed.</summary>
    public static TableauField UpdatedAt { get; } = new(Projects, nameof(ProjectField), "updatedAt");
}

/// <summary>
/// The fields Get Users on Site filters and sorts on, as Tableau's reference lists them. Each takes <c>eq</c>; the names,
/// the domain, the LUID and the site role take <c>in</c> too, <see cref="Name"/> <c>cieq</c>, and
/// <see cref="LastLogin"/> <c>gt</c>, <c>gte</c>, <c>lt</c> and <c>lte</c>.
/// </summary>
public static class UserField
{
    private const string Users = "users";

    /// <summary><c>domainName</c>: the user's domain; <c>local</c> for a user Tableau holds itself.</summary>
    public static TableauField DomainName { get; } = new(Users, nameof(UserField), "domainName");

    /// <summary><c>friendlyName</c>: the user's display name.</summary>
    public static TableauField FriendlyName { get; } = new(Users, nameof(UserField), "friendlyName");

    /// <summary><c>isLocal</c>: whether Tableau holds the user itself. Tableau Server 2025.1 refuses it, though the reference lists it.</summary>
    public static TableauField IsLocal { get; } = new(Users, nameof(UserField), "isLocal");

    /// <summary><c>lastLogin</c>: when the user last signed in.</summary>
    public static TableauField LastLogin { get; } = new(Users, nameof(UserField), "lastLogin");

    /// <summary><c>luid</c>: the user's LUID. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Luid { get; } = new(Users, nameof(UserField), "luid");

    /// <summary><c>name</c>: the user's name, which they sign in with.</summary>
    public static TableauField Name { get; } = new(Users, nameof(UserField), "name");

    /// <summary><c>siteRole</c>: the user's site role, such as <c>Viewer</c>.</summary>
    public static TableauField SiteRole { get; } = new(Users, nameof(UserField), "siteRole");
}

/// <summary>
/// The fields Query Groups filters and sorts on, as Tableau's reference lists them. Each takes <c>eq</c>; the names, the
/// domain, the LUID and the minimum site role take <c>in</c> too, <see cref="Name"/> <c>cieq</c>, and
/// <see cref="UserCount"/> <c>gt</c>, <c>gte</c>, <c>lt</c> and <c>lte</c>.
/// </summary>
public static class GroupField
{
    private const string Groups = "groups";

    /// <summary><c>domainName</c>: the group's domain; <c>local</c> for a group Tableau holds itself.</summary>
    public static TableauField DomainName { get; } = new(Groups, nameof(GroupField), "domainName");

    /// <summary><c>domainNickname</c>: the nickname of the group's domain.</summary>
    public static TableauField DomainNickname { get; } = new(Groups, nameof(GroupField), "domainNickname");

    /// <summary><c>isExternalUserEnabled</c>: whether the group has on-demand access; Tableau Cloud, from REST API 3.21.</summary>
    public static TableauField IsExternalUserEnabled { get; } = new(Groups, nameof(GroupField), "isExternalUserEnabled");

    /// <summary><c>isLocal</c>: whether Tableau holds the group itself.</summary>
    public static TableauField IsLocal { get; } = new(Groups, nameof(GroupField), "isLocal");

    /// <summary><c>luid</c>: the group's LUID; Tableau Cloud, from REST API 3.22. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Luid { get; } = new(Groups, nameof(GroupField), "luid");

    /// <summary><c>minimumSiteRole</c>: the site role the group grants its members.</summary>
    public static TableauField MinimumSiteRole { get; } = new(Groups, nameof(GroupField), "minimumSiteRole");

    /// <summary><c>name</c>: the group's name.</summary>
    public static TableauField Name { get; } = new(Groups, nameof(GroupField), "name");

    /// <summary><c>userCount</c>: how many users the group has.</summary>
    public static TableauField UserCount { get; } = new(Groups, nameof(GroupField), "userCount");
}
