using System.Text.Json;
using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class PricingImageExtractionTests
{
    [TestMethod]
    public void AiStages_UseVisionFirstAndPreserveOcrTextFallback()
    {
        using var json = JsonDocument.Parse("{}");
        var response = new DataExtractionAiEmailRequestResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "hash", "correlation", "pricing-email-analysis",
            json.RootElement.Clone(),
            new DataExtractionAiEmailImageResponse(false, null, null)
        );
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(),
            null,
            "supplier@example.com",
            "Tarifa escaneada octubre",
            null,
            null,
            "Attachment",
            "tarifario-escaneado.png",
            "image/png",
            "## OCR de imagen: POL Shanghai / POE Moin / 40HC USD 1200",
            "correlation",
            null,
            null,
            0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(),
            null,
            "image/png"
        );

        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var stages = PricingEmailAiExecutionFactory.CreateStages(response, payload, imageBytes).ToArray();

        Assert.AreEqual(2, stages.Length);
        Assert.AreEqual("image-or-repair", stages[0].StageName);
        Assert.IsNotNull(stages[0].ImageBytes);
        Assert.AreEqual("ocr-text-fallback", stages[1].StageName);
        Assert.IsNull(stages[1].ImageBytes);
        StringAssert.Contains(stages[1].PromptJson, "Shanghai");
        StringAssert.Contains(stages[1].PromptJson, "USD 1200");
    }
}
