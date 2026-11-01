using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>A user's favorites: <see cref="TableauClient.Favorites"/>.</summary>
/// <remarks>
/// A user who is no server or site administrator reaches only their own favorites, and adds only content they can see.
/// </remarks>
public class FavoritesClient
{
    private const string Takes = "a workbook, a view, a data source, a project or a flow";

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected FavoritesClient()
    {
    }

    internal FavoritesClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    // A favorite as Tableau's JSON writes it: Get Favorites for User answers no pagination, so a page that starts with the
    // page before's first favorite is how a server that ignores the page's number shows.
    private static string Identity(Favorite favorite) => JsonSerializer.Serialize(favorite, TableauJsonContext.Default.Favorite);

    /// <summary>
    /// Get Favorites for User, every page: the user's favorites, read a page at a time as you go. A favorite whose content
    /// was deleted comes with its label alone.
    /// </summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="cancellationToken">Stops the list, between favorites and between pages.</param>
    /// <returns>The favorites, as you read them, in Tableau's order.</returns>
    /// <exception cref="ArgumentException"><paramref name="userId"/> is no LUID, which the first read throws.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Favorite> ListAsync(string userId, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        await foreach (var favorite in TableauPaging.ListAsync((number, token) => TableauPaging.PageAsync(Pipeline, Endpoints.GetFavoritesForUser,
                               [userId], number, TableauPaging.DefaultPageSize, null, null, null, "favorites", static body => body.Favorites, token),
                           Endpoints.GetFavoritesForUser.Name, Identity, cancellationToken).ConfigureAwait(false))
            yield return favorite;
    }

    /// <summary>
    /// Add a workbook, a view, a data source, a project or a flow to the user's favorites, under a label; adding it again
    /// with another label changes its label. Each kind is added from its own REST API version: a workbook and a view from
    /// 1.0, a data source from 2.3, a project from 3.1, a flow from 3.3.
    /// </summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="label">The favorite's label, which the user sees.</param>
    /// <param name="content">The content, such as <c>TableauContent.Workbook(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The favorites Tableau answers with, empty, never null, when it lists none.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="userId"/> is no LUID, <paramref name="label"/> is empty, or <paramref name="content"/> is of a kind
    /// favorites don't take, such as a table: nothing is sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<IReadOnlyList<Favorite>> AddAsync(string userId, string label, TableauContent content,
        CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(content);
        var (add, _) = Calls(content);
        return SendAsync(Pipeline, new TableauRequest([userId], Body: new TsRequest { Favorite = Labelled(content, label) }), add,
            cancellationToken);

        static async Task<IReadOnlyList<Favorite>> SendAsync(TableauPipeline pipeline, TableauRequest request, Endpoint endpoint,
            CancellationToken cancellationToken)
        {
            var body = await pipeline.SendAsync(endpoint, request, static body => body, "a body", cancellationToken).ConfigureAwait(false);
            return body.Favorites ?? [];
        }
    }

    /// <summary>
    /// Delete a workbook, a view, a data source, a project or a flow from the user's favorites; one that isn't there stays
    /// so. Each kind is deleted from its own REST API version: a workbook and a view from 2.0, a data source from 2.3, a
    /// project from 3.1, a flow from 3.3.
    /// </summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="content">The content, such as <c>TableauContent.Workbook(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="userId"/> is no LUID, or <paramref name="content"/> is of a kind favorites don't take: nothing is
    /// sent.
    /// </exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveAsync(string userId, TableauContent content, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(content);
        var (_, delete) = Calls(content);
        return Pipeline.SendAsync(delete, new TableauRequest([userId, content.Id]), cancellationToken);
    }

    // The add and the delete of a kind favorites take, each from its own version; another kind is refused.
    private static (Endpoint Add, Endpoint Delete) Calls(TableauContent content) => content.Kind switch
    {
        TableauContentKind.Workbook => (Endpoints.AddWorkbookToFavorites, Endpoints.DeleteWorkbookFromFavorites),
        TableauContentKind.View => (Endpoints.AddViewToFavorites, Endpoints.DeleteViewFromFavorites),
        TableauContentKind.DataSource => (Endpoints.AddDataSourceToFavorites, Endpoints.DeleteDataSourceFromFavorites),
        TableauContentKind.Project => (Endpoints.AddProjectToFavorites, Endpoints.DeleteProjectFromFavorites),
        TableauContentKind.Flow => (Endpoints.AddFlowToFavorites, Endpoints.DeleteFlowFromFavorites),
        _ => throw content.Refused("A favorite takes", Takes, nameof(content)),
    };

    // The favorite Add Favorite sends: its label, and the content by its id.
    private static Favorite Labelled(TableauContent content, string label) => content.Kind switch
    {
        TableauContentKind.Workbook => new Favorite { Label = label, Workbook = new Workbook { Id = content.Id } },
        TableauContentKind.View => new Favorite { Label = label, View = new View { Id = content.Id } },
        TableauContentKind.DataSource => new Favorite { Label = label, Datasource = new DataSource { Id = content.Id } },
        TableauContentKind.Project => new Favorite { Label = label, Project = new Project { Id = content.Id } },
        _ => new Favorite { Label = label, Flow = new Flow { Id = content.Id } },
    };
}
