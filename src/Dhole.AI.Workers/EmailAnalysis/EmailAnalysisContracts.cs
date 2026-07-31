using System.Text.Json;

namespace Dhole.AI.Worker.EmailAnalysis;

internal static class EmailAnalysisMessageTypes
{
    public const string Requested = "ai.pricing-email-analysis.requested";
    public const string Started = "ai.pricing-email-analysis.started";
    public const string Completed = "ai.pricing-email-analysis.completed";
    public const string Failed = "ai.pricing-email-analysis.failed";
}

internal sealed record AiPricingEmailAnalysisRequestedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    Guid ProvisionalPricingImportId,
    Guid? ExtractionExecutionId,
    string CorrelationId,
    string RequestHash,
    string PayloadUrl,
    DateTime OccurredAtUtc
);

internal sealed record AiPricingEmailAnalysisStartedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    string CorrelationId,
    DateTime OccurredAtUtc
);

internal sealed record AiPricingEmailAnalysisCompletedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    Guid AiExecutionId,
    string CorrelationId,
    string RequestHash,
    decimal Confidence,
    IReadOnlyCollection<AiPricingEmailResultRow> Rows,
    IReadOnlyCollection<string> Warnings,
    DateTime OccurredAtUtc
);

internal sealed record AiPricingEmailAnalysisFailedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    Guid? AiExecutionId,
    string CorrelationId,
    string RequestHash,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    int AttemptCount,
    DateTime OccurredAtUtc
);

internal sealed record AiPricingEmailResultRow(
    string? Pol,
    string? Poe,
    string? Pod,
    string? ContainerType,
    string? Carrier,
    string? Agent,
    string? Commodity,
    string? Currency,
    int? FreeDays,
    int? TransitDays,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    string? SpaceComment,
    string? Remarks
);

internal sealed record DataExtractionAiEmailRequestResponse(
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string RequestHash,
    string CorrelationId,
    string ProfileKey,
    JsonElement Payload,
    DataExtractionAiEmailImageResponse Image
);

internal sealed record DataExtractionAiEmailImageResponse(
    bool Available,
    string? ContentType,
    string? DownloadUrl
);

internal sealed record AiPricingEmailPayload(
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string FromAddress,
    string Subject,
    string? BodyText,
    string? BodyHtml,
    string SourceType,
    string SourceName,
    string? SourceContentType,
    string SourceContent,
    string CorrelationId,
    string? PreviousErrorCode,
    string? PreviousErrorMessage,
    decimal PreviousConfidence,
    IReadOnlyCollection<AiPreviousPricingEmailRow> PreviousRows,
    IReadOnlyCollection<AiPreviousExtractionIssue> PreviousIssues,
    IReadOnlyCollection<AiCatalogGroupHint> CatalogHints,
    string? SourceImageBase64,
    string? SourceImageMimeType
);

internal sealed record AiPreviousPricingEmailRow(
    string? OriginPort,
    string? PortOfExit,
    string? DestinationPort,
    string? ContainerType,
    string? Carrier,
    string? Agent,
    string? Commodity,
    string? Currency,
    int? FreeDays,
    int? TransitDays,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    string? SpaceComment,
    string? Remarks
);

internal sealed record AiPreviousExtractionIssue(
    string Code,
    string Message,
    bool IsBlocking,
    string? ColumnName,
    string? RawValue
);

internal sealed record AiCatalogGroupHint(
    string GroupSlug,
    IReadOnlyCollection<AiCatalogItemHint> Items
);

internal sealed record AiCatalogItemHint(
    string Code,
    string Slug,
    string Name,
    string? Value
);

internal sealed record PreparedAiEmailExecution(
    string ProfileKey,
    string PromptJson,
    string JsonSchema,
    string CorrelationId,
    string RequestHash,
    string? ImageMimeType,
    byte[]? ImageBytes,
    string StageName,
    int StageNumber,
    int StageCount
);

internal sealed record ParsedAiPricingEmailResult(
    decimal Confidence,
    IReadOnlyCollection<AiPricingEmailResultRow> Rows,
    IReadOnlyCollection<string> Warnings
);
