using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Scripting.Dialog;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-1551 (FR-MAGIC-007): the readers of an NPC's raw stored stats that took them from its map row see what a TraitIdx spell wrote into the NPC (<see cref="SessionCritterIndexedWrites"/>, laid over by
/// <see cref="AreaWrittenCritterFields"/>): the greeting facts, the follow candidate's level and alignment, the AI host's critter data and the hover window. The fixture NPC is the AI tests' (stats: level 1,
/// alignment 0, gender 1 male, race 0, intelligence 20); invented rows, no original data.
/// </summary>
[TestClass]
public sealed class AreaTraitIdxReadersTests
{
    private static void Write(AreaAiHostRebindTests.Fixture f, int stat, int value) =>
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WriteCritterStat(f.Session.Version, f.Session.QueryActor().Map, f.A, stat, value).Disposition);

    [TestMethod]
    public void The_greeting_facts_see_a_written_gender_race_and_intelligence()
    {
        using var f = AreaAiHostRebindTests.Open();
        var row = f.RowA;
        var speaker = new AreaSpeaker(0, row, 1, 0, 0);
        var state = new AreaConversationState(f.Session, new(1), [AreaConversationState.EntityId(row)], 50);
        var texts = new AreaGreetingTexts(AreaGreetingTextsStatus.Ready, GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]), new MesFile?[4], "synthetic");
        var greeting = new AreaGreeting(f.Session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), texts, null);
        var query = new DialogReactionQuery(new(1000), new(800));
        var stored = greeting.Facts(query)!;
        Assert.AreEqual((true, false, 0), (stored.NpcMale, stored.NpcDumb, stored.NpcRace), "stored: male, not dumb, race 0");
        Write(f, 26, 0);
        Write(f, 27, 3);
        Write(f, 4, 3);
        var written = greeting.Facts(query)!;
        Assert.AreEqual((false, true, 3), (written.NpcMale, written.NpcDumb, written.NpcRace), "the written gender, intelligence (dumb at 4 or less) and race");
    }

    [TestMethod]
    public void The_follow_candidates_level_and_alignment_see_a_written_stat()
    {
        using var f = AreaAiHostRebindTests.Open();
        var table = AiParameterTable.Create(MesFile.Parse(Encoding.ASCII.GetBytes("{0}{" + string.Join(' ', Enumerable.Repeat(0, 17)) + "}")).File!, null);
        var data = new AreaCompanionData(table, _ => null);
        var speaker = new AreaSpeaker(0, f.RowA, 1, 0, 0);
        var stored = data.Facts(speaker, out var refusal, f.Session)!;
        Assert.IsNull(refusal);
        Assert.AreEqual((1, 0), (stored.Level, stored.Alignment));
        Write(f, 17, 9);
        Write(f, 19, -40);
        Assert.AreEqual((9, -40), (data.Facts(speaker, out _, f.Session)!.Level, data.Facts(speaker, out _, f.Session)!.Alignment), "the written level and alignment over the stored ones");
        var without = data.Facts(speaker, out _)!;
        Assert.AreEqual((1, 0), (without.Level, without.Alignment), "no session: the stored stats");
    }

    [TestMethod]
    public void The_ai_hosts_critter_data_sees_a_written_level_and_alignment()
    {
        using var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f)]));
        var stored = host.Find(f.A)!;
        Assert.AreEqual((1, 0), (stored.Level, stored.Alignment));
        Write(f, 17, 9);
        Write(f, 19, -40);
        var written = host.Find(f.A)!;
        Assert.AreEqual((9, -40), (written.Level, written.Alignment), "the hostility facts follow the written stats");
    }

    [TestMethod]
    public void The_hover_windows_level_alignment_and_race_see_a_written_stat()
    {
        using var f = AreaAiHostRebindTests.Open();
        Assert.AreEqual((1, 0, 0), AreaHoverWindow.CritterStats(AreaWrittenCritterFields.Read(f.Session, f.RowA)!, null));
        Write(f, 17, 9);
        Write(f, 19, -40);
        Write(f, 27, 3);
        var fields = AreaWrittenCritterFields.Read(f.Session, f.RowA)!;
        Assert.AreEqual((9, -40, 3), AreaHoverWindow.CritterStats(fields, null), "the written stats over the stored ones");
        Assert.AreEqual(12, AreaHoverWindow.CritterStats(fields, 12).Level, "a follower's saved level still wins");
        Assert.AreEqual((1, 0, 0), AreaHoverWindow.CritterStats(AreaWrittenCritterFields.Read(null, f.RowA)!, null), "no session: the stored stats");
    }
}
