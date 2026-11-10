using FluentValidation;
using Mawrid.Application.Common.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers handlers, validators and the request pipeline.
    /// Behaviors run in the order they are added: logging wraps validation.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly, includeInternalTypes: true);

        return services;
    }
}
