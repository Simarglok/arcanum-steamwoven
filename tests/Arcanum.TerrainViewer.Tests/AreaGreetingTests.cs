using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Reputation;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-WORLD-058: the AREA viewer's greeting seam: the NPC's facts from its stored fields, its override script, the
/// reputation candidates for its origin and faction, the session's labelled rolls and dialog rules, the texts of the module.</summary>
[TestClass]
public sealed class AreaGreetingTests
{
    private static readonly ReputationCatalog Catalog = ReputationCatalog.Create([new(1001, [], [new(5, 0, 6)]), new(1002, [], [new(-5, 0, 0)])])!;

    [TestMethod]
    public void The_facts_come_from_the_npcs_stored_fields_and_the_pc()
    {
        var stats = new int[CombatFieldReader.BaseStats];
        stats[4] = 4; stats[26] = 1; stats[27] = 3;
        var facts = AreaGreeting.FactsFor(Npc(stats, socialClass: 7), new DialogPcFacts(DialogPcGender.Female, 8, false, 2), 85, true, dead: true)!;
        Assert.AreEqual(new DialogGreetingFacts(true, false, true, 7, 3, 2, DialogReactionCategory.Amiable, true, DialogGreetingStates.NpcDown), facts);
        stats[4] = 5; stats[26] = 0;
        var other = AreaGreeting.FactsFor(Npc(stats, 0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), 0, false, dead: false)!;
        Assert.AreEqual((false, true, false, DialogReactionCategory.Hatred, DialogGreetingStates.None), (other.NpcMale, other.PcMale, other.NpcDumb, other.Category, other.States));
        Assert.IsNull(AreaGreeting.FactsFor(Npc(stats, 0) with { Npc = null }, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), 50, false, false), "not an NPC");
        Assert.IsNull(AreaGreeting.FactsFor(null, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), 50, false, false));
    }

    [TestMethod]
    public void The_override_script_is_the_speakers_sap_key_31_attachment()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var row = source.Owner.LoadStrict().Objects.First();
        AreaSpeaker Speaker(params AreaSparseElement[] elements) =>
            new(0, row with { Type = 16, EffectiveFields = [new(31, AreaPresence.Value, AreaValueOrigin.Instance, null, elements, null)] }, 1, 0, 0);
        Assert.AreEqual(4021, AreaGreeting.OverrideScript(Speaker(new(9, [0, 0, 77]), new(31, [0, 0, 4021]))));
        Assert.IsNull(AreaGreeting.OverrideScript(Speaker(new AreaSparseElement(9, [0, 0, 77]))));
        Assert.IsNull(AreaGreeting.OverrideScript(Speaker(new AreaSparseElement(31, [0, 0, 0]))), "script 0 is none");
    }

    [TestMethod]
    public void Candidates_rolls_and_rules_are_the_sessions()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var created = GameSessionOwner.Create(new(ConversationSessionFixture.Clock,
            Arcanum.Scripting.GlobalFlags.OriginalGlobalFlags.NewGame(new(new(Guid.NewGuid()), new(Guid.NewGuid())), new(Guid.NewGuid())),
            new(new(area.RuntimeMap), new(0, 0), Arcanum.Simulation.Movement.NeighborDirection.XPlus), Reputations: Catalog));
        using var session = created.Owner!;
        session.AddReputation(session.Version, 1002);
        session.AddReputation(session.Version, 1001);
        var row = area.Objects.First() with
        {
            Type = 16,
            EffectiveFields = [new(80, AreaPresence.Value, AreaValueOrigin.Instance, 2, [], null), new(81, AreaPresence.Value, AreaValueOrigin.Instance, 6, [], null)],
        };
        var speaker = new AreaSpeaker(0, row, 1, 0, 0);
        var state = new AreaConversationState(session, new(area.RuntimeMap), [AreaConversationState.EntityId(row)], 50, scopes: [AreaConversationHost.Scope(row)]);
        var texts = new AreaGreetingTexts(AreaGreetingTextsStatus.Ready, GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]), new MesFile?[4], "synthetic");
        var greeting = new AreaGreeting(session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), texts, null);
        var query = new DialogReactionQuery(new(1000), new(800));
        CollectionAssert.AreEqual(new[] { 1001 }, greeting.ReputationCandidates(query, DialogReactionCategory.Love).ToArray(), "faction 6 for the positive effect");
        CollectionAssert.AreEqual(new[] { 1002 }, greeting.ReputationCandidates(query, DialogReactionCategory.Hatred).ToArray());

        var draws = session.QueryRandom().Draws;
        Assert.AreEqual(42, greeting.Roll(DialogGreetingRoll.ReputationChance, 42, 42), "equal bounds draw nothing");
        var value = greeting.Roll(DialogGreetingRoll.Line, -1, 0);
        Assert.IsTrue(value is -1 or 0);
        Assert.AreEqual(draws + 1, session.QueryRandom().Draws);
        Assert.AreSame(DialogRuleProfile.Steamwoven, greeting.Rules);
        session.SelectRules(RuleMode.Original);
        Assert.AreSame(DialogRuleProfile.Original, greeting.Rules, "read at each greeting");
        Assert.IsNull(greeting.Overrides);
        Assert.IsNull(greeting.Facts(query), "the synthetic row holds no NPC fields: no facts");
        Assert.ThrowsExactly<ArgumentException>(() => new AreaGreeting(session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0),
            texts with { Files = null }, null));
    }

    [TestMethod]
    public void The_texts_load_through_the_content_stack()
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-greeting-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = Path.Combine(root, "module");
            Write(module, "mes/gd_cls_m2m.mes", "{10000}{Hello}");
            Write(module, "rules/gamerep.mes", "{1000}{0 0 0, 5 0 0}");
            Write(module, "mes/gamereplog.mes", "{1000}{Name}");
            Write(module, "mes/game_rp_npc_m2m.mes", "{1000}{Hero}");
            var plan = new ContentMountPlan(root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module)]);
            var texts = AreaGreetingTexts.Load(plan);
            Assert.AreEqual(AreaGreetingTextsStatus.Ready, texts.Status, texts.Detail);
            Assert.AreEqual("1 generated-dialog files, 1 reputation greeting files", texts.Detail);
            Assert.IsNotNull(texts.Files!.File(GeneratedDialogFile.ClassMaleToMale));
            Assert.IsNotNull(texts.ReputationLines[0]);
            Write(module, "mes/gd_npc_f2f.mes", "{1}{broken");
            Assert.AreEqual(AreaGreetingTextsStatus.FilesFailed, AreaGreetingTexts.Load(plan).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static CombatFields Npc(int[] stats, int socialClass) =>
        new(CombatFieldOrigin.AreaObject, new(16, 1, 0, 0, 0, 0, 0, 10, 0, 0, 0, [0, 0, 0, 0, 0], 0), null, null, null, null, null,
            new(0, 0, stats, new int[12], new int[4], new int[25], 0, 0, 0, 0, 0, 0),
            new(0, 0, 0, 0, 0, 50, socialClass, Enumerable.Repeat(new CombatDamageRange(0, 0), 5).ToArray()));

    private static void Write(string root, string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.ASCII);
    }
}
