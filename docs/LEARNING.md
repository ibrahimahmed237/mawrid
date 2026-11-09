# Learning notes: NestJS → ASP.NET Core

These are my notes while building Mawrid. I'm coming from Node.js/NestJS/TypeScript, so most concepts map one-to-one; the differences are where the learning is.

## Concept map

| NestJS / Node | ASP.NET Core / .NET | Notes |
|---|---|---|
| `package.json` / npm | `.csproj` + NuGet (`dotnet add package`) | A solution (`.sln`) groups several projects, like a monorepo with workspaces |
| `main.ts` bootstrap | `Program.cs` (minimal hosting) | `builder.Services...` = providers, `app.Use...` = middleware |
| `@Module({ providers })` | `IServiceCollection` extension methods (`AddApplication()`, `AddInfrastructure()`) | No module decorators; each layer exposes one `Add*` method |
| `@Injectable()` + constructor injection | Constructor injection, registered as `AddScoped/AddTransient/AddSingleton` | Scoped = per HTTP request (NestJS `REQUEST` scope, but cheap and the default for DbContext) |
| `@Controller()` / `@Get()` | `[ApiController]` + `[HttpGet]` | Model binding replaces `@Body()`, `@Param()`, `@Query()` |
| `class-validator` DTOs + `ValidationPipe` | FluentValidation + MediatR pipeline behavior | Rules live in a validator class, not as decorators on the DTO |
| Interceptors | Middleware, filters, MediatR `IPipelineBehavior` | Pipeline behaviors wrap every command: validation, logging, transactions |
| Exception filters | `IExceptionHandler` + `ProblemDetails` (RFC 7807) | Standard error shape out of the box |
| Guards | Authorization policies + `IAuthorizationHandler` | `[Authorize(Policy = "...")]` |
| Prisma / TypeORM | EF Core | `DbContext` ≈ Prisma client; `IEntityTypeConfiguration<T>` ≈ schema; migrations are C# files |
| Prisma `$transaction` | `SaveChanges()` is already one transaction; `BeginTransactionAsync` for more | Change tracker batches all changes |
| `@nestjs/config` + `.env` | `appsettings.json` + env vars + `IOptions<T>` | Env var `ConnectionStrings__Default` overrides JSON `ConnectionStrings:Default` |
| BullMQ | Hangfire / `BackgroundService` | Hangfire gives a dashboard, retries and cron like Bull |
| `@nestjs/websockets` / Socket.IO | SignalR | Groups ≈ rooms |
| Jest | xUnit + FluentAssertions + NSubstitute | `[Fact]` = `it`, `[Theory]` + `[InlineData]` = `it.each` |
| Supertest e2e | `WebApplicationFactory<Program>` | Boots the real app in-memory |
| `interface` (structural) | `interface` (nominal) | A class must declare `: IFoo`; shape alone is not enough |
| `string \| null` | `string?` with nullable reference types on | Compiler warns on possible null; treat warnings as errors |
| `Promise<T>` / `async` | `Task<T>` / `async` | Pass `CancellationToken` all the way down; Node has no equivalent |
| `number` | `int`, `long`, `decimal`, `double` | **Money is always `decimal`** |
| `readonly` class / `as const` | `record`, `init` setters | Records give value equality, great for value objects and DTOs |

## Milestone notes

Each milestone adds a section here: what was built, the .NET concept behind it, and the questions an interviewer would likely ask about it.
