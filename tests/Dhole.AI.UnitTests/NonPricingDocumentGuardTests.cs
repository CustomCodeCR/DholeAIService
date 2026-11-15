using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class NonPricingDocumentGuardTests
{
    [TestMethod]
    public void CustomsDocumentsWithoutTariffs_UseSingleShortVerificationStage()
    {
        Assert.IsTrue(NonPricingDocumentGuard.IsDocumentWithoutTariff(
            "Commercial Invoice.pdf",
            "COMMERCIAL INVOICE\nInvoice No: INV-102\nTotal Amount: USD 2000\nFreight prepaid"));
        Assert.IsTrue(NonPricingDocumentGuard.IsDocumentWithoutTariff(
            "HBL-121.pdf", "HOUSE BILL OF LADING\nShipper: ABC\nConsignee: XYZ"));
    }

    [TestMethod]
    public void FreightRateOffers_ContinueThroughPricingExtractor()
    {
        Assert.IsFalse(NonPricingDocumentGuard.IsDocumentWithoutTariff(
            "Bill of Lading rates.pdf", "FREIGHT RATES\nShanghai USD 1200 40HC"));
        Assert.IsFalse(NonPricingDocumentGuard.IsDocumentWithoutTariff(
            "tarifario-octubre.xlsx", "RATES VALID UNTIL 2026-11-30"));
        Assert.IsFalse(NonPricingDocumentGuard.IsDocumentWithoutTariff(
            "unknown.xlsx", "no freight data"));
    }
}
