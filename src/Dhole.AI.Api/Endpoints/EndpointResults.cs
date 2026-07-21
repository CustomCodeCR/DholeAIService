
using CustomCodeFramework.Api.Responses;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Core.Results;

namespace Dhole.AI.Api.Endpoints;

internal static class EndpointResults
{
    public static IResult FromResult<T>(Result<T> result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Results.Ok(ApiResponse<T>.Ok(result.Value))
            : ErrorResult(result.Error, httpContext);
    }

    public static IResult FromResult(Result result, HttpContext httpContext)
    {
        return result.IsSuccess
            ? Results.NoContent()
            : ErrorResult(result.Error, httpContext);
    }

    public static IResult FromPaged<T>(Result<PagedResult<T>> result, HttpContext httpContext)
    {
        if (result.IsFailure)
        {
            return Results.BadRequest(
                ApiErrorResponse.Create(
                    result.Error.Code,
                    result.Error.Message,
                    httpContext.TraceIdentifier
                )
            );
        }

        return FromPaged(result.Value);
    }

    public static IResult FromNullable<T>(
        T? value,
        string notFoundCode,
        string notFoundMessage,
        HttpContext httpContext
    )
        where T : class
    {
        return value is not null
            ? Results.Ok(ApiResponse<T>.Ok(value))
            : Results.NotFound(
                ApiErrorResponse.Create(notFoundCode, notFoundMessage, httpContext.TraceIdentifier)
            );
    }

    public static IResult Ok<T>(T value)
    {
        return Results.Ok(ApiResponse<T>.Ok(value));
    }

    public static IResult BadRequest(string code, string message, HttpContext httpContext)
    {
        return Results.BadRequest(
            ApiErrorResponse.Create(code, message, httpContext.TraceIdentifier)
        );
    }

    public static IResult FromPaged<T>(PagedResult<T> result)
    {
        return Results.Ok(
            ApiPagedResponse<T>.Create(
                result.Items,
                result.PageNumber,
                result.PageSize,
                result.TotalCount
            )
        );
    }

    public static IResult Unauthorized(string code, string message, HttpContext httpContext)
    {
        return Results.Json(
            ApiErrorResponse.Create(code, message, httpContext.TraceIdentifier),
            statusCode: StatusCodes.Status401Unauthorized
        );
    }
    private static IResult ErrorResult(Error error, HttpContext httpContext)
    {
        var statusCode = error.Code switch
        {
            "AI.ProviderTimeout" => StatusCodes.Status504GatewayTimeout,
            "AI.ProviderOperationFailed" or "AI.ExecutionFailed" =>
                StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(
            ApiErrorResponse.Create(
                error.Code,
                error.Message,
                httpContext.TraceIdentifier
            ),
            statusCode: statusCode
        );
    }

}
