using BlueContract.Application.Common;
using BlueContract.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BlueContract.Api.Middleware;

/// <summary>Maps exceptions to ProblemDetails with a stable `code` and a Vietnamese message; never leaks stack traces.</summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            DomainException de => (StatusCodes.Status400BadRequest, de.Code),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONFLICT"),

            // Unique-key race: the loser is told which business rule the database enforced (AUDIT-1 §10.6).
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } unique } =>
                (StatusCodes.Status409Conflict, UniqueConstraints.CodeFor(unique.ConstraintName)),
            Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } unique =>
                (StatusCodes.Status409Conflict, UniqueConstraints.CodeFor(unique.ConstraintName)),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested => (499, "REQUEST_CANCELLED"),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR"),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception");
        }

        httpContext.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ErrorMessages.Get(code),
            Type = $"https://blueprocure.local/errors/{code}",
        };
        problem.Extensions["code"] = code;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
