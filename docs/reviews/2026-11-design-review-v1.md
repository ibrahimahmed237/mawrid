# Design review: DESIGN.md v1

- **Reviewed:** commit `a731631` (design v1)
- **Result:** changes needed. Design v2 fixes every Blocker and Major item below.

## Summary

The basic structure of v1 is good: Clean Architecture, the RFQ → PO → Invoice flow, and `decimal` for money. But it looks like a demo, not a real product. Three things are missing:

- **The business problem.** There are no goals, no users, no volumes and no way to measure success.
- **Real procurement rules.** There are no requisitions, no approval matrix, bids are open, invoice matching is too strict, and ZATCA is handled from the wrong side.
- **Production concerns.** Events can be lost, and there is no idempotency, no file handling, no audit, no tenancy and no migration plan.

It also uses a .NET version that is about to lose support.

## Blockers

### B1. .NET 8 support ends on 10 Nov 2026

v1 uses .NET 8. Support for .NET 8 ends on 10 November 2026. After that date it gets no security fixes. A new product should not start on it.

**Decision:** use **.NET 10 (LTS, supported until Nov 2028)** and EF Core 10. See [ADR-0002](../adr/0002-target-dotnet-10.md).

### B2. ZATCA is handled from the wrong side

In v1, our platform *creates* the QR code for invoices. But the **supplier** creates the invoice, using its own e-invoicing system. For B2B invoices, ZATCA checks and clears the invoice before the buyer receives it.

If our platform created the supplier's invoice, it would act as the supplier's e-invoicing system. That requires the supplier's own ZATCA registration, so it is not our job.

Mawrid is a **buyer-side** system (procure-to-pay and accounts payable). Its job is to **receive and check** supplier invoices.

**Decision:**

- Accept the supplier's invoice as UBL 2.1 XML, or as a PDF that contains the XML. As a fallback, accept a plain PDF with manual entry.
- Decode the QR code (TLV format) and check it:
  - the seller VAT number matches our supplier record;
  - the totals match the invoice body;
  - the VAT amounts are correct.
- If anything does not match, put the invoice on hold for the AP team.

See [ADR-0006](../adr/0006-buyer-side-invoice-ingestion.md).

### B3. Domain events can be lost

In v1, domain events are sent in memory after `SaveChanges` succeeds. If the app crashes between the commit and the send, the event is lost. That means no audit row and no notification, and nothing retries. In procurement, the audit trail is required, so "usually saved" is not enough.

**Decision:**

- Write audit rows **in the same transaction** as the change.
- Write side effects (notifications, webhooks, emails) to an **outbox table** in the same transaction. A background worker sends them, with retries. Every handler must be idempotent.

See [ADR-0004](../adr/0004-transactional-outbox.md).

### B4. Suppliers can submit after the deadline

In v1, a Hangfire job closes the RFQ at the deadline. If the job runs late, suppliers can still submit.

**Decision:** check the deadline **in the domain**, using `IClock`. `Quotation.Submit` fails if `now >= rfq.Deadline`, whatever the status column says. The job only updates the status and sends notifications. A late job is a small UX issue, not a data bug.

## Major

### M1. No problem statement, users or non-functional requirements

v1 does not say why the product exists, who uses it, how much load it has, how fast or available it must be, or how we measure success. Without this, we cannot judge any technical choice.

**Decision:** v2 adds context, goals, success measures, volume assumptions and non-functional requirements (NFRs).

### M2. The process starts too late: no purchase requisition

Real procurement starts with an internal request:

1. An employee creates a **Purchase Requisition (PR)** for a cost center.
2. The PR is approved.
3. Procurement either asks suppliers for prices (RFQ) or buys directly (PO).

v1 starts at the RFQ. So we lose who asked, why, and which budget pays.

**Decision:** add `PurchaseRequisition` (cost center, reason, lines, needed-by date). An approved PR becomes an RFQ, or a direct PO for small amounts.

### M3. One approval limit is not realistic

Real companies use an **approval matrix**:

- amount bands, sometimes also by department or category;
- **several levels in order** (for example Manager → Director → CFO);
- **delegation** when an approver is away.

v1's single limit would need a rewrite in the first month.

**Decision:** build one **Approvals module** (`ApprovalPolicy` → `ApprovalRequest` → ordered `ApprovalStep`s), used by both PRs and POs. Support delegation for a date range. Keep segregation of duties: the requester can never approve their own document.

### M4. Bids are open: buyers can see prices before the deadline

In v1, the buyer can read quotations as they arrive. A buyer could then share one supplier's price with another. Sealed bidding exists to stop this (Etimad, the Saudi government tender platform, works this way).

**Decision:** quotations are **sealed** until the RFQ closes. The comparison endpoint returns `409` before close. The buyer can see *that* a supplier submitted, but not the prices or attachments.

### M5. All-or-nothing award

Buyers often split an award. For example, line 1 goes to supplier A and lines 2–3 go to supplier B.

**Decision:** award **per line**. The award creates one PO per winning supplier.

### M6. Exact invoice matching will put almost every invoice on hold

v1 holds an invoice if the price is not *exactly* equal to the PO price. Real three-way matching (PO ↔ goods receipt ↔ invoice) allows small **tolerances**, because of rounding and small price or freight changes.

**Decision:**

- Price and quantity tolerances are configurable (percent and amount), plus 0.01 SAR rounding per line.
- Duplicate check: one invoice per `(SupplierId, SupplierInvoiceNumber)`, enforced by a unique index.
- VAT category per line (standard, zero-rated, exempt).
- VAT rates have a **start date**. KSA VAT changed from 5% to 15% on 1 July 2020, so a fixed constant would break old invoices.

### M7. Supplier onboarding is one Admin click

Real onboarding works like this: the supplier registers, uploads documents (CR, VAT certificate, bank letter) with **expiry dates**, and someone reviews them.

Changing bank details is the most common AP fraud: a fake email says "please pay our new IBAN". Bank changes must be checked again, and payments must wait until they are.

**Decision:**

- Suppliers register themselves and upload documents with expiry dates.
- A daily job sends warnings before documents expire. After expiry, the supplier cannot win new awards.
- IBAN check: `SA` + 22 characters, with the mod-97 checksum.
- A bank change puts the supplier's invoices on hold until a second person verifies it.

### M8. Single tenant is built in

v1 supports only one organization. Adding tenancy later means changing every table, query and cache key.

**Decision:** every business row has an `OrganizationId` from day one. EF Core global query filters limit reads to the user's organization. Each organization has its own supplier records; a shared supplier network is a non-goal. We run with one organization, but the code is safe for many. See [ADR-0003](../adr/0003-tenant-ready-from-day-one.md).

### M9. No idempotency on commands

If "submit invoice" or "award" is sent twice (network drop, double click, gateway retry), we get duplicates.

**Decision:** non-idempotent POSTs accept an `Idempotency-Key` header. We store the key, a hash of the request and the response for 24 hours:

- same key, same body → return the stored response;
- same key, different body → `422`.

### M10. Optimistic concurrency only on PO

Two people can edit any aggregate at the same time, not only POs.

**Decision:** every aggregate root has a `RowVersion`, returned as an `ETag`. Updates and state changes require `If-Match`. An old version returns `412`.

### M11. No file handling

RFQs need spec sheets, quotations need offer documents, invoices need PDF/XML files, and onboarding needs certificates. v1 has none of this.

**Decision:**

- `IFileStorage` interface: Azure Blob in production, Azurite locally.
- Read links that expire quickly.
- Allowed file types and a size limit.
- A virus-scan step (status `PendingScan` → `Clean` / `Rejected`). Files are not served until they are clean.

### M12. Library licenses not checked

MediatR (v13+) and AutoMapper became commercial in July 2025. FluentAssertions v8+ is commercial too. A real team must make this choice on purpose.

**Decision:**

- Keep the mediator pattern, but pin **MediatR 12.x** (Apache-2.0) and use only its basic interfaces, so it is easy to replace.
- **No AutoMapper.** Write mappings by hand; they are clearer.
- Pin **FluentAssertions 7.x**.

See [ADR-0005](../adr/0005-dependency-licensing.md).

### M13. Layers without module boundaries

"Domain / Application / Infrastructure / Api" alone turns into a big ball of mud after about ten features, because any handler can use any table.

**Decision:** a **modular monolith**. The modules are Identity, Suppliers, Requisitions, Sourcing, Purchasing, Payables and Approvals; each is a folder in every layer. A module talks to another only through its public interface or its events, never by reading its tables. Architecture tests (NetArchTest) check the layer and module rules. See [ADR-0001](../adr/0001-modular-monolith-clean-architecture.md).

### M14. No ERP integration

Every buyer already has an ERP (SAP, Oracle, Odoo, Dynamics). Approved POs and invoices must go to the ERP, and master data (cost centers, GL accounts) must come from it. Without this, nobody will buy the product.

**Decision:**

- Outbound **webhooks** from the outbox, signed with HMAC-SHA256, with retries, backoff and a delivery log that can be replayed.
- Cost center import by CSV or API.
- A mock ERP receiver in the repo, for demos and tests.

## Minor

- **m1. Time.** Store everything in UTC. Show and check business deadlines in `Asia/Riyadh`. Reminder SLAs use the Saudi work week (Sunday to Thursday), and holidays are configurable.
- **m2. Error codes.** Add a stable `code` (for example `rfq.deadline_passed`) to `ProblemDetails`, so clients can translate and react without reading the message text.
- **m3. Paging.** Use one list format everywhere (`items`, `page`, `pageSize`, `total`). The maximum `pageSize` is 100.
- **m4. Migrations.** Do not run migrations on app startup in production. CI builds an EF migration bundle, and the deploy runs it as its own step.
- **m5. Observability.** Serilog alone is not enough. Add OpenTelemetry traces and metrics, plus business metrics such as PR→PO time, quotations per RFQ and invoices on hold.
- **m6. Secrets.** Use user-secrets in development and Key Vault in production. No connection strings in `appsettings.json`.
- **m7. Glossary.** Add Arabic/English procurement terms, so the team and Arabic-speaking users share the same words.
- **m8. Scope.** AI extraction (v1 M8) is useful, but v1 had no MVP line. v2 shows clearly what is MVP and what comes later.

## Kept as is, and why

- **Clean Architecture + CQRS with a mediator.** Some teams find it heavy for a monolith. We keep it because it is common in the target job market, and it gives clean places for validation, transactions, idempotency and audit.
- **SQL Server.** It is common in the target market, and `rowversion` fits our concurrency model.
- **No frontend in this repo for now.** The API is built so a React client can be added later (stable error codes, ETags, paging).
