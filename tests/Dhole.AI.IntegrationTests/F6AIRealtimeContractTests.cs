using System.Text.Json;
using Dhole.AI.Persistence.Messaging;
using Microsoft.Extensions.Configuration;

namespace Dhole.AI.IntegrationTests;

[TestClass]
public sealed class F6AIRealtimeContractTests
{
    private static IConfiguration Build(bool approved)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:UniversalRealtimeEnabled"] = "true",
            ["Realtime:Outbox:Enabled"] = "true",
            ["Realtime:BatchApproved"] = approved.ToString(),
            ["Realtime:ReleaseId"] = AIRealtimeInvalidation.ReleaseId
        }).Build();

    [TestMethod]
    public void Four_release_gates_disable_unapproved_event_producers()
    {
        Assert.IsFalse(AIRealtimeInvalidation.IsEnabled(null));
        Assert.IsFalse(AIRealtimeInvalidation.IsEnabled(Build(false)));
        var config = Build(true);
        Assert.IsTrue(AIRealtimeInvalidation.IsEnabled(config));
        config["Realtime:ReleaseId"] = "wrong";
        Assert.IsFalse(AIRealtimeInvalidation.IsEnabled(config));
        config["Realtime:ReleaseId"] = AIRealtimeInvalidation.ReleaseId;
        config["Realtime:Outbox:Enabled"] = "false";
        Assert.IsFalse(AIRealtimeInvalidation.IsEnabled(config));
    }

    [TestMethod]
    public void Each_processing_resource_emits_only_non_sensitive_identity_metadata()
    {
        var resourceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        foreach (var kind in new[] { "execution", "emailjob" })
        {
            var json = AIRealtimeInvalidation.Serialize(eventId, resourceId,
                kind, AIRealtimeInvalidation.NextVersionHint(),
                DateTimeOffset.UtcNow, "f6-ai-test");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.AreEqual("ai." + kind + ".changed.v1",
                root.GetProperty("eventType").GetString());
            Assert.AreEqual($"ai:{kind}:{resourceId:D}",
                root.GetProperty("audienceRef").GetString());
            Assert.IsTrue(root.GetProperty("aggregateVersion").GetInt64() > 0);
            CollectionAssert.AreEquivalent(new[] { "changedFields" },
                root.GetProperty("payload").EnumerateObject().Select(p => p.Name).ToArray());
            foreach (var forbidden in new[] {"email", "body", "prompt", "output", "token", "password", "payloadUrl"})
                Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    [TestMethod]
    public void Invalid_identifiers_and_unsupported_kinds_fail_closed()
    {
        var id = Guid.NewGuid();
        var v = AIRealtimeInvalidation.NextVersionHint();
        Assert.IsTrue(AIRealtimeInvalidation.NextVersionHint() > v);
        Assert.ThrowsExactly<ArgumentException>(() =>
            AIRealtimeInvalidation.EventType("private"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            AIRealtimeInvalidation.Serialize(Guid.Empty, id, "execution", v,
                DateTimeOffset.UtcNow, "x"));
    }
}
