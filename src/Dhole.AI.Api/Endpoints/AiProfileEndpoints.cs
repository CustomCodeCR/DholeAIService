
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Profiles.ConfigureModels;
using Dhole.AI.Application.Features.Profiles.Create;
using Dhole.AI.Application.Features.Profiles.Delete;
using Dhole.AI.Application.Features.Profiles.GetById;
using Dhole.AI.Application.Features.Profiles.GetByKey;
using Dhole.AI.Application.Features.Profiles.GetProfiles;
using Dhole.AI.Application.Features.Profiles.SetActive;
using Dhole.AI.Application.Features.Profiles.Update;
using Dhole.AI.Contracts.Profiles.Request;
using Dhole.AI.Domain.Profiles.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiProfileEndpoints
{
    public static IEndpointRouteBuilder MapAiProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/profiles")
            .WithTags("AI Profiles")
            .RequireAuthorization();

        group.MapGet("/", GetProfilesAsync).RequireScope(AiConstants.Scopes.ProfileView);

        group
            .MapGet("/{profileId:guid}", GetProfileByIdAsync)
            .RequireScope(AiConstants.Scopes.ProfileView);

        group
            .MapGet("/by-key/{key}", GetProfileByKeyAsync)
            .RequireScope(AiConstants.Scopes.ProfileView);

        group.MapPost("/", CreateProfileAsync).RequireScope(AiConstants.Scopes.ProfileCreate);

        group
            .MapPut("/{profileId:guid}", UpdateProfileAsync)
            .RequireScope(AiConstants.Scopes.ProfileUpdate);

        group
            .MapPut("/{profileId:guid}/models", ConfigureProfileModelsAsync)
            .RequireScope(AiConstants.Scopes.ProfileConfigureModels);

        group
            .MapPatch("/{profileId:guid}/active", SetProfileActiveAsync)
            .RequireScope(AiConstants.Scopes.ProfileSetActive);

        group
            .MapDelete("/{profileId:guid}", DeleteProfileAsync)
            .RequireScope(AiConstants.Scopes.ProfileDelete);

        return app;
    }

    private static async Task<IResult> GetProfilesAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        AiRoutingMode? routingMode,
        AiResponseFormat? responseFormat,
        bool? isActive,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetProfilesQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                routingMode,
                responseFormat,
                isActive
            ),
            cancellationToken
        );

        return EndpointResults.FromPaged(result, httpContext);
    }

    private static async Task<IResult> GetProfileByIdAsync(
        Guid profileId,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetProfileByIdQuery(profileId),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetProfileByKeyAsync(
        string key,
        IQueryDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetProfileByKeyQuery(key),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> CreateProfileAsync(
        CreateAiProfileRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.RoutingMode, out AiRoutingMode routingMode))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidRoutingMode",
                "El modo de enrutamiento no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.ResponseFormat, out AiResponseFormat responseFormat))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidResponseFormat",
                "El formato de respuesta no es válido.",
                httpContext
            );
        }

        var models = request.Models
            .Select(x => new CreateProfileModelCommand(x.ModelId, x.Priority, x.IsFallback))
            .ToArray();

        var result = await dispatcher.DispatchAsync(
            new CreateProfileCommand(
                request.Key,
                request.Name,
                request.Description,
                request.PromptTemplateId,
                routingMode,
                responseFormat,
                request.Temperature,
                request.MaximumOutputTokens,
                request.TimeoutSeconds,
                request.JsonSchema,
                models,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> UpdateProfileAsync(
        Guid profileId,
        UpdateAiProfileRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.RoutingMode, out AiRoutingMode routingMode))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidRoutingMode",
                "El modo de enrutamiento no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.ResponseFormat, out AiResponseFormat responseFormat))
        {
            return EndpointResults.BadRequest(
                "AI.InvalidResponseFormat",
                "El formato de respuesta no es válido.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new UpdateProfileCommand(
                profileId,
                request.Key,
                request.Name,
                request.Description,
                request.PromptTemplateId,
                routingMode,
                responseFormat,
                request.Temperature,
                request.MaximumOutputTokens,
                request.TimeoutSeconds,
                request.JsonSchema,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ConfigureProfileModelsAsync(
        Guid profileId,
        ConfigureAiProfileModelsRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var models = request.Models
            .Select(x => new ConfigureProfileModelCommand(x.ModelId, x.Priority, x.IsFallback))
            .ToArray();

        var result = await dispatcher.DispatchAsync(
            new ConfigureProfileModelsCommand(
                profileId,
                models,
                httpContext.GetCurrentUserId()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> SetProfileActiveAsync(
        Guid profileId,
        SetAiProfileActiveRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new SetProfileActiveCommand(profileId, request.IsActive, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DeleteProfileAsync(
        Guid profileId,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteProfileCommand(profileId, httpContext.GetCurrentUserId()),
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
