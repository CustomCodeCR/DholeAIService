using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CustomCodeFramework.Api.Responses;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Api.Services;
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Api.Endpoints;

public static class AiFileExecutionEndpoints
{
    private const long DefaultMaximumFileBytes = 25L * 1024L * 1024L;

    public static IEndpointRouteBuilder MapAiFileExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/ai/executions/chat-file", ExecuteAsync)
            .WithTags("AI Executions")
            .RequireAuthorization()
            .RequireScope(AiConstants.Scopes.ExecutionExecute)
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> ExecuteAsync(
        HttpRequest request,
        HttpContext httpContext,
        AiFileChatService service,
        IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        if (!request.HasFormContentType)
        {
            return EndpointResults.BadRequest(
                "AI.InvalidFileChatContentType",
                "La solicitud debe enviarse como multipart/form-data.",
                httpContext
            );
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        var prompt = form["prompt"].ToString().Trim();
        var profileKey = form["profileKey"].ToString().Trim();

        if (file is null)
        {
            return EndpointResults.BadRequest(
                "AI.MissingChatFile",
                "Debe adjuntar un archivo CSV o XLSX.",
                httpContext
            );
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return EndpointResults.BadRequest(
                "AI.MissingFileChatPrompt",
                "Debe indicar qué desea analizar o transformar.",
                httpContext
            );
        }

        if (string.IsNullOrWhiteSpace(profileKey))
        {
            profileKey = "assistant";
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx"))
        {
            return EndpointResults.BadRequest(
                "AI.UnsupportedChatFile",
                "Solo puede adjuntar archivos CSV o XLSX.",
                httpContext
            );
        }

        var maximumFileBytes = ReadPositiveLong(
            configuration["AI:FileChat:MaximumFileBytes"],
            DefaultMaximumFileBytes
        );
        if (file.Length <= 0 || file.Length > maximumFileBytes)
        {
            return EndpointResults.BadRequest(
                "AI.InvalidChatFileSize",
                $"El archivo debe pesar entre 1 byte y {maximumFileBytes / 1024 / 1024} MB.",
                httpContext
            );
        }

        IReadOnlyCollection<AiMessageRequest> messages = [];
        var messagesJson = form["messagesJson"].ToString();
        if (!string.IsNullOrWhiteSpace(messagesJson))
        {
            try
            {
                messages = JsonSerializer.Deserialize<AiMessageRequest[]>(
                    messagesJson,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                    {
                        PropertyNameCaseInsensitive = true,
                    }
                ) ?? [];
            }
            catch (JsonException)
            {
                return EndpointResults.BadRequest(
                    "AI.InvalidFileChatHistory",
                    "El historial enviado al chat no contiene JSON válido.",
                    httpContext
                );
            }
        }

        await using var inputStream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await inputStream.CopyToAsync(memoryStream, cancellationToken);
        var content = memoryStream.ToArray();

        try
        {
            var result = await service.ExecuteAsync(
                new ExecuteAiFileChatInput(
                    profileKey,
                    prompt,
                    messages,
                    NullIfEmpty(form["correlationId"].ToString())
                        ?? httpContext.TraceIdentifier,
                    NullIfEmpty(form["requestHash"].ToString())
                        ?? ComputeRequestHash(prompt, content),
                    httpContext.GetCurrentUserId(),
                    httpContext.GetCurrentUserName(),
                    httpContext.Request.Headers["Authorization"].ToString(),
                    file.FileName,
                    file.ContentType,
                    content
                ),
                cancellationToken
            );

            return EndpointResults.Ok(result);
        }
        catch (AiFileChatException exception)
        {
            return Error(exception, httpContext);
        }
    }

    private static IResult Error(AiFileChatException exception, HttpContext httpContext)
    {
        var statusCode = exception.Code switch
        {
            "AI.ClientRequestCancelled" => 499,
            "AI.ProviderTimeout" => StatusCodes.Status504GatewayTimeout,
            "AI.FileChatContextLimit" => StatusCodes.Status422UnprocessableEntity,
            "AI.ProviderOperationFailed"
                or "AI.ExecutionFailed"
                or "AI.FileExtractionFailed"
                or "AI.InvalidFileExtractionResponse"
                or "AI.FileGenerationFailed" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Json(
            ApiErrorResponse.Create(
                exception.Code,
                exception.Message,
                httpContext.TraceIdentifier
            ),
            statusCode: statusCode
        );
    }

    private static string ComputeRequestHash(string prompt, byte[] content)
    {
        var promptBytes = Encoding.UTF8.GetBytes(prompt);
        var combined = new byte[promptBytes.Length + 1 + content.Length];
        Buffer.BlockCopy(promptBytes, 0, combined, 0, promptBytes.Length);
        combined[promptBytes.Length] = 0;
        Buffer.BlockCopy(content, 0, combined, promptBytes.Length + 1, content.Length);
        return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static long ReadPositiveLong(string? value, long fallback)
    {
        return long.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
