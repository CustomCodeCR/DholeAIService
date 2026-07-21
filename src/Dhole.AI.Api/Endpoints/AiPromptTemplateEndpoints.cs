
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.PromptTemplates.Create;
using Dhole.AI.Application.Features.PromptTemplates.Delete;
using Dhole.AI.Application.Features.PromptTemplates.GetById;
using Dhole.AI.Application.Features.PromptTemplates.GetPromptTemplates;
using Dhole.AI.Application.Features.PromptTemplates.SetActive;
using Dhole.AI.Application.Features.PromptTemplates.Update;
using Dhole.AI.Contracts.PromptTemplates.Request;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiPromptTemplateEndpoints
{
    public static IEndpointRouteBuilder MapAiPromptTemplateEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app
            .MapGroup("/api/ai/prompt-templates")
            .WithTags("AI Prompt Templates")
            .RequireAuthorization();

        group
            .MapGet("/", GetPromptTemplatesAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateView);

        group
            .MapGet("/{promptTemplateId:guid}", GetPromptTemplateByIdAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateView);

        group
            .MapPost("/", CreatePromptTemplateAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateCreate);

        group
            .MapPut("/{promptTemplateId:guid}", UpdatePromptTemplateAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateUpdate);

        group
            .MapPatch("/{promptTemplateId:guid}/active", SetPromptTemplateActiveAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateSetActive);

        group
            .MapDelete("/{promptTemplateId:guid}", DeletePromptTemplateAsync)
            .RequireScope(AiConstants.Scopes.PromptTemplateDelete);

        return app;
    }

    private static async Task<IResult> GetPromptTemplatesAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetPromptTemplatesQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                isActive
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetPromptTemplateByIdAsync(
        Guid promptTemplateId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetPromptTemplateByIdQuery(promptTemplateId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreatePromptTemplateAsync(
        CreateAiPromptTemplateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new CreatePromptTemplateCommand(
                request.Key,
                request.Name,
                request.Description,
                request.SystemPrompt,
                request.UserPromptTemplate,
                request.Variables,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdatePromptTemplateAsync(
        Guid promptTemplateId,
        UpdateAiPromptTemplateRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new UpdatePromptTemplateCommand(
                promptTemplateId,
                request.Key,
                request.Name,
                request.Description,
                request.SystemPrompt,
                request.UserPromptTemplate,
                request.Variables,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetPromptTemplateActiveAsync(
        Guid promptTemplateId,
        SetAiPromptTemplateActiveRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new SetPromptTemplateActiveCommand(
                promptTemplateId,
                request.IsActive,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeletePromptTemplateAsync(
        Guid promptTemplateId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeletePromptTemplateCommand(promptTemplateId, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }
}
