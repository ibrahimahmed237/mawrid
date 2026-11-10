using Mawrid.Application.Common.Behaviors;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mawrid.Application.Tests.Common;

public class PipelineRegistrationTests
{
    [Fact]
    public void AddApplication_registers_behaviors_in_order_logging_then_validation()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));

        services.AddApplication();

        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToList();

        behaviors.Should().ContainInOrder(typeof(LoggingBehavior<,>), typeof(ValidationBehavior<,>));
    }
}
