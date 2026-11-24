using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-2038 (ledger TT20): a teleport (44) or fade-and-teleport (102) in a script of the talk and deferred-call host (<see cref="AreaScriptHost.Apply"/>, reached by <see cref="AreaConversationHost.RunCall"/> and by a dialog line's run): the hero's is a request
/// the viewer queues as the map transfer after the run applied, an NPC's goes to the shown map's use host as the #1837 request, a run that moves both or one the hosts cannot take is left unapplied whole. Scripts, entities and numbers are invented.
/// </summary>
[TestClass]
public sealed class AreaScriptTeleportCallTests
{
    private const int HeroTeleport = 920, HeroFade = 921, NpcTeleport = 922, Both = 923, HeroTeleportThenDialog = 924, DialogThenHeroTeleport = 925, FlagThenDialog = 926, NpcToOtherMap = 927;

    private static byte[] Program(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var clause = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(clause, lines[line].Action);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                clause[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(clause[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        Assert.AreEqual(ScrReadStatus.Read, ScrProgram.Read(bytes).Status);
        return bytes;
    }

    private const byte Triggerer = 0, Attachee = 1, Number = 3;

    private static void With(Action<AreaConversationHost, GameSessionOwner, Arcanum.Data.Area.AreaSnapshot, ScriptContent, SessionScriptRole> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-world-2038-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr"));
            using var source = new AreaOwnerFixture(motionBodies: true);
            var snapshot = source.Owner.LoadStrict();
            // 920: the triggerer to map 5000 (30, 40) (script 2375's shape with the hero); 921: a fade of 2 s and the teleport (script 1061's); 922: the attachee (script 3072's); 923: both.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00920hero.scr"), Program((ScrCatalogue.Action.Teleport, [(Triggerer, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 30), (Number, 40)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00921fade.scr"), Program((ScrCatalogue.Action.FadeAndTeleport, [(Number, 2), (Number, 0), (Number, 0), (Triggerer, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 31), (Number, 41)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00922npc.scr"), Program((ScrCatalogue.Action.Teleport, [(Attachee, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 32), (Number, 42)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00923both.scr"), Program((ScrCatalogue.Action.Teleport, [(Attachee, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 32), (Number, 42)]), (ScrCatalogue.Action.Teleport, [(Triggerer, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 30), (Number, 40)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            // 924: the hero's teleport, then the dialog action (the action ends the run, 0x567460); 925: the dialog action first, so the teleport after it never runs.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00924talk.scr"), Program((ScrCatalogue.Action.Teleport, [(Triggerer, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 33), (Number, 43)]), (ScrCatalogue.Action.Dialog, [(Number, 1)])));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00925late.scr"), Program((ScrCatalogue.Action.Dialog, [(Number, 1)]), (ScrCatalogue.Action.Teleport, [(Triggerer, 0), (Number, 4999 + snapshot.RuntimeMap), (Number, 34), (Number, 44)])));
            // 926 (ARC-WORLD-2358): a local flag set before the dialog action, which the action would store into the speaker's use slot.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00926flag.scr"), Program((ScrCatalogue.Action.SetLocalFlag, [(Number, 2)]), (ScrCatalogue.Action.Dialog, [(Number, 1)])));
            // 927 (ARC-WORLD-2039): the attachee to catalogue map 5008 (runtime 9), the shape of script 3019's tile teleport of an NPC to another map.
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00927away.scr"), Program((ScrCatalogue.Action.Teleport, [(Attachee, 0), (Number, 5008), (Number, 60), (Number, 70)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            using var content = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat")).Content!;
            using var session = ConversationSessionFixture.CreateWithHero(snapshot.RuntimeMap);
            var host = new AreaConversationHost(snapshot, content, session);
            test(host, session, snapshot, content, new(new(snapshot.RuntimeMap), SessionEventKinds.PlayerEntity));
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void A_deferred_calls_teleport_of_the_hero_is_queued_after_the_run_applied_and_a_fade_travels_with_it()
    {
        With((host, session, snapshot, content, pc) =>
        {
            var log = new List<string>();
            host.Scripts.HeroTeleportRefusal = t => { log.Add($"check {t.CatalogueMap} {t.X},{t.Y}"); return null; };
            host.Scripts.RequestHeroTeleport = t => log.Add($"request {t.CatalogueMap} {t.X},{t.Y} fade={(t.Fade is null ? "-" : "yes")}");
            Assert.IsTrue(host.RunCall(new(HeroTeleport, 0, pc, pc), out var detail), detail);
            Assert.IsTrue(host.RunCall(new(HeroFade, 0, pc, pc), out detail), detail);
            var map = 4999 + snapshot.RuntimeMap;
            CollectionAssert.AreEqual(new[] { $"check {map} 30,40", $"request {map} 30,40 fade=-", $"check {map} 31,41", $"request {map} 31,41 fade=yes" }, log, "asked before the run applies, recorded after it");
            Assert.AreEqual(1, host.Scripts.LastTeleportRequests.Count);
        });
    }

    [TestMethod]
    public void A_run_that_teleports_the_hero_and_then_starts_a_conversation_applies_and_records_the_teleport_but_one_after_the_dialog_action_never_runs()
    {
        With((host, session, snapshot, content, pc) =>
        {
            var log = new List<string>();
            host.Scripts.HeroTeleportRefusal = t => null;
            host.Scripts.RequestHeroTeleport = t => log.Add($"request {t.X},{t.Y}");
            ScrRunResult Run(int script) => ScrDispatcher.RunCall(new(script, 0, host.Scripts.Local(pc), host.Scripts.Local(pc)), host.Scripts,
                new() { Rules = session.Rules.Scripts, Admission = host.Scripts.CallAdmissionFor(host.Scripts.Local(pc)) });
            var run = Run(HeroTeleportThenDialog);
            Assert.IsNotNull(run.Effects.Dialog, "the dialog action ends the run with the conversation requested");
            Assert.AreEqual(1, run.Effects.Teleports.Count);
            Assert.IsTrue(host.Scripts.Apply(run.Effects), "the conversation no longer refuses the hero's teleport (ledger TT20)");
            CollectionAssert.AreEqual(new[] { "request 33,43" }, log);
            log.Clear();
            var late = Run(DialogThenHeroTeleport);
            Assert.IsNotNull(late.Effects.Dialog);
            Assert.AreEqual(0, late.Effects.Teleports.Count, "a teleport written after the dialog action is never reached");
            Assert.IsTrue(host.Scripts.Apply(late.Effects));
            Assert.AreEqual(0, log.Count);
        });
    }

    // ARC-WORLD-2358 (ledger TT20): a deferred call whose run starts a conversation is run: its effects apply (the hero's teleport written before the dialog action is queued) and the conversation is handed to the caller, which opens it at its next
    // frame; a dialog with an object that is no dialog NPC here leaves the call unapplied whole.
    [TestMethod]
    public void A_deferred_call_that_starts_a_conversation_applies_and_hands_the_conversation_over()
    {
        With((host, session, snapshot, content, pc) =>
        {
            var key = new AreaObjectKey(new string('a', 64), snapshot.RuntimeMap, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
            var row = new AreaObjectRecord(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
            var speaker = new AreaSpeaker(0, row, 1, 0, 0);
            host = new AreaConversationHost(snapshot, [speaker], content, session);
            var log = new List<string>();
            host.Scripts.HeroTeleportRefusal = t => null;
            host.Scripts.RequestHeroTeleport = t => log.Add($"request {t.X},{t.Y}");
            var talker = new SessionScriptRole(new(snapshot.RuntimeMap), AreaConversationState.EntityId(speaker.Row));

            Assert.IsTrue(host.RunCall(new(HeroTeleportThenDialog, 0, pc, talker), out var detail), detail);
            CollectionAssert.AreEqual(new[] { "request 33,43" }, log, "the teleport of the run applied");
            Assert.AreEqual((HeroTeleportThenDialog, 1, host.Scripts.Local(talker)?.Code), (host.LastCallDialog?.Script, host.LastCallDialog?.Node, host.LastCallDialog?.Npc?.Code), detail);
            StringAssert.Contains(detail, "conversation of script " + HeroTeleportThenDialog);

            // A call that starts none clears it; one with the dialog action first runs no teleport after it.
            Assert.IsTrue(host.RunCall(new(HeroTeleport, 0, pc, pc), out detail), detail);
            Assert.IsNull(host.LastCallDialog);
            log.Clear();
            Assert.IsTrue(host.RunCall(new(DialogThenHeroTeleport, 0, pc, talker), out detail), detail);
            Assert.IsNotNull(host.LastCallDialog);
            Assert.AreEqual(0, log.Count);

            // ARC-WORLD-2382 (point 1, 0x447282): a run that changed its local flags before the dialog action stores them in the speaker's use slot, which only the shown map's use host writes: without one the run is unapplied whole, with the reason.
            Assert.IsFalse(host.RunCall(new(FlagThenDialog, 0, pc, talker), out detail));
            StringAssert.Contains(detail, "use-slot");
            Assert.IsNull(host.LastCallDialog);
            // A use host whose row of the speaker holds no use slot cannot place the write either.
            var world = new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null);
            var bare = new AreaUseScriptHost(session, snapshot.RuntimeMap, [row], [], content, world);
            host.Scripts.QueryHost = () => bare;
            Assert.IsFalse(host.RunCall(new(FlagThenDialog, 0, pc, talker), out detail));
            StringAssert.Contains(detail, "use-slot");
            // With the use slot loaded (flags 0, counters 0x11, script 55) the run applies, stores the run's flag 2 and counters (the file header's 0 replaces the loaded 0x11; the script number keeps) into it and offers the conversation.
            var withSlot = row with { EffectiveFields = [new AreaFieldValue(31, AreaPresence.Value, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Use, [0L, 0x11L, 55L])], null)] };
            var useHost = new AreaUseScriptHost(session, snapshot.RuntimeMap, [withSlot], [], content, world);
            host.Scripts.QueryHost = () => useHost;
            var entity = AreaConversationState.EntityId(speaker.Row);
            Assert.AreEqual(new ScrSlot(55, 0, 0x11), host.Scripts.Slot(host.Scripts.Local(talker)!.Value, ScrPoint.Use));
            Assert.IsTrue(host.RunCall(new(FlagThenDialog, 0, pc, talker), out detail), detail);
            Assert.AreEqual(new ScrSlot(55, 4, 0), host.Scripts.Slot(host.Scripts.Local(talker)!.Value, ScrPoint.Use), "the use slot took the run's local flags");
            Assert.AreEqual(4L, session.QueryObjectChange(new SessionMapId(snapshot.RuntimeMap), MapChangeKey.ForEntity(entity, AreaUseScriptHost.SlotProperty(ScrPoint.Use, "lf"))));
            Assert.AreEqual((FlagThenDialog, 1), (host.LastCallDialog?.Script, host.LastCallDialog?.Node), detail);
            host.Scripts.QueryHost = null;

            // The dialog action's attachee is the hero here (the speaker is no NPC of this host): nothing applies, no conversation is offered.
            log.Clear();
            Assert.IsFalse(host.RunCall(new(HeroTeleportThenDialog, 0, pc, pc), out detail));
            StringAssert.Contains(detail, "no dialog NPC here");
            Assert.AreEqual(0, log.Count);
            Assert.IsNull(host.LastCallDialog);
        });
    }

    [TestMethod]
    public void A_teleport_the_host_cannot_take_leaves_the_call_unapplied_and_records_nothing()
    {
        With((host, session, snapshot, content, pc) =>
        {
            // No seam: not modelled, as before.
            Assert.IsFalse(host.RunCall(new(HeroTeleport, 0, pc, pc), out _));
            var requested = 0;
            host.Scripts.HeroTeleportRefusal = _ => "no map";
            host.Scripts.RequestHeroTeleport = _ => requested++;
            Assert.IsFalse(host.RunCall(new(HeroTeleport, 0, pc, pc), out _));
            Assert.AreEqual(0, requested);
            // An NPC's teleport without the shown map's use host, and a run that moves both, are not applied either.
            host.Scripts.HeroTeleportRefusal = _ => null;
            Assert.IsFalse(host.RunCall(new(NpcTeleport, 0, pc, new(new(snapshot.RuntimeMap), "oid:npc")), out _));
            Assert.IsFalse(host.RunCall(new(Both, 0, pc, new(new(snapshot.RuntimeMap), "oid:npc")), out _));
            Assert.AreEqual(0, requested);
        });
    }

    [TestMethod]
    public void A_deferred_calls_teleport_of_an_npc_goes_to_the_shown_maps_use_host_as_the_request_of_ticket_1837()
    {
        With((host, session, snapshot, content, pc) =>
        {
            var key = new AreaObjectKey(new string('a', 64), snapshot.RuntimeMap, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
            var npc = new AreaObjectRecord(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
            var log = new List<string>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                NpcTeleportRefusal: t => { log.Add($"check {t.Object.Code}"); return null; },
                RequestNpcTeleport: t => log.Add($"request {t.Object.Code} {t.X},{t.Y}"));
            var use = new AreaUseScriptHost(session, snapshot.RuntimeMap, [npc], [], content, world);
            host.Scripts.QueryHost = () => use;
            var entity = AreaUseScriptHost.EntityId(npc);
            var attachee = new SessionScriptRole(new(snapshot.RuntimeMap), entity);
            Assert.IsTrue(host.RunCall(new(NpcTeleport, 0, pc, attachee), out var detail), detail);
            CollectionAssert.AreEqual(new[] { $"check {entity}", $"request {entity} 32,42" }, log, "asked before the run applies, recorded after it");
            // The same run for an object that is no NPC of the shown map is not applied, and a run that moves the hero too is not told apart (one slot).
            log.Clear();
            Assert.IsFalse(host.RunCall(new(NpcTeleport, 0, pc, new(new(snapshot.RuntimeMap), "oid:stranger")), out _));
            host.Scripts.HeroTeleportRefusal = _ => null; host.Scripts.RequestHeroTeleport = _ => log.Add("hero");
            Assert.IsFalse(host.RunCall(new(Both, 0, pc, attachee), out _));
            CollectionAssert.AreEqual(Array.Empty<string>(), log);
        });
    }
    // ARC-WORLD-2039 (ledger TT20, FM1): a deferred call's teleport of an NPC of the shown map to another map is asked of the world's seam (which owns the map rules) and recorded with the map the script named.
    [TestMethod]
    public void A_deferred_calls_teleport_of_an_npc_to_another_map_is_asked_and_recorded_with_that_map()
    {
        With((host, session, snapshot, content, pc) =>
        {
            var key = new AreaObjectKey(new string('a', 64), snapshot.RuntimeMap, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
            var npc = new AreaObjectRecord(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);
            var log = new List<string>();
            var refused = false;
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null,
                NpcTeleportRefusal: t => { log.Add($"check {t.Object.Code} map={t.RuntimeMap}"); return refused ? "map not listed" : null; },
                RequestNpcTeleport: t => log.Add($"request {t.Object.Code} map={t.RuntimeMap} {t.X},{t.Y}"));
            var use = new AreaUseScriptHost(session, snapshot.RuntimeMap, [npc], [], content, world);
            host.Scripts.QueryHost = () => use;
            var entity = AreaUseScriptHost.EntityId(npc);
            var attachee = new SessionScriptRole(new(snapshot.RuntimeMap), entity);
            Assert.IsTrue(host.RunCall(new(NpcToOtherMap, 0, pc, attachee), out var detail), detail);
            CollectionAssert.AreEqual(new[] { $"check {entity} map=9", $"request {entity} map=9 60,70" }, log);
            // The world's seam decides: a map it does not list leaves the call unapplied.
            log.Clear(); refused = true;
            Assert.IsFalse(host.RunCall(new(NpcToOtherMap, 0, pc, attachee), out _));
            CollectionAssert.AreEqual(new[] { $"check {entity} map=9" }, log);
        });
    }
}
