# 0005. Dependency licensing policy

- Status: Accepted
- Date: 2026-11-09

## Context

Several popular .NET libraries changed to commercial licensing: MediatR (v13+) and AutoMapper in July 2025, and FluentAssertions (v8+). A commercial product must not pull in a license obligation by accident during a routine `dotnet add package`.

## Decision

- Only permissive licenses (MIT, Apache-2.0, BSD) are used, or LGPL for runtime-only use (Hangfire core).
- **MediatR is pinned to 12.x** (Apache-2.0). Handler code depends only on `IRequest`/`IRequestHandler`, so a swap (to a source-generated mediator or plain services) is mechanical.
- **No AutoMapper.** Mapping is explicit, in projections (`Select(x => new Dto(...))`).
- **FluentAssertions is pinned to 7.x** in test projects.
- `Directory.Packages.props` (central package management) pins every version. Dependabot is configured to ignore major-version bumps for the pinned packages.

## Consequences

- We consciously stay on older majors of two libraries and track their security advisories.
- Mapping code is a bit more verbose, and much easier to debug.
