# 0006. Buyer-side invoice ingestion and ZATCA validation

- Status: Accepted
- Date: 2026-11-09

## Context

Under KSA e-invoicing (FATOORA), the **seller** issues the tax invoice from its own compliant solution. In the integration phase, standard (B2B) tax invoices are cleared by ZATCA before they are shared with the buyer. They are UBL 2.1 XML, often delivered inside a PDF/A-3, and carry a QR code.

Mawrid is the **buyer's** procure-to-pay system. Generating invoices for suppliers would make us their e-invoicing solution, which needs each supplier's own ZATCA onboarding, and it is the wrong responsibility for this product.

## Decision

Mawrid **receives and verifies** supplier invoices.

1. **Intake channels:** the supplier uploads UBL XML (preferred) or a PDF. For PDF-only invoices, AI-assisted extraction proposes the fields and a person confirms them.
2. **Parsing:** read the UBL fields that matter to AP: invoice number, issue date, seller VAT number, lines, VAT breakdown and totals.
3. **QR validation:** decode the Base64 TLV and check:
   - tags 1–5 are present;
   - the seller VAT number equals the supplier master record;
   - the invoice total and VAT total equal the parsed body;
   - the timestamp is consistent with the issue date.
   Cryptographic tags from the integration phase are parsed and stored. Verifying them against ZATCA certificates is a later milestone.
4. **Matching:** a tolerant three-way match against the PO and goods receipts (see DESIGN.md).
5. Any failure puts the invoice **On hold** with machine-readable reasons, for an AP clerk to resolve.

## Consequences

- The product does what real AP teams need: catch wrong VAT numbers, totals and duplicates before payment.
- We never claim compliance on a supplier's behalf.
- Full XML schema and business-rule validation (the ZATCA SDK rules) is out of scope for the MVP.
