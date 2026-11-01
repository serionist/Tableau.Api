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

/// <summary>
/// The fields Query Workbooks for Site filters and sorts on, as Tableau's reference lists them; each field says the
/// operators the reference gives it.
/// </summary>
public static class WorkbookField
{
    private const string Workbooks = "workbooks";

    /// <summary>
    /// <c>contentUrl</c>, the workbook's name in a URL, the site's own: <c>eq</c>, <c>in</c>. Tableau Server 2025.1
    /// refuses it in a sort.
    /// </summary>
    public static TableauField ContentUrl { get; } = new(Workbooks, nameof(WorkbookField), nameof(ContentUrl), "contentUrl");

    /// <summary><c>createdAt</c>, when the workbook was published first: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField CreatedAt { get; } = new(Workbooks, nameof(WorkbookField), nameof(CreatedAt), "createdAt");

    /// <summary><c>displayTabs</c>, whether the workbook shows its views in tabs: <c>eq</c>.</summary>
    public static TableauField DisplayTabs { get; } = new(Workbooks, nameof(WorkbookField), nameof(DisplayTabs), "displayTabs");

    /// <summary><c>favoritesTotal</c>, how many users have it as a favorite: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField FavoritesTotal { get; } = new(Workbooks, nameof(WorkbookField), nameof(FavoritesTotal), "favoritesTotal");

    /// <summary>
    /// <c>hasAlerts</c>, whether a data-driven alert watches it: <c>eq</c>. Tableau Server 2025.1 refuses it, though
    /// the reference lists it.
    /// </summary>
    public static TableauField HasAlerts { get; } = new(Workbooks, nameof(WorkbookField), nameof(HasAlerts), "hasAlerts");

    /// <summary><c>hasExtracts</c>, whether it holds an extract: <c>eq</c>.</summary>
    public static TableauField HasExtracts { get; } = new(Workbooks, nameof(WorkbookField), nameof(HasExtracts), "hasExtracts");

    /// <summary><c>name</c>, the workbook's name, its project's own: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Name { get; } = new(Workbooks, nameof(WorkbookField), nameof(Name), "name");

    /// <summary><c>ownerDomain</c>, the domain of the workbook's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerDomain { get; } = new(Workbooks, nameof(WorkbookField), nameof(OwnerDomain), "ownerDomain");

    /// <summary><c>ownerEmail</c>, the email address of the workbook's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerEmail { get; } = new(Workbooks, nameof(WorkbookField), nameof(OwnerEmail), "ownerEmail");

    /// <summary><c>ownerName</c>, the user name of the workbook's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerName { get; } = new(Workbooks, nameof(WorkbookField), nameof(OwnerName), "ownerName");

    /// <summary><c>projectName</c>, the name of the workbook's project: <c>eq</c>, <c>in</c>. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField ProjectName { get; } = new(Workbooks, nameof(WorkbookField), nameof(ProjectName), "projectName");

    /// <summary><c>sheetCount</c>, how many sheets it has: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField SheetCount { get; } = new(Workbooks, nameof(WorkbookField), nameof(SheetCount), "sheetCount");

    /// <summary><c>size</c>, its size in megabytes: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField Size { get; } = new(Workbooks, nameof(WorkbookField), nameof(Size), "size");

    /// <summary><c>subscriptionsTotal</c>, how many subscriptions it has: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField SubscriptionsTotal { get; } = new(Workbooks, nameof(WorkbookField), nameof(SubscriptionsTotal), "subscriptionsTotal");

    /// <summary><c>tags</c>, its tags: <c>eq</c> for one, <c>in</c> for any of several. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Tags { get; } = new(Workbooks, nameof(WorkbookField), nameof(Tags), "tags");

    /// <summary><c>updatedAt</c>, when it last changed: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField UpdatedAt { get; } = new(Workbooks, nameof(WorkbookField), nameof(UpdatedAt), "updatedAt");
}

/// <summary>
/// The fields Query Views for Site filters and sorts on, as Tableau's reference lists them; each field says the
/// operators the reference gives it.
/// </summary>
public static class ViewField
{
    private const string Views = "views";

    /// <summary><c>caption</c>, the view's caption: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Caption { get; } = new(Views, nameof(ViewField), nameof(Caption), "caption");

    /// <summary>
    /// <c>contentUrl</c>, the view's path in a URL, the site's own: <c>eq</c>, <c>in</c>. Tableau Server 2025.1 refuses
    /// it in a sort.
    /// </summary>
    public static TableauField ContentUrl { get; } = new(Views, nameof(ViewField), nameof(ContentUrl), "contentUrl");

    /// <summary><c>createdAt</c>, when the view was published first: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField CreatedAt { get; } = new(Views, nameof(ViewField), nameof(CreatedAt), "createdAt");

    /// <summary><c>favoritesTotal</c>, how many users have it as a favorite: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField FavoritesTotal { get; } = new(Views, nameof(ViewField), nameof(FavoritesTotal), "favoritesTotal");

    /// <summary><c>fields</c>, the fields the view uses: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Fields { get; } = new(Views, nameof(ViewField), nameof(Fields), "fields");

    /// <summary><c>hitsTotal</c>, how often it was seen: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField HitsTotal { get; } = new(Views, nameof(ViewField), nameof(HitsTotal), "hitsTotal");

    /// <summary><c>name</c>, the view's name: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Name { get; } = new(Views, nameof(ViewField), nameof(Name), "name");

    /// <summary><c>ownerDomain</c>, the domain of the view's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerDomain { get; } = new(Views, nameof(ViewField), nameof(OwnerDomain), "ownerDomain");

    /// <summary><c>ownerEmail</c>, the email address of the view's owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerEmail { get; } = new(Views, nameof(ViewField), nameof(OwnerEmail), "ownerEmail");

    /// <summary><c>ownerName</c>, the user name of the view's owner: <c>eq</c>.</summary>
    public static TableauField OwnerName { get; } = new(Views, nameof(ViewField), nameof(OwnerName), "ownerName");

    /// <summary><c>projectName</c>, the name of the view's project: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField ProjectName { get; } = new(Views, nameof(ViewField), nameof(ProjectName), "projectName");

    /// <summary><c>sheetNumber</c>, the view's place in its workbook: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField SheetNumber { get; } = new(Views, nameof(ViewField), nameof(SheetNumber), "sheetNumber");

    /// <summary><c>sheetType</c>, what the view is, such as a worksheet, a dashboard or a story: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField SheetType { get; } = new(Views, nameof(ViewField), nameof(SheetType), "sheetType");

    /// <summary><c>tags</c>, its tags: <c>eq</c> for one, <c>in</c> for any of several. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Tags { get; } = new(Views, nameof(ViewField), nameof(Tags), "tags");

    /// <summary><c>title</c>, the view's title: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Title { get; } = new(Views, nameof(ViewField), nameof(Title), "title");

    /// <summary><c>updatedAt</c>, when it last changed: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField UpdatedAt { get; } = new(Views, nameof(ViewField), nameof(UpdatedAt), "updatedAt");

    /// <summary><c>viewUrlName</c>, the view's name in a URL: <c>eq</c>, <c>in</c>. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField ViewUrlName { get; } = new(Views, nameof(ViewField), nameof(ViewUrlName), "viewUrlName");

    /// <summary><c>workbookDescription</c>, the description of the view's workbook: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField WorkbookDescription { get; } = new(Views, nameof(ViewField), nameof(WorkbookDescription), "workbookDescription");

    /// <summary><c>workbookName</c>, the name of the view's workbook: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField WorkbookName { get; } = new(Views, nameof(ViewField), nameof(WorkbookName), "workbookName");
}

/// <summary>
/// The fields Query Data Sources filters and sorts on, as Tableau's reference lists them; each field says the operators
/// the reference gives it.
/// </summary>
public static class DataSourceField
{
    private const string DataSources = "datasources";

    /// <summary><c>authenticationType</c>, how its connection signs in: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField AuthenticationType { get; } = new(DataSources, nameof(DataSourceField), nameof(AuthenticationType), "authenticationType");

    /// <summary>
    /// <c>connectedWorkbookType</c>: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>. Tableau Server 2025.1
    /// refuses it, though the reference lists it.
    /// </summary>
    public static TableauField ConnectedWorkbookType { get; } =
        new(DataSources, nameof(DataSourceField), nameof(ConnectedWorkbookType), "connectedWorkbookType");

    /// <summary><c>connectionTo</c>, what its connection reaches: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField ConnectionTo { get; } = new(DataSources, nameof(DataSourceField), nameof(ConnectionTo), "connectionTo");

    /// <summary><c>connectionType</c>, its connection's kind: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField ConnectionType { get; } = new(DataSources, nameof(DataSourceField), nameof(ConnectionType), "connectionType");

    /// <summary>
    /// <c>contentUrl</c>, the data source's name in a URL, the site's own: <c>eq</c>, <c>in</c>. Tableau Server 2025.1
    /// refuses it in a sort.
    /// </summary>
    public static TableauField ContentUrl { get; } = new(DataSources, nameof(DataSourceField), nameof(ContentUrl), "contentUrl");

    /// <summary><c>createdAt</c>, when it was published first: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField CreatedAt { get; } = new(DataSources, nameof(DataSourceField), nameof(CreatedAt), "createdAt");

    /// <summary><c>databaseName</c>, the database its connection reaches: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField DatabaseName { get; } = new(DataSources, nameof(DataSourceField), nameof(DatabaseName), "databaseName");

    /// <summary>
    /// <c>databaseUserName</c>, the user its connection signs in as: <c>eq</c>, <c>in</c>. Tableau Server 2025.1
    /// refuses it, though the reference lists it.
    /// </summary>
    public static TableauField DatabaseUserName { get; } = new(DataSources, nameof(DataSourceField), nameof(DatabaseUserName), "databaseUserName");

    /// <summary><c>description</c>, its description: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Description { get; } = new(DataSources, nameof(DataSourceField), nameof(Description), "description");

    /// <summary><c>favoritesTotal</c>, how many users have it as a favorite: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField FavoritesTotal { get; } = new(DataSources, nameof(DataSourceField), nameof(FavoritesTotal), "favoritesTotal");

    /// <summary><c>hasAlert</c>, whether a data-driven alert watches it: <c>eq</c>.</summary>
    public static TableauField HasAlert { get; } = new(DataSources, nameof(DataSourceField), nameof(HasAlert), "hasAlert");

    /// <summary><c>hasEmbeddedPassword</c>, whether its connection embeds a password: <c>eq</c>.</summary>
    public static TableauField HasEmbeddedPassword { get; } = new(DataSources, nameof(DataSourceField), nameof(HasEmbeddedPassword), "hasEmbeddedPassword");

    /// <summary><c>hasExtracts</c>, whether it holds an extract: <c>eq</c>.</summary>
    public static TableauField HasExtracts { get; } = new(DataSources, nameof(DataSourceField), nameof(HasExtracts), "hasExtracts");

    /// <summary><c>isCertified</c>, whether it is certified: <c>eq</c>.</summary>
    public static TableauField IsCertified { get; } = new(DataSources, nameof(DataSourceField), nameof(IsCertified), "isCertified");

    /// <summary><c>isConnectable</c>, whether the user can connect to it: <c>eq</c>.</summary>
    public static TableauField IsConnectable { get; } = new(DataSources, nameof(DataSourceField), nameof(IsConnectable), "isConnectable");

    /// <summary>
    /// <c>isDefaultPort</c>, whether its connection uses its database's default port: <c>eq</c>. Tableau Server 2025.1
    /// refuses it, though the reference lists it.
    /// </summary>
    public static TableauField IsDefaultPort { get; } = new(DataSources, nameof(DataSourceField), nameof(IsDefaultPort), "isDefaultPort");

    /// <summary><c>isHierarchical</c>, whether it is a hierarchical, multidimensional source: <c>eq</c>.</summary>
    public static TableauField IsHierarchical { get; } = new(DataSources, nameof(DataSourceField), nameof(IsHierarchical), "isHierarchical");

    /// <summary><c>isPublished</c>, whether it is published: <c>eq</c>.</summary>
    public static TableauField IsPublished { get; } = new(DataSources, nameof(DataSourceField), nameof(IsPublished), "isPublished");

    /// <summary><c>name</c>, the data source's name: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField Name { get; } = new(DataSources, nameof(DataSourceField), nameof(Name), "name");

    /// <summary><c>ownerDomain</c>, the domain of its owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerDomain { get; } = new(DataSources, nameof(DataSourceField), nameof(OwnerDomain), "ownerDomain");

    /// <summary><c>ownerEmail</c>, the email address of its owner: <c>eq</c>.</summary>
    public static TableauField OwnerEmail { get; } = new(DataSources, nameof(DataSourceField), nameof(OwnerEmail), "ownerEmail");

    /// <summary><c>ownerName</c>, the user name of its owner: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField OwnerName { get; } = new(DataSources, nameof(DataSourceField), nameof(OwnerName), "ownerName");

    /// <summary><c>projectName</c>, the name of its project: <c>eq</c>, <c>in</c>; Tableau lacks it from REST API 3.13 to 3.17.</summary>
    public static TableauField ProjectName { get; } = new(DataSources, nameof(DataSourceField), nameof(ProjectName), "projectName");

    /// <summary>
    /// <c>serverName</c>, the server its connection reaches: <c>eq</c>, <c>in</c>. Tableau Server 2025.1 refuses it,
    /// though the reference lists it.
    /// </summary>
    public static TableauField ServerName { get; } = new(DataSources, nameof(DataSourceField), nameof(ServerName), "serverName");

    /// <summary>
    /// <c>serverPort</c>, the port its connection reaches: <c>eq</c>. Tableau Server 2025.1 refuses it, though the
    /// reference lists it.
    /// </summary>
    public static TableauField ServerPort { get; } = new(DataSources, nameof(DataSourceField), nameof(ServerPort), "serverPort");

    /// <summary><c>size</c>, its size: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField Size { get; } = new(DataSources, nameof(DataSourceField), nameof(Size), "size");

    /// <summary><c>tableName</c>, a table its connection reads: <c>eq</c>, <c>in</c>.</summary>
    public static TableauField TableName { get; } = new(DataSources, nameof(DataSourceField), nameof(TableName), "tableName");

    /// <summary><c>tags</c>, its tags: <c>eq</c> for one, <c>in</c> for any of several. Tableau Server 2025.1 refuses it in a sort.</summary>
    public static TableauField Tags { get; } = new(DataSources, nameof(DataSourceField), nameof(Tags), "tags");

    /// <summary><c>type</c>, its connection's type, such as <c>sqlserver</c>: <c>eq</c>.</summary>
    public static TableauField Type { get; } = new(DataSources, nameof(DataSourceField), nameof(Type), "type");

    /// <summary><c>updatedAt</c>, when it last changed: <c>eq</c>, <c>gt</c>, <c>gte</c>, <c>lt</c>, <c>lte</c>.</summary>
    public static TableauField UpdatedAt { get; } = new(DataSources, nameof(DataSourceField), nameof(UpdatedAt), "updatedAt");
}
