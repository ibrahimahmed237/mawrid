# 0003. Tenant-ready data model from day one

- Status: Accepted
- Date: 2026-11-09

## Context

We start with one buyer organization. But procurement platforms are sold as SaaS to many buyers. Adding tenancy later means changing every table, unique index, query, cache key and background job.

## Decision

- Every business entity has a required `OrganizationId`.
- The user's organization comes from the login token (`org` claim), read through `ICurrentUser`. It is **never** taken from the request body or the URL.
- EF Core global query filters limit all reads to the user's organization.
- A `SaveChanges` interceptor sets `OrganizationId` on new rows.
- Unique indexes include `OrganizationId`. For example: `(OrganizationId, VatNumber)` for suppliers.
- Background jobs carry the organization ID and set the tenant context before they run.
- Each organization has its own supplier records. A shared supplier network is a non-goal.

## Consequences

- Small extra work on every entity and index now. Much less work later.
- Skipping the filter (`IgnoreQueryFilters`) is allowed only in a few named, reviewed places, such as admin jobs that work across tenants.
- Integration tests always use **two** organizations, to prove that one cannot see the other's data.
