## What and why

<!-- A few lines: what changes and why. The PR title becomes the commit subject on main: a plain imperative sentence. -->

## Plan

<!-- The plan in docs/plans/ for a non-trivial change, or "Trivial: <why>". -->

## Tested by

<!-- What checks the change, then what ran:
- `dotnet run eng/gate.cs -- full`, with its test count and coverage;
- the Live tests or a scratch program against a site: "ran on <Tableau Server and its version, or Tableau Cloud>",
  what passed and what fails on main too; or "not run", and why. Nothing else reaches a Tableau site, so a change to a
  request, a model or the Vizportal client always says which. Never the site's address, a user or a password. -->

## Server only

<!-- Does the change reach what only Tableau Server has, such as schedules, site management or Active Directory groups?
If so: what ran by hand on a Tableau Server, and its version (the ServerOnly tests, or calls from a scratch program),
and what Tableau Cloud couldn't show. If not: "None". -->

## Review

<!-- review-code's verdict, the findings it fixed and any it left with the reason, or who else reviewed it. -->

## One-way doors

<!-- "None", or the one this changes (AGENTS.md, One-way doors) and the issue where a maintainer agreed. -->

<!-- Closes #<n> when this finishes the issue; Refs #<n> when it is one step of it. -->
Closes #
