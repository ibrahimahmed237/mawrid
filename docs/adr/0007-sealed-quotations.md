# 0007. Sealed quotations until RFQ close

- Status: Accepted
- Date: 2026-11-09

## Context

If buyers can see prices while an RFQ is open, one supplier's price can be leaked to another, which breaks fair competition. Sealed bidding is standard practice in formal tendering, including government tendering in KSA.

## Decision

- While an RFQ is `Published`, buyers see **who** has submitted (supplier and time), but not prices, line details or attachments.
- Comparison and award are allowed only after close. A deadline that has passed counts as closed even if the close job has not run yet.
- Suppliers can revise their own quotation until the deadline. Each revision is kept, and only the latest one counts.
- If the RFQ is amended after publishing (items, quantities or deadline), suppliers who already submitted must re-confirm, and the deadline must leave a configurable minimum window after the amendment.

## Consequences

- Read authorization depends on RFQ state, not only on role. This is tested explicitly.
- A small extra audit benefit: every reveal (the first comparison view) is logged.
