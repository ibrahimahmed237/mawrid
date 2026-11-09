# 0006. Receive and check supplier invoices (buyer side)

- Status: Accepted
- Date: 2026-11-09

## Context

Under Saudi e-invoicing (FATOORA), the **seller** creates the tax invoice using its own approved system. In the integration phase, ZATCA clears standard (B2B) tax invoices before they go to the buyer. These invoices are UBL 2.1 XML (often inside a PDF/A-3 file) and have a QR code.

Mawrid is the **buyer's** procure-to-pay system. If we created invoices for suppliers, we would become their e-invoicing system. That needs each supplier's own ZATCA registration, and it is not this product's job.

## Decision

Mawrid **receives and checks** supplier invoices:

1. **Upload:** the supplier uploads UBL XML (preferred) or a PDF. For a PDF-only invoice, AI extraction suggests the fields and a person confirms them.
2. **Parse:** read the UBL fields that AP needs: invoice number, issue date, seller VAT number, lines, VAT breakdown and totals.
3. **QR check:** decode the Base64 TLV and check that:
   - tags 1–5 exist;
   - the seller VAT number matches our supplier record;
   - the invoice total and VAT total match the invoice body;
   - the timestamp fits the issue date.

   We read and store the extra cryptographic tags from the integration phase. Checking them against ZATCA certificates is a later milestone.
4. **Match:** three-way match with tolerances against the PO and goods receipts (see DESIGN.md).
5. **Hold:** if any check fails, the invoice goes **on hold** with machine-readable reasons, for an AP clerk to fix.

## Consequences

- The product does what real AP teams need: catch wrong VAT numbers, wrong totals and duplicates before payment.
- We never claim ZATCA compliance on a supplier's behalf.
- Full XML schema and business-rule validation (the ZATCA SDK rules) is not part of the MVP.
