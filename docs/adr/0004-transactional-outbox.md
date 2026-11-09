# 0004. Transactional outbox for side effects

- Status: Accepted
- Date: 2026-11-09

## Context

State changes cause side effects: audit rows, SignalR notifications, emails and ERP webhooks.

- If we send them **after** the commit, a crash in between loses them.
- If we send them **before** the commit and the transaction rolls back, we have told people about something that never happened.

## Decision

- **Audit rows** are written **in the same transaction** as the change. A `SaveChanges` interceptor builds them from the domain events raised by aggregates.
- **Side effects** are written as rows in an `OutboxMessages` table, in the same transaction.
- A background worker reads the outbox, sends each message, and marks it as processed.
  - It uses `UPDLOCK, READPAST`, so two app instances never pick the same row.
  - Failures retry with exponential backoff.
  - After N failed attempts, the message is parked so someone can check it.
- Every consumer is **idempotent**, using the outbox message ID. Delivery is at-least-once, not exactly-once.

## Consequences

- No lost events, and no events for changes that were rolled back.
- Notifications arrive a little later (one polling interval, about 1–2 seconds).
- The outbox table needs a cleanup job.
