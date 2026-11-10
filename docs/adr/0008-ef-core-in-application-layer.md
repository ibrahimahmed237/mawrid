# 0008. Use EF Core directly in the Application layer

- Status: Accepted
- Date: 2026-11-10

## Context

Handlers need to load aggregates and run read queries. There are two common options:

- **Repositories.** One interface per aggregate (`ISupplierRepository`), implemented in Infrastructure. The Application layer does not know EF Core.
- **EF Core directly.** Handlers use an `IAppDbContext` with `DbSet<T>` properties and write LINQ.

Repositories on top of EF Core often only wrap `DbSet` methods. They hide useful features (projections, `AsNoTracking`, `Include`) or grow one method per query.

## Decision

- The Application layer references the `Microsoft.EntityFrameworkCore` **core** package and uses `IAppDbContext`.
- Only Infrastructure references the SQL Server provider, migrations and interceptors.
- Queries project straight to DTOs with `Select(...)` and `AsNoTracking()`.
- Architecture tests still stop Application from referencing `Microsoft.EntityFrameworkCore.SqlServer`, Infrastructure or ASP.NET Core.

## Consequences

- Fewer layers and less code per feature. Queries are easy to read and fast.
- Handlers are tested against a real database (integration tests), not with mocked repositories. This also tests the SQL that EF generates.
- Changing the ORM later would touch handlers. We accept this; it is unlikely for this product.
