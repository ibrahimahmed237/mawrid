# 0003. Tenant-ready data model from day one

- Status: Accepted
- Date: 2026-11-09

## Context

We start with one buyer organization. But procurement platforms are sold as SaaS to many buyers, and adding tenancy later means changing every table, unique index, query, cache key and background job.

## Decision

- Every business entity has a non-null `OrganizationId`.
- The caller's organization comes from the authenticated principal (an `org` claim) through `ICurrentUser`. It is **never** taken from the request body or the route.
- EF Core global query filters scope all reads. Writes stamp `OrganizationId` in a `SaveChanges` interceptor.
- Unique indexes include `OrganizationId`, e.g. `(OrganizationId, VatNumber)` for suppliers.
- Background jobs carry the organization explicitly and set the tenant context before they run.
- Suppliers are per-organization records. A shared supplier network is a non-goal.

## Consequences

- There is a small cost on every entity and index now, and a large saving later.
- Bypassing the filter (`IgnoreQueryFilters`) is allowed only in named, reviewed places, such as cross-tenant admin jobs.
- Integration tests always run with **two** organizations, to prove isolation.
