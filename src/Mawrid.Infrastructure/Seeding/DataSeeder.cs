using Mawrid.Application.Common.Security;
using Mawrid.Application.Contracts.Approvals;
using Mawrid.Domain.Approvals;
using Mawrid.Domain.Common;
using Mawrid.Domain.Identity;
using Mawrid.Infrastructure.Identity;
using Mawrid.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mawrid.Infrastructure.Seeding;

/// <summary>
/// Creates the first organization and admin (bootstrap), and optional demo data.
/// Safe to run on every start: it does nothing when the data already exists.
/// </summary>
public static partial class DataSeeder
{
    /// <summary>Password for every demo user. Development only.</summary>
    public const string DemoPassword = "Mawrid-Demo-2026";

    public static async Task SeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (options.Bootstrap is null || string.IsNullOrWhiteSpace(options.Bootstrap.OrganizationName))
        {
            return;
        }

        var context = scope.ServiceProvider.GetRequiredService<CurrentUserContext>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataSeeder));

        context.RunAsSystem(null);
        if (await db.Organizations.AnyAsync(cancellationToken))
        {
            return;
        }

        var organization = Organization.Create(options.Bootstrap.OrganizationName).Value;
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(cancellationToken);
        context.RunAsSystem(organization.Id);

        await AddUserAsync(db, identity, organization.Id, options.Bootstrap.AdminEmail, options.Bootstrap.AdminName,
            options.Bootstrap.AdminPassword, [new RoleAssignment(Role.Admin)], cancellationToken);
        LogBootstrapped(logger, organization.Name);

        if (options.DemoData)
        {
            await AddDemoDataAsync(db, identity, organization.Id, cancellationToken);
            LogDemoData(logger);
        }
    }

    private static async Task AddDemoDataAsync(AppDbContext db, IIdentityService identity, Guid organizationId, CancellationToken cancellationToken)
    {
        var it = CostCenter.Create(organizationId, "IT-001", "Information Technology").Value;
        var ops = CostCenter.Create(organizationId, "OPS-001", "Operations").Value;
        db.CostCenters.AddRange(it, ops);
        await db.SaveChangesAsync(cancellationToken);

        (string Email, string Name, RoleAssignment[] Roles)[] users =
        [
            ("buyer@acme.test", "Sara Buyer", [new RoleAssignment(Role.Buyer)]),
            ("approver@acme.test", "Khalid Approver", [new RoleAssignment(Role.Approver, it.Id)]),
            ("director@acme.test", "Noura Director", [new RoleAssignment(Role.Approver)]),
            ("requester@acme.test", "Omar Requester", [new RoleAssignment(Role.Requester, it.Id)]),
            ("receiver@acme.test", "Fahad Receiver", [new RoleAssignment(Role.Receiver)]),
            ("ap@acme.test", "Reem AP Clerk", [new RoleAssignment(Role.ApClerk)]),
            ("cfo@acme.test", "Hassan CFO", [new RoleAssignment(Role.Approver)]),
        ];

        var ids = new Dictionary<string, Guid>();
        foreach (var (email, name, roles) in users)
        {
            ids[email] = await AddUserAsync(db, identity, organizationId, email, name, DemoPassword, roles, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await AddDemoApprovalMatrixAsync(db, organizationId, ids["director@acme.test"], ids["cfo@acme.test"], cancellationToken);
    }

    /// <summary>The default matrix from DESIGN section 6.3, for requisitions and purchase orders.</summary>
    private static async Task AddDemoApprovalMatrixAsync(
        AppDbContext db, Guid organizationId, Guid directorId, Guid cfoId, CancellationToken cancellationToken)
    {
        var directors = ApproverGroup.Create(organizationId, "Department directors", [directorId]).Value;
        var cfo = ApproverGroup.Create(organizationId, "CFO", [cfoId]).Value;
        db.ApproverGroups.AddRange(directors, cfo);

        var manager = new ApprovalStepTemplate("Cost center manager", ApprovalStepKind.CostCenterApprovers);
        var director = new ApprovalStepTemplate("Department director", ApprovalStepKind.Group, directors.Id);
        var chiefFinance = new ApprovalStepTemplate("CFO", ApprovalStepKind.Group, cfo.Id);
        ApprovalRule[] rules =
        [
            new(10_000m, [manager]),
            new(100_000m, [manager, director]),
            new(null, [manager, director, chiefFinance]),
        ];

        foreach (var documentType in ApprovalDocumentTypes.All)
        {
            db.ApprovalPolicies.Add(ApprovalPolicy.Create(organizationId, documentType, rules).Value);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Guid> AddUserAsync(
        AppDbContext db, IIdentityService identity, Guid organizationId, string email, string name, string password,
        RoleAssignment[] roles, CancellationToken cancellationToken)
    {
        var user = User.Create(organizationId, email, name, roles).Value;
        db.Users.Add(user);
        var result = await identity.CreateCredentialAsync(user.Id, user.Email, password, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seeding user '{email}' failed: {result.Error!.Message}");
        }

        return user.Id;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Created organization {OrganizationName} and its admin")]
    private static partial void LogBootstrapped(ILogger logger, string organizationName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added demo cost centers and users")]
    private static partial void LogDemoData(ILogger logger);
}
