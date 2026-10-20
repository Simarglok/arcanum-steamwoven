using Arcanum.Data.Area;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-048 follow-up: which host a speaker talks through. Over the strict owner fixture, which has no dialog NPC (as the
/// viewer's synthetic self-test core), so the shown host lists no speaker; its scenery body stands in for a visiting companion
/// (see <see cref="AreaVisitorTalkTests"/>).
/// </summary>
[TestClass]
public sealed class AreaTalkRoutingTests
{
    [TestMethod]
    public void A_speaker_no_host_lists_talks_through_the_shown_host_and_a_visitors_only_through_its_own()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        using var session = ConversationSessionFixture.Create(area.RuntimeMap);
        var shown = new AreaConversationHost(area, null, session);
        Assert.AreEqual(0, shown.Speakers.Count, "the synthetic core has no dialog NPC");

        // The self-test's stand-in: a row of the map as speaker 0. It reaches the shown host, whose start is a typed status.
        var row = area.Objects[0];
        var standIn = new AreaSpeaker(0, row, 1, row.WorldX ?? 0, row.WorldY ?? 0);
        Assert.AreSame(shown, AreaTalkRouting.HostOf(standIn, shown, []));
        Assert.AreEqual(AreaConversationStartStatus.ContentUnavailable, shown.Begin(standIn).Status);

        // A visiting companion talks through its own host, never the shown one.
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
            [new AreaSparseElement(ScrPoint.Dialog, [0, 0, AreaCompanionRoster.Virgil.ScriptId])], null);
        var companionRow = scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype, EffectiveFields = [.. scenery.EffectiveFields.Where(f => f.Slot != 31), scripts] };
        var template = AreaCompanionTemplate.From([new AreaSpeaker(3, companionRow, AreaCompanionRoster.Virgil.ScriptId, scenery.WorldX!.Value, scenery.WorldY!.Value)],
            AreaScenePlan.Create(area), area.Visuals, AreaChart.Exterior, new(1)).Single();
        var visitor = AreaConversationHost.ForVisitor(template, null, session);
        Assert.AreSame(visitor, AreaTalkRouting.HostOf(visitor.Speakers[0], shown, [visitor]));
        // A speaker of that companion's object that its host does not list (an earlier host's) has no host.
        Assert.IsNull(AreaTalkRouting.HostOf(visitor.Speakers[0] with { Index = 3 }, shown, [visitor]));
        // The shown host keeps its own speakers.
        var listed = new AreaConversationHost(area, [standIn], null, session);
        Assert.AreSame(listed, AreaTalkRouting.HostOf(standIn, listed, [visitor]));
        var other = area.Objects.First(o => o.Key != scenery.Key);
        Assert.AreSame(shown, AreaTalkRouting.HostOf(new AreaSpeaker(0, other, 1, other.WorldX ?? 0, other.WorldY ?? 0), shown, [visitor]),
            "a stand-in of another object than the visitor's");
    }
}
