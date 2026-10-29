# Releasing a 1.x patch

A 1.2.x release is a tag on `release/1.x`; `.github/workflows/release.yml` does the rest once a maintainer approves.

1. **In a PR into `release/1.x`,** set the version and date the changelog:
   - in `Tableau.Api/Tableau.Api.csproj`, `<Version>` to the release, such as `1.2.7`, and `AssemblyVersion` and
     `FileVersion` to match (`1.0.2.7`);
   - in `CHANGELOG.md`, turn `## [Unreleased]` into `## [1.2.7] - <yyyy-mm-dd>`, add a new empty `## [Unreleased]`
     above it, and add the version's link at the bottom.

   A release without a dated section fails before anything is published.
2. **Check it against a live site by hand** before tagging, on a Tableau site you may change: sign in, list, publish
   and delete something small, with any credentials in environment variables, never in the clone. Say in the PR what
   ran, on which Tableau version, and what Tableau Cloud could not show (Server-only calls, such as schedules).
3. **Merge it, wait for CI's push run on `release/1.x`** to go green, then tag the merged commit and push the tag:

   ```
   git fetch origin
   git tag -a v1.2.7 -m "Tableau.Api 1.2.7" origin/release/1.x
   git push origin v1.2.7
   ```

4. **Approve the `nuget` deployment** when `release.yml` asks. Before it asks, it has checked that the tag is on
   `release/1.x`, that the project's version is the tag's, that the changelog has the dated section, and that `ci.yml`
   concluded success on the tagged commit's push run; it has packed the package and checked it. Then it pushes the
   `.nupkg` and the `.snupkg` to nuget.org and creates the GitHub release, with the changelog section as its notes.

If the run fails before the approval, nothing was published: fix `release/1.x` in a PR and tag the new commit. If the
push fails partway, re-run the failed jobs; what is already on nuget.org is skipped.

## Setup

What the workflow needs, set up once by a maintainer:

- **The `nuget` environment:** deployments from `v*` tags only, a maintainer as required reviewer, and the secret
  `NUGET_USER`, the nuget.org profile name of the account that owns the package.
- **nuget.org trusted publishing:** a policy for `serionist/Tableau.Api`, workflow `release.yml`, environment `nuget`.
  `NuGet/login` trades the job's OIDC token for a key that lives an hour, so no API key is stored anywhere.
