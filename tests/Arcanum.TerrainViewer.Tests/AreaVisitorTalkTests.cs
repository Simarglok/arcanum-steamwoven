using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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

    internal static AreaCompanionTemplate Template() => Load().Template;

    /// <summary>The template and the snapshot it was cut from (the shown map's host is built over the snapshot, on map 2).</summary>
    internal static (AreaCompanionTemplate Template, AreaSnapshot Area) Load()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var scenery = area.Objects.Single(o => o.WorldX == AreaGridSession.ChartOriginX + 5 && o.WorldY == AreaGridSession.ChartOriginY + 1);
        // Index 3 on its home map: the shown map's host must not depend on the home host's indexes.
        // Its dialog attachment: local flags 0, counters 0, script 1324 (as AreaScriptHostTests builds one).
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, AreaCompanionRoster.Virgil.ScriptId])], null);
        var row = scenery with { PrototypeNumber = AreaCompanionRoster.Virgil.Prototype, EffectiveFields = [.. scenery.EffectiveFields.Where(f => f.Slot != 31), scripts] };
        var speaker = new AreaSpeaker(3, row, AreaCompanionRoster.Virgil.ScriptId,
            scenery.WorldX!.Value, scenery.WorldY!.Value);
        return (AreaCompanionTemplate.From([speaker], AreaScenePlan.Create(area), area.Visuals, AreaChart.Exterior, Home).Single(), area);
    }

    /// <summary>A dialog NPC of the shown map with an object id of its own (the fixture's rows share one).</summary>
    private static AreaSpeaker OwnSpeaker(AreaCompanionTemplate template) =>
        new(0, template.Speaker.Row with { Key = template.Key with { ObjectIdSha256 = new string('9', 64), SourceIndex = 77 } }, 1, 0, 0);

    /// <summary>The one host of the visited map 2, party-wired, with no dialog NPC of its own, over <paramref name="session"/>.</summary>
    internal static AreaConversationHost Shown(AreaSnapshot area, GameSessionOwner session, AreaInventoryHost? inventory = null) =>
        new(area, Visited, [], null, session, party: true, inventory);

    private static DialogParticipantId Npc => AreaConversationState.Npc(0);

    [TestMethod]
    public void A_carried_companion_is_one_more_speaker_of_the_shown_host_over_its_home_maps_dialog_state()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = Shown(area, session);
        var speaker = host.AdmitVisitor(template, out var refusal)!;
        Assert.IsNull(refusal);
        Assert.AreEqual(Visited, host.Map, "the host is the shown map's: there is no second one");
        Assert.AreEqual(Home, host.StateMap(speaker), "the companion's dialog state lives on its home map");
        Assert.AreEqual((0, template.Key), (speaker.Index, speaker.Row.Key), "listed after the map's own speakers");
        Assert.AreSame(speaker, host.Speakers.Single());
        Assert.IsTrue(host.PartyWired);

        // A DLG local flag written where it visits is its home map's key, and a home-map state reads it.
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 2, 1)));
        Assert.AreEqual(4L, session.QueryMapChange(Home, MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty)));
        Assert.IsNull(session.QueryMapChange(Visited, MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty)));
        var home = new AreaConversationState(session, Home, [template.Entity], 50);
        Assert.IsTrue(home.LocalFlag(0, 2), "the same state at home");

        // The script host's object stands on the shown map; its dialog slot is the home map's key.
        var obj = host.Scripts.Object(speaker);
        Assert.AreEqual(Visited.RuntimeMap, obj.Map, "the companion is an object of the map it stands on");
        Assert.AreEqual(host.Scripts.Pc.Map, obj.Map);
        Assert.AreEqual(AreaCompanionRoster.Virgil.ScriptId, host.Scripts.Slot(obj, ScrPoint.Dialog)?.Script);
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, Home,
            MapChangeKey.ForEntity(template.Entity, AreaScriptHost.ScriptProperty), 0).Disposition);
        Assert.AreEqual(0, host.Scripts.Slot(obj, ScrPoint.Dialog)?.Script ?? -1, "the dialog slot follows its home map's changes");

        // Its following is the session party, whichever way it is asked.
        Assert.IsFalse(host.State.Follows(0));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, template.Entity).Disposition);
        Assert.IsTrue(host.State.Follows(0));
    }

    // ARC-DEV-096 (MIG8): the dialog slot is a field of the companion itself, one record wherever a script wrote it: a write on the map it stands on moves
    // it, the dialog state still finds it, and a flag back at its loaded value clears it where it is.
    [TestMethod]
    public void A_carried_companions_dialog_slot_is_the_objects_own_wherever_a_script_wrote_it()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = Shown(area, session);
        host.AdmitVisitor(template, out var refusal);
        Assert.IsNull(refusal);
        var flags = MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty);
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 2, 1)));
        Assert.AreEqual(Home, session.QueryObjectRecordMap(flags), "written under its home map");

        // A heartbeat's call-ex of the dialog point wrote the slot on the map the companion stands on: the one record moves there.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, Visited, flags, 5).Disposition);
        Assert.AreEqual(Visited, session.QueryObjectRecordMap(flags));
        Assert.AreEqual(1, session.QueryObjectState(template.Entity).Count(field => field.Property == AreaConversationState.LocalFlagsProperty));
        Assert.IsTrue(host.State.LocalFlag(0, 0) && host.State.LocalFlag(0, 2), "the dialog state finds the record on the map it was moved to");

        // The flags go back to the loaded value through the port: the record is cleared on the map it sits on, not left behind on the home map's key.
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 0, 0)));
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 2, 0)));
        Assert.IsNull(session.QueryObjectRecordMap(flags));
        Assert.IsNull(session.QueryObjectChange(Home, flags));
        Assert.IsFalse(host.State.LocalFlag(0, 2));
    }

    [TestMethod]
    public void Admitting_a_companion_is_idempotent_and_follows_the_maps_own_speakers()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var own = OwnSpeaker(template);
        var host = new AreaConversationHost(area, Visited, [own], null, session, party: true);
        var speaker = host.AdmitVisitor(template, out _)!;
        Assert.AreEqual(1, speaker.Index, "after the map's own speaker");
        Assert.AreEqual((Visited, Home), (host.StateMap(own), host.StateMap(speaker)), "only the companion's state is on another map");
        Assert.AreSame(speaker, host.AdmitVisitor(template, out var again));
        Assert.IsNull(again);
        Assert.AreEqual(2, host.Speakers.Count, "listed once");
        Assert.AreSame(own, host.AdmitVisitor(new AreaCompanionTemplate(own, template.Facts, template.Home, template.HomeOrigin, template.HomeWorld, template.Commands, template.Visuals), out _),
            "a companion the host already lists as its own is returned as it is");
        Assert.AreEqual(Visited, host.StateMap(own));
        // The companion's reactions and following are its entity's, at its index.
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), AreaConversationState.Npc(1), 3, 1)));
        Assert.AreEqual(8L, session.QueryMapChange(Home, MapChangeKey.ForEntity(template.Entity, AreaConversationState.LocalFlagsProperty)));
        Assert.IsNull(session.QueryMapChange(Visited, MapChangeKey.ForEntity(AreaConversationState.EntityId(own.Row), AreaConversationState.LocalFlagsProperty)),
            "the map's own speaker keeps its own state");
    }

    [TestMethod]
    public void A_host_without_the_party_lists_no_companion()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = new AreaConversationHost(area, Visited, [], null, session, party: false);
        Assert.IsNull(host.AdmitVisitor(template, out var refusal));
        Assert.IsNull(refusal);
        Assert.AreEqual(0, host.Speakers.Count);
    }

    [TestMethod]
    public void A_companion_whose_stored_dialog_state_cannot_be_read_is_refused_once_and_listed_after_a_rebind_over_a_readable_session()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = Shown(area, session);
        // A restored session may carry any long under these keys: a follow consent other than 1 is not a state this port reads.
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetMapChange(session.Version, Home,
            MapChangeKey.ForEntity(template.Entity, AreaConversationState.FollowProperty), 7).Disposition);
        Assert.IsNull(host.AdmitVisitor(template, out var refusal));
        StringAssert.Contains(refusal, "AREA_SESSION_DIALOG_STATE_INVALID");
        Assert.AreEqual(0, host.Speakers.Count, "nothing is listed, and no index is used");
        Assert.IsNull(host.AdmitVisitor(template, out var second));
        Assert.IsNull(second, "the refusal is reported once, then kept");
        Assert.AreEqual(GameSessionDisposition.Changed, session.ClearMapChange(session.Version, Home,
            MapChangeKey.ForEntity(template.Entity, AreaConversationState.FollowProperty)).Disposition);
        Assert.IsNull(host.AdmitVisitor(template, out _), "kept until the host is rebound over a session");
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        Assert.IsNotNull(host.Rebind(loaded).AdmitVisitor(template, out _));
    }

    [TestMethod]
    public void The_companions_state_survives_a_save_and_load()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var host = Shown(area, session);
        host.AdmitVisitor(template, out _);
        Assert.AreEqual(DialogCommandResult.Applied, host.State.Execute(new LocalFlagWriteCommand(new DialogId(1324), Npc, 5, 1)));
        Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, template.Entity).Disposition);
        using var loaded = ConversationSessionFixture.SaveAndLoad(session);
        var rebound = host.Rebind(loaded);
        Assert.AreEqual(Visited, rebound.Map);
        Assert.AreEqual(0, rebound.Speakers.Count, "the companions standing here are listed again over the new session, when asked");
        var speaker = rebound.AdmitVisitor(template, out _)!;
        Assert.AreEqual(Home, rebound.StateMap(speaker));
        Assert.IsTrue(rebound.State.LocalFlag(speaker.Index, 5) && rebound.State.Follows(speaker.Index));
        var fresh = Shown(area, loaded);
        var again = fresh.AdmitVisitor(template, out _)!;
        Assert.IsTrue(fresh.State.LocalFlag(again.Index, 5) && fresh.State.Follows(again.Index), "a new host after a load reads the same state");
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
        // A companion waiting elsewhere on its own map records its stored cell too, but the window is the one of the NPC that is away.
        var waiting = "oid:" + new string((char)48, 64);   // sorts before any entity id
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            [new MapChangeEdit(Home, AreaCompanionPlacement.HomeKey(waiting), AreaCompanionPlacement.Encode(new(1, 2), facing))]).Disposition);
        Assert.AreEqual(2, AreaCompanionPlacement.HomeCells(session, Home).Count);
        Assert.AreEqual(template.HomeWorld, AreaVisitors.HomeWindow(session, Home.RuntimeMap), "the NPC away decides the window");
        // Back on the home map, off its stored cell: the placement and the stored cell stay (the body is not where its object lies).
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, AreaCompanionGroup.Arrive(template, Visited, Home, new(10, 20), facing)).Disposition);
        Assert.IsNull(session.QueryMapChange(Home, AreaCompanionPlacement.MapKey(template.Entity)));
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
        // On its stored cell facing the default: both are cleared, as the placement always was.
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null, AreaCompanionPlacement.Edits(template, Home, template.HomeWorld, facing)).Disposition);
        Assert.IsNull(session.QueryMapChange(Home, AreaCompanionPlacement.Key(template.Entity)));
        Assert.IsNull(AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
        // On its stored cell facing another way the placement is recorded, so the stored cell is too.
        Assert.AreEqual(GameSessionDisposition.Changed, session.ApplyChanges(session.Version, null,
            AreaCompanionPlacement.Edits(template, Home, template.HomeWorld, Arcanum.Simulation.Movement.NeighborDirection.YPlus)).Disposition);
        Assert.AreEqual(template.HomeWorld, AreaCompanionPlacement.HomeCell(session, Home, template.Entity));
    }

    [TestMethod]
    public void A_carried_companions_give_item_goes_through_the_session_and_is_refused_by_it_for_a_holder_the_heros_map_does_not_list()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var shownMap = new AreaInventoryMap(Visited.RuntimeMap, new([], []), new Dictionary<string, AreaObjectRecord>());
        var inventory = new AreaInventoryHost([shownMap]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var host = Shown(area, session, inventory);
        host.AdmitVisitor(template, out _);
        Assert.IsTrue(host.InventoryWired, "the shown map's seed is attached: no second host to ask");
        // The session looks in the seed of the hero's map only: the companion's holder is on its home map, so it says so (no host-side refusal rule).
        var port = host.InventoryPort()!;
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new GiveItemCommand(Npc, AreaConversationState.PcParticipant, 4001)));
        Assert.AreEqual(GameSessionFailure.UnknownHolder, ((SessionDialogInventoryPort)port).LastResult!.Value.Failure);
        // A host with no inventory records the item only, as the map's own speakers do.
        var plain = Shown(area, session);
        plain.AdmitVisitor(template, out _);
        Assert.IsNull(plain.InventoryPort());
        Assert.IsFalse(plain.InventoryWired);
    }

    [TestMethod]
    public void A_companion_of_the_shown_maps_other_window_gives_an_item_from_the_seed_holder_the_session_holds()
    {
        var (template, area) = Load();
        // The companion's home is the shown map (the main map's other window): the hero's map seed lists its holder, so the session moves the item.
        using var session = ConversationSessionFixture.Create(Home.RuntimeMap);
        var kind = new SessionItemKind(SessionItemType.Generic, 8000, 6000, 6001);
        var held = "oid:" + new string('7', 64);
        var map = new AreaInventoryMap(Home.RuntimeMap,
            new([new SessionSeedHolder(template.Entity, SessionHolderKind.Npc, false, false, [], [held])], [new SessionSeedItem(held, new(kind, 1, 0, 0, null, []), null)]),
            new Dictionary<string, AreaObjectRecord>
            {
                [template.Entity] = template.Speaker.Row,
                [held] = template.Speaker.Row with { Key = template.Key with { SourceIndex = 78, ObjectIdSha256 = new string('7', 64) } },
            });
        var inventory = new AreaInventoryHost([map]);
        Assert.AreEqual(GameSessionFailure.None, inventory.Attach(session));
        var host = new AreaConversationHost(area, Home, [], null, session, party: true, inventory);
        var speaker = host.AdmitVisitor(template, out _)!;
        Assert.AreEqual(Home, host.StateMap(speaker));
        var port = host.InventoryPort()!;
        var given = port.Execute(new GiveItemCommand(Npc, AreaConversationState.PcParticipant, 6000));
        Assert.AreEqual(DialogCommandResult.Applied, given, $"{((SessionDialogInventoryPort)port).LastResult?.Failure}");
        Assert.IsTrue(session.QueryPcInventory().Any(item => item.Entity == held), "the item is the hero's now");
    }

    [TestMethod]
    public void A_carried_companion_keeps_the_narrow_script_set_and_the_maps_own_speakers_the_wide_one()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        var own = OwnSpeaker(template);
        var host = new AreaConversationHost(area, Visited, [own], null, session, party: true);
        var speaker = host.AdmitVisitor(template, out _)!;
        host.Scripts.QueryHost = () => null;
        Assert.IsTrue(host.IsCarried(speaker));
        Assert.IsFalse(host.IsCarried(own));
        Assert.IsFalse(host.IsCarried(speaker with { Index = 5 }), "a speaker this host does not list is not carried");
        // Without a use host both sets are the narrow one; the carried companion's never widens (a query about it would be answered with no row).
        Assert.AreSame(AreaScriptHost.Admission, host.Scripts.AdmissionFor(host.Scripts.Object(speaker)));
        Assert.AreSame(host.Scripts.RunAdmission, host.Scripts.AdmissionFor(host.Scripts.Object(own)));
    }

    [TestMethod]
    public void A_row_of_an_entity_the_host_lists_already_is_not_listed_twice()
    {
        var (template, area) = Load();
        using var session = ConversationSessionFixture.Create(Visited.RuntimeMap);
        // Another speaker (another row key) of the same entity id: one object, one speaker, so its state stays on the shown map.
        var twin = new AreaSpeaker(0, template.Speaker.Row with { Key = template.Key with { SourceIndex = 77 } }, 1, 0, 0);
        Assert.AreEqual(template.Entity, AreaConversationState.EntityId(twin.Row), "the same object id");
        var host = new AreaConversationHost(area, Visited, [twin], null, session, party: true);
        Assert.AreSame(twin, host.AdmitVisitor(template, out _));
        Assert.AreEqual(1, host.Speakers.Count);
        Assert.AreEqual(Visited, host.StateMap(twin));
    }
}
