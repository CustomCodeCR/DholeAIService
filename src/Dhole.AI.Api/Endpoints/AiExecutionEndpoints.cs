
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Executions.Cancel;
using Dhole.AI.Application.Features.Executions.ExecuteChat;
using Dhole.AI.Application.Features.Executions.ExecuteEmbeddings;
using Dhole.AI.Application.Features.Executions.ExecuteStructured;
using Dhole.AI.Application.Features.Executions.GetById;
using Dhole.AI.Application.Features.Executions.GetExecutions;
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiExecutionEndpoints
{
    public static IEndpointRouteBuilder MapAiExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/executions")
            .WithTags("AI Executions")
            .RequireAuthorization();

        group.MapGet("/", GetExecutionsAsync).RequireScope(AiConstants.Scopes.ExecutionView);

        group
            .MapGet("/{executionId:guid}", GetExecutionByIdAsync)
            .RequireScope(AiConstants.Scopes.ExecutionView);

        group
            .MapPost("/chat", ExecuteChatAsync)
            .RequireScope(AiConstants.Scopes.ExecutionExecute);

        group
            .MapPost("/structured", ExecuteStructuredAsync)
            .RequireScope(AiConstants.Scopes.ExecutionExecute);

        group
            .MapPost("/embeddings", ExecuteEmbeddingsAsync)
            .RequireScope(AiConstants.Scopes.ExecutionExecute);

        group
            .MapPost("/{executionId:guid}/cancel", CancelExecutionAsync)
            .RequireScope(AiConstants.Scopes.ExecutionCancel);

        return app;
    }

    private static async Task<IResult> GetExecutionsAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        string? profileKey,
        AiExecutionType? executionType,
        AiExecutionStatus? status,
        AiProviderType? providerType,
        Guid? modelId,
        DateTime? dateFromUtc,
        DateTime? dateToUtc,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetExecutionsQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                profileKey,
                executionType,
                status,
                providerType,
                modelId,
                dateFromUtc,
                dateToUtc
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetExecutionByIdAsync(
        Guid executionId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetExecutionByIdQuery(executionId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ExecuteChatAsync(
        ExecuteAiChatRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ExecuteChatCommand(
                request.ProfileKey,
                request.Messages,
                request.Variables,
                request.CorrelationId ?? httpContext.TraceIdentifier,
                request.RequestHash,
                httpContext.GetCurrentUserId(),
                httpContext.GetCurrentUserName()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ExecuteStructuredAsync(
        ExecuteAiStructuredRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ExecuteStructuredCommand(
                request.ProfileKey,
                request.Messages,
                request.Variables,
                request.JsonSchemaOverride,
                request.CorrelationId ?? httpContext.TraceIdentifier,
                request.RequestHash,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ExecuteEmbeddingsAsync(
        ExecuteAiEmbeddingsRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new ExecuteEmbeddingsCommand(
                request.ProfileKey,
                request.Inputs,
                request.CorrelationId ?? httpContext.TraceIdentifier,
                request.RequestHash,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CancelExecutionAsync(
        Guid executionId,
        CancelAiExecutionRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new CancelExecutionCommand(
                executionId,
                request.Reason,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }
}
