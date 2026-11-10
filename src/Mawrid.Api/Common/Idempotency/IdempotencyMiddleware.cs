using System.Security.Cryptography;
using System.Text;
using Mawrid.Api.Common.Errors;
using Mawrid.Infrastructure.Idempotency;
using Mawrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Mawrid.Api.Common.Idempotency;

/// <summary>Runs for endpoints marked <see cref="IdempotentAttribute"/> when the request has an <c>Idempotency-Key</c>.</summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";
    private const int MaxKeyLength = 100;
    private const int MaxStoredBodyBytes = 1024 * 1024;

    public async Task InvokeAsync(
        HttpContext context, IdempotencyStore store, CurrentUserContext currentUser, IProblemDetailsService problems)
    {
        var key = context.Request.Headers[HeaderName].ToString();
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null
            || !HttpMethods.IsPost(context.Request.Method)
            || string.IsNullOrEmpty(key)
            || currentUser.OrganizationId is not { } organizationId)
        {
            await next(context);
            return;
        }

        if (key.Length > MaxKeyLength || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
        {
            await WriteProblemAsync(context, problems, StatusCodes.Status400BadRequest, "idempotency.key_invalid",
                "The Idempotency-Key must be 1 to 100 characters: letters, digits, '-', '_', ':' or '.'.");
            return;
        }

        var requestHash = await HashRequestAsync(context.Request);
        var begin = await store.BeginAsync(organizationId, key, requestHash, context.RequestAborted);

        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Replay:
                await ReplayAsync(context, begin.Saved!);
                return;
            case IdempotencyOutcome.InProgress:
                await WriteProblemAsync(context, problems, StatusCodes.Status409Conflict, "idempotency.in_progress",
                    "A request with this Idempotency-Key is still running. Retry in a moment.");
                return;
            case IdempotencyOutcome.KeyReused:
                await WriteProblemAsync(context, problems, StatusCodes.Status422UnprocessableEntity, "idempotency.key_reused",
                    "This Idempotency-Key was already used for a different request.");
                return;
        }

        var originalBody = context.Response.Body;
        await using var captured = new MemoryStream();
        context.Response.Body = captured;
        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            await store.AbandonAsync(organizationId, key, CancellationToken.None);
            throw;
        }

        context.Response.Body = originalBody;
        captured.Position = 0;
        await captured.CopyToAsync(originalBody, context.RequestAborted);

        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError || captured.Length > MaxStoredBodyBytes)
        {
            await store.AbandonAsync(organizationId, key, CancellationToken.None);
        }
        else
        {
            await store.CompleteAsync(organizationId, key, context.Response.StatusCode, context.Response.ContentType,
                context.Response.Headers.Location.ToString() is { Length: > 0 } location ? location : null,
                captured.ToArray(), CancellationToken.None);
        }
    }

    private static async Task<string> HashRequestAsync(HttpRequest request)
    {
        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer);
        request.Body.Position = 0;

        var prefix = Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n");
        return Convert.ToHexStringLower(SHA256.HashData([.. prefix, .. buffer.ToArray()]));
    }

    private static async Task ReplayAsync(HttpContext context, IdempotencyRecord saved)
    {
        context.Response.StatusCode = saved.StatusCode!.Value;
        context.Response.Headers[ReplayedHeaderName] = "true";
        if (saved.ResponseContentType is not null)
        {
            context.Response.ContentType = saved.ResponseContentType;
        }

        if (saved.ResponseLocation is not null)
        {
            context.Response.Headers.Location = saved.ResponseLocation;
        }

        if (saved.ResponseBody is { Length: > 0 } body)
        {
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, IProblemDetailsService problems, int status, string code, string detail)
    {
        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = "Idempotency", Detail = detail };
        problem.Extensions["code"] = code;
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }
}
