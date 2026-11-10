using Mawrid.Api.Common.Errors;
using Mawrid.Domain.Common;

namespace Mawrid.Api.Tests.Common;

public class ResultHttpExtensionsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.RuleViolation, 422)]
    [InlineData(ErrorType.Precondition, 412)]
    [InlineData(ErrorType.Unavailable, 503)]
    public void Each_error_type_maps_to_its_status(ErrorType type, int expected)
    {
        type.ToStatusCode().Should().Be(expected);
    }

    [Fact]
    public void Problem_details_carry_the_error_code_and_message()
    {
        var error = Error.RuleViolation("rfq.deadline_passed", "The RFQ deadline has passed.");

        var problem = error.ToProblemDetails();

        problem.Status.Should().Be(422);
        problem.Detail.Should().Be("The RFQ deadline has passed.");
        problem.Extensions["code"].Should().Be("rfq.deadline_passed");
    }
}
