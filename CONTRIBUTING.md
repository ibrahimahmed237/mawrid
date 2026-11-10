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

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `build:`, `chore:`, `ci:`.
- Keep each commit small. Each one must build and pass the tests.
- The subject line is in the imperative ("add supplier approval"), at most 72 characters. Use the body to explain why.

## Code changes

- Put a business rule in the domain and test it there first; test the flow through the API.
- A module uses another module only through `Mawrid.Application.Contracts.{Module}`. The architecture tests enforce this.
- Changing an endpoint or DTO changes the API contract. Accept it on purpose with `UPDATE_SNAPSHOTS=1` (see the [development guide](docs/DEVELOPMENT.md#run-the-tests)) and commit the snapshot.
- A model change needs a migration (`dotnet ef migrations add <Name> ...`) in the same commit.
