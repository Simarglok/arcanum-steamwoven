using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>Opt-in ARC-UI-043 check (DAT_ROOT = the game folder): the voice identities of the NPC lines the interiors'
/// conversations show (the first choosable reply taken each turn), and whether the voiced ones resolve to installed files for
/// a male and a female PC (counts only, no original text or audio).</summary>
[TestClass]
public sealed class AreaDialogVoiceOriginalTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod, TestCategory("RealData")]
    public void The_lines_of_the_interiors_conversations_find_their_voices()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required for the opt-in dialogue voice check.");
        using var stack = ContentStack.Open(ContentMountPlan.Discover(root).Plan!).Stack!;
        var catalog = WorldCatalog.Open(stack);
        var sounds = SoundCatalog.Open(stack).Catalog!;
        using var scripts = ConversationSessionFixture.Scripts(root);
        int lines = 0, voiced = 0, found = 0, female = 0;
        var maps = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var map in catalog.Maps.Where(m => m.Name != "arcanum1-024-fixed"))
        {
            AreaSnapshot snapshot;
            using (var owner = AreaMapPlans.Create(stack, catalog, map.Name).Plan!.Open()) snapshot = owner.LoadStrict();
            if (AreaConversationHost.FindSpeakers(snapshot).Count == 0 || snapshot.RuntimeMap == 0) continue;
            using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
            var host = new AreaConversationHost(snapshot, scripts, session);
            foreach (var speaker in host.Speakers)
            {
                var start = host.Begin(speaker);
                if (start.Conversation is not { } conversation) continue;
                Assert.IsNotNull(start.Document, "a started conversation carries its DLG");
                var turn = -1;
                while (conversation.Turn != turn && conversation.Turn < 64)
                {
                    turn = conversation.Turn;
                    if (conversation.NpcLine is { } line)
                    {
                        lines++;
                        var identity = start.VoiceIdentity(line);
                        if (identity != -1)
                        {
                            voiced++;
                            if (sounds.ResolveSpeech(identity, SpeechGender.Male).Status == SpeechStatus.Found) { found++; maps[map.Name] = maps.GetValueOrDefault(map.Name) + 1; }
                            if (sounds.ResolveSpeech(identity, SpeechGender.Female).Status == SpeechStatus.Found) female++;
                        }
                    }
                    // An insult (ARC-WORLD-043) is a deliberate choice the walk does not make.
                    if (conversation.Phase != DialogConversationPhase.AwaitingSelection
                        || conversation.Options.FirstOrDefault(o => o.Block is null && o.Marker is not { Letter: 'i' }) is not { } option) break;
                    conversation.Select(option.Token);
                }
            }
        }
        TestContext.WriteLine($"lines {lines} voiced {voiced} found {found} femaleFound {female} maps {string.Join(", ", maps.Select(m => $"{m.Key}={m.Value}"))}");
        // Moves only when the SCR admission set or the dialog runtime grows (with AreaMapConversationOriginalTests): 124 lines shown
        // (57 before ARC-WORLD-043 admitted ME: the two conversations that stopped at it walk on; 155 since ARC-WORLD-058 says the
        // g: greetings, whose generated lines have no voice), 27 with a voice identity, every one of them installed (the Caladon
        // underground and the Derian Ka temple); 175 lines shown since ARC-SCRIPT-1165 admitted the story and time commands (20 more lines, no new voice).
        Assert.AreEqual((175, 27, 27), (lines, voiced, found));
        Assert.AreEqual(found, female, "a female PC hears every voice a male PC hears (female falls back to male)");
    }
}
