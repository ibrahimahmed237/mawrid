# 0001. Modular monolith on Clean Architecture

- Status: Accepted
- Date: 2026-11-09

## Context

Procurement has several sub-domains with their own language and rules: supplier management, requisitions, sourcing, purchasing, payables and approvals. The team is small and the load is modest, so microservices would add network failure modes, distributed transactions and ops cost with no benefit. A plain layered app, though, lets any handler query any table, and boundaries erode within months.

## Decision

One deployable, organized as **modules inside Clean Architecture layers**:

- The layers are `Domain → Application → Infrastructure → Api`, and dependencies point inward only.
- The modules are Identity, Suppliers, Requisitions, Sourcing, Purchasing, Payables and Approvals. Each module is a folder/namespace in every layer.
- A module never reads another module's tables. It calls that module's published application interface (e.g. `ISupplierDirectory`) or reacts to its events.
- Commands and queries go through a mediator, so cross-cutting behavior lives in pipeline behaviors: validation, transaction, idempotency, logging.
- Architecture tests (NetArchTest) fail the build on a layer or module violation.

## Consequences

- One database and one transaction for most use cases: simple and consistent.
- A module can be extracted into a service later, because its seams are already explicit.
- There is more ceremony per feature than in a flat app. That is accepted for the learning value and the readability it gives at scale.
