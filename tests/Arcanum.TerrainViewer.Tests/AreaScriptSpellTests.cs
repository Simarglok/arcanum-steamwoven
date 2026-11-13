using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-MAGIC-030 (MT21b, ledger SC1–SC6): the spell conditions (has body spell 14, influenced by spell 35, knows spell 48, mastered spell college 49) and give spell mastery in college (112) through the
/// use-script host and the talk host over an invented content stack and map (no original data): the hero's spell book and the NPC's spell flags answer the conditions, the mastery action writes the session's book
/// through its own request branch (a toggle of state would have refused it for the hero and for a scenery object), and the sets hold the five commands. The opt-in check counts the installed scripts the hosts' sets admit.</summary>
[TestClass]
public sealed class AreaScriptSpellTests
{
    private const byte Triggerer = 0, Attachee = 1, Number = 3;
    private const int KnowsFlag = 2370, KnowsNotFlag = 2371, BodyFlag = 2372, MasteredFlag = 2373, InfluencedFlag = 2374, PolymorphedFlag = 2375, ShrunkFlag = 2376, MirrorFlag = 2377, EyeCandyFlag = 2378, Map = 1;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A row with a use script and, when given, the stored spell flags (field 20, slot 19).
    private static AreaObjectRecord Row(int index, int type, int script, uint? spellFlags = null, int point = ScrPoint.Use)
    {
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(point, [0, 0, script])], null) };
        if (spellFlags is { } flags) fields.Add(new(19, AreaPresence.Value, AreaValueOrigin.Instance, flags, [], null));
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));
    private static Clause SetFlag(int flag) => new(ScrCatalogue.Action.SetGlobalFlag, N(flag));
    private static Clause Nothing => new(ScrCatalogue.Action.DoNothing);

    private static byte[] Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        return bytes;
    }

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-magic-030-");
        try
        {
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Write("tig.dat", AreaOwnerFixture.Dat());
            Write("modules/Syn.dat", AreaOwnerFixture.Dat());
            // 920: the triggerer knows spell 4 (rank 5 of college 0) and spell 5 (rank 1 of college 1): flags 2370 and 2371.
            Write("scr/00920knows.scr", Program(
                If(new(ScrCatalogue.Condition.KnowsSpell, (Triggerer, -1), N(4)), SetFlag(KnowsFlag), Nothing),
                If(new(ScrCatalogue.Condition.KnowsSpell, (Triggerer, -1), N(5)), SetFlag(KnowsNotFlag), Nothing),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 921: the attachee has a body spell: flag 2372.
            Write("scr/00921body.scr", Program(
                If(new(ScrCatalogue.Condition.HasBodySpell, (Attachee, -1)), SetFlag(BodyFlag), Nothing),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 922: give spell mastery of college 0 to the triggerer; 923: the triggerer mastered college 0: flag 2373.
            Write("scr/00922master.scr", Program(Do(new(ScrCatalogue.Action.GiveSpellMastery, (Triggerer, -1), N(0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00923mastered.scr", Program(
                If(new(ScrCatalogue.Condition.MasteredSpellCollege, (Triggerer, -1), N(0)), SetFlag(MasteredFlag), Nothing),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 924: give spell mastery of college 0 to the attachee (an NPC, a scenery object). 925: the attachee is under the influence of spell 7: flag 2374.
            Write("scr/00924masterattachee.scr", Program(Do(new(ScrCatalogue.Action.GiveSpellMastery, (Attachee, -1), N(0))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00925influenced.scr", Program(
                If(new(ScrCatalogue.Condition.InfluencedBySpell, (Attachee, -1), N(7)), SetFlag(InfluencedFlag), Nothing),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 926: the attachee is polymorphed, shrunk or has a mirror image: flags 2375, 2376, 2377 (ARC-SCRIPT-1734). 927: spell and script eye candy actions, then flag 2378 (the run is admitted and goes on).
            Write("scr/00926spellstate.scr", Program(
                If(new(ScrCatalogue.Condition.IsPolymorphed, (Attachee, -1)), SetFlag(PolymorphedFlag), Nothing),
                If(new(ScrCatalogue.Condition.IsShrunk, (Attachee, -1)), SetFlag(ShrunkFlag), Nothing),
                If(new(ScrCatalogue.Condition.HasMirrorImage, (Attachee, -1)), SetFlag(MirrorFlag), Nothing),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/00927eyecandy.scr", Program(
                Do(new(ScrCatalogue.Action.PlaySpellEyeCandy, N(12), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.StopScriptEyeCandy, N(3), (Attachee, -1))),
                Do(SetFlag(EyeCandyFlag)),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    // A hero who can learn: willpower 18 and level 15 take every rank of college 0 (the spells page test's shape).
    private static GameSessionOwner Hero(int ranks)
    {
        int[] stats = [8, 8, 8, 8, 8, 8, 18, 8, 0, 0];
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, stats, 15, 10, 0, null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), Map, load).Owner!;
        session.OpenHeroEditor();
        for (var rank = 0; rank < ranks; rank++) Assert.AreEqual(GameSessionDisposition.Changed, session.SpendHeroPoint(session.Version, AdvancementTrack.College, 0).Disposition, $"rank {rank + 1}");
        if (ranks > 0) Assert.AreEqual(GameSessionDisposition.Changed, session.SpendHeroPoint(session.Version, AdvancementTrack.College, 1).Disposition, "rank 1 of college 1");
        session.CloseHeroEditor();
        return session;
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, params AreaObjectRecord[] shown) =>
        new(session, Map, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    private static AreaScriptedUse Run(AreaUseScriptHost host, GameSessionOwner session, AreaObjectRecord row) => host.Run(row, session.Rules.Scripts);

    [TestMethod]
    public void Knows_spell_asks_the_heros_book_by_rank_within_the_college()
    {
        WithContent(content =>
        {
            using var session = Hero(5);
            var row = Row(1, 16, 920);
            var ran = Run(Host(session, content, row), session, row);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.IsTrue(Flag(session, KnowsFlag), "spell 4: rank 5 of college 0, five ranks known");
            Assert.IsTrue(Flag(session, KnowsNotFlag), "spell 5: rank 1 of college 1, one rank known");
        });
    }

    [TestMethod]
    public void Knows_spell_is_false_for_a_college_without_ranks_and_the_run_stops_without_a_hero()
    {
        WithContent(content =>
        {
            using var session = Hero(3);
            var row = Row(1, 16, 920);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(Host(session, content, row), session, row).Outcome);
            Assert.IsFalse(Flag(session, KnowsFlag), "spell 4 needs rank 5");
            // A session without a hero has no book to read: the object is unknown and the run stops (not a made-up answer).
            using var bare = ConversationSessionFixture.Create(Map);
            var stopped = Run(Host(bare, content, row), bare, row);
            Assert.AreEqual(ScrStop.MissingObject, stopped.Run?.Stop, stopped.Detail);
            Assert.IsFalse(Flag(bare, KnowsFlag));
        });
    }

    [TestMethod]
    public void Polymorphed_shrunk_and_mirror_image_read_one_bit_each_of_the_stored_and_written_spell_flags()
    {
        WithContent(content =>
        {
            foreach (var (flags, polymorphed, shrunk, mirror) in new (uint, bool, bool, bool)[] { (0x20000, true, false, false), (0x80000, false, true, false), (0x40000, false, false, true), (0xE0000, true, true, true), (0x3C, false, false, false), (0, false, false, false) })
            {
                using var session = ConversationSessionFixture.CreateWithHero(Map);
                var row = Row(1, 16, 926, flags);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(Host(session, content, row), session, row).Outcome, $"flags {flags:X}");
                Assert.AreEqual((polymorphed, shrunk, mirror), (Flag(session, PolymorphedFlag), Flag(session, ShrunkFlag), Flag(session, MirrorFlag)), $"stored spell flags 0x{flags:X}");
            }
            // The bit a spell component wrote on the NPC counts (the shrunk bit is the one the ObjFlag component writes for the shrink spell).
            using var written = ConversationSessionFixture.CreateWithHero(Map);
            var npc = Row(1, 16, 926, 0u);
            written.WriteObjectFlag(written.Version, AreaUseScriptHost.EntityId(npc), SessionObjectFlagField.Spell, 0x80000, true);
            Run(Host(written, content, npc), written, npc);
            Assert.AreEqual((false, true, false), (Flag(written, PolymorphedFlag), Flag(written, ShrunkFlag), Flag(written, MirrorFlag)), "a written shrunk bit");
        });
    }

    [TestMethod]
    public void The_spell_eye_candy_actions_are_admitted_by_the_use_host_and_the_run_goes_on()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(Map);
            var row = Row(1, 16, 927);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(Host(session, content, row), session, row).Outcome);
            Assert.IsTrue(Flag(session, EyeCandyFlag), "play spell eye candy (104) and stop script eye candy (124) do not refuse the script");
        });
    }

    [TestMethod]
    public void Has_body_spell_reads_the_four_body_bits_of_the_spell_flags_and_the_flags_the_session_wrote()
    {
        WithContent(content =>
        {
            foreach (var (flags, expected) in new (uint, bool)[] { (0x4, true), (0x8, true), (0x10, true), (0x20, true), (0x1, false), (0x2, false), (0x40, false), (0x3, false) })
            {
                using var session = ConversationSessionFixture.CreateWithHero(Map);
                var row = Row(1, 16, 921, flags);
                Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(Host(session, content, row), session, row).Outcome, $"flags {flags:X}");
                Assert.AreEqual(expected, Flag(session, BodyFlag), $"stored spell flags 0x{flags:X}");
            }
            // The flags a spell component wrote on the NPC count over its data: an entangle bit is no body bit; a written body bit is one.
            using var written = ConversationSessionFixture.CreateWithHero(Map);
            var npc = Row(1, 16, 921, 0u);
            var entity = AreaUseScriptHost.EntityId(npc);
            written.WriteObjectFlag(written.Version, entity, SessionObjectFlagField.Spell, 0x1000000, true);
            Run(Host(written, content, npc), written, npc);
            Assert.IsFalse(Flag(written, BodyFlag), "entangled is no body spell");
            written.WriteObjectFlag(written.Version, entity, SessionObjectFlagField.Spell, 0x10, true);
            Run(Host(written, content, npc), written, npc);
            Assert.IsTrue(Flag(written, BodyFlag), "a written body-of-fire bit");
        });
    }

    [TestMethod]
    public void Influenced_by_spell_is_false_with_no_run_and_an_object_of_no_row_is_unknown()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero(Map);
            var row = Row(1, 16, 925);
            var host = Host(session, content, row);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(host, session, row).Outcome);
            Assert.IsFalse(Flag(session, InfluencedFlag), "no spell run");
            Assert.AreEqual(false, host.IsInfluencedBy(host.Pc, 7));
            Assert.IsNull(host.IsInfluencedBy(new ScrObjectRef(Map, "oid:nobody"), 7), "no loaded row: unknown");
            Assert.IsNull(host.SpellFlags(new ScrObjectRef(Map, "oid:nobody")));
            Assert.IsNull(host.KnowsSpell(new ScrObjectRef(Map, "oid:nobody"), 0));
            Assert.IsNull(host.MasteredCollege(new ScrObjectRef(Map, "oid:nobody")));
        });
    }

    [TestMethod]
    public void Give_spell_mastery_masters_the_heros_college_once_and_only_with_five_ranks()
    {
        WithContent(content =>
        {
            using var few = Hero(3);
            var row = Row(1, 16, 922);
            var ran = Run(Host(few, content, row), few, row);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, "the run goes on whatever the book says");
            Assert.AreEqual(SpellBookRules.NoMastery, few.QueryHeroSpellBook()!.MasteredCollege, "three ranks: nothing happens");

            using var session = Hero(5);
            var host = Host(session, content, row, Row(2, 16, 923));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(host, session, row).Outcome, "not a toggle of state");
            Assert.AreEqual(0, session.QueryHeroSpellBook()!.MasteredCollege);
            // The next question reads it: mastered college 0, and only that one.
            var asker = Row(2, 16, 923);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(Host(session, content, asker), session, asker).Outcome);
            Assert.IsTrue(Flag(session, MasteredFlag));
            Assert.AreEqual(0, Host(session, content).MasteredCollege(new ScrObjectRef(Map, AreaScriptHost.PcCode)));
            // Mastery cannot be given twice (the rule keeps the first).
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Run(host, session, row).Outcome);
            Assert.AreEqual(0, session.QueryHeroSpellBook()!.MasteredCollege);
        });
    }

    [TestMethod]
    public void Give_spell_mastery_to_an_object_that_is_not_a_critter_does_nothing_and_an_unreadable_NPC_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var session = Hero(5);
            // Scenery (type 3) as the attachee: the original does nothing for an object that is not a PC or NPC, and the run is applied (not refused as a toggle of state).
            var scenery = Row(1, 3, 924);
            var ran = Run(Host(session, content, scenery), session, scenery);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(SpellBookRules.NoMastery, session.QueryHeroSpellBook()!.MasteredCollege);
            // An NPC of the shown map whose map data gives no sheet (the synthetic row has no stat block): the write is not modelled and the run is left unapplied, with the reason named.
            var npc = Row(2, 16, 924);
            var refused = Run(Host(session, content, npc), session, npc);
            Assert.AreNotEqual(AreaScriptedUseOutcome.NoTeleport, refused.Outcome);
            StringAssert.Contains(refused.Detail, "give spell mastery");
            Assert.AreEqual(SpellBookRules.NoMastery, session.QueryHeroSpellBook()!.MasteredCollege);
        });
    }

    [TestMethod]
    public void The_mastery_write_for_an_NPC_goes_through_its_loaded_advancement_and_the_rule()
    {
        // A follower like the advancement tests': human, level 1, five ranks of college 12 (fire), no mastery; the stat block is invented.
        static CombatFields Npc(int fireRank)
        {
            var stats = new int[28];
            for (var i = 0; i < 8; i++) stats[i] = 8;
            stats[17] = 1; stats[25] = 20;
            var spellTech = new int[25];
            spellTech[12] = fireRank; spellTech[16] = -1;
            return new(CombatFieldOrigin.AreaObject, new CombatObjectFields(16, 17102, 0, 0, 0, 0, 0, 1, 2, 0, 4, [0, 0, 0, 0, 0], 0),
                null, null, null, null, null,
                new CombatCritterFields(0, 0, stats, new int[12], new int[4], spellTech, 0, 0, 0, 1, 0, 62, []), null);
        }
        using var session = ConversationSessionFixture.CreateWithHero(Map);
        var few = AreaAdvancement.Npc("oid:" + new string('a', 64), Npc(4), _ => null)!.Value.Loaded;
        Assert.IsTrue(AreaScriptSpells.GiveMastery(session, few, 12), "refused by the rule: the run goes on");
        Assert.IsNull(session.QueryNpcSpellBook(few.Entity), "nothing was recorded");
        var full = AreaAdvancement.Npc("oid:" + new string('b', 64), Npc(5), _ => null)!.Value.Loaded;
        Assert.IsTrue(AreaScriptSpells.GiveMastery(session, full, 12));
        Assert.AreEqual(12, session.QueryNpcSpellBook(full.Entity)!.MasteredCollege);
        Assert.IsTrue(AreaScriptSpells.GiveMastery(session, full, 3), "once only");
        Assert.AreEqual(12, session.QueryNpcSpellBook(full.Entity)!.MasteredCollege);
        Assert.IsTrue(AreaScriptSpells.GiveMastery(session, full, 99), "a college outside 0..15 changes nothing");
        Assert.IsTrue(AreaScriptSpells.GiveMastery(session, null, 0), "the hero has no ranks: nothing");
        Assert.AreEqual(SpellBookRules.NoMastery, session.QueryHeroSpellBook()!.MasteredCollege);
    }

    [TestMethod]
    public void The_use_heartbeat_dying_and_talk_sets_hold_the_five_spell_commands_and_the_hosts_answer_them()
    {
        foreach (var set in new[] { AreaUseScriptHost.Admission, AreaUseScriptHost.PlacedAdmission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission,
                     AreaScriptHost.TalkAdmission, AreaScriptHost.SkillTalkAdmission })
        {
            foreach (var condition in ScrAdmissionSet.SpellQueryConditions) Assert.IsTrue(set.Conditions.Contains(condition), $"{set.Id}: condition {condition}");
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.GiveSpellMastery), $"{set.Id}: action 112");
            Assert.IsTrue(set.IsSubsetOfBesidesStores(ScrAdmissionSet.S53), set.Id);
        }
        // A dialog host without a query host cannot answer the questions: its own sets stay without them.
        Assert.IsFalse(AreaScriptHost.Admission.Conditions.Overlaps(ScrAdmissionSet.SpellQueryConditions));
        Assert.IsFalse(AreaScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.GiveSpellMastery));
        Assert.IsTrue(typeof(IScrSpellQueries).IsAssignableFrom(typeof(AreaUseScriptHost)) && typeof(IScrSpellQueries).IsAssignableFrom(typeof(AreaScriptHost)));
    }

    // The four conditions of S31 (ledger SC9 counts the scripts that use one of them or give spell mastery); ARC-SCRIPT-1734's three spell-state conditions are counted by their own check.
    private static readonly HashSet<int> OriginalSpellConditions = [ScrCatalogue.Condition.HasBodySpell, ScrCatalogue.Condition.InfluencedBySpell, ScrCatalogue.Condition.KnowsSpell, ScrCatalogue.Condition.MasteredSpellCollege];

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_the_spell_commands_are_admitted_by_the_hosts_sets()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in spell-command check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission), ("heartbeat", AreaUseScriptHost.HeartbeatAdmission),
            ("talk", AreaScriptHost.SkillTalkAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var withSpell = 0;
        var blockers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (var number = 0; number <= ScrCatalogue.MaxScriptNumber; number++)
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            var uses = program.Lines.Any(l => OriginalSpellConditions.Contains(l.Condition.Type) || l.OnTrue.Type == ScrCatalogue.Action.GiveSpellMastery || l.OnFalse.Type == ScrCatalogue.Action.GiveSpellMastery);
            if (!uses) continue;
            withSpell++;
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
            // The first command of the broadest set's refusal: what still keeps a script with a spell command from running.
            if (ScrAdmission.Admit(program, AreaScriptHost.SkillTalkAdmission).Refusal is { } refusal)
            {
                var key = $"{refusal.Code} {refusal.Type}";
                blockers[key] = blockers.GetValueOrDefault(key) + 1;
            }
        }
        Console.WriteLine($"installed scripts with a spell command {withSpell}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        Console.WriteLine("first refusal under the talk set: " + string.Join(", ", blockers.Select(b => $"{b.Key} x{b.Value}")));
        // Ledger SC9: 46 installed scripts use one of the five commands; the sets admit few of them whole. ARC-SCRIPT-1481: the placed set admits one more (the use set gained the heal, destroy and vitals commands).
        Assert.AreEqual((46, 1, 4, 1, 1), (withSpell, counts["use"], counts["placed"], counts["heartbeat"], counts["talk"]));
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_with_the_spell_state_commands_are_admitted_by_the_hosts_sets()
    {
        // ARC-SCRIPT-1734: obj is polymorphed (12), obj is shrunk (13), obj has mirror image (16), play spell eye candy (104) and stop script eye candy (124).
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in spell-state check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        int[] conditions = [ScrCatalogue.Condition.IsPolymorphed, ScrCatalogue.Condition.IsShrunk, ScrCatalogue.Condition.HasMirrorImage];
        int[] actions = [ScrCatalogue.Action.PlaySpellEyeCandy, ScrCatalogue.Action.StopScriptEyeCandy];
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission), ("heartbeat", AreaUseScriptHost.HeartbeatAdmission),
            ("dying", AreaUseScriptHost.DyingAdmission), ("talk", AreaScriptHost.SkillTalkAdmission), ("combat", AreaUseScriptHost.CombatPointAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var withState = 0;
        for (var number = 0; number <= ScrCatalogue.MaxScriptNumber; number++)
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.Any(l => conditions.Contains(l.Condition.Type) || actions.Contains(l.OnTrue.Type) || actions.Contains(l.OnFalse.Type))) continue;
            withState++;
            foreach (var (name, set) in sets) if (ScrAdmission.Admit(program, set).IsAdmitted) counts[name]++;
        }
        Console.WriteLine($"installed scripts with a spell-state command {withState}; admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")));
        // 60 installed scripts use one of the five commands; the main map's 21 heartbeats that stop a script eye candy stay refused by other commands the narrow heartbeat set never admitted (assign object 15, get reaction 29, float line 23).
        Assert.AreEqual((60, 21, 27, 1, 29, 0, 27), (withState, counts["use"], counts["placed"], counts["heartbeat"], counts["dying"], counts["talk"], counts["combat"]));
    }
}
