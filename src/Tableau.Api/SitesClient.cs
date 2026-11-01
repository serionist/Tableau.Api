using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>Sites: <see cref="TableauClient.Sites"/>.</summary>
/// <remarks>
/// Tableau answers Query Site, by any key, only for the site the session is on, and only to a server or site
/// administrator. Query Sites, Create Site and Delete Site are Tableau Server's alone, and refused on Tableau Cloud before
/// anything is sent.
/// </remarks>
public class SitesClient
{
    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected SitesClient()
    {
    }

    internal SitesClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>Query Site, by its LUID, such as <see cref="TableauSession.SiteId"/>.</summary>
    /// <param name="id">The site's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The site.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Site> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QuerySite, new TableauRequest([RequestUri.Luid(id, nameof(id))]), static body => body.Site, "site",
            cancellationToken);

    /// <summary>
    /// Query Site, by its content URL, the part of a site's address after <c>/site/</c>. The default site has none: get it
    /// by its LUID.
    /// </summary>
    /// <param name="contentUrl">The site's content URL, as written, not URL-encoded.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The site.</returns>
    /// <exception cref="ArgumentException"><paramref name="contentUrl"/> is empty or only dots, and so names no site.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Site> GetByContentUrlAsync(string contentUrl, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QuerySiteByContentUrl,
            new TableauRequest([RequestUri.Segment(contentUrl, nameof(contentUrl))], [new("key", "contentUrl")]),
            static body => body.Site, "site", cancellationToken);

    /// <summary>Query Site, by its name.</summary>
    /// <param name="name">The site's name, as written, not URL-encoded.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The site.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or only dots, and so names no site.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Site> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.QuerySiteByName, new TableauRequest([RequestUri.Segment(name, nameof(name))], [new("key", "name")]),
            static body => body.Site, "site", cancellationToken);

    /// <summary>
    /// Query Sites, every page: the sites the user can reach, in Tableau's order, read a page at a time as you go, until
    /// the list ends; Tableau Server only. It reads its pages through <see cref="ListPageAsync"/>, so a mock that
    /// overrides that serves this too.
    /// </summary>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between sites and between pages.</param>
    /// <returns>The sites, as you read them.</returns>
    /// <exception cref="ArgumentException">The page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">
    /// A page's call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual async IAsyncEnumerable<Site> ListAsync(SiteListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var site in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token), Endpoints.QuerySites.Name, null,
                               cancellationToken)
                           .ConfigureAwait(false))
            yield return site;
    }

    /// <summary>Query Sites, one page; Tableau Server only.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The page size; null for 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its sites empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">The page number is below 1, or the page size outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Page<Site>> ListPageAsync(int pageNumber, SiteListOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SiteListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QuerySites, null, pageNumber, options.PageSize, null, null, null, "sites",
            static body => body.Sites, cancellationToken);
    }

    /// <summary>
    /// Create Site; Tableau Server only, by a server administrator. Sent once, never again after a failure, since a second
    /// would fail on the content URL.
    /// </summary>
    /// <param name="site">
    /// The site: its <see cref="Site.Name"/> and <see cref="Site.ContentUrl"/>, and any setting Tableau's reference lists
    /// for Create Site, such as <see cref="Site.AdminMode"/>, <see cref="Site.StorageQuota"/> or <see cref="Site.RevisionLimit"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The site as Tableau made it, with its id.</returns>
    /// <exception cref="ArgumentException">The site has no name or no content URL.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task<Site> CreateAsync(Site site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (string.IsNullOrEmpty(site.Name) || string.IsNullOrEmpty(site.ContentUrl))
            throw new ArgumentException("A new site needs a name and a content URL.", nameof(site));
        return Pipeline.SendAsync(Endpoints.CreateSite, new TableauRequest(Body: new TsRequest { Site = site }), static body => body.Site,
            "site", cancellationToken);
    }

    /// <summary>
    /// Update Site: changes the settings of <paramref name="changes"/> that are set, and leaves the others as they are.
    /// Tableau updates only the site the session is on, and on Tableau Server only for a server administrator.
    /// </summary>
    /// <param name="id">The site's LUID.</param>
    /// <param name="changes">What to change: set only those members, such as <c>new Site { RevisionLimit = 25 }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The site as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Site> UpdateAsync(string id, Site changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateSite, new TableauRequest([id], Body: new TsRequest { Site = changes }),
            static body => body.Site, "site", cancellationToken);
    }

    /// <summary>
    /// Delete Site: the site and everything on it; Tableau Server only, by a server administrator signed in to a site.
    /// The default site can't be deleted. Sent once, never again after a failure, since deleting a site can outlast an
    /// attempt and a second would find it half deleted.
    /// </summary>
    /// <param name="id">The site's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how, <see cref="TableauErrorKind.UnsupportedDeployment"/>
    /// on Tableau Cloud.
    /// </exception>
    public virtual Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteSite, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);
}
