using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-048: talking to a companion that visits another map (rows OL31–OL33 of the companion orders study: one object,
/// wherever it stands). Over the strict owner fixture, whose scenery body stands in for the companion (see
/// <see cref="AreaCompanionTests"/>); home is runtime map 1, the visited map 2. No script content: the host's state, keys,
/// party and inventory ports are checked, not a DLG.
/// </summary>
[TestClass]
public sealed class AreaVisitorTalkTests
{
    private static readonly SessionMapId Home = new(1), Visited = new(2);

    internal static AreaCompanionTemplate Template()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        // Index 3 on its home map: the visitor host must not depend on the home host's indexes.
        // Its dialog attachment: local flags 0, counters 0, script 1324 (as AreaScriptHostTests builds one).
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, AreaCompanionRoster.Virgil.ScriptId])], null);
        var row = scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype, EffectiveFields = [.. scenery.EffectiveFields.Where(f => f.Slot != 31), scripts] };
        var speaker = new AreaSpeaker(3, row, AreaCompanionRoster.Virgil.ScriptId,
            scenery.WorldX!.Value, scenery.WorldY!.Value);
        return AreaCompanionTemplate.From([speaker], AreaScenePlan.Create(area), area.Visuals, AreaChart.Exterior, Home).Single();
    }

    private static DialogParticipantId Npc => AreaConversationState.Npc(0);

    [TestMethod]
    public void A_visitor_talks_with_its_home_maps_dialog_state()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = AreaConversationHost.ForVisitor(template, null, session);
        Assert.AreEqual(Home, host.Map, "the visitor's state lives on its home map");
        Assert.AreEqual((0, template.Key), (host.Speakers.Single().Index, host.Speakers.Single().Row.Key), "its own host, index 0");
        Assert.IsTrue(host.PartyWired);

        // A DLG local flag written where it visits is its home map's key, and a home-map state reads it.
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 2, 1)));
        Assert.AreEqual(4L, session.QueryMapChange(Home, MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty)));
        Assert.IsNull(session.QueryMapChange(Visited, MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty)));
        var home = new AreaConversationState(session, Home, [template.Entity], 50);
        Assert.IsTrue(home.LocalFlag(0, 2), "the same state at home");

        // The script host's dialog slot is the home map's key too.
        var obj = host.Scripts.Object(host.Speakers[0]);
        Assert.AreEqual(Home.RuntimeMap, obj.Map);
        Assert.AreEqual(AreaCompanionRoster.Virgil.ScriptId, host.Scripts.Slot(obj, ScrPoint.Dialog)?.Script);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, Home,
            MapChangeKey.ForEntity(template.Entity, AreaScriptHost.ScriptProperty), 0).Disposition);
        Assert.AreEqual(0, host.Scripts.Slot(obj, ScrPoint.Dialog)?.Script ?? -1, "the visitor's dialog slot follows its home map's changes");

        // Its following is the session party, whichever host asks.
        Assert.IsFalse(host.State.Follows(0));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, template.Entity).Disposition);
        Assert.IsTrue(host.State.Follows(0));
    }

    [TestMethod]
    public void The_visitors_state_survives_a_save_and_load()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = AreaConversationHost.ForVisitor(template, null, session);
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 5, 1)));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, template.Entity).Disposition);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var rebound = host.Rebind(loaded);
        Assert.AreEqual(Home, rebound.Map);
        Assert.IsTrue(rebound.State.LocalFlag(0, 5) && rebound.State.Follows(0));
        var fresh = AreaConversationHost.ForVisitor(template, null, loaded);
        Assert.IsTrue(fresh.State.LocalFlag(0, 5) && fresh.State.Follows(0), "a new host after a load reads the same state");
    }

    [TestMethod]
    public void A_visitor_without_a_body_in_the_shown_scene_is_found_with_its_home_and_stored_cell()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        // What the transfer writes when the companion arrives on map 2 (placement, home change, stored cell).
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            AreaCompanionGroup.Arrive(template, Home, Visited, new(10, 20), Arcanum.Simulation.Movement.NeighborDirection.XPlus)).Disposition);
        var found = AreaVisitors.Here(session, Visited, []);
        Assert.AreEqual(1, found.Count);
        Assert.AreEqual(new AreaVisitor(template.Entity, Home.RuntimeMap, template.HomeWorld), found[0], "its home map and the cell its stored object lies on");
        Assert.AreEqual(0, AreaVisitors.Here(session, Visited, [template]).Count, "a body the shown scene draws is not a visitor");
        Assert.AreEqual(0, AreaVisitors.Here(session, Home, []).Count, "its home map records only where it went");
        Assert.AreEqual(0, AreaVisitors.Here(session, Visited, [], cell => cell.X > 100).Count,
            "a cell the viewer has not loaded (the main map's other windows) is not here");
        Assert.AreEqual(1, AreaVisitors.Here(session, Visited, [], cell => cell == new Arcanum.Simulation.Movement.CellCoord(10, 20)).Count);
    }

    [TestMethod]
    public void A_visitor_recorded_before_the_stored_cell_was_kept_has_a_home_but_no_cell()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var cell = AreaCompanionPlacement.Encode(new(10, 20), Arcanum.Simulation.Movement.NeighborDirection.XPlus);
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
        [
            new MapChangeEdit(Home, AreaCompanionPlacement.MapKey(template.Entity), Visited.RuntimeMap),
            new MapChangeEdit(Visited, AreaCompanionPlacement.Key(template.Entity), cell),
        ]).Disposition);
        Assert.AreEqual(new AreaVisitor(template.Entity, Home.RuntimeMap, null), AreaVisitors.Here(session, Visited, []).Single());
    }

    [TestMethod]
    public void The_stored_cell_is_recorded_with_every_placement_away_from_it_and_cleared_with_the_one_on_it()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var facing = Arcanum.Simulation.Movement.NeighborDirection.XPlus;
        // Away on another map: the stored cell is kept on the home map, the placement on the visited one.
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, AreaCompanionGroup.Arrive(template, Home, Visited, new(10, 20), facing)).Disposition);
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCells(session, Home)[template.Entity]);
        Assert.AreEqual(template.HomeWorld, AreaVisitors.HomeWindow(session, Home.RuntimeMap));
        Assert.IsNull(AreaVisitors.HomeWindow(session, Visited.RuntimeMap), "nothing is recorded for a map that holds no NPC away");
        // Back on the home map, off its stored cell: the placement and the stored cell stay (the body is not where its object lies).
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, AreaCompanionGroup.Arrive(template, Visited, Home, new(10, 20), facing)).Disposition);
        Assert.IsNull(session.QueryMapChange(Home, AreaCompanionPlacement.MapKey(template.Entity)));
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
        // On its stored cell facing the default: both are cleared, as the placement always was.
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, AreaCompanionPlacement.Edits(template, Home, template.HomeWorld, facing)).Disposition);
        Assert.IsNull(session.QueryMapChange(Home, AreaCompanionPlacement.Key(template.Entity)));
        Assert.IsNull(AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
    }

    [TestMethod]
    public void A_visitors_give_item_is_refused_when_its_home_seed_is_not_attached()
    {
        var template = Template();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var uncovered = AreaConversationHost.ForVisitor(template, null, session, inventory: new AreaInventoryHost([]));
        Assert.IsFalse(uncovered.InventoryWired);
        Assert.AreEqual(DialogCommandResult.Rejected, uncovered.InventoryPort()!.Execute(new GiveItemCommand(Npc, AreaConversationState.PcParticipant, 4001)),
            "a typed refusal, not a crash or a silent record");
        Assert.AreEqual(DialogCommandResult.Rejected, AreaConversationHost.ForVisitor(template, null, session).InventoryPort()!
            .Execute(new GiveItemCommand(Npc, AreaConversationState.PcParticipant, 4001)), "no inventory host at all");
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var rebound = uncovered.Rebind(loaded);
        Assert.AreEqual((Home, false), (rebound.Map, rebound.InventoryWired));
        Assert.AreEqual(DialogCommandResult.Rejected, rebound.InventoryPort()!.Execute(new GiveItemCommand(Npc, AreaConversationState.PcParticipant, 4001)),
            "a rebound visitor host keeps refusing");
    }
}
