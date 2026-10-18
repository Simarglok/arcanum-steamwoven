using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Data.Tests.Area.AreaIdentityTests;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-DATA-036 on synthetic fixtures: a plan's endpoints are published only when each names a present static
/// core object with that id, prototype and use script; malformed endpoint lists are refused by the plan.</summary>
[TestClass]
public sealed class AreaEndpointsTests
{
    private const long Start = 67108865;
    private const int Script = 2793;

    [TestMethod]
    public void An_endpoint_is_published_only_for_its_objects_own_use_script()
    {
        string objectId;
        using (var plain = new Fixture([(Start, [Scripted(Script)])]))
        {
            var snapshot = plain.Owner.LoadStrict();
            Assert.AreEqual(0, snapshot.Endpoints.Count);
            var item = snapshot.Objects.Single();
            Assert.AreEqual(Script, AreaEndpoints.UseScript(item));
            objectId = item.Key.ObjectIdSha256;
        }
        var endpoint = new AreaEndpoint(Start, 0, 7001, AreaEndpoints.UseSap, Script, 5078, 79, 2087, 2018, objectId);
        using (var f = new Fixture([(Start, [Scripted(Script)])], endpoints: [endpoint]))
            CollectionAssert.AreEqual(new[] { endpoint }, f.Owner.LoadStrict().Endpoints.ToArray());
        // Another script, ordinal, prototype or object id, or an object without a use script, is not the object's transition.
        foreach (var (wrong, records) in new (AreaEndpoint, byte[][])[]
        {
            (endpoint with { Script = Script + 1 }, [Scripted(Script)]), (endpoint with { Ordinal = 1 }, [Scripted(Script)]),
            (endpoint with { Prototype = 7002 }, [Scripted(Script)]), (endpoint with { SourceObjectIdSha256 = new string('a', 64) }, [Scripted(Script)]),
            (endpoint, [Record(Id(0))]),
        })
        {
            using var f = new Fixture([(Start, records)], endpoints: [wrong]);
            var report = f.Owner.Inspect();
            Assert.IsFalse(report.IsComplete, wrong.ToString());
            Assert.IsTrue(report.Issues.Any(i => i.Code == AreaErrorCode.InputIdentityMismatch && i.Stage == AreaStage.Appearance), wrong.ToString());
        }
    }

    [TestMethod]
    public void A_plan_refuses_an_endpoint_list_that_is_not_distinct_use_transitions()
    {
        var endpoint = new AreaEndpoint(Start, 0, 7001, AreaEndpoints.UseSap, Script, 5078, 79, 2087, 2018, new string('a', 64));
        AreaSourcePlan Plan(IReadOnlyList<AreaEndpoint> endpoints) => new(1, 1, new(1, 1), 2, "maps/test", [], []) { Endpoints = endpoints };
        Assert.AreEqual(1, Plan([endpoint]).Endpoints.Count);
        foreach (var bad in new IReadOnlyList<AreaEndpoint>[] { [endpoint with { Sap = 0 }], [endpoint with { SourceObjectIdSha256 = "A" + new string('a', 63) }],
            [endpoint, endpoint with { Script = 1 }], [null!] })
            Assert.ThrowsExactly<ArgumentException>(() => Plan(bad));
    }

    /// <summary>A static record at local cell 0 whose script field (31) holds the use element (key 1): header flags,
    /// counters and the script number.</summary>
    private static byte[] Scripted(int script)
    {
        using var s = new MemoryStream();
        // Version, the object and prototype ids, then the type and the field count and masks (AreaWireBoundaryTests.Fields).
        I32(s, 119); s.Write(Id(1, 7001)); s.Write(Id(0)); I32(s, 3);
        s.WriteByte(2); s.WriteByte(0);
        I32(s, (1 << 1) | (1 << 31)); I32(s, 0); I32(s, 0);
        s.WriteByte(1); I64(s, 0);
        // One 12-byte element (header flags, counters, script) under key 1 (key bit-set word 2).
        s.WriteByte(1); I32(s, 12); I32(s, 1); I32(s, 19); I32(s, 0); I32(s, 0); I32(s, script); I32(s, 1); I32(s, 2);
        return s.ToArray();
    }
}
