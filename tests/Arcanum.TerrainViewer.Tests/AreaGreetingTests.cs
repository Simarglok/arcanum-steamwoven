using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Reputation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    public void An_npc_listeners_facts_are_its_own_gender_race_and_intelligence_not_the_pcs()
    {
        // ARC-AI-2114 (0x418780): the line's listener object's stat 26 picks the file, so a follower that is the actor of a guard witness's shout speaks to its own gender.
        var stats = new int[CombatFieldReader.BaseStats];
        stats[4] = 4; stats[26] = 1; stats[27] = 3;
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Male, 4, true, 3), AreaGreeting.ListenerFacts(Npc(stats, 0)));
        stats[4] = 9; stats[26] = 0; stats[27] = 5;
        var female = AreaGreeting.ListenerFacts(Npc(stats, 0))!;
        Assert.AreEqual(new DialogPcFacts(DialogPcGender.Female, 9, false, 5), female);
        // The facts of the line a male NPC says to that listener: the PC-side gender is the listener's, not the hero's.
        var facts = AreaGreeting.FactsFor(Npc(new int[CombatFieldReader.BaseStats].Select((v, i) => i == 26 ? 1 : v).ToArray(), 0), female, 50, false, dead: false)!;
        Assert.AreEqual((true, false, 5), (facts.NpcMale, facts.PcMale, facts.PcRace));
        Assert.IsNull(AreaGreeting.ListenerFacts(null));
        var pc = new DialogPcFacts(DialogPcGender.Male, 8, false, 0);
        var fields = Npc(stats, 0);
        Assert.AreEqual(female, AreaConversationHost.ListenerFactsOf("npc:1", pc, entity => entity == "npc:1" ? fields : null), "an NPC listener speaks for itself");
        Assert.AreEqual(pc, AreaConversationHost.ListenerFactsOf(Arcanum.Simulation.Ai.AiSchedulerRules.Pc, pc, _ => fields), "the hero listener is the PC's facts");
        Assert.AreEqual(pc, AreaConversationHost.ListenerFactsOf(null, pc, _ => fields), "no listener: the PC's");
        Assert.AreEqual(pc, AreaConversationHost.ListenerFactsOf("oid:critter:9", pc, _ => null), "an object the host holds no fields of (a created critter that is no speaker) falls back to the PC's");
        Assert.IsNull(AreaGreeting.ListenerFacts(Npc(stats, 0) with { Critter = null }), "no critter stats: the PC's facts stay");
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
    public void An_override_line_speaks_with_its_records_voice_and_generated_lines_are_silent()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        using var session = ConversationSessionFixture.Create(area.RuntimeMap);
        var row = area.Objects.First() with
        {
            Type = 16,
            EffectiveFields = [new(31, AreaPresence.Value, AreaValueOrigin.Instance, null, [new AreaSparseElement(31, [0, 0, 0x18007])], null)],
        };
        var speaker = new AreaSpeaker(0, row, 1, 0, 0);
        var state = new AreaConversationState(session, new(area.RuntimeMap), [AreaConversationState.EntityId(row)], 50);
        var texts = new AreaGreetingTexts(AreaGreetingTextsStatus.Ready, GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]), new MesFile?[4], "synthetic");
        var overrides = Arcanum.Data.DlgReader.Read(Encoding.ASCII.GetBytes("{10009}{Hi}{Hi}{}{ 42}{}{}"), new() { Mode = Arcanum.Data.DlgReadMode.Original });
        var greeting = new AreaGreeting(session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), texts, overrides);
        Assert.AreEqual(0x18007, greeting.OverrideScriptNumber);
        var document = Arcanum.Data.DlgReader.Read(Encoding.ASCII.GetBytes("{10}{g:}{}{}{ 5}{}{}"), new() { Mode = Arcanum.Data.DlgReadMode.Original });
        var start = new AreaConversationStart(AreaConversationStartStatus.Started, null, null, "", Document: document, DialogScript: 3) { Greeting = greeting };
        Assert.AreEqual((7 << 16) | 42, start.VoiceIdentity(new DialogNpcLine(new(10), "Hi", new(DialogLineOriginKind.Override, 10009))), "the override record's own voice");
        Assert.AreEqual(-1, start.VoiceIdentity(new DialogNpcLine(new(10), "Hi", new(DialogLineOriginKind.Override, 10010))), "no such override record");
        foreach (var kind in new[] { DialogLineOriginKind.Generated, DialogLineOriginKind.Reputation, DialogLineOriginKind.MissingLine, DialogLineOriginKind.Empty, DialogLineOriginKind.Rumor })
            Assert.AreEqual(-1, start.VoiceIdentity(new DialogNpcLine(new(10), "x", new(kind, 10000))), kind.ToString());
        Assert.AreEqual((3 << 16) | 5, start.VoiceIdentity(new DialogNpcLine(new(10), "x")), "a record's own line keeps its voice");
        Assert.AreEqual(-1, (start with { Greeting = null }).VoiceIdentity(new DialogNpcLine(new(10), "Hi", new(DialogLineOriginKind.Override, 10009))));
    }

    // ARC-AI-1640 (VB4-1, VB4-2, VB4-4): the follower comment lines and the order lines carry the voice of their override record, as the builders write the voice id beside the text.
    [TestMethod]
    public void A_floated_comment_line_carries_the_voice_of_its_override_record_and_generated_lines_none()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        using var session = ConversationSessionFixture.Create(area.RuntimeMap);
        var row = area.Objects.First() with
        {
            Type = 16,
            EffectiveFields = [new(31, AreaPresence.Value, AreaValueOrigin.Instance, null, [new AreaSparseElement(31, [0, 0, 0x18007])], null)],
        };
        var speaker = new AreaSpeaker(0, row, 1, 0, 0);
        var state = new AreaConversationState(session, new(area.RuntimeMap), [AreaConversationState.EntityId(row)], 50);
        var texts = new AreaGreetingTexts(AreaGreetingTextsStatus.Ready, GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]), new MesFile?[4], "synthetic");
        // The taunt record 12023 (kind 0), the near-death record 12055 (range 5500, 11999 + 55) with an empty and a text-only voice field.
        var overrides = Arcanum.Data.DlgReader.Read(Encoding.ASCII.GetBytes("{12023}{Ha}{Ha}{}{ 42}{}{}{12055}{Ow}{Ow}{}{}{}{}"), new() { Mode = Arcanum.Data.DlgReadMode.Original });
        var greeting = new AreaGreeting(session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), texts, overrides);
        Assert.AreEqual((7 << 16) | 42, greeting.OverrideVoice(new(DialogLineOriginKind.Override, 12023)), "the taunt override record's voice");
        Assert.AreEqual(-1, greeting.OverrideVoice(new(DialogLineOriginKind.Override, 12055)), "an override record with an empty voice field: none");
        Assert.AreEqual(-1, greeting.OverrideVoice(new(DialogLineOriginKind.Override, 12024)), "no such override record");
        foreach (var kind in new[] { DialogLineOriginKind.Generated, DialogLineOriginKind.Reputation, DialogLineOriginKind.MissingLine, DialogLineOriginKind.Empty, DialogLineOriginKind.Rumor })
            Assert.AreEqual(-1, greeting.OverrideVoice(new(kind, 12023)), kind.ToString());
        Assert.AreEqual(-1, greeting.OverrideVoice(null), "no origin: none");
        var plain = new AreaGreeting(session, speaker, state, new DialogPcFacts(DialogPcGender.Male, 8, false, 0), texts, null);
        Assert.AreEqual(-1, plain.OverrideVoice(new(DialogLineOriginKind.Override, 12023)), "no override dialog: none");
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
            Write(module, "mes/game_rd_npc_m2f.mes", "{20001}{Tale}");
            var plan = new ContentMountPlan(root, "Mod", [new(ContentSourceRole.ModuleDirectory, 0, module)]);
            var texts = AreaGreetingTexts.Load(plan);
            Assert.AreEqual(AreaGreetingTextsStatus.Ready, texts.Status, texts.Detail);
            Assert.AreEqual("1 generated-dialog files, 1 reputation greeting files, 1 rumor files", texts.Detail);
            Assert.IsNull(texts.RumorLines![0], "the male-to-male rumor file is not in this module");
            Assert.IsNotNull(texts.RumorLines[1]?.Find(20001), "ARC-SCRIPT-1304: the module's rumor files are loaded in the original's handle order");
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
