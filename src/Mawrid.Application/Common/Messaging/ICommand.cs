using Mawrid.Domain.Common;
using MediatR;

namespace Mawrid.Application.Common.Messaging;

/// <summary>A request that changes state and returns no value.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>A request that changes state and returns a value, such as the id of a new record.</summary>
public interface ICommand<TResponse> : IRequest<Result<TResponse>>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
