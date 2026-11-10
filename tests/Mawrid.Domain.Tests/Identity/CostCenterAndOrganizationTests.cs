using Mawrid.Domain.Identity;

namespace Mawrid.Domain.Tests.Identity;

public class CostCenterAndOrganizationTests
{
    [Fact]
    public void Organization_trims_its_name_and_raises_OrganizationCreated()
    {
        var organization = Organization.Create("  Acme Trading  ").Value;

        organization.Name.Should().Be("Acme Trading");
        organization.DomainEvents.Should().ContainSingle().Which.Should().Be(new OrganizationCreated(organization.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public void Organization_name_must_be_long_enough(string name)
    {
        Organization.Create(name).Error.Should().Be(IdentityErrors.OrganizationNameInvalid);
    }

    [Fact]
    public void Cost_center_code_is_upper_cased()
    {
        var costCenter = CostCenter.Create(Guid.NewGuid(), " it-001 ", "Information Technology").Value;

        costCenter.Code.Should().Be("IT-001");
        costCenter.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("-IT")]
    [InlineData("I")]
    [InlineData("IT 001")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU")]
    public void Cost_center_code_must_follow_the_format(string code)
    {
        CostCenter.Create(Guid.NewGuid(), code, "Information Technology").Error.Should().Be(IdentityErrors.CostCenterCodeInvalid);
    }
}
