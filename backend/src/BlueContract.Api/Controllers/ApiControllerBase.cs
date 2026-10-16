using BlueContract.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace BlueContract.Api.Controllers;

/// <summary>Base for business controllers: maps <see cref="Result"/> to HTTP + ProblemDetails with a Vietnamese message.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult<T> FromResult<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Problem(result.Error!);

    protected ActionResult FromResult(Result result) =>
        result.IsSuccess ? NoContent() : Problem(result.Error!);

    protected ObjectResult Problem(Error error)
    {
        var status = error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Detail is null ? ErrorMessages.Get(error.Code) : $"{ErrorMessages.Get(error.Code)} {error.Detail}",
            Type = $"https://blueprocure.local/errors/{error.Code}",
        };
        problem.Extensions["code"] = error.Code;
        problem.Extensions["correlationId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    protected FileContentResult Excel(byte[] content, string fileName) =>
        File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
}
