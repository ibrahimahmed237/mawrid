using Mawrid.Domain.Identity;

namespace Mawrid.Domain.Tests.Identity;

public class UserTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid CostCenterId = Guid.NewGuid();

    [Fact]
    public void Create_normalizes_email_and_raises_UserCreated()
    {
        var result = User.Create(OrgId, "  Sara@Acme.SA ", "Sara Ali", [new RoleAssignment(Role.Buyer)]);

        result.IsSuccess.Should().BeTrue();
        var user = result.Value;
        user.Email.Should().Be("sara@acme.sa");
        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle().Which.Should().Be(new UserCreated(user.Id, OrgId));
    }

    [Theory]
    [InlineData("not-an-email", "Sara Ali", "user.email_invalid")]
    [InlineData("sara@acme.sa", "S", "user.display_name_invalid")]
    public void Create_rejects_bad_input(string email, string name, string code)
    {
        User.Create(OrgId, email, name, [new RoleAssignment(Role.Buyer)]).Error!.Code.Should().Be(code);
    }

    [Fact]
    public void A_user_needs_at_least_one_role()
    {
        User.Create(OrgId, "a@b.sa", "Sara Ali", []).Error.Should().Be(IdentityErrors.RolesRequired);
    }

    [Fact]
    public void Approver_and_requester_can_be_scoped_to_a_cost_center()
    {
        var result = User.Create(OrgId, "a@b.sa", "Sara Ali",
            [new RoleAssignment(Role.Approver, CostCenterId), new RoleAssignment(Role.Requester, CostCenterId)]);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Role.Buyer)]
    [InlineData(Role.Admin)]
    [InlineData(Role.ApClerk)]
    public void Other_roles_cannot_be_scoped(Role role)
    {
        User.Create(OrgId, "a@b.sa", "Sara Ali", [new RoleAssignment(role, CostCenterId)])
            .Error.Should().Be(IdentityErrors.RoleCannotBeScoped);
    }

    [Fact]
    public void A_supplier_user_has_a_supplier_and_only_the_supplier_role()
    {
        var supplierId = Guid.NewGuid();

        User.Create(OrgId, "a@b.sa", "Omar", [new RoleAssignment(Role.Supplier)], supplierId).IsSuccess.Should().BeTrue();
        User.Create(OrgId, "a@b.sa", "Omar", [new RoleAssignment(Role.Supplier)]).Error.Should().Be(IdentityErrors.SupplierRoleRules);
        User.Create(OrgId, "a@b.sa", "Omar", [new RoleAssignment(Role.Supplier), new RoleAssignment(Role.Buyer)], supplierId)
            .Error.Should().Be(IdentityErrors.SupplierRoleRules);
        User.Create(OrgId, "a@b.sa", "Omar", [new RoleAssignment(Role.Buyer)], supplierId)
            .Error.Should().Be(IdentityErrors.SupplierRoleRules);
    }

    [Fact]
    public void Duplicate_roles_are_stored_once()
    {
        var user = User.Create(OrgId, "a@b.sa", "Sara Ali", [new RoleAssignment(Role.Buyer), new RoleAssignment(Role.Buyer)]).Value;

        user.Roles.Should().ContainSingle();
    }

    [Fact]
    public void ReplaceRoles_validates_and_raises_an_event()
    {
        var user = User.Create(OrgId, "a@b.sa", "Sara Ali", [new RoleAssignment(Role.Buyer)]).Value;
        user.ClearDomainEvents();

        user.ReplaceRoles([]).Error.Should().Be(IdentityErrors.RolesRequired);
        user.Roles.Should().ContainSingle(r => r.Role == Role.Buyer);

        user.ReplaceRoles([new RoleAssignment(Role.ApClerk)]).IsSuccess.Should().BeTrue();
        user.HasRole(Role.ApClerk).Should().BeTrue();
        user.HasRole(Role.Buyer).Should().BeFalse();
        user.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserRolesChanged>();
    }

    [Fact]
    public void Deactivate_raises_the_event_only_once()
    {
        var user = User.Create(OrgId, "a@b.sa", "Sara Ali", [new RoleAssignment(Role.Buyer)]).Value;
        user.ClearDomainEvents();

        user.Deactivate();
        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<UserDeactivated>();
    }
}
