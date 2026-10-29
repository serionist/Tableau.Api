# Security

## Report a vulnerability

Report it privately: the repository's **Security** tab > **Report a vulnerability**, or
[this link](https://github.com/serionist/Tableau.Api/security/advisories/new). Only the maintainers see it. Never put a
vulnerability in an issue, a pull request or a discussion: anyone can read those.

Include what you can:

- the Tableau.Api version, and Tableau Server with its version, or Tableau Cloud;
- what an attacker can do, and what they need first;
- the smallest code that shows it, with no server address, user name, password or token of yours.

What counts, for example:

- a password, personal access token or session token that reaches a log, an exception message, a URL or a host the
  caller didn't name;
- certificate validation turned off when the caller didn't turn it off;
- a value the caller passes that sends a request to an endpoint or a site other than the one the call names.

## How a fix ships

- A maintainer answers in the advisory.
- Until the fixed package is on nuget.org, everything public about the fix, its issue, pull request, commits,
  CHANGELOG and release notes, describes what it changes ("escape every path segment"), never how to attack the code
  before it.
- The fix ships as a patch of every supported line it affects. Then the advisory is published with the fixed
  versions, crediting you unless you ask otherwise.

## Supported versions

| Version | Fixes |
|---|---|
| 2.x | Every fix, once 2.0.0 is released. It is in progress on `main`, which is not for use until then |
| 1.2.x | Security fixes only, for 12 months after 2.0.0 is released; the end date is written here when it is |
| Older | None: move to the latest 1.2.x |
