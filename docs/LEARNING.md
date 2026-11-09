# Learning notes: NestJS → ASP.NET Core

My notes while building Mawrid. I come from Node.js, NestJS and TypeScript. Most ideas map one-to-one to .NET. The differences are where the learning is.

## Concept map

| NestJS / Node | ASP.NET Core / .NET | Notes |
|---|---|---|
| `package.json` / npm | `.csproj` + NuGet (`dotnet add package`) | A solution (`.sln`) groups several projects, like a monorepo with workspaces |
| `main.ts` bootstrap | `Program.cs` (minimal hosting) | `builder.Services...` = providers, `app.Use...` = middleware |
| `@Module({ providers })` | `IServiceCollection` extension methods (`AddApplication()`, `AddInfrastructure()`) | No module decorators. Each layer has one `Add*` method |
| `@Injectable()` + constructor injection | Constructor injection, registered as `AddScoped/AddTransient/AddSingleton` | Scoped = one instance per HTTP request. It is cheap, and it is the default for DbContext |
| `@Controller()` / `@Get()` | `[ApiController]` + `[HttpGet]` | Model binding does the work of `@Body()`, `@Param()` and `@Query()` |
| `class-validator` DTOs + `ValidationPipe` | FluentValidation + MediatR pipeline behavior | Rules live in a validator class, not as decorators on the DTO |
| Interceptors | Middleware, filters, MediatR `IPipelineBehavior` | Pipeline behaviors run around every command: validation, logging, transactions |
| Exception filters | `IExceptionHandler` + `ProblemDetails` (RFC 7807) | A standard error format, built in |
| Guards | Authorization policies + `IAuthorizationHandler` | `[Authorize(Policy = "...")]` |
| Prisma / TypeORM | EF Core | `DbContext` ≈ Prisma client. `IEntityTypeConfiguration<T>` ≈ schema. Migrations are C# files |
| Prisma `$transaction` | `SaveChanges()` is already one transaction; `BeginTransactionAsync` for more | The change tracker saves all changes together |
| `@nestjs/config` + `.env` | `appsettings.json` + env vars + `IOptions<T>` | The env var `ConnectionStrings__Default` overrides `ConnectionStrings:Default` in the JSON file |
| BullMQ | Hangfire / `BackgroundService` | Hangfire gives a dashboard, retries and cron, like Bull |
| `@nestjs/websockets` / Socket.IO | SignalR | Groups ≈ rooms |
| Jest | xUnit + FluentAssertions + NSubstitute | `[Fact]` = `it`, `[Theory]` + `[InlineData]` = `it.each` |
| Supertest e2e | `WebApplicationFactory<Program>` | Starts the real app in memory |
| `interface` (structural) | `interface` (nominal) | A class must declare `: IFoo`. Having the same shape is not enough |
| `string \| null` | `string?` with nullable reference types on | The compiler warns about possible nulls. We treat warnings as errors |
| `Promise<T>` / `async` | `Task<T>` / `async` | Pass a `CancellationToken` all the way down. Similar to `AbortSignal`, but every async API in .NET accepts one |
| `number` | `int`, `long`, `decimal`, `double` | **Money is always `decimal`** |
| `readonly` class / `as const` | `record`, `init` setters | Records compare by value. Good for value objects and DTOs |

## Milestone notes

Each milestone adds a section here: what we built, the .NET idea behind it, and the questions an interviewer will probably ask.
