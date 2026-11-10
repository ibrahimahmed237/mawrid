using Mawrid.Domain.Common;
using MediatR;

namespace Mawrid.Application.Common.Messaging;

/// <summary>A request that only reads data. Queries never change state.</summary>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
