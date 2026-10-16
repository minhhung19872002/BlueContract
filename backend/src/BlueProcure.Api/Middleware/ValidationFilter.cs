using BlueProcure.Application.Common;
using BlueProcure.Infrastructure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueProcure.Api.Middleware;

/// <summary>Runs FluentValidation for every action argument that has a validator; returns 400 ProblemDetails with field errors.</summary>
internal sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var group in result.Errors.GroupBy(e => ToCamel(e.PropertyName)))
            {
                errors[group.Key] = [.. group.Select(e => e.ErrorMessage)];
            }
        }

        if (errors.Count > 0)
        {
            var problem = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = ErrorMessages.Get("VALIDATION_FAILED"),
                Type = "https://blueprocure.local/errors/VALIDATION_FAILED",
            };
            problem.Extensions["code"] = "VALIDATION_FAILED";
            problem.Extensions["correlationId"] = context.HttpContext.TraceIdentifier;
            context.Result = new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
            return;
        }

        await next();
    }

    private static string ToCamel(string name) =>
        string.Join('.', name.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}

/// <summary>Accounts flagged "must change password" may only call the auth endpoints until they do.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class AllowWithPendingPasswordChangeAttribute : Attribute
{
}

internal sealed class PasswordChangeRequiredFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.User.HasClaim(AppClaims.MustChangePassword, "true")
            && !context.ActionDescriptor.EndpointMetadata.OfType<AllowWithPendingPasswordChangeAttribute>().Any())
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = ErrorMessages.Get("PASSWORD_CHANGE_REQUIRED"),
                Type = "https://blueprocure.local/errors/PASSWORD_CHANGE_REQUIRED",
            };
            problem.Extensions["code"] = "PASSWORD_CHANGE_REQUIRED";
            context.Result = new ObjectResult(problem) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
