using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
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
        // (then this census is updated with them): 131 entries stop at an SCR action, condition, goto or value kind not admitted yet, and
        // 35 of the 50 started conversations stop at a generated NPC line, which is not loaded yet.
        Assert.AreEqual((34, 182), (maps, speakers));
        Assert.AreEqual("EntryNotAdmitted=131, ScriptEnded=1, Started=50", string.Join(", ", statuses.Select(s => $"{s.Key}={s.Value}")));
        // ARC-WORLD-043: the ME condition is admitted, so two conversations that stopped at it offer replies (one of them ends);
        // the walk takes no insult, and with it the counts are the same.
        Assert.AreEqual((35, 6), (stops.GetValueOrDefault("frontier UnsupportedGeneratedText"), stops.GetValueOrDefault("frontier UnsupportedOperation")));
        Assert.AreEqual((9, 7), (offered, ended));
        CollectionAssert.AreEqual(new[] { "scienticians underground lev 2" }, unlisted);
    }
}
