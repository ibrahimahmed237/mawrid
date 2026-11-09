# 0001. Modular monolith on Clean Architecture

- Status: Accepted
- Date: 2026-11-09

## Context

Procurement has several sub-domains, each with its own words and rules: suppliers, requisitions, sourcing, purchasing, payables and approvals.

- **Microservices are too much.** The team is small and the load is low, so they would only add network failures, distributed transactions and operations cost.
- **A plain layered app is too little.** Any handler can read any table, so the boundaries break down within months.

## Decision

Build one deployable app, split into **modules inside Clean Architecture layers**:

- Layers: `Domain → Application → Infrastructure → Api`. Dependencies point inward only.
- Modules: Identity, Suppliers, Requisitions, Sourcing, Purchasing, Payables, Approvals. Each module is a folder (and namespace) in every layer.
- A module never reads another module's tables. It calls the other module's public interface (for example `ISupplierDirectory`) or reacts to its events.
- Commands and queries go through a mediator. Shared logic (validation, transactions, idempotency, logging) lives in pipeline behaviors.
- Architecture tests (NetArchTest) fail the build if a layer or module rule is broken.

## Consequences

- One database and one transaction for most use cases. This is simple and consistent.
- A module can become its own service later, because its boundaries are already clear.
- Each feature needs a few more files than in a flat app. We accept this for readability and for the learning value.
