using System;
using System.Text;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>
/// What <see cref="WorkbooksClient.UpdateAsync"/> changes: the members Tableau's reference lists for Update Workbook.
/// A member left null stays as it is, and is not sent.
/// </summary>
public sealed record WorkbookUpdate
{
    /// <summary>The workbook's new name.</summary>
    public string? Name { get; init; }

    /// <summary>The workbook's new description, from REST API 3.20.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the workbook shows its views in tabs.</summary>
    public bool? ShowTabs { get; init; }

    /// <summary>
    /// True to put the workbook in the site's recently viewed list; once it is there, false changes nothing, and it leaves
    /// the list as newer content comes.
    /// </summary>
    public bool? RecentlyViewed { get; init; }

    /// <summary>Whether the workbook's extracts are encrypted.</summary>
    public bool? EncryptExtracts { get; init; }

    /// <summary>The LUID of the project to move the workbook to.</summary>
    public string? ProjectId { get; init; }

    /// <summary>The LUID of the user to make the workbook's owner, which only an administrator may change.</summary>
    public string? OwnerId { get; init; }

    /// <summary>
    /// How fresh the workbook's data is kept, from REST API 3.22: its <see cref="DataFreshnessPolicy.Option"/> and, for
    /// <c>FreshAt</c> or <c>FreshEvery</c>, that option's schedule.
    /// </summary>
    public DataFreshnessPolicy? DataFreshnessPolicy { get; init; }

    /// <summary>The body: the members set, a project and an owner by their LUIDs alone, and no element for one not set.</summary>
    internal Workbook Sent(string parameter) => new()
    {
        Name = Name,
        Description = Description,
        ShowTabs = Flag(ShowTabs),
        RecentlyViewed = RecentlyViewed,
        EncryptExtracts = Flag(EncryptExtracts),
        Project = ProjectId is null ? null : new Project { Id = ContentUpdates.Luid(ProjectId, nameof(ProjectId), parameter) },
        Owner = OwnerId is null ? null : new User { Id = ContentUpdates.Luid(OwnerId, nameof(OwnerId), parameter) },
        DataFreshnessPolicy = DataFreshnessPolicy,
    };

    // The schema holds showTabs and encryptExtracts as text.
    private static string? Flag(bool? value) => value is null ? null : value.Value ? "true" : "false";
}

/// <summary>
/// What <see cref="DataSourcesClient.UpdateAsync"/> changes: the members Tableau's reference lists for Update Data
/// Source. A member left null stays as it is, and is not sent.
/// </summary>
public sealed record DataSourceUpdate
{
    /// <summary>The data source's new name.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the data source is certified, which a project leader or an administrator may change.</summary>
    public bool? IsCertified { get; init; }

    /// <summary>A note on the data source's certification.</summary>
    public string? CertificationNote { get; init; }

    /// <summary>Whether the data source's extracts are encrypted.</summary>
    public bool? EncryptExtracts { get; init; }

    /// <summary>The LUID of the project to move the data source to.</summary>
    public string? ProjectId { get; init; }

    /// <summary>The LUID of the user to make the data source's owner.</summary>
    public string? OwnerId { get; init; }

    /// <summary>The body: the members set, a project and an owner by their LUIDs alone, and no element for one not set.</summary>
    internal DataSource Sent(string parameter) => new()
    {
        Name = Name,
        IsCertified = IsCertified,
        CertificationNote = CertificationNote,
        EncryptExtracts = EncryptExtracts is null ? null : EncryptExtracts.Value ? "true" : "false",
        Project = ProjectId is null ? null : new Project { Id = ContentUpdates.Luid(ProjectId, nameof(ProjectId), parameter) },
        Owner = OwnerId is null ? null : new User { Id = ContentUpdates.Luid(OwnerId, nameof(OwnerId), parameter) },
    };
}

/// <summary>
/// What <see cref="WorkbooksClient.UpdateConnectionAsync"/> and <see cref="DataSourcesClient.UpdateConnectionAsync"/>
/// change on a connection: the members Tableau's reference lists for Update Workbook Connection and Update Data Source
/// Connection. A member left null stays as it is, and is not sent; with none set, Tableau changes nothing.
/// </summary>
/// <remarks>
/// <see cref="Password"/> travels in the request's body alone, and <see cref="ToString"/> prints it as <c>***</c>.
/// </remarks>
public sealed record ConnectionUpdate
{
    /// <summary>The server the connection reaches.</summary>
    public string? ServerAddress { get; init; }

    /// <summary>The port the connection reaches the server on.</summary>
    public int? ServerPort { get; init; }

    /// <summary>
    /// How the connection signs in, such as <c>auth-user-pass</c>, <c>oauth</c> or <c>auth-keypair</c>, or a connector's
    /// own value, as the connection's XML names it: from REST API 3.27, below which an update that sets it is refused
    /// before anything is sent.
    /// </summary>
    public string? AuthenticationType { get; init; }

    /// <summary>The user name the connection signs in with.</summary>
    public string? UserName { get; init; }

    /// <summary>The password the connection signs in with.</summary>
    public string? Password { get; init; }

    /// <summary>Whether the password is embedded in the connection.</summary>
    public bool? EmbedPassword { get; init; }

    /// <summary>The body: the members set.</summary>
    internal Connection Sent() => new()
    {
        ServerAddress = ServerAddress,
        ServerPort = ServerPort,
        AuthenticationType = AuthenticationType,
        UserName = UserName,
        Password = Password,
        EmbedPassword = EmbedPassword,
    };

    // The members a record prints, but the password, as ***.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ServerAddress = ").Append(ServerAddress)
            .Append(", ServerPort = ").Append(ServerPort)
            .Append(", AuthenticationType = ").Append(AuthenticationType)
            .Append(", UserName = ").Append(UserName)
            .Append(", Password = ").Append(Password is null ? null : "***")
            .Append(", EmbedPassword = ").Append(EmbedPassword);
        return true;
    }
}

/// <summary>What the update records share.</summary>
internal static class ContentUpdates
{
    /// <summary>A LUID an update sends in its body, or <see cref="ArgumentException"/> naming the record and its member.</summary>
    public static string Luid(string value, string member, string parameter)
    {
        try
        {
            return RequestUri.Luid(value, member);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"The update's {member} is '{value}', which is no LUID.", parameter);
        }
    }
}
