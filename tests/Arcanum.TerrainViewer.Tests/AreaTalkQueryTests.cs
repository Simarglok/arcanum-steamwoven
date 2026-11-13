using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-050: a talk's dialog script with the shown map's use host answering its queries (<see cref="AreaScriptHost.QueryHost"/>):
/// float line (23) floats its DLG line over the speaker, get area (84) reads the shown map's map-list area, and the set widens only while a use
/// host of the talk's map answers. Invented content stack and map; no original data.</summary>
[TestClass]
public sealed class AreaTalkQueryTests
{
    private const byte Triggerer = 0, Attachee = 1, Local = 2, Number = 3, GlobalFlag = 4;
    private const int Map = 1, AreaFlag = 100;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int dialog)
    {
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, dialog])], null) };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));

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
        var root = Directory.CreateTempSubdirectory("arc-script-050-");
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
            // 790: float line 1 over the attachee, get area of the attachee into local 0, set global flag 100 when it is 7, skip default.
            Write("scr/00790float.scr", Program(
                Do(new(ScrCatalogue.Action.FloatLine, N(1), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.GetArea, (Attachee, -1), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(7)), new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag)), new(ScrCatalogue.Action.DoNothing)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // Line 1's action field holds spaces only; line 2 acts.
            Write("dlg/00790float.dlg", Encoding.Latin1.GetBytes("{1}{Not now, @pcname@.}{Not now, lady.}{}{}{}{  }\n{2}{Acted.}{}{}{}{}{gf 5 1}\n"));
            // 792: get reaction of the attachee toward the triggerer into local 0, set global flag 100 when it is 62 (not the original's 50 for a non-NPC pair), skip default (ARC-DEV-178, B19).
            Write("scr/00792reaction.scr", Program(
                Do(new(ScrCatalogue.Action.GetReaction, (Attachee, -1), (Triggerer, -1), (Local, 0))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(62)), new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag)), new(ScrCatalogue.Action.DoNothing)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("dlg/00792reaction.dlg", Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}\n"));
            // 793 (ARC-SCRIPT-1563): sets global flag 100, damages the PC for 40 of type 1 (poison) and the attachee for 7 of type 0 unresistably, heals the attachee for 5, skip default; a poison talk like the installed 1059.
            Write("scr/00793damage.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.Damage, (Triggerer, -1), N(40), N(1))),
                Do(new(ScrCatalogue.Action.DamageUnresistable, (Attachee, -1), N(7), N(0))),
                Do(new(ScrCatalogue.Action.HealHitPoints, (Attachee, -1), N(5))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("dlg/00793damage.dlg", Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}\n"));
            // 794 (ARC-SCRIPT-1620, TI6): sets global flag 100, has the PC attack its attachee (the shape of the installed 1000's lines) and then the attachee attack the PC, skip default.
            Write("scr/00794attack.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.Attack, (Attachee, -1), (Triggerer, -1))),
                Do(new(ScrCatalogue.Action.Attack, (Triggerer, -1), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("dlg/00794attack.dlg", Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}\n"));
            // 791: sets global flag 100, then floats line 2 of 790's DLG shape (an action field the viewer does not run).
            Write("scr/00791acting.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.FloatLine, N(2), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("dlg/00791acting.dlg", Encoding.Latin1.GetBytes("{1}{Plain.}{}{}{}{}{}\n{2}{Acted.}{}{}{}{}{gf 5 1}\n"));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed record Talk(GameSessionOwner Session, AreaSpeaker Speaker, AreaScriptHost Host) : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public ScrRunResult Run() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Host.Object(Speaker), Host.Pc, null), Host,
            new() { Rules = Session.Rules.Scripts, Admission = Host.RunAdmission });
        public bool Flag => Host.GlobalFlag(AreaFlag);
    }

    // The PC stands next to the NPC (row cell (11, 20)); the shown map's map-list row has the given area.
    private static Talk Create(ScriptContent content, int dialog, (int Area, bool StartMap)? mapArea, int useMap = Map, Func<CellCoord, int?>? startMapAreaAt = null, int reaction = 50,
        Func<string, int, int, bool, string>? damage = null, Func<string, int, string>? heal = null, Func<string, string, string>? attack = null)
    {
        var session = ConversationSessionFixture.Create(Map);
        var row = Row(1, dialog);
        var speaker = new AreaSpeaker(0, row, dialog, row.WorldX!.Value, row.WorldY!.Value);
        var map = new SessionMapId(Map);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(row)], reaction);
        var use = new AreaUseScriptHost(session, useMap, [row], [], content,
            new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null, MapArea: () => mapArea,
                StartMapAreaAt: startMapAreaAt, ScriptDamage: damage, ScriptHeal: heal, ScriptAttack: attack));
        var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = () => use };
        return new(session, speaker, host);
    }

    [TestMethod]
    public void A_float_line_floats_the_speakers_line_and_get_area_reads_the_shown_maps_area()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 790, (7, false));
            Assert.AreSame(AreaScriptHost.TalkAdmission, talk.Host.RunAdmission, "no hero: the talk set without get skill");
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            CollectionAssert.AreEqual(new[] { new ScrFloatLine(790, 1, talk.Host.Object(talk.Speaker), talk.Host.Pc) }, run.Effects.Floats.ToArray());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsTrue(talk.Flag, "get area read 7");
            // The diagnostic profile: a male PC named [PC]; the separators-only action field runs nothing.
            CollectionAssert.AreEqual(new[] { new AreaScriptFloat(talk.Host.Object(talk.Speaker), "Not now, [PC].") }, talk.Host.TakeFloats().ToArray());
            Assert.AreEqual(0, talk.Host.TakeFloats().Count);
        });
    }

    [TestMethod]
    public void Get_area_is_zero_off_the_start_map_and_stops_the_talk_on_it()
    {
        WithContent(content =>
        {
            using (var plain = Create(content, 790, (0, false)))
            {
                Assert.IsTrue(plain.Host.Apply(plain.Run().Effects));
                Assert.IsFalse(plain.Flag);
            }
            // The start map with area 0 and a world that cannot place the object (ARC-DEV-178: no StartMapAreaAt, or an answer of null), or a world
            // without the map list's row (outside map mode), ends the dispatch at get area with nothing applied (the float line before it included).
            foreach (var mapArea in new (int, bool)?[] { (0, true), null })
            {
                using var stopped = Create(content, 790, mapArea);
                var run = stopped.Run();
                Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.NotModelled), (run.Outcome, run.Stop), mapArea?.ToString() ?? "no map list");
                Assert.IsTrue(run.Effects.IsEmpty);
            }
        });
    }

    [TestMethod]
    public void Get_area_on_the_start_map_places_the_object_by_its_location()
    {
        WithContent(content =>
        {
            // ARC-DEV-178 (B19, GA3): the speaker stands on its row's cell (11, 20), the area the world gives for it is 7.
            var asked = new List<CellCoord>();
            using (var town = Create(content, 790, (0, true), startMapAreaAt: cell => { asked.Add(cell); return 7; }))
            {
                var run = town.Run();
                Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
                Assert.IsTrue(town.Host.Apply(run.Effects));
                Assert.IsTrue(town.Flag, "get area read 7");
                CollectionAssert.AreEqual(new[] { new CellCoord(11, 20) }, asked);
            }
            // The map-list area wins when it is set, and off the start map the answer is 0, whatever the world says.
            foreach (var mapArea in new (int, bool)[] { (7, true), (0, false) })
            {
                asked.Clear();
                using var plain = Create(content, 790, mapArea, startMapAreaAt: cell => { asked.Add(cell); return 5; });
                Assert.IsTrue(plain.Host.Apply(plain.Run().Effects));
                Assert.AreEqual(mapArea.Item1 == 7, plain.Flag, mapArea.ToString());
                Assert.AreEqual(0, asked.Count, "the location is not asked");
            }
            // A town the world cannot place (null) is the old stop.
            using var unknown = Create(content, 790, (0, true), startMapAreaAt: _ => null);
            Assert.AreEqual(ScrStop.NotModelled, unknown.Run().Stop);
        });
    }

    [TestMethod]
    public void Get_reaction_reads_the_speakers_reaction_toward_the_pc_and_stops_for_any_other_pair()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 792, (7, false), reaction: 62);
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsTrue(talk.Flag, "the speaker's reaction is the state's 62");
            Assert.AreEqual(62, talk.Host.ReactionOf(talk.Host.Object(talk.Speaker), talk.Host.Pc));
            Assert.IsNull(talk.Host.ReactionOf(talk.Host.Pc, talk.Host.Pc), "the PC is no speaker");
            Assert.IsNull(talk.Host.ReactionOf(talk.Host.Object(talk.Speaker), talk.Host.Object(talk.Speaker)), "the second object is not the PC");
            Assert.IsNull(talk.Host.ReactionOf(new ScrObjectRef(Map + 1, "npc"), talk.Host.Pc), "an object of another map");
            // Without the query host the set has no get reaction and the talk is refused at it.
            talk.Host.QueryHost = null;
            Assert.AreEqual((ScrOutcome.Refused, ScrCatalogue.Action.GetReaction), (talk.Run().Outcome, talk.Run().Refusal!.Type));
        });
    }

    [TestMethod]
    public void A_floated_line_with_an_action_field_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 791, (7, false));
            var run = talk.Run();
            Assert.AreEqual(1, run.Effects.Floats.Count);
            Assert.IsFalse(talk.Host.Apply(run.Effects));
            Assert.IsFalse(talk.Flag, "nothing applied");
            Assert.AreEqual(0, talk.Host.TakeFloats().Count);
        });
    }

    /// <summary>ARC-SCRIPT-1563 (GT194–GT196): a talk's damage and heal actions are applied by the dialog host through the use host's session path, in action order, the PC by its scheduler id.</summary>
    [TestMethod]
    public void A_talks_damage_and_heal_are_applied_through_the_use_hosts_session_path()
    {
        WithContent(content =>
        {
            var calls = new List<string>();
            using var talk = Create(content, 793, (7, false),
                damage: (victim, amount, type, unresistable) => { calls.Add($"damage {victim} {amount} {type} {unresistable}"); return "damaged"; },
                heal: (victim, amount) => { calls.Add($"heal {victim} {amount}"); return "healed"; });
            var npc = talk.Host.Object(talk.Speaker).Code;
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            Assert.AreEqual(3, run.Effects.ObjectStates.Count);
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            CollectionAssert.AreEqual(new[] { $"damage {Arcanum.Simulation.Ai.AiSchedulerRules.Pc} 40 1 False", $"damage {npc} 7 0 True", $"heal {npc} 5" }, calls);
            Assert.IsTrue(talk.Flag, "the run's other effects apply");
            Assert.AreEqual("damaged", talk.Host.LastDamage);
            Assert.AreEqual("healed", talk.Host.LastHeal);
        });
    }

    /// <summary>ARC-SCRIPT-1620 (TI6): a talk's attack actions are applied by the dialog host through the use host's AI path, in action order, the PC by its scheduler id; a world with no AI host refuses the run whole.</summary>
    [TestMethod]
    public void A_talks_attack_is_applied_through_the_use_hosts_ai_path_and_a_world_without_it_refuses_the_run_whole()
    {
        WithContent(content =>
        {
            var calls = new List<string>();
            using var talk = Create(content, 794, (7, false), attack: (attacker, victim) => { calls.Add($"{attacker}>{victim}"); return "None"; });
            var npc = talk.Host.Object(talk.Speaker).Code;
            var pc = Arcanum.Simulation.Ai.AiSchedulerRules.Pc;
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            Assert.AreEqual(2, run.Effects.ObjectStates.Count);
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            CollectionAssert.AreEqual(new[] { $"{pc}>{npc}", $"{npc}>{pc}" }, calls);
            Assert.IsTrue(talk.Flag, "the run's other effects apply");
            Assert.AreEqual("None", talk.Host.LastAttack);
            using var noWorld = Create(content, 794, (7, false));
            var refused = noWorld.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, refused.Outcome, refused.Refusal?.ToString());
            Assert.IsFalse(noWorld.Host.Apply(refused.Effects));
            Assert.IsFalse(noWorld.Flag, "nothing applied");
        });
    }

    /// <summary>ARC-SCRIPT-1563: a world that cannot damage refuses the run whole (the flag before the damage action is not applied), and without the shown map's use host the talk set holds no damage action, so the run is refused at it.</summary>
    [TestMethod]
    public void A_talks_damage_the_world_cannot_apply_refuses_the_run_whole()
    {
        WithContent(content =>
        {
            using var noWorld = Create(content, 793, (7, false));
            var run = noWorld.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsFalse(noWorld.Host.Apply(run.Effects));
            Assert.IsFalse(noWorld.Flag, "nothing applied");
            Assert.IsNull(noWorld.Host.LastDamage);
            // Damage with no heal in the world: the damage runs, the heal request alone refuses.
            using var noHeal = Create(content, 793, (7, false), damage: (_, _, _, _) => "damaged");
            Assert.IsFalse(noHeal.Host.Apply(noHeal.Run().Effects));
            Assert.IsFalse(noHeal.Flag);
            // Heal with no damage in the world's hands the other way round: the heal request alone refuses.
            using var noDamage = Create(content, 793, (7, false), heal: (_, _) => "healed");
            Assert.IsFalse(noDamage.Host.Apply(noDamage.Run().Effects));
            Assert.IsFalse(noDamage.Flag);
            // A use host of another map answers nothing for this talk: the set is the plain one and refuses at the first damage line's earlier command or at it.
            using var otherMap = Create(content, 793, (7, false), useMap: Map + 1, damage: (_, _, _, _) => "damaged", heal: (_, _) => "healed");
            Assert.AreEqual(ScrOutcome.Refused, otherMap.Run().Outcome);
            // No use host of the map: the talk set is the plain one and the run is refused at the first damage action.
            using var noQueries = Create(content, 793, (7, false), damage: (_, _, _, _) => "damaged", heal: (_, _) => "healed");
            noQueries.Host.QueryHost = null;
            var refused = noQueries.Run();
            Assert.AreEqual((ScrOutcome.Refused, ScrCatalogue.Action.Damage), (refused.Outcome, refused.Refusal!.Type));
        });
    }

    [TestMethod]
    public void The_talk_set_widens_only_while_a_use_host_of_its_map_answers()
    {
        WithContent(content =>
        {
            using var other = Create(content, 790, (7, false), useMap: Map + 1);
            Assert.AreSame(AreaScriptHost.Admission, other.Host.RunAdmission);
            other.Host.QueryHost = null;
            Assert.AreSame(AreaScriptHost.Admission, other.Host.RunAdmission);
            // Without the query commands the talk is refused at its float line, as before.
            Assert.AreEqual((ScrOutcome.Refused, ScrCatalogue.Action.FloatLine), (other.Run().Outcome, other.Run().Refusal!.Type));
            Assert.IsTrue(AreaScriptHost.Admission.IsSubsetOf(AreaScriptHost.TalkAdmission) && AreaScriptHost.SkillAdmission.IsSubsetOf(AreaScriptHost.SkillTalkAdmission));
            // ARC-DEV-178: plus get reaction (29) of S23; ARC-MAGIC-030: plus give spell mastery (112) and the four spell conditions of S31.
            Assert.IsTrue(AreaScriptHost.SkillTalkAdmission.IsSubsetOfBesidesStores(ScrAdmissionSet.S54));
            // ARC-SCRIPT-1563: plus damage (60), apply unresistable damage (138) and heal hit points (75) of S30. ARC-SCRIPT-1620: plus attack (36) of S33.
            CollectionAssert.AreEquivalent(new[] { ScrCatalogue.Action.Random, ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea, ScrCatalogue.Action.GetReaction, ScrCatalogue.Action.GiveSpellMastery,
                ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints, ScrCatalogue.Action.Attack },
                AreaScriptHost.TalkAdmission.Actions.Except(AreaScriptHost.Admission.Actions).ToArray());
            // ARC-SCRIPT-1733: plus obj is animal, obj is in combat and the nine critter-state conditions, which the use host answers.
            CollectionAssert.AreEquivalent(ScrAdmissionSet.ObjectQueryConditions.Concat(ScrAdmissionSet.ObjectStateQueryConditions).Concat(ScrAdmissionSet.SpellQueryConditions).Concat(ScrAdmissionSet.CritterQueryConditions).Concat(ScrAdmissionSet.CritterStateConditions).ToArray(),
                AreaScriptHost.TalkAdmission.Conditions.Except(AreaScriptHost.Admission.Conditions).ToArray());
        });
    }
}
