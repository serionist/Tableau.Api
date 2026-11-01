using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;
using Tableau.Api.Rest.Models;

namespace Tableau.Api;

/// <summary>The site's projects: <see cref="TableauClient.Projects"/>.</summary>
/// <remarks>
/// Tableau lists a project from its search index, so one just made may be missing from a list for a moment, and one
/// just deleted may stay in it for minutes.
/// </remarks>
public class ProjectsClient
{
    private const string Resource = "projects";

    // Made once and never changed: the oldest first, then by name, so pages don't shift between requests.
    private static readonly TableauSort _stable = TableauSort.Asc(ProjectField.CreatedAt).Then(TableauSort.Asc(ProjectField.Name));

    private readonly TableauPipeline? _pipeline;

    /// <summary>A sub-client with no connection, for a mock to derive from: its methods throw until overridden.</summary>
    protected ProjectsClient()
    {
    }

    internal ProjectsClient(TableauPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    private TableauPipeline Pipeline => _pipeline ?? throw TableauClient.ForMocksOnly();

    /// <summary>
    /// Query Projects, every page: the projects of the site the user can see, read a page at a time as you go, until the
    /// list ends. It reads its pages through <see cref="ListPageAsync"/>, so a mock that overrides that serves this too.
    /// </summary>
    /// <remarks>
    /// Without a sort the oldest come first, then by name, so the pages of a list that doesn't change neither skip nor
    /// repeat a project; one made, deleted or renamed while you read can still be missed or met twice.
    /// </remarks>
    /// <param name="options">The filter, the sort and the page size; null for every project, 100 a page.</param>
    /// <param name="cancellationToken">Stops the list, between projects and between pages.</param>
    /// <returns>The projects, as you read them.</returns>
    /// <exception cref="ArgumentException">The filter or the sort names another resource's field, or the page size is outside 1 to 1,000.</exception>
    /// <exception cref="TableauException">A page's call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual async IAsyncEnumerable<Project> ListAsync(ProjectListOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var project in TableauPaging.ListAsync((number, token) => ListPageAsync(number, options, token), cancellationToken)
                           .ConfigureAwait(false))
            yield return project;
    }

    /// <summary>Query Projects, one page.</summary>
    /// <param name="pageNumber">The page's number, from 1.</param>
    /// <param name="options">The filter, the sort and the page size; null for every project, 100 a page.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The page, its projects empty, never null, when it has none.</returns>
    /// <exception cref="ArgumentException">
    /// The page number is below 1, the page size outside 1 to 1,000, or the filter or the sort names another resource's
    /// field.
    /// </exception>
    /// <exception cref="TableauException">
    /// The call failed; <see cref="TableauException.Kind"/> says how. Tableau answers a page past the end with an error.
    /// </exception>
    public virtual Task<Page<Project>> ListPageAsync(int pageNumber, ProjectListOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ProjectListOptions();
        return TableauPaging.PageAsync(Pipeline, Endpoints.QueryProjects, null, pageNumber, options.PageSize, options.Filter, options.Sort,
            _stable, Resource, static body => body.Projects, cancellationToken);
    }

    /// <summary>
    /// Create Project: a project in the site, or in another project when <see cref="Project.ParentProjectId"/> names one.
    /// Sent once, never again after a failure, since a second would make a second project or fail on the name.
    /// </summary>
    /// <param name="project">
    /// The project: its <see cref="Project.Name"/>, and as you choose <see cref="Project.Description"/>,
    /// <see cref="Project.ParentProjectId"/>, <see cref="Project.ContentPermissions"/> and <see cref="Project.Owner"/>'s id.
    /// </param>
    /// <param name="publishSamples">Whether Tableau publishes its sample workbooks into it.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The project as Tableau made it, with its id.</returns>
    /// <exception cref="ArgumentException">The project has no name.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Project> CreateAsync(Project project, bool publishSamples = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrEmpty(project.Name))
            throw new ArgumentException("A project needs a name.", nameof(project));
        return Pipeline.SendAsync(Endpoints.CreateProject, new TableauRequest(
                Query: publishSamples ? [new("publishSamples", "true")] : null, Body: new TsRequest { Project = project }),
            static body => body.Project, "project", cancellationToken);
    }

    /// <summary>
    /// Update Project: changes the members of <paramref name="changes"/> that are set, and leaves the others as they are.
    /// An empty <see cref="Project.ParentProjectId"/> moves the project to the top of the site.
    /// </summary>
    /// <param name="id">The project's LUID.</param>
    /// <param name="changes">What to change: set only those members, such as <c>new Project { Name = "Finance" }</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The project as Tableau holds it now.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task<Project> UpdateAsync(string id, Project changes, CancellationToken cancellationToken = default)
    {
        RequestUri.Luid(id, nameof(id));
        ArgumentNullException.ThrowIfNull(changes);
        return Pipeline.SendAsync(Endpoints.UpdateProject, new TableauRequest([id], Body: new TsRequest { Project = changes }),
            static body => body.Project, "project", cancellationToken);
    }

    /// <summary>Delete Project: the project, and every workbook, data source, flow and project in it.</summary>
    /// <param name="id">The project's LUID.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is no LUID.</exception>
    /// <exception cref="TableauException">The call failed; <see cref="TableauException.Kind"/> says how.</exception>
    public virtual Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
        Pipeline.SendAsync(Endpoints.DeleteProject, new TableauRequest([RequestUri.Luid(id, nameof(id))]), cancellationToken);
}
