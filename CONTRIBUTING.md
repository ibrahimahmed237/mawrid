# Contributing

## Documentation style

All docs in this repo use **simple technical English**:

- Short sentences. One idea per sentence.
- Common words. Write "use", not "leverage". Write "check", not "validate the integrity of".
- Keep technical terms as they are (`RowVersion`, outbox, ETag). Explain a term the first time you use it, or add it to the [glossary](docs/GLOSSARY.md).
- Use tables and lists for rules, options and steps. Use short paragraphs for reasons.
- Say *why*, not only *what*. Every rule should have a reason a new team member can understand.
- Record important decisions as an ADR in [docs/adr](docs/adr/README.md).

## Commits

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`, `ci:`.
- Keep each commit small. Each one must build and pass the tests.
- The subject line is in the imperative ("add supplier approval"), at most 72 characters. Use the body to explain why.
