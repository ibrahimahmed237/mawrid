# Mawrid

A buyer-side procure-to-pay platform for companies in Saudi Arabia. It covers purchase requisitions, approval matrices, sealed RFQs, purchase orders, goods receipts, and supplier invoices. Every invoice is checked with a three-way match and a ZATCA QR check.

> Status: design done, code in progress. See the [delivery plan](docs/DESIGN.md#13-delivery-plan).

## Quick start

```bash
docker compose up --build
curl http://localhost:8080/health/ready
```

Run the tests with `dotnet test Mawrid.slnx` (Docker must be running). See the [development guide](docs/DEVELOPMENT.md) for details.

## Docs

- [Design](docs/DESIGN.md): context, domain model, architecture, API, operations
- [Development guide](docs/DEVELOPMENT.md): run, test, migrate, configure
- [Architecture decisions](docs/adr/README.md)
- [Glossary (EN/AR)](docs/GLOSSARY.md)
- [Design review of v1](docs/reviews/2026-11-design-review-v1.md)
- [Learning notes: NestJS → ASP.NET Core](docs/LEARNING.md)
- [Contributing and docs style](CONTRIBUTING.md)

## Tech

.NET 10 · ASP.NET Core · EF Core 10 · SQL Server 2022 · MediatR 12 · FluentValidation · ASP.NET Core Identity + JWT · Hangfire · SignalR · Serilog · OpenTelemetry · xUnit · Testcontainers · NetArchTest · Docker · GitHub Actions
