# 0002. Use .NET 10 LTS

- Status: Accepted
- Date: 2026-11-09

## Context

The first design used .NET 8. Microsoft support for .NET 8 (and .NET 9) ends on **10 November 2026**. After that there are no security fixes. .NET 10 is the current LTS version, supported until November 2028.

## Decision

Target `net10.0`, with ASP.NET Core 10, EF Core 10 and C# 14. Pin the SDK in `global.json` with `rollForward: latestFeature`.

## Consequences

- We stay on a supported runtime for the expected life of the project.
- Some tutorials and Stack Overflow answers show older APIs, so we use the official docs first.
- Upgrading to the next LTS (.NET 12, expected in Nov 2027) is planned work, not an emergency.
