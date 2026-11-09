# Mawrid — Design

Mawrid (مورد, "supplier") is a procurement platform for a buyer company and its suppliers. It covers the path from request for quotation to paid-ready invoice: **RFQ → Quotation → Award → Purchase Order → Approval → Invoice**. Invoices follow Saudi VAT rules and carry a ZATCA Phase 1 QR code.

It is a portfolio project built to learn ASP.NET Core properly. Every major .NET building block is used once, for a real reason. That includes Clean Architecture, CQRS, EF Core, Identity, background jobs, real-time notifications, localization and testing.

## 1. Scope

In scope:

- **One buyer organization** (single tenant) with internal users: Admin, Buyer, Approver.
- **Many suppliers**, each with one or more Supplier users who log in to the supplier portal.
- **Arabic/English** API messages and validation errors, chosen with `Accept-Language`.
- **Saudi VAT** at 15%, plus a ZATCA Phase 1 (Generation) QR code on every invoice.
- **AI-assisted invoice capture.** A supplier uploads a PDF, an LLM extracts the fields, and a human confirms before anything is saved.

Out of scope, on purpose:

- **Multi-tenancy.**
- **ZATCA Phase 2 (Integration).** It needs cryptographic stamping, CSIDs and Fatoora API onboarding, which needs a real taxpayer.
- **Payments.**
- **A frontend.** The API is documented with Swagger, and a frontend can come later.

## 2. Actors and permissions

| Role | Can do |
|---|---|
| Admin | Manage users, approve or suspend suppliers, configure the approval threshold |
| Buyer | Create and publish RFQs, invite suppliers, compare quotations, award, create POs, record goods receipt |
| Approver | Approve or reject POs above the threshold |
| Supplier | See RFQs they are invited to, submit and edit quotations before the deadline, acknowledge POs, submit invoices |

Authorization is **policy-based**, not only role checks. For example, `CanApprovePurchaseOrder` means the user has the Approver role **and** is not the PO's creator (segregation of duties). The `IsSupplierOfResource` policy means a supplier only ever sees its own quotations, POs and invoices. That check is enforced in the handlers and through EF Core global query filters.

## 3. Domain model

```
Supplier ─┬─< SupplierUser
          └─< Quotation >── Rfq ──< RfqItem
                 │            └──< RfqInvitation >── Supplier
                 └─< QuotationLine (per RfqItem)

Rfq (awarded) ──> PurchaseOrder ──< PurchaseOrderLine
                       ├──< ApprovalDecision
                       ├──< GoodsReceipt
                       └──< Invoice ──< InvoiceLine
```

### Key entities and rules

**Supplier**

- Fields: `NameAr`, `NameEn`, `VatNumber`, `CrNumber`, `Status` (Pending → Approved → Suspended).
- `VatNumber` is 15 digits and starts and ends with `3`. `CrNumber` is 10 digits.
- Only Approved suppliers can be invited to RFQs.

**Rfq**

- Status: `Draft → Published → Closed → Awarded | Cancelled`.
- It needs at least one item and one invited supplier before it can be published.
- The deadline must be in the future when it is published.
- A background job closes it automatically when the deadline passes.

**Quotation**

- There is one per (RFQ, Supplier). This is a unique index, not just a check in code.
- It can be edited until the RFQ deadline, and every edit bumps its `Revision`.
- Each line gives a unit price per RFQ item, and the totals are computed by the domain.

**Award**

- The buyer picks one quotation on a Closed RFQ.
- The RFQ becomes Awarded, the other quotations become Rejected, and a Draft PO is created from the winning lines.

**PurchaseOrder**

- Status: `Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received | Cancelled`.
- If the total including VAT is above the configured threshold, the PO needs an Approver's decision. Otherwise it is auto-approved on submit.
- It uses **optimistic concurrency** (a `RowVersion` column). If two approvers act at the same time, one gets `409 Conflict` instead of a double decision.

**Invoice**

- It is issued against a PO that is Sent or Received.
- **Matching rule:** the invoice quantity per line cannot exceed the received quantity, and the price must equal the PO price. A mismatch puts the invoice in `OnHold` with a reason.
- **VAT:** 15% per line, rounded to 2 decimals using `MidpointRounding.AwayFromZero`.
- **QR:** a ZATCA Phase 1 TLV encoding, then Base64. It holds five tags: (1) seller name, (2) seller VAT number, (3) timestamp, (4) total with VAT, (5) VAT total.
- Status: `Draft → Submitted → Matched | OnHold → ApprovedForPayment`.

### Money

Amounts use a `Money` value object (`decimal Amount`, `string Currency = "SAR"`). EF Core stores it as an owned type with `decimal(18,2)`. A `double` is never used for money.

### Domain events

Aggregates raise events such as `RfqPublished`, `QuotationSubmitted`, `PurchaseOrderApproved` and `InvoiceOnHold`. They are dispatched **after** `SaveChanges` succeeds (via an EF Core `SaveChangesInterceptor`), so a handler never acts on data that was rolled back. The handlers write the audit log and push SignalR notifications.

## 4. Architecture

Clean Architecture. Dependencies point inward only.

```
Mawrid.Api ──> Mawrid.Application ──> Mawrid.Domain
     │                ▲
     └──> Mawrid.Infrastructure
```

| Project | Contains | Depends on |
|---|---|---|
| `Mawrid.Domain` | Entities, value objects, domain events, domain errors. No framework references. | — |
| `Mawrid.Application` | MediatR commands/queries and handlers, FluentValidation validators, pipeline behaviors (validation, logging, transaction), interfaces (`IAppDbContext`, `ICurrentUser`, `IClock`, `IInvoiceExtractor`) | Domain |
| `Mawrid.Infrastructure` | EF Core `AppDbContext` and configurations, migrations, Identity, JWT issuing, Hangfire jobs, ZATCA QR encoder, OpenAI extractor, `SystemClock` | Application |
| `Mawrid.Api` | Controllers, `ProblemDetails` exception handler, auth policies, SignalR hub, Swagger, localization, health checks, Serilog | Application, Infrastructure (composition root only) |

**CQRS without two databases.** Commands go through aggregates and `SaveChanges`. Queries read with `AsNoTracking()` and project straight into DTOs. That keeps reads fast and keeps write logic in the domain.

**Errors.** Expected failures (validation, not found, conflict, rule violated) come back as a `Result<T>` and map to `ProblemDetails` with the right status: 400, 404, 409 or 422. Exceptions are reserved for bugs and infrastructure failures, and those map to 500 with a correlation id.

## 5. API surface (v1)

```
POST   /api/v1/auth/login                        -> JWT access + refresh
POST   /api/v1/auth/refresh

GET    /api/v1/suppliers?status=&search=&page=   (Admin, Buyer)
POST   /api/v1/suppliers                         (Admin)  register
POST   /api/v1/suppliers/{id}/approve            (Admin)

POST   /api/v1/rfqs                              (Buyer)
PUT    /api/v1/rfqs/{id}                         (Buyer, Draft only)
POST   /api/v1/rfqs/{id}/invitations             (Buyer)
POST   /api/v1/rfqs/{id}/publish                 (Buyer)
GET    /api/v1/rfqs/{id}/comparison              (Buyer)  side-by-side quotations
POST   /api/v1/rfqs/{id}/award                   (Buyer)  -> creates PO

GET    /api/v1/supplier/rfqs                     (Supplier) invited RFQs only
PUT    /api/v1/supplier/rfqs/{id}/quotation      (Supplier) upsert before deadline

POST   /api/v1/purchase-orders/{id}/submit       (Buyer)
POST   /api/v1/purchase-orders/{id}/approve      (Approver) If-Match: rowversion
POST   /api/v1/purchase-orders/{id}/reject       (Approver)
POST   /api/v1/purchase-orders/{id}/receipts     (Buyer)

POST   /api/v1/supplier/invoices/extract         (Supplier) PDF -> draft fields
POST   /api/v1/supplier/invoices                 (Supplier)
GET    /api/v1/invoices/{id}/qr                  -> PNG

GET    /health/live  /health/ready
WS     /hubs/notifications
```

## 6. Cross-cutting

| Concern | Choice |
|---|---|
| Database | SQL Server 2022 (Docker); EF Core 8 code-first migrations |
| Auth | ASP.NET Core Identity + JWT bearer; refresh tokens stored hashed |
| Validation | FluentValidation through a MediatR pipeline behavior |
| Background jobs | Hangfire with SQL Server storage: RFQ auto-close, deadline reminders |
| Real-time | SignalR hub; users join groups per role and per supplier |
| Localization | `IStringLocalizer` + `.resx` (ar, en); `RequestLocalizationMiddleware` |
| Logging | Serilog, structured JSON; correlation id per request |
| Health | `/health/live` (process) and `/health/ready` (SQL Server + Hangfire) |
| Time | `IClock` abstraction, all UTC in storage, so deadline tests are deterministic |
| Testing | xUnit + FluentAssertions; domain unit tests; handler tests; API integration tests with `WebApplicationFactory` + Testcontainers (real SQL Server) |
| Delivery | Dockerfile (multi-stage) + docker-compose (api, sqlserver); GitHub Actions: build, test, coverage |

## 7. Milestones

Each milestone is a set of small commits, and each one ends green (build + tests pass).

| # | Milestone | You learn |
|---|---|---|
| M0 | Design (this doc) | — |
| M1 | Solution skeleton, Docker SQL Server, EF Core context, health checks, Serilog, CI | .NET solution layout, DI, configuration, middleware pipeline |
| M2 | Suppliers: register, approve, list with paging/search | MediatR, FluentValidation, EF Core mapping, value objects, Result → ProblemDetails |
| M3 | Identity + JWT + policies | ASP.NET Core Identity, auth handlers, policy-based authorization |
| M4 | RFQ lifecycle + supplier quotations | Aggregates, invariants, unique indexes, global query filters |
| M5 | Award → PO → approval with concurrency | Domain events, interceptors, optimistic concurrency, `If-Match` |
| M6 | Invoices, matching, VAT, ZATCA QR | Money handling, TLV encoding, file responses |
| M7 | Hangfire jobs + SignalR notifications + ar/en localization | Background processing, real-time, `IStringLocalizer` |
| M8 | AI invoice extraction (human-in-the-loop) | `HttpClientFactory`, typed clients, resilience (Polly), structured LLM output |

## 8. Interview talking points this design creates

- Why the **unique index** on (RfqId, SupplierId) and not just a check in code: two concurrent submits would both pass the check.
- Why domain events are dispatched **after commit**.
- How `RowVersion` gives optimistic concurrency, and why approval needs it.
- How VAT rounding works, and why `decimal` is used for money.
- How the design maps to NestJS. See [LEARNING.md](LEARNING.md).
