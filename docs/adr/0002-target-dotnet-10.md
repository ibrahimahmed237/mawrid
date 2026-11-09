# 0002. Target .NET 10 LTS

- Status: Accepted
- Date: 2026-11-09

## Context

The first design draft targeted .NET 8. Microsoft ends support for .NET 8 (and .NET 9) on **10 November 2026**, after which it gets no security fixes. .NET 10 is the current LTS release, supported until November 2028.

## Decision

Target `net10.0`, with ASP.NET Core 10, EF Core 10 and C# 14. The SDK is pinned in `global.json` with `rollForward: latestFeature`.

## Consequences

- We are on a supported runtime for the expected life of the project.
- Some tutorials and Stack Overflow answers show older APIs. We prefer the official docs.
- The upgrade to the next LTS (.NET 12, expected Nov 2027) is planned work, not an emergency.
