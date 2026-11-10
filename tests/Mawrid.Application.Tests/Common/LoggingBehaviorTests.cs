using Mawrid.Application.Common.Behaviors;
using Mawrid.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Mawrid.Application.Tests.Common;

public class LoggingBehaviorTests
{
    private sealed record DoThing;

    [Fact]
    public async Task Success_is_logged_as_information()
    {
        var logger = new FakeLogger<LoggingBehavior<DoThing, Result>>();
        var behavior = new LoggingBehavior<DoThing, Result>(logger);

        await behavior.Handle(new DoThing(), _ => Task.FromResult(Result.Success()), CancellationToken.None);

        logger.LatestRecord.Level.Should().Be(LogLevel.Information);
        logger.LatestRecord.Message.Should().StartWith("Handled DoThing");
    }

    [Fact]
    public async Task Failure_is_logged_as_warning_with_the_error_code()
    {
        var logger = new FakeLogger<LoggingBehavior<DoThing, Result>>();
        var behavior = new LoggingBehavior<DoThing, Result>(logger);
        var error = Error.Conflict("supplier.duplicate_vat", "A supplier with this VAT number already exists.");

        var result = await behavior.Handle(new DoThing(), _ => Task.FromResult(Result.Failure(error)), CancellationToken.None);

        result.Error.Should().Be(error);
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Contain("supplier.duplicate_vat");
    }
}
