using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Medora.Application.Abstractions;
using Medora.Domain.Common;

namespace Medora.Api.Infrastructure;

/// <summary>Maps domain <see cref="Error"/> codes to HTTP + RFC 9457 problem+json (with a machine <c>code</c>).</summary>
internal static class ApiResults
{
    public static int StatusFor(string code) => code switch
    {
        "NOT_FOUND" => StatusCodes.Status404NotFound,
        "FORBIDDEN" => StatusCodes.Status403Forbidden,
        "INVALID_CREDENTIALS" or "INVALID_REFRESH_TOKEN" => StatusCodes.Status401Unauthorized,
        "LOCKED_OUT" => StatusCodes.Status423Locked,
        "WEAK_PASSWORD" => StatusCodes.Status422UnprocessableEntity,
        "PAYMENT_FAILED" => StatusCodes.Status402PaymentRequired,
        "AI_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
        "OUT_OF_STOCK" or "SLOT_FULL" or "EMAIL_TAKEN" or "ILLEGAL_TRANSITION" or "NOT_CANCELLABLE" or "HOLD_EXPIRED"
            or "TOO_LATE_TO_CANCEL" or "CONCURRENCY_CONFLICT" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest, // VALIDATION, QTY_LIMIT, RX_REQUIRED, RX_INVALID, SLOT_INVALID
    };

    public static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(
            title: error.Message,
            detail: error.Message,
            statusCode: StatusFor(error.Code),
            type: $"https://medora.dev/errors/{error.Code.ToLowerInvariant().Replace('_', '-')}",
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static IResult ToHttp<T>(this Result<T> result) => result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error!);

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) => result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error!);
}

/// <summary>Current user from the validated JWT. Inbound claim mapping is off, so names are the raw JWT ones.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirst("sub")?.Value;
    public string? BranchId => accessor.HttpContext?.User.FindFirst("branch")?.Value;
    public bool IsInRole(string role) => accessor.HttpContext?.User.HasClaim("role", role) == true;
}

/// <summary>Correlation id: accept the caller's (validated) X-Correlation-Id or use the trace id; echo it and log with it.</summary>
internal sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers["X-Correlation-Id"].ToString();
        var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        Activity.Current?.SetTag("correlation.id", id);
        context.Response.Headers["X-Correlation-Id"] = id;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(context);
        }
    }
}

/// <summary>Defensive response headers for a JSON API; per-user responses are never stored by shared caches.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.Headers.Authorization.Count > 0) headers.CacheControl = "private, no-cache";
        return next(context);
    }
}

/// <summary>
/// Conditional GETs: hashes successful JSON GET bodies into a strong ETag and answers a matching
/// If-None-Match with 304 and no body. The mobile cache revalidates stale entries for the price of a
/// few hundred bytes instead of re-downloading lists and images metadata.
/// </summary>
internal sealed class ETagMiddleware(RequestDelegate next)
{
    private const int MaxBufferedBytes = 512 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || !context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
            buffer.Position = 0;
            if (context.Response.StatusCode == StatusCodes.Status200OK && buffer.Length is > 0 and <= MaxBufferedBytes
                && context.Response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                var etag = $"\"{Convert.ToBase64String(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))[..22]}\"";
                context.Response.Headers.ETag = etag;
                if (context.Request.Headers.IfNoneMatch.Any(v => v is not null && v.Split(',').Any(t => t.Trim() == etag)))
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = 0;
                    return;
                }
            }
            buffer.Position = 0;
            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally
        {
            context.Response.Body = original;
        }
    }
}

/// <summary>Turns unexpected exceptions into problem+json without leaking internals; concurrency conflicts become 409.</summary>
internal sealed partial class ProblemExceptionHandler(ILogger<ProblemExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        IResult result = exception switch
        {
            DbUpdateConcurrencyException => ApiResults.Problem(new Error("CONCURRENCY_CONFLICT", "This was changed by another request. Reload and try again.")),
            UnauthorizedAccessException => TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication required."),
            BadHttpRequestException bad => TypedResults.Problem(statusCode: bad.StatusCode, title: "Bad request."),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => TypedResults.StatusCode(499),
            Medora.Infrastructure.Ai.LlmProviderException or HttpRequestException => ApiResults.Problem(new Error("AI_UNAVAILABLE", "The AI model couldn't be reached. Check that the model server is running.")),
            TaskCanceledException when !context.RequestAborted.IsCancellationRequested => ApiResults.Problem(new Error("AI_UNAVAILABLE", "The AI model took too long to answer. Try a shorter question.")),
            Anthropic.Exceptions.AnthropicRateLimitException => ApiResults.Problem(new Error("AI_UNAVAILABLE", "The assistant is busy right now. Try again in a minute.")),
            Anthropic.Exceptions.AnthropicApiException => ApiResults.Problem(new Error("AI_UNAVAILABLE", "The assistant couldn't be reached. Try again shortly.")),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Something went wrong.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = Activity.Current?.TraceId.ToString() }),
        };
        if (result is ProblemHttpResult { StatusCode: >= 500 }) LogUnhandled(logger, exception);
        await result.ExecuteAsync(context);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);
}
