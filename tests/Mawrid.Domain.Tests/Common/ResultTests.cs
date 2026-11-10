using Mawrid.Domain.Common;

namespace Mawrid.Domain.Tests.Common;

public class ResultTests
{
    private static readonly Error SomeError = Error.RuleViolation("rfq.deadline_passed", "The RFQ deadline has passed.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_keeps_the_error()
    {
        var result = Result.Failure(SomeError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Typed_success_exposes_the_value()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws()
    {
        Result<int> result = SomeError;

        var read = () => result.Value;

        read.Should().Throw<InvalidOperationException>().WithMessage("*rfq.deadline_passed*");
    }

    [Fact]
    public void Failure_requires_an_error()
    {
        var create = () => Result.Failure(null!);

        create.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("code", " ")]
    public void Error_requires_code_and_message(string code, string message)
    {
        var create = () => Error.Validation(code, message);

        create.Should().Throw<ArgumentException>();
    }
}
