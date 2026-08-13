using System.Text.Json;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Ollama;
using Dhole.AI.Worker.EmailAnalysis;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class PricingEmailFallbackTests
{
    [TestMethod]
    public void NarrativeNacPrompt_RequestsCompactRowsForLocalModels()
    {
        const string source = """
            Pls consider rate USD6300/6400, valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            Below the details of ONE NAC:
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL
            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights
            """;

        using var payloadDocument = JsonDocument.Parse("{}");
        var response = new DataExtractionAiEmailRequestResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "request-hash",
            "correlation-id",
            "pricing-email-analysis",
            payloadDocument.RootElement.Clone(),
            new DataExtractionAiEmailImageResponse(false, null, null)
        );
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(),
            null,
            "agent@example.com",
            "CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            source,
            null,
            "EmailBody",
            "email-body.txt",
            "text/plain",
            source,
            "correlation-id",
            "DataExtraction.NoRows",
            "No rows",
            0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(),
            null,
            null
        );

        var stage = PricingEmailAiExecutionFactory.CreateStages(
            response,
            payload,
            imageBytes: null
        ).Single();

        Assert.Contains("fcl-email-v10-newest-thread-priority", stage.PromptJson);
        Assert.Contains("filas compactas", stage.PromptJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("processingDateUtc", stage.PromptJson);
        Assert.Contains("ISPS 15/cntr + P/S 50/cntr = 65", stage.PromptJson);
        Assert.IsFalse(
            stage.PromptJson.Contains(
                "Crea una fila por combinación aplicable",
                StringComparison.OrdinalIgnoreCase
            )
        );
    }


    [TestMethod]
    public void ForwardedThreadPrompt_UsesNewestWrappedOfferAndDropsHistoricalRates()
    {
        const string source = """
            AVISO LEGAL: contenido confidencial
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Pls consider rate USD6300/6400
            ,
            valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest, subject to space
            (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            Below the details of ONE NAC:
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb
            USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL (shoes/furniture/toys/diaper
            /bicycle/home appliance)
            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights
            Un saludo cordial
            Veronica Jiang
            发件人: Veronica.jiang
            Pls consider rate ONE USD5815 per 40HC, MSC USD6050 per 40HC, valid 1-7/Aug with 21 days free at dest
            """;

        using var payloadDocument = JsonDocument.Parse("{}");
        var response = new DataExtractionAiEmailRequestResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "request-hash",
            "correlation-id",
            "pricing-email-analysis",
            payloadDocument.RootElement.Clone(),
            new DataExtractionAiEmailImageResponse(false, null, null)
        );
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(),
            null,
            "agent@example.com",
            "CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            source,
            null,
            "EmailBody",
            "email-body.txt",
            "text/plain",
            source,
            "correlation-id",
            "DataExtraction.NoRows",
            "No rows",
            0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(),
            null,
            null
        );

        var stage = PricingEmailAiExecutionFactory.CreateStages(
            response,
            payload,
            imageBytes: null
        ).Single();

        StringAssert.Contains(stage.PromptJson, "USD6300/6400 valid 8-14/Aug");
        StringAssert.Contains(stage.PromptJson, "Nanjing(+arb USD400)");
        StringAssert.Contains(stage.PromptJson, "diaper /bicycle");
        Assert.IsFalse(stage.PromptJson.Contains("USD5815", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(stage.PromptJson.Contains("AVISO LEGAL", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void LegacyLlamaStructuredRequest_AllowsEnoughTokensForCompactJson()
    {
        var request = new AiProviderChatRequest(
            [new AiProviderMessage("user", new string('x', 15_000))],
            0.05m,
            6_000,
            true,
            PricingEmailAiExecutionFactory.JsonSchema
        );

        var payload = OllamaRequestMapper.CreateChatPayload(
            request,
            "llama3:8b",
            stream: false
        );
        var options = payload["options"]!.AsObject();

        Assert.AreEqual(3_072, options["num_predict"]!.GetValue<int>());
        Assert.AreEqual(8_192, options["num_ctx"]!.GetValue<int>());
        Assert.AreEqual("json", payload["format"]!.GetValue<string>());
    }

    [TestMethod]
    public void Parse_OverflowingSurcharge_DoesNotDiscardUsableRow()
    {
        const string json = """
            {
              "success": true,
              "confidence": 95,
              "rows": [
                {
                  "pol": "Shanghai",
                  "poe": "Caldera",
                  "pod": null,
                  "containerType": "40HC",
                  "carrier": "ONE",
                  "agent": "WWL",
                  "commodity": "Auto Spare Parts",
                  "currency": "USD",
                  "freeDays": 21,
                  "transitDays": null,
                  "validFrom": "2026-08-08",
                  "validTo": "2026-08-14",
                  "oceanFreight": 6400,
                  "originCharges": 0,
                  "destinationCharges": 0,
                  "surcharges": 1e999,
                  "totalCost": null,
                  "totalSale": null,
                  "profit": null,
                  "margin": null,
                  "spaceComment": "subject to space",
                  "remarks": "MBL RLS USD75/BL"
                }
              ],
              "warnings": []
            }
            """;

        var result = PricingEmailAiExecutionFactory.Parse(json);
        var row = result.Rows.Single();

        Assert.AreEqual(6400m, row.OceanFreight);
        Assert.IsNull(row.Surcharges);
        Assert.AreEqual("ONE", row.Carrier);
    }

    [TestMethod]
    public void Parse_TextSurchargeExpression_ConvertsPerContainerAmounts()
    {
        const string json = """
            {
              "success": true,
              "confidence": 95,
              "rows": [
                {
                  "pol": "Shanghai",
                  "poe": "Caldera",
                  "pod": null,
                  "containerType": "40HC",
                  "carrier": "MSC",
                  "agent": "WWL",
                  "commodity": "Auto Spare Parts",
                  "currency": "USD",
                  "freeDays": 21,
                  "transitDays": null,
                  "validFrom": "2026-08-08",
                  "validTo": "2026-08-14",
                  "oceanFreight": "USD 6300",
                  "originCharges": null,
                  "destinationCharges": null,
                  "surcharges": "$15/cntr + $50/cntr",
                  "totalCost": null,
                  "totalSale": null,
                  "profit": null,
                  "margin": null,
                  "spaceComment": null,
                  "remarks": "MBL RLS USD75/BL"
                }
              ],
              "warnings": []
            }
            """;

        var result = PricingEmailAiExecutionFactory.Parse(json);
        var row = result.Rows.Single();

        Assert.AreEqual(6300m, row.OceanFreight);
        Assert.AreEqual(65m, row.Surcharges);
    }

    [TestMethod]
    public void NormalizeForSource_NarrativeNac_Defaults40HcAndMovesPodToPoe()
    {
        const string source = """
            Pls consider rate USD6300/6400, valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            """;
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(),
            null,
            "agent@example.com",
            "CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            source,
            null,
            "EmailBody",
            "email-body.txt",
            "text/plain",
            source,
            "correlation-id",
            null,
            null,
            0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(),
            null,
            null
        );
        var parsed = new ParsedAiPricingEmailResult(
            95m,
            [
                new AiPricingEmailResultRow(
                    "Shanghai",
                    null,
                    "Caldera",
                    null,
                    "ONE",
                    null,
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 8),
                    new DateTime(2026, 8, 14),
                    6400m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            []
        );

        var result = PricingEmailAiExecutionFactory.NormalizeForSource(parsed, payload);
        var row = result.Rows.Single();

        Assert.AreEqual("40HC", row.ContainerType);
        Assert.AreEqual("Caldera", row.Poe);
        Assert.IsNull(row.Pod);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("40HC")));
    }

    [TestMethod]
    public void NormalizeForSource_WwlPairedNac_RebuildsCarrierDatesAndContainer()
    {
        const string source = """
            Pls consider rate USD6300/6400 , valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            Below the details of ONE NAC:
            Pls note, ONE NAC must match COMM as I listed below
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL
            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights
            """;
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(),
            null,
            "agent@example.com",
            "CASTRO FALLS | WWL CONTRACT ONE-MSC | AUG",
            source,
            null,
            "EmailBody",
            "email-body.txt",
            "text/plain",
            source,
            "correlation-id",
            null,
            null,
            0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(),
            null,
            null
        );
        var parsed = new ParsedAiPricingEmailResult(
            100m,
            [
                new AiPricingEmailResultRow(
                    "Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo",
                    "Acajutla/Corinto/Caldera",
                    null,
                    null,
                    "MSC",
                    "WWL",
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 4),
                    new DateTime(2026, 8, 14),
                    6300m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                new AiPricingEmailResultRow(
                    "Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin",
                    "Acajutla/Corinto/Caldera",
                    null,
                    null,
                    "MSC",
                    "WWL",
                    "RETAIL",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 4),
                    new DateTime(2026, 8, 14),
                    6400m,
                    100m,
                    null,
                    75m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            []
        );

        var result = PricingEmailAiExecutionFactory.NormalizeForSource(parsed, payload);

        Assert.AreEqual(8, result.Rows.Count);
        Assert.IsTrue(result.Rows.All(row => row.ContainerType == "40HC"));
        Assert.IsTrue(result.Rows.All(row => row.ValidFrom?.Date == new DateTime(2026, 8, 8)));
        Assert.IsTrue(result.Rows.All(row => row.ValidTo?.Date == new DateTime(2026, 8, 14)));
        Assert.IsTrue(result.Rows.All(row => row.Surcharges == 65m));

        var msc = result.Rows.Single(row => row.Carrier == "MSC");
        Assert.AreEqual(6300m, msc.OceanFreight);
        Assert.IsNull(msc.Commodity);
        Assert.IsFalse(msc.Pol!.Contains("Tianjin", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(msc.Pol.Contains("Xiamen", StringComparison.OrdinalIgnoreCase));

        var oneRows = result.Rows.Where(row => row.Carrier == "ONE").ToArray();
        Assert.AreEqual(7, oneRows.Length);
        Assert.IsTrue(oneRows.All(row => row.OceanFreight == 6400m));
        Assert.IsTrue(oneRows.Any(row => row.OriginCharges == 100m && row.Pol == "Tianjin"));
        Assert.IsTrue(oneRows.Any(row => row.OriginCharges == 400m && row.Pol == "Nanjing"));
        Assert.IsTrue(oneRows.Any(row => row.OriginCharges == 450m && row.Pol == "Wuhan"));
        Assert.IsTrue(oneRows.Any(row => row.OriginCharges == 850m && row.Pol == "Chongqing"));
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("MSC=primer monto")));
    }


    [TestMethod]
    public void ForwardedWwlFakPrompt_KeepsNewestTableAndDropsOlderQuotedOffer()
    {
        const string source = """
            Website : https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ---
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: miércoles, 12 de agosto de 2026 03:19
            Asunto: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG
            Dear Royner,
            Published FAK for your ref:
            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            14 Aug-20 Aug
            $7,700
            $7,900
            $7,900
            Un saludo cordial
            Veronica Jiang
            ·¢¼þÈË: Veronica.jiang <veronica.jiang@wwl.sg>
            ·¢ËÍÊ±¼ä: 2026Äê7ÔÂ31ÈÕ 19:21
            Ö÷Ìâ: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Published FAK for your ref:
            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            7 Aug-14 Aug
            $6,700
            $6,900
            $6,900
            """;

        using var payloadDocument = JsonDocument.Parse("{}");
        var response = new DataExtractionAiEmailRequestResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "request-hash", "correlation-id", "pricing-email-analysis",
            payloadDocument.RootElement.Clone(),
            new DataExtractionAiEmailImageResponse(false, null, null)
        );
        var payload = new AiPricingEmailPayload(
            Guid.NewGuid(), null, "agent@example.com",
            "UPDATE FAK WWL / CASTRO FALLAS / 12-AUG",
            source, null, "EmailBody", "email-body.txt", "text/plain", source,
            "correlation-id", "DataExtraction.NoRows", "No rows", 0m,
            Array.Empty<AiPreviousPricingEmailRow>(),
            Array.Empty<AiPreviousExtractionIssue>(),
            Array.Empty<AiCatalogGroupHint>(), null, null
        );

        var stage = PricingEmailAiExecutionFactory.CreateStages(response, payload, null).Single();

        StringAssert.Contains(stage.PromptJson, "$7,700");
        Assert.IsFalse(stage.PromptJson.Contains("$6,700", StringComparison.Ordinal));
        Assert.IsFalse(stage.PromptJson.Contains("AVISO LEGAL", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(stage.PromptJson, "primera sección visible");
    }

}
