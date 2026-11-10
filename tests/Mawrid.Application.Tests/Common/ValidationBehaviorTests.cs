using FluentValidation;
using Mawrid.Application.Common.Behaviors;
using Mawrid.Application.Common.Exceptions;
using Mawrid.Domain.Common;

namespace Mawrid.Application.Tests.Common;

public class ValidationBehaviorTests
{
    private sealed record CreateThing(string Name, int Quantity);

    private sealed class CreateThingValidator : AbstractValidator<CreateThing>
    {
        public CreateThingValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
            RuleFor(x => x.Quantity).GreaterThan(0);
        }
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([new CreateThingValidator()]);
        var handlerCalled = false;

        var result = await behavior.Handle(new CreateThing("Laptop", 2), _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        handlerCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_request_throws_with_errors_per_field_and_skips_the_handler()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([new CreateThingValidator()]);
        var handlerCalled = false;

        var act = () => behavior.Handle(new CreateThing("", 0), _ =>
        {
            handlerCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<RequestValidationException>();
        thrown.Which.Errors.Keys.Should().BeEquivalentTo("Name", "Quantity");
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Request_without_validators_passes_through()
    {
        var behavior = new ValidationBehavior<CreateThing, Result>([]);

        var result = await behavior.Handle(new CreateThing("", 0), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
