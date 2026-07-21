
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Models.Create;
using Dhole.AI.Application.Features.Models.Delete;
using Dhole.AI.Application.Features.Models.GetById;
using Dhole.AI.Application.Features.Models.GetModels;
using Dhole.AI.Application.Features.Models.SetActive;
using Dhole.AI.Application.Features.Models.Update;
using Dhole.AI.Contracts.Models.Request;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiModelEndpoints
{
    public static IEndpointRouteBuilder MapAiModelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/models")
            .WithTags("AI Models")
            .RequireAuthorization();

        group.MapGet("/", GetModelsAsync).RequireScope(AiConstants.Scopes.ModelView);

        group
            .MapGet("/{modelId:guid}", GetModelByIdAsync)
            .RequireScope(AiConstants.Scopes.ModelView);

        group.MapPost("/", CreateModelAsync).RequireScope(AiConstants.Scopes.ModelCreate);

        group
            .MapPut("/{modelId:guid}", UpdateModelAsync)
            .RequireScope(AiConstants.Scopes.ModelUpdate);

        group
            .MapPatch("/{modelId:guid}/active", SetModelActiveAsync)
            .RequireScope(AiConstants.Scopes.ModelSetActive);

        group
            .MapDelete("/{modelId:guid}", DeleteModelAsync)
            .RequireScope(AiConstants.Scopes.ModelDelete);

        return app;
    }

    private static async Task<IResult> GetModelsAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        Guid? connectionId,
        AiProviderType? providerType,
        AiModelCapability? capability,
        AiModelStatus? status,
        bool? isLocal,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetModelsQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                connectionId,
                providerType,
                capability,
                status,
                isLocal,
                isActive
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetModelByIdAsync(
        Guid modelId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetModelByIdQuery(modelId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateModelAsync(
        CreateAiModelRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseCapabilities(request.Capabilities, out var capabilities))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidModelCapabilities",
                "Una o más capacidades del modelo no son válidas.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new CreateModelCommand(
                request.ConnectionId,
                request.ExternalModelId,
                request.Name,
                capabilities,
                request.ContextWindow,
                request.MaximumOutputTokens,
                request.InputCostPerMillionTokens,
                request.OutputCostPerMillionTokens,
                request.IsLocal,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdateModelAsync(
        Guid modelId,
        UpdateAiModelRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseCapabilities(request.Capabilities, out var capabilities))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidModelCapabilities",
                "Una o más capacidades del modelo no son válidas.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new UpdateModelCommand(
                modelId,
                request.ConnectionId,
                request.ExternalModelId,
                request.Name,
                capabilities,
                request.ContextWindow,
                request.MaximumOutputTokens,
                request.InputCostPerMillionTokens,
                request.OutputCostPerMillionTokens,
                request.IsLocal,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetModelActiveAsync(
        Guid modelId,
        SetAiModelActiveRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new SetModelActiveCommand(modelId, request.IsActive, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteModelAsync(
        Guid modelId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteModelCommand(modelId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static bool TryParseCapabilities(
        IReadOnlyCollection<string> values,
        out AiModelCapability capabilities
    )
    {
        capabilities = AiModelCapability.None;

        if (values.Count == 0)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (
                !Enum.TryParse<AiModelCapability>(value, ignoreCase: true, out var parsed)
                || parsed == AiModelCapability.None
                || !Enum.IsDefined(parsed)
            )
            {
                capabilities = AiModelCapability.None;
                return false;
            }

            capabilities |= parsed;
        }

        return capabilities != AiModelCapability.None;
    }
}
