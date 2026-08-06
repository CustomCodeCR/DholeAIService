using System.Text.Json;
using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class AiPricingEmailResultRowJsonConverterTests
{
    [TestMethod]
    public void Deserialize_AcceptsSemanticAliasesForRouteAndEquipment()
    {
        const string json = """
            {
              "originPort": "Shanghai",
              "destinationPort": "Caldera",
              "equipmentType": "40HC",
              "shippingLine": "MSC",
              "currency": "USD",
              "validFrom": "2026-08-08",
              "validTo": "2026-08-14",
              "oceanFreight": 7515
            }
            """;
        var options = new JsonSerializerOptions();
        options.Converters.Add(new AiPricingEmailResultRowJsonConverter());

        var row = JsonSerializer.Deserialize<AiPricingEmailResultRow>(json, options);

        Assert.IsNotNull(row);
        Assert.AreEqual("Shanghai", row.Pol);
        Assert.AreEqual("Caldera", row.Poe);
        Assert.AreEqual("40HC", row.ContainerType);
        Assert.AreEqual("MSC", row.Carrier);
        Assert.AreEqual(7515m, row.OceanFreight);
    }
}
