namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiGeneratedFileDto(
    string FileName,
    string ContentType,
    string Base64Data,
    long SizeBytes
);

public sealed record AiFileChatResultDto(
    AiChatResultDto Chat,
    string SourceFileName,
    int SourceRowCount,
    bool SourceWasTruncated,
    AiGeneratedFileDto? GeneratedFile
);
