using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
                           cancellationToken).ConfigureAwait(false))
            yield return favorite;
    }

    /// <summary>
    /// Add a workbook, a view, a data source, a project or a flow to the user's favorites, under a label; adding it again
    /// with another label changes its label.
    /// </summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="label">The favorite's label, which the user sees.</param>
    /// <param name="target">The content, such as <c>FavoriteTarget.Workbook(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The favorites Tableau answers with, empty, never null, when it lists none.</returns>
    /// <exception cref="ArgumentException"><paramref name="userId"/> is no LUID, or <paramref name="label"/> is empty.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<IReadOnlyList<Favorite>> AddAsync(string userId, string label, FavoriteTarget target,
        CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentNullException.ThrowIfNull(target);
        return SendAsync(Pipeline, new TableauRequest([userId], Body: new TsRequest { Favorite = target.Labelled(label) }), target.Add,
            cancellationToken);

        static async Task<IReadOnlyList<Favorite>> SendAsync(TableauPipeline pipeline, TableauRequest request, Endpoint endpoint,
            CancellationToken cancellationToken)
        {
            var body = await pipeline.SendAsync(endpoint, request, static body => body, "a body", cancellationToken).ConfigureAwait(false);
            return body.Favorites ?? [];
        }
    }

    /// <summary>Delete a workbook, a view, a data source, a project or a flow from the user's favorites; one that isn't there stays so.</summary>
    /// <param name="userId">The user's LUID.</param>
    /// <param name="target">The content, such as <c>FavoriteTarget.Workbook(id)</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="userId"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task RemoveAsync(string userId, FavoriteTarget target, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(userId, nameof(userId));
        ArgumentNullException.ThrowIfNull(target);
        return Pipeline.SendAsync(target.Delete, new TableauRequest([userId, target.Id]), cancellationToken);
    }
}

/// <summary>
/// The content a favorite names: a workbook, a view, a data source, a project or a flow, by its LUID, each from the REST
/// API version Tableau's reference gives for its favorites.
/// </summary>
public sealed class FavoriteTarget
{
    private readonly Func<string, string, Favorite> _favorite;

    private FavoriteTarget(FavoriteType type, string id, Endpoint add, Endpoint delete, Func<string, string, Favorite> favorite)
    {
        Type = type;
        Id = id;
        Add = add;
        Delete = delete;
        _favorite = favorite;
    }

    /// <summary>What kind of content it is, as a favorite in Tableau's answer names it.</summary>
    public FavoriteType Type { get; }

    /// <summary>The content's LUID.</summary>
    public string Id { get; }

    /// <summary>The call that adds this kind of content.</summary>
    internal Endpoint Add { get; }

    /// <summary>The call that deletes this kind of content.</summary>
    internal Endpoint Delete { get; }

    /// <summary>A workbook, by its LUID: added to favorites from REST API 1.0, and removed from 2.0.</summary>
    /// <param name="id">The workbook's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static FavoriteTarget Workbook(string id) =>
        new(FavoriteType.Workbook, RequestUri.Luid(id, nameof(id)), Endpoints.AddWorkbookToFavorites, Endpoints.DeleteWorkbookFromFavorites,
            static (label, luid) => new Favorite { Label = label, Workbook = new Rest.Models.Workbook { Id = luid } });

    /// <summary>A view, by its LUID: added to favorites from REST API 1.0, and removed from 2.0.</summary>
    /// <param name="id">The view's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static FavoriteTarget View(string id) =>
        new(FavoriteType.View, RequestUri.Luid(id, nameof(id)), Endpoints.AddViewToFavorites, Endpoints.DeleteViewFromFavorites,
            static (label, luid) => new Favorite { Label = label, View = new Rest.Models.View { Id = luid } });

    /// <summary>A published data source, by its LUID: in favorites from REST API 2.3.</summary>
    /// <param name="id">The data source's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static FavoriteTarget DataSource(string id) =>
        new(FavoriteType.Datasource, RequestUri.Luid(id, nameof(id)), Endpoints.AddDataSourceToFavorites, Endpoints.DeleteDataSourceFromFavorites,
            static (label, luid) => new Favorite { Label = label, Datasource = new Rest.Models.DataSource { Id = luid } });

    /// <summary>A project, by its LUID: in favorites from REST API 3.1.</summary>
    /// <param name="id">The project's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static FavoriteTarget Project(string id) =>
        new(FavoriteType.Project, RequestUri.Luid(id, nameof(id)), Endpoints.AddProjectToFavorites, Endpoints.DeleteProjectFromFavorites,
            static (label, luid) => new Favorite { Label = label, Project = new Rest.Models.Project { Id = luid } });

    /// <summary>A flow, by its LUID: in favorites from REST API 3.3.</summary>
    /// <param name="id">The flow's LUID.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    public static FavoriteTarget Flow(string id) =>
        new(FavoriteType.Flow, RequestUri.Luid(id, nameof(id)), Endpoints.AddFlowToFavorites, Endpoints.DeleteFlowFromFavorites,
            static (label, luid) => new Favorite { Label = label, Flow = new Rest.Models.Flow { Id = luid } });

    /// <summary>The kind and the LUID, such as <c>workbook 1f2f3e4e-...</c>.</summary>
    public override string ToString() => $"{Type} {Id}";

    /// <summary>The favorite Add Favorite sends: its label, and this content by its id.</summary>
    internal Favorite Labelled(string label) => _favorite(label, Id);
}
