using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class PlusCargoPdfNormalizationTests
{
    [TestMethod]
    public void NormalizeForSource_PlusCargoPdf_RepairsValidityAndAgent()
    {
        const string source =
            "26-Aug-2026Effective :CASTRO FALLAS Costa RicaCustomer:\n"
            + "25-Sep-2026Expiration:\n"
            + "Quotation Ref. : m3390\n"
            + "8501 Northwest 17th\n"
            + "Street, Suite 102\n"
            + "Santiago Fioravanti\n"
            + "MAERSK Port Everglades Puerto Moin";

        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(), Guid.NewGuid(), "rates@example.com",
            "FCL quotation", null, null, "Attachment", "Quote-m3390.pdf",
            "application/pdf", source, "correlation-id",
            "DataExtraction.AiResultHasBlockingIssues",
            "missing_valid_from, missing_valid_to", 80m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(), null, null
        );
        var parsed = new ParsedAiPricingEmailResult(
            91m,
            [new AiPricingEmailResultRow(
                "Port Everglades", "Puerto Moin", null, "20DC", "MAERSK",
                null, null, "USD", 12, null, null, null, 1349m, null,
                null, null, 2369m, null, null, null, null, null)],
            []
        );

        var result = PricingEmailAiExecutionFactory.NormalizeForSource(parsed, payload);
        var row = result.Rows.Single();

        Assert.AreEqual(new DateTime(2026, 8, 26), row.ValidFrom);
        Assert.AreEqual(new DateTime(2026, 9, 25), row.ValidTo);
        Assert.AreEqual("PlusCargo", row.Agent);
        Assert.AreEqual("MAERSK", row.Carrier);
    }

    [TestMethod]
    public void ExtractDocumentValidity_LabelBeforeDate_IsAccepted()
    {
        const string source =
            "Customer: CASTRO FALLAS Costa Rica Effective : 26-Aug-2026\n"
            + "Expiration: 25-Sep-2026\nPLUSCARGO";

        var validity = PricingEmailAiExecutionFactory.ExtractDocumentValidity(source);

        Assert.AreEqual(new DateTime(2026, 8, 26), validity.ValidFrom);
        Assert.AreEqual(new DateTime(2026, 9, 25), validity.ValidTo);
    }
}
