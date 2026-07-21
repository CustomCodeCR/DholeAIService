
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Connections.Create;
using Dhole.AI.Application.Features.Connections.Delete;
using Dhole.AI.Application.Features.Connections.DiscoverModels;
using Dhole.AI.Application.Features.Connections.GetById;
using Dhole.AI.Application.Features.Connections.GetConnections;
using Dhole.AI.Application.Features.Connections.SetActive;
using Dhole.AI.Application.Features.Connections.Test;
using Dhole.AI.Application.Features.Connections.Update;
using Dhole.AI.Contracts.Connections.Request;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiConnectionEndpoints
{
    public static IEndpointRouteBuilder MapAiConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/connections")
            .WithTags("AI Connections")
            .RequireAuthorization();

        group.MapGet("/", GetConnectionsAsync).RequireScope(AiConstants.Scopes.ConnectionView);

        group
            .MapGet("/{connectionId:guid}", GetConnectionByIdAsync)
            .RequireScope(AiConstants.Scopes.ConnectionView);

        group.MapPost("/", CreateConnectionAsync).RequireScope(AiConstants.Scopes.ConnectionCreate);

        group
            .MapPut("/{connectionId:guid}", UpdateConnectionAsync)
            .RequireScope(AiConstants.Scopes.ConnectionUpdate);

        group
            .MapPatch("/{connectionId:guid}/active", SetConnectionActiveAsync)
            .RequireScope(AiConstants.Scopes.ConnectionSetActive);

        group
            .MapPost("/{connectionId:guid}/test", TestConnectionAsync)
            .RequireScope(AiConstants.Scopes.ConnectionTest);

        group
            .MapPost("/{connectionId:guid}/discover-models", DiscoverModelsAsync)
            .RequireScope(AiConstants.Scopes.ConnectionDiscoverModels);

        group
            .MapDelete("/{connectionId:guid}", DeleteConnectionAsync)
            .RequireScope(AiConstants.Scopes.ConnectionDelete);

        return app;
    }

    private static async Task<IResult> GetConnectionsAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        AiProviderType? providerType,
        AiConnectionStatus? status,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetConnectionsQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                providerType,
                status,
                isActive
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetConnectionByIdAsync(
        Guid connectionId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetConnectionByIdQuery(connectionId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateConnectionAsync(
        CreateAiConnectionRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.ProviderType, out AiProviderType providerType))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidProviderType",
                "El tipo de proveedor de inteligencia artificial no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new CreateConnectionCommand(
                request.Name,
                providerType,
                request.BaseUrl,
                request.SecretReference,
                request.TimeoutSeconds,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdateConnectionAsync(
        Guid connectionId,
        UpdateAiConnectionRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.ProviderType, out AiProviderType providerType))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidProviderType",
                "El tipo de proveedor de inteligencia artificial no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new UpdateConnectionCommand(
                connectionId,
                request.Name,
                providerType,
                request.BaseUrl,
                request.SecretReference,
                request.TimeoutSeconds,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetConnectionActiveAsync(
        Guid connectionId,
        SetAiConnectionActiveRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new SetConnectionActiveCommand(
                connectionId,
                request.IsActive,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> TestConnectionAsync(
        Guid connectionId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new TestConnectionCommand(connectionId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DiscoverModelsAsync(
        Guid connectionId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DiscoverModelsCommand(connectionId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteConnectionAsync(
        Guid connectionId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteConnectionCommand(connectionId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
