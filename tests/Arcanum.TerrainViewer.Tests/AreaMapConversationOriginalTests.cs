using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-WORLD-032 check (DAT_ROOT = the game folder): the dialog NPCs of every interior talk through the
/// viewer's conversation host, from the map's own snapshot and the game's script content as map mode opens them (without
/// map mode's inventory port and hero facts, which change only what happens after a start).</summary>
[TestClass]
public sealed class AreaMapConversationOriginalTests
{
    [TestMethod, TestCategory("RealData")]
    public void Every_dialog_npc_of_every_interior_starts_through_the_host()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in map conversation check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        using var scripts = ConversationSessionFixture.Scripts(root);
        var sounds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var statuses = new SortedDictionary<string, int>(StringComparer.Ordinal); var stops = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var maps = 0; var speakers = 0; var offered = 0; var ended = 0; var unlisted = new List<string>();
        void Count(SortedDictionary<string, int> into, string key) => into[key] = into.GetValueOrDefault(key) + 1;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            AreaSnapshot snapshot;
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open()) snapshot = owner.LoadStrict();
            if (AreaConversationHost.FindSpeakers(snapshot).Count == 0) continue;
            // A map the map list does not name has no runtime number, so map mode has no session there and no dialogs.
            if (snapshot.RuntimeMap == 0) { unlisted.Add(map.Name); continue; }
            maps++;
            using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
            var host = new AreaConversationHost(snapshot, scripts, session);
            foreach (var speaker in host.Speakers)
            {
                speakers++;
                var start = host.Begin(speaker);
                Count(statuses, start.Status.ToString());
                // ARC-SCRIPT-040: the sound actions the dialog scripts ran.
                foreach (var sound in host.LastRun?.Effects.Sounds ?? []) Count(sounds, sound.Kind.ToString());
                // The refusal of an entry the SCR admission set does not admit yet: its code and operation type.
                if (start.Status == AreaConversationStartStatus.EntryNotAdmitted) Count(stops, start.Detail[(start.Detail.IndexOf(": ", StringComparison.Ordinal) + 2)..]);
                if (start.Conversation is not { } conversation) continue;
                if (conversation.Phase == DialogConversationPhase.AwaitingSelection) offered++;
                // The first reply a player could choose, until the dialog ends (no exception on any original line); an insult
                // (ARC-WORLD-043) is a deliberate choice the walk does not make.
                while (conversation.Phase == DialogConversationPhase.AwaitingSelection && conversation.Turn < 64)
                {
                    var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Marker is not { Letter: 'i' });
                    if (option is null) break;
                    conversation.Select(option.Token);
                }
                if (conversation.Frontier is { } frontier) Count(stops, "frontier " + frontier.Kind);
                if (conversation.Phase == DialogConversationPhase.Ended) ended++;
            }
        }
        Console.WriteLine($"maps {maps} speakers {speakers} offered {offered} ended {ended} statuses {string.Join(", ", statuses.Select(s => $"{s.Key}={s.Value}"))}");
        Console.WriteLine("stops " + string.Join("; ", stops.Select(d => $"{d.Key}={d.Value}")));
        Console.WriteLine("sounds " + string.Join("; ", sounds.Select(d => $"{d.Key}={d.Value}")));
        Assert.AreEqual(0, sounds.Count, "ARC-SCRIPT-040: no dialog script these talks run reaches a sound action yet");
        // Every speaker gets a typed start; the counts move only when the SCR admission set or the dialog runtime grows
        // (then this census is updated with them): 131 entries stop at an SCR action, condition, goto or value kind not admitted yet.
        Assert.AreEqual((34, 182), (maps, speakers));
        Assert.AreEqual("EntryNotAdmitted=131, ScriptEnded=1, Started=50", string.Join(", ", statuses.Select(s => $"{s.Key}={s.Value}")));
        // ARC-WORLD-043: the ME condition is admitted, so two conversations that stopped at it offer replies (one of them ends);
        // the walk takes no insult, and with it the counts are the same. ARC-WORLD-058: the 35 conversations that stopped at their
        // NPC's g: greeting now greet; 10 more offer replies and end, the other 25 stop later at an operation not admitted yet.
        // Re-pinned in ARC-DEV-194: origin/main already counted 26 stops and 24 offering, 22 ending conversations here (the dialog condition slices since ARC-DEV-179 had not
        // updated this opt-in walk); in and ni change none of these counts, no talk of the walk asks them.
        Assert.AreEqual((0, 26), (stops.GetValueOrDefault("frontier UnsupportedGeneratedText"), stops.GetValueOrDefault("frontier UnsupportedOperation")));
        Assert.AreEqual((24, 22), (offered, ended));
        CollectionAssert.AreEqual(new[] { "scienticians underground lev 2" }, unlisted);
    }

    /// <summary>ARC-SCRIPT-050: the same walk with the shown map's use host answering the talks' queries (<see cref="AreaScriptHost.QueryHost"/>),
    /// as map mode wires it: the PC on the speaker's cell, the map list's area. The talk set admits float line, get area, random and the guard
    /// conditions, so more entries run; a floated line is shown or, with an action field the viewer does not run, leaves the talk unapplied.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Every_dialog_npc_of_every_interior_starts_with_the_talk_queries()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in map conversation check; this is not a synthetic pass.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var list = MapList.Read(stack);
        using var scripts = ConversationSessionFixture.Scripts(root);
        var statuses = new SortedDictionary<string, int>(StringComparer.Ordinal); var stops = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var speakers = 0; var floated = new List<string>();
        void Count(SortedDictionary<string, int> into, string key) => into[key] = into.GetValueOrDefault(key) + 1;
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            AreaSnapshot snapshot;
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open()) snapshot = owner.LoadStrict();
            if (AreaConversationHost.FindSpeakers(snapshot).Count == 0 || snapshot.RuntimeMap == 0) continue;
            using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
            var host = new AreaConversationHost(snapshot, scripts, session);
            var entry = list.ByRuntime(snapshot.RuntimeMap)!;
            var area = (entry.Area, list.OfType(MapListType.Start)?.Runtime == entry.Runtime);
            Arcanum.Simulation.Movement.CellCoord? pc = null;
            var use = new AreaUseScriptHost(session, snapshot.RuntimeMap, snapshot.Objects, [], scripts,
                new AreaScriptWorld(() => pc, _ => false, _ => null, () => null, MapArea: () => area));
            host.Scripts.QueryHost = () => use;
            foreach (var speaker in host.Speakers)
            {
                speakers++;
                pc = new(checked((int)speaker.WorldX), checked((int)speaker.WorldY));
                var start = host.Begin(speaker);
                Count(statuses, start.Status.ToString());
                if (start.Status is AreaConversationStartStatus.EntryNotAdmitted or AreaConversationStartStatus.ScriptEffectsRejected)
                    Count(stops, start.Status == AreaConversationStartStatus.ScriptEffectsRejected ? "effects " + host.LastRun!.Script
                        : start.Detail[(start.Detail.IndexOf(": ", StringComparison.Ordinal) + 2)..]);
                floated.AddRange(host.Scripts.TakeFloats().Select(f => $"{map.Name}:{host.LastRun!.Script}"));
            }
        }
        Console.WriteLine($"speakers {speakers} statuses {string.Join(", ", statuses.Select(s => $"{s.Key}={s.Value}"))}");
        Console.WriteLine("stops " + string.Join("; ", stops.Select(d => $"{d.Key}={d.Value}")));
        Console.WriteLine($"floated {floated.Count}: " + string.Join(", ", floated));
        Assert.AreEqual(182, speakers);
        // 44 more talks start and 13 more end in their script (57 fewer entries refused; obj is dead of ARC-SCRIPT-049 included); 12 of them
        // float a line, and none reaches a floated line with an action field. The counts move only when the talk set or the dialog runtime grows
        // (ARC-SCRIPT-1165: one entry more is admitted and falls to the generic greeting).
        // Since the later condition and action sets (S53 to S56, measured on 2026-11-14 at 5c02daa9e) 8 more entries are admitted: Started 99, ScriptEnded 16, one ends in effects the host rejects (script 1092, see the check below) and the generic greeting stays 1.
        Assert.AreEqual("EntryNotAdmitted=65, GenericGreeting=1, ScriptEffectsRejected=1, ScriptEnded=16, Started=99", string.Join(", ", statuses.Select(s => $"{s.Key}={s.Value}")));
        Assert.AreEqual(13, floated.Count); // one more since ARC-SCRIPT-1165: the entry now admitted floats a line
        // The one run whose effects the session refuses is script 1092 (caladon-panarrii temple): it asks the NPC to attack, which only a world with the AI host can apply (ARC-SCRIPT-1620, TI6), and this test world has none.
        CollectionAssert.AreEqual(new[] { "effects 1092" }, stops.Keys.Where(k => k.StartsWith("effects ", StringComparison.Ordinal)).ToArray(), string.Join("; ", stops.Keys));
    }
}
