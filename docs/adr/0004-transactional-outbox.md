# 0004. Transactional outbox for side effects

- Status: Accepted
- Date: 2026-11-09

## Context

State changes trigger side effects: audit entries, SignalR notifications, emails and ERP webhooks. If those are sent in-process after commit, a crash between commit and send loses them. If they are sent before commit, a rollback leaves notifications about things that never happened.

## Decision

- **Audit** entries are written in the **same transaction** as the change, by a `SaveChanges` interceptor that reads the domain events raised by aggregates.
- **Side effects** are written as rows in an `OutboxMessages` table, in the same transaction.
- A background worker polls the outbox (with `UPDLOCK, READPAST` so several instances do not double-pick), dispatches each message, and marks it processed. Failures retry with exponential backoff. After N attempts a message is parked for inspection.
- Every consumer is **idempotent**, keyed by the outbox message id. Delivery is at-least-once, never exactly-once.

## Consequences

- No lost events, and no phantom events.
- Notifications have a small delay (the polling interval, about 1–2 seconds).
- The outbox table needs a retention and cleanup job.
