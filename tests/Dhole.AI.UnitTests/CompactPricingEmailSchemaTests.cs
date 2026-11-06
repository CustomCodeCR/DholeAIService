using System.Text.Json;
using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class CompactPricingEmailSchemaTests
{
    [TestMethod]
    public void RowSchema_DoesNotRequireTwentyTwoNullFields()
    {
        using var schema = JsonDocument.Parse(PricingEmailAiExecutionFactory.JsonSchema);
        var rows = schema.RootElement.GetProperty("properties").GetProperty("rows");
        var row = rows.GetProperty("items");
        Assert.AreEqual(22, row.GetProperty("properties").EnumerateObject().Count());
        Assert.AreEqual(0, row.GetProperty("required").GetArrayLength());
        Assert.IsFalse(row.GetProperty("additionalProperties").GetBoolean());
        Assert.AreEqual(4, schema.RootElement.GetProperty("required").GetArrayLength());
    }

    [TestMethod]
    public void SparsePricingRow_PreservesRealValuesAndLeavesUnknownFieldsNull()
    {
        const string json = """
        {
          "success": true,
          "confidence": 90,
          "warnings": [],
          "rows": [
            {
              "pol": "Shanghai",
              "poe": "Moin",
              "containerType": "40HC",
              "carrier": "MSC",
              "currency": "USD",
              "validFrom": "2026-11-09",
              "validTo": "2026-11-14",
              "oceanFreight": 1234.5
            }
          ]
        }
        """;
        var parsed = PricingEmailAiExecutionFactory.Parse(json);
        Assert.HasCount(1, parsed.Rows);
        var row = parsed.Rows.Single();
        Assert.AreEqual("Shanghai", row.Pol);
        Assert.AreEqual("Moin", row.Poe);
        Assert.AreEqual("40HC", row.ContainerType);
        Assert.AreEqual(1234.5m, row.OceanFreight);
        Assert.AreEqual("USD", row.Currency);
        Assert.IsNull(row.TotalSale);
        Assert.IsNull(row.Surcharges);
        Assert.IsNull(row.Agent);
    }

    [TestMethod]
    public void CompactModelRow_NeverAllowsUnknownPropertiesInTheContract()
    {
        using var schema = JsonDocument.Parse(PricingEmailAiExecutionFactory.JsonSchema);
        var row = schema.RootElement.GetProperty("properties")
            .GetProperty("rows").GetProperty("items");
        Assert.IsFalse(row.GetProperty("additionalProperties").GetBoolean());
        Assert.IsTrue(row.GetProperty("properties").TryGetProperty("oceanFreight", out _));
        Assert.IsTrue(row.GetProperty("properties").TryGetProperty("totalCost", out _));
    }
}
