# Design review: DESIGN.md v1

- **Reviewed:** commit `a731631` (design v1)
- **Outcome:** changes requested. Design v2 addresses every Blocker and Major item below.

## Summary

The v1 skeleton is sound: Clean Architecture, an RFQ → PO → Invoice flow, and money as `decimal`. But it reads like a demo, not a product someone could pick up and run with. Three things are missing:

- **The business problem.** There are no goals, users, volumes or success measures.
- **How procurement actually works.** No requisitions, no approval matrix, open bids, exact-match invoicing, and the wrong side of ZATCA.
- **The production concerns that show up in week two:** lost events, idempotency, files, audit, tenancy, migrations and supportability.

It also targets a runtime that is about to go out of support.

## Blockers

### B1. Wrong runtime: .NET 8 support ends on 10 Nov 2026

v1 chose ".NET 8 LTS". Support for .NET 8 ends on 10 November 2026, which is the day this review is written. A new product must not start on a runtime that gets no security patches.

**Decision:** target **.NET 10 (LTS, supported until Nov 2028)** and EF Core 10. See [ADR-0002](../adr/0002-target-dotnet-10.md).

### B2. ZATCA modelled from the wrong side

v1 has the platform *generate* a Phase 1 QR for invoices. But the invoice is issued by the **supplier**, from the supplier's own e-invoicing solution. For B2B (standard tax) invoices in the integration phase, the invoice is cleared by ZATCA before it reaches the buyer. If our platform minted the supplier's invoice, it would be acting as the supplier's e-invoicing solution, which needs the supplier's own ZATCA onboarding.

Mawrid is a **buyer-side** (procure-to-pay / accounts payable) system. Its job is to **receive and verify** supplier invoices, not to issue them.

**Decision:**

- Ingest the supplier's invoice as UBL 2.1 XML (or PDF with embedded XML), with a plain PDF + manual entry as fallback.
- Decode and validate the QR (TLV): seller VAT number matches the supplier master, totals match the invoice body, and VAT is internally consistent.
- Flag mismatches for AP review.

See [ADR-0006](../adr/0006-buyer-side-invoice-ingestion.md).

### B3. Domain events can be lost

v1 dispatches domain events "after `SaveChanges` succeeds", in-process. A crash, deploy or exception between commit and dispatch loses the audit entry and the notification. Nothing retries. Audit is a compliance requirement in procurement, so "usually written" is not good enough.

**Decision:**

- Audit records are written **in the same transaction** as the change.
- Side effects (notifications, webhooks, emails) go through a **transactional outbox**. A background worker publishes them with retries, and every handler is idempotent.

See [ADR-0004](../adr/0004-transactional-outbox.md).

### B4. Late bids possible

v1 relies on a Hangfire job to close the RFQ at the deadline. If the job runs late, suppliers can keep submitting after the deadline.

**Decision:** the deadline is enforced **in the domain** with `IClock`: `Quotation.Submit` fails if `now >= rfq.Deadline`, whatever the status column says. The job only flips the status and sends notifications. A late job affects UX, never correctness.

## Major

### M1. No problem statement, users or non-functional requirements

There is no "why", no target customer, no volumes, no latency or availability goals, and no definition of success. Without these, every technical choice is unfalsifiable.

**Decision:** v2 adds context, goals and success metrics, explicit assumptions on volume, and NFRs.

### M2. The process starts too late: no purchase requisition

Real procurement starts with an internal need. An employee raises a **Purchase Requisition (PR)** against a cost center, it gets approved, and only then does Procurement source it (RFQ) or buy directly (PO from a contract or catalog). v1 starts at the RFQ, so it skips who asked, why, and from which budget.

**Decision:** add `PurchaseRequisition` (cost center, justification, lines, needed-by date). An approved PR converts to an RFQ, or straight to a PO for low-value buys.

### M3. One approval threshold is not how companies approve spend

Real companies use an **approval matrix**: amount bands, sometimes by department or category, with **sequential levels** (for example Manager → Director → CFO) and **delegation** when an approver is away. v1's single threshold would be rewritten in the first month.

**Decision:** build a generic **Approvals module** (`ApprovalPolicy` → `ApprovalRequest` → ordered `ApprovalStep`s) that PRs and POs both use, and support delegation with a validity window. Segregation of duties stays: the requester can never approve.

### M4. Open bids: buyers can see prices before the deadline

In v1 the buyer can read quotations as they come in. That invites leaking one supplier's price to another, and it is exactly what sealed bidding prevents (compare Etimad tendering practice).

**Decision:** quotations are **sealed** until the RFQ closes. The comparison endpoint returns `409` before close. The fact that a supplier has submitted is visible, but prices and attachments are not.

### M5. All-or-nothing award

Buyers often split an award: line 1 to supplier A, lines 2–3 to supplier B.

**Decision:** award per **line**. The award creates one PO per winning supplier.

### M6. Invoice matching with exact equality will put everything on hold

v1 holds an invoice if the price is not *exactly* equal to the PO. Real three-way match (PO ↔ goods receipt ↔ invoice) uses **tolerances**, because of rounding and small freight or price adjustments.

**Decision:**

- Configurable price and quantity tolerances (percentage and absolute), with a per-line rounding allowance of 0.01 SAR.
- Duplicate detection: one invoice per `(SupplierId, SupplierInvoiceNumber)`, enforced by a unique index.
- VAT category per line: standard, zero-rated or exempt. The rate is **effective-dated**, because KSA VAT moved from 5% to 15% on 1 July 2020 and a hard-coded constant breaks history.

### M7. Supplier onboarding is a single Admin click

Real onboarding is self-registration plus documents (CR, VAT certificate, bank letter), each with **expiry dates**, then a review.

Bank-detail changes are the classic AP fraud vector (business email compromise: "please pay our new IBAN"). They must be re-verified, and payments blocked until then.

**Decision:**

- Supplier self-registration with document uploads and expiry tracking.
- A daily job warns before documents expire and blocks new awards once they have.
- IBAN validated as `SA` + 22 characters with a mod-97 check.
- A bank change puts the supplier's payables on hold until the change is verified by a second person.

### M8. Single tenant baked in

v1 is explicitly single-tenant. Retrofitting tenancy later means touching every table, query and cache key.

**Decision:** every business row carries `OrganizationId` from day one. EF Core global query filters scope reads to the caller's organization. Suppliers are records per organization. A shared supplier network across buyers is a non-goal. We run with one organization, but the code is tenant-safe. See [ADR-0003](../adr/0003-tenant-ready-from-day-one.md).

### M9. No idempotency on commands

A retry on "submit invoice" or "award" (a mobile network drop, a double click, a gateway retry) creates duplicates.

**Decision:** `Idempotency-Key` header on all non-idempotent POSTs. The key, the request hash and the stored response are kept for 24 hours. A replay returns the original response. A same key with a different body returns `422`.

### M10. Optimistic concurrency only on PO

Any aggregate can be edited by two people at once.

**Decision:** `RowVersion` on every aggregate root, exposed as an `ETag`. Updates and state transitions require `If-Match`. A stale version returns `412`.

### M11. No files

RFQs need spec sheets, quotations need offer documents, invoices need PDFs and XML, and onboarding needs certificates. v1 has none of this.

**Decision:**

- `IFileStorage` abstraction: Azure Blob in production, Azurite locally.
- Short-lived read URLs.
- Size and content-type allow-lists, plus a malware-scan hook (status `PendingScan` → `Clean`/`Rejected`). Files are not served until clean.

### M12. Library licensing not considered

MediatR (v13+) and AutoMapper moved to commercial licensing in July 2025, and FluentAssertions v8+ is commercial too. A real team must decide this deliberately.

**Decision:**

- Keep the mediator pattern, but pin **MediatR 12.x** (Apache-2.0), behind thin usage, so replacing it is cheap.
- **No AutoMapper.** Explicit projections are clearer anyway.
- **FluentAssertions 7.x** pinned.

See [ADR-0005](../adr/0005-dependency-licensing.md).

### M13. Layering without module boundaries

"Domain / Application / Infrastructure / Api" alone becomes a big ball of mud by feature 10. Every handler can reach every table.

**Decision:** a **modular monolith**. The modules are Identity, Suppliers, Requisitions, Sourcing, Purchasing, Payables and Approvals, organized as folders/namespaces inside the layers. Modules talk through published interfaces or events, never by querying each other's tables. Architecture tests (NetArchTest) enforce both the layer rules and the module rules. See [ADR-0001](../adr/0001-modular-monolith-clean-architecture.md).

### M14. Integration with the customer's ERP ignored

Every buyer already runs an ERP (SAP, Oracle, Odoo, Dynamics). Approved POs and approved invoices must flow there, and master data (cost centers, GL accounts) must flow back. A procurement platform with no integration story will not get bought.

**Decision:**

- Outbound **webhooks** fed by the outbox, signed with HMAC-SHA256, with retries and backoff, and a delivery log that can be replayed.
- Cost centers importable by CSV/API.
- A mock ERP receiver in the repo for demos and tests.

## Minor

- **m1. Time.** Store UTC, but display and evaluate business deadlines in `Asia/Riyadh`. Reminder SLAs count the Saudi working week (Sunday to Thursday). Public holidays are configurable.
- **m2. Error contract.** Add a stable machine-readable `code` (e.g. `rfq.deadline_passed`) to `ProblemDetails`, so clients can localize and branch without parsing text.
- **m3. Paging.** Use one list envelope (`items`, `page`, `pageSize`, `total`) everywhere. Cap `pageSize` at 100.
- **m4. Migrations.** Never auto-migrate on app startup in production. CI builds an EF migration bundle, and the deploy runs it as a separate step.
- **m5. Observability.** Serilog alone is not enough. Add OpenTelemetry traces and metrics, and business metrics such as PR→PO cycle time, quotations per RFQ and invoices on hold.
- **m6. Secrets.** Use user-secrets locally and Key Vault in production. No connection strings in `appsettings.json`.
- **m7. Glossary.** Arabic/English procurement terms, so the domain language is shared with Arabic-speaking users and stakeholders.
- **m8. Scope.** v1's M8 (AI) is valuable, but the milestone list had no MVP line. v2 marks clearly what is MVP and what comes after.

## Not changed (and why)

- **Clean Architecture + CQRS with a mediator.** Some teams find it heavy for a monolith. We keep it because the target teams use it widely, and it gives clean seams for pipeline behaviors (validation, transactions, idempotency, audit).
- **SQL Server.** It is common in the target market, and features like `rowversion` fit our concurrency model.
- **No frontend in this repo for now.** The API is designed for a React client later (stable error codes, ETags, paging).
