# Plan review: the private notes

> The scenarios behind three findings of the plan review (`docs/plans/2026-10-28-modernization.md`, Plan review),
> kept out of the public plan until Tableau.Api 1.2.6 shipped on 2026-10-30 with their fixes (GHSA-wqhr-qp8x-c5gm).
> The plan records each finding and its fix; these say why each fix had to be what it is.

## Round 1

P3, the path-escaping rule as first planned for 1.2.6 ("no empty or dot-only segment, and /, ?, # and % always
escaped") can be bypassed: System.Uri converts "\" to "/" in http and https paths before it removes dot segments,
so an id like "..\users\U1" passes a dot-only check, is left alone by escaping that covers only four characters,
and becomes a traversal. The public fix: escape every character outside RFC 3986's unreserved set (keeping only
":" and "@"), and generate the test rows over all of ASCII.

## Round 2

P15: escaping "/" and "\" inside a segment is not enough on .NET Framework. 1.2.5 builds the request with
new Uri(url) (TableauApiClient.cs:121-126 at b23503e). Under .NET Framework's legacy URI parsing, which an app
keeps when it targets 4.0 (for ASP.NET, by <httpRuntime targetFramework> regardless of the build target),
System.Uri un-escapes percent-encoded path delimiters before path compression. So a segment holding %2F-escaped
slashes around ".." is decoded and its dot segments removed, and the request reaches another endpoint with the
caller's token. The dot-only check misses it, since the segment as a whole is not dot-only.

The public fix: after the request URI is built, its escaped path must equal the path the handler wrote, or the call
throws before sending; and a net48 test runs the path rows under the older URI parsing (an AppDomain whose
TargetFrameworkName is .NETFramework,Version=v4.0).

## Round 4

Improvement 2 (bidi rows): on .NET Framework targets 4.0 and 4.5 to 4.7.1, Uri strips Unicode bidi control
characters, even percent-encoded ones, before dot-segment compression. A segment made of dots with a
percent-encoded bidi character between them passes the dot-only argument check, then becomes ".." inside Uri. Only
the exact path check stops it, so that check carries weight on every target before 4.7.2, and 1.x must never drop
it. S2b's rows include that segment in the 4.0 and 4.6.1 domains. Against 1.2.5 itself this adds nothing: 1.2.5
sends ".." raw anyway.
