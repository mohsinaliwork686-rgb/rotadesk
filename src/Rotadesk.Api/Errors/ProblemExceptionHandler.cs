using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Rotadesk.Domain;

namespace Rotadesk.Api.Errors;

/// <summary>
/// Turns the exceptions the domain and the database raise on purpose into answers: bad input is a 400, a broken
/// rule or a duplicate is a 409, and a save based on a stale read is a 409 that says so. Anything else stays a 500.
/// </summary>
internal sealed class ProblemExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        (int Status, string Code, string Title)? answer = exception switch
        {
            DomainException e when IsBadInput(e.Code) => (StatusCodes.Status400BadRequest, e.Code, e.Message),
            DomainException e when IsWrongPerson(e.Code) => (StatusCodes.Status403Forbidden, e.Code, e.Message),
            DomainException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency", "Someone else changed this first. Reload and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "duplicate", "That already exists."),
            BadHttpRequestException e => (StatusCodes.Status400BadRequest, "bad-request", e.Message),
            _ => null,
        };

        if (answer is not { } a)
        {
            return false;
        }

        httpContext.Response.StatusCode = a.Status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = { Status = a.Status, Title = a.Title, Extensions = { ["code"] = a.Code } },
        });
    }

    /// <summary>The right request from the wrong person: someone else's swap to accept, or a manager deciding their own.</summary>
    private static bool IsWrongPerson(string code) => code is "swap.wrong-person" or "swap.not-a-manager" or "swap.own-swap";

    /// <summary>Input that can never be right, as opposed to a rule that refuses it now.</summary>
    private static bool IsBadInput(string code) =>
        code.Contains("invalid", StringComparison.Ordinal)
        || code.EndsWith("-required", StringComparison.Ordinal)
        || code is "shift.too-long" or "week.not-monday" or "business.unknown-time-zone";
}
