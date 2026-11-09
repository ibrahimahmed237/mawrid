# 0007. Sealed quotations until the RFQ closes

- Status: Accepted
- Date: 2026-11-09

## Context

If buyers can see prices while an RFQ is open, one supplier's price can be shared with another. That makes the competition unfair. Sealed bids are standard in formal tenders, including government tenders in Saudi Arabia.

## Decision

- While an RFQ is `Published`, buyers see **who** submitted and when. They do not see prices, line details or attachments.
- Comparison and award are allowed only after the RFQ closes. If the deadline has passed, the RFQ counts as closed, even if the close job has not run yet.
- A supplier can change its own quotation until the deadline. Every version is saved, and only the latest one counts.
- If the RFQ changes after it is published (items, quantities or deadline):
  - suppliers who already submitted must confirm again;
  - the deadline must leave a minimum window after the change (configurable).

## Consequences

- Who can read a quotation depends on the RFQ's state, not only the user's role. We test this explicitly.
- Extra audit benefit: the first time a buyer opens the comparison is logged.
