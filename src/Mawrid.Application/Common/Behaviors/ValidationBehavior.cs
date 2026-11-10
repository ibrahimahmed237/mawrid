using FluentValidation;
using Mawrid.Application.Common.Exceptions;
using MediatR;

namespace Mawrid.Application.Common.Behaviors;

/// <summary>
/// Runs every FluentValidation validator for the request before the handler.
/// Like a NestJS ValidationPipe, but for commands and queries instead of controllers.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var errors = results
            .SelectMany(r => r.Errors)
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return await next(cancellationToken);
    }
}
