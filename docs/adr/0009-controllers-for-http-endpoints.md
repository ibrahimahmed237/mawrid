# 0009. Use controllers for HTTP endpoints

- Status: Accepted
- Date: 2026-11-10

## Context

ASP.NET Core offers two styles: **controllers** (`[ApiController]` classes) and **Minimal APIs** (`app.MapGet(...)`). Both are fully supported and fast enough for this product.

## Decision

Use controllers, one per module resource (for example `SuppliersController`). Controllers stay thin: they map HTTP to a command or query, send it through MediatR, and map the `Result` to a response.

## Consequences

- The structure is familiar to most .NET teams and close to NestJS controllers.
- Attributes (`[Authorize]`, `[ProducesResponseType]`) keep routing, auth and OpenAPI metadata next to each action.
- Slightly more ceremony than Minimal APIs. This is acceptable.
