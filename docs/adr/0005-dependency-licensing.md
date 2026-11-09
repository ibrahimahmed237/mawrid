# 0005. Library license rules

- Status: Accepted
- Date: 2026-11-09

## Context

Some popular .NET libraries became commercial:

- MediatR (v13+) and AutoMapper, in July 2025;
- FluentAssertions (v8+).

A commercial product must not get a license obligation by accident, just from a normal `dotnet add package`.

## Decision

- Use only permissive licenses (MIT, Apache-2.0, BSD). LGPL is allowed for runtime use only (Hangfire core).
- **Pin MediatR to 12.x** (Apache-2.0). Our code uses only `IRequest` and `IRequestHandler`, so replacing it later (with a source-generated mediator or plain services) is simple.
- **Do not use AutoMapper.** Write mappings by hand in projections (`Select(x => new Dto(...))`).
- **Pin FluentAssertions to 7.x** in test projects.
- `Directory.Packages.props` (central package management) pins every version. Dependabot ignores major-version updates for the pinned packages.

## Consequences

- We stay on older major versions of two libraries on purpose, and we watch their security advisories.
- Hand-written mapping is a bit longer, but much easier to debug.
