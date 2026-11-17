using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-2198: the state actions a talk's dialog script holds while the shown map's use host answers (<see cref="AreaScriptHost.TalkStateActions"/>): loops, play script eye candy, change script
/// of another point of an object, critter disband and create item through the use host, a transform refused whole, and the movie and end-game actions of the endings. Invented content stack and map; no original data.</summary>
[TestClass]
public sealed class AreaTalkStateTests
{
    private const byte Triggerer = 0, Attachee = 1, Local = 2, Number = 3, GlobalFlag = 4;
    private const int Map = 1, AreaFlag = 100;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int dialog, int destroy = 0, long spellFlags = 0)
    {
        var scripts = new List<AreaSparseElement> { new(ScrPoint.Dialog, [0, 0, dialog]), new(ScrPoint.Use, [0, 0, 5000]) };
        if (destroy != 0) scripts.Add(new(ScrPoint.Destroy, [0, 0, destroy]));
        var fields = new List<AreaFieldValue> { new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, scripts, null) };
        if (spellFlags != 0) fields.Add(new(19, AreaPresence.Value, AreaValueOrigin.Instance, spellFlags, [], null));
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
        var root = Directory.CreateTempSubdirectory("arc-script-2198-");
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
            // 800: change script at the attachee's use point to 4001, critter disband of the attachee (it follows no one), skip default.
            Write("scr/00800state.scr", Program(
                Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Use), N(4001))),
                Do(new(ScrCatalogue.Action.CritterDisband, (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 801: the attachee transforms into prototype 27368 (Kerghan's change of shape), skip default.
            Write("scr/00801transform.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.TransformObject, N(27368))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 805: the transform of the speaker, then eye candy 31 on the attachee and a call script in (script 806, line 0, 5 s) naming the attachee: both name the object the transform made (the installed 1105 lines 80-84's shape).
            Write("scr/00805after.scr", Program(
                Do(new(ScrCatalogue.Action.TransformObject, N(27368))),
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(31), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.CallScriptIn, N(806), N(0), (Triggerer, -1), (Attachee, -1), N(5))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 807: an attack by the attachee on the triggerer, then the transform (the attack lands before the old NPC is destroyed); 808: the transform, then a dialog action (the run is refused whole).
            Write("scr/00807order.scr", Program(
                Do(new(ScrCatalogue.Action.Attack, (Triggerer, -1), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.TransformObject, N(27368))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            Write("scr/00808dialog.scr", Program(
                Do(new(ScrCatalogue.Action.TransformObject, N(27368))),
                Do(new(ScrCatalogue.Action.Dialog, N(1), N(1)))));
            // 802: create item 8278 in the PC (the installed 1105 line 68's shape), skip default; the invented world holds no item prototype.
            Write("scr/00802create.scr", Program(
                Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag))),
                Do(new(ScrCatalogue.Action.CreateItem, N(8278), (Triggerer, -1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 803: the ending's shape: a movie through fade (103), the end of the game (132), skip default.
            Write("scr/00803ending.scr", Program(
                Do(new(ScrCatalogue.Action.Fade, N(5), N(0), N(3), N(2))),
                Do(new(ScrCatalogue.Action.EndGame)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 804: a loop over the attachee's followers with play script eye candy and a loop end, skip default (admission only).
            Write("scr/00804loop.scr", Program(
                Do(new(ScrCatalogue.Action.LoopFor, (2, -1))),
                Do(new(ScrCatalogue.Action.LoopEnd)),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            // 4003: a destroy script that runs the default; 4004: one that returns 0 (skip default) and keeps the object (0x43CCA0 returns without destroying).
            Write("scr/04003destroy.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag + 1))), Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            Write("scr/04004keep.scr", Program(Do(new(ScrCatalogue.Action.SetGlobalFlag, N(AreaFlag + 2))), Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private sealed record Talk(GameSessionOwner Session, AreaSpeaker Speaker, AreaScriptHost Host) : IDisposable
    {
        public AreaUseScriptHost Use { get; init; } = null!;
        public void Dispose() => Session.Dispose();
        public ScrRunResult Run() => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Host.Object(Speaker), Host.Pc, null), Host,
            new() { Rules = Session.Rules.Scripts, Admission = Host.RunAdmission });
        public bool Flag => Host.GlobalFlag(AreaFlag);
    }

    private static Talk Create(ScriptContent content, int dialog, bool useHost = true, Func<int, bool>? critter = null, Action<string>? made = null, Func<string, AreaScriptVitals?>? vitals = null, int destroyScript = 0, Func<string, string, string>? attack = null, long spellFlags = 0, Func<string, int, int, bool>? carry = null)
    {
        var session = ConversationSessionFixture.Create(Map);
        var row = Row(1, dialog, destroyScript, spellFlags);
        var speaker = new AreaSpeaker(0, row, dialog, row.WorldX!.Value, row.WorldY!.Value);
        var map = new SessionMapId(Map);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(row)], 50);
        var use = new AreaUseScriptHost(session, Map, [row], [], content, new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null, Critter: critter, CritterMade: made, Vitals: vitals, ScriptAttack: attack, CarryVitals: carry));
        var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = useHost ? () => use : null };
        return new(session, speaker, host) { Use = use };
    }

    [TestMethod]
    public void A_change_script_of_another_point_and_a_disband_apply_through_the_use_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 800);
            var run = talk.Run();
            Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop), run.Refusal?.ToString());
            Assert.AreEqual(1, run.Effects.SlotWrites.Count);
            var attachee = talk.Host.Object(talk.Speaker);
            Assert.AreEqual(5000, talk.Host.Slot(attachee, ScrPoint.Use)!.Value.Script);
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.AreEqual(4001, talk.Host.Slot(attachee, ScrPoint.Use)!.Value.Script, "the use slot's script is the written one");
        });
    }

    [TestMethod]
    public void A_non_speaker_npc_of_the_shown_map_meets_the_dialog_gates_through_the_use_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 800);
            var other = new ScrObjectRef(Map, "oid:" + new string('z', 64));
            Assert.AreEqual(ScrDialogState.Normal, talk.Host.DialogState(other), "no row on the shown map: normal");
            Assert.AreEqual(ScrDialogState.Normal, talk.Host.DialogState(talk.Host.Pc));
        });
    }

    // ARC-SCRIPT-2464: the speaker meets the surrendered and mind-controlled gates as any NPC of the map, through the shown map's use host; without that host only its dead gate is modelled.
    [TestMethod]
    public void The_speaker_meets_the_surrendered_and_mind_controlled_gates_through_the_use_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 800, spellFlags: AreaUseScriptHost.MindControlledFlag);
            var speaker = talk.Host.Object(talk.Speaker);
            Assert.AreEqual(ScrDialogState.MindControlled, talk.Host.DialogState(speaker));
            Assert.AreEqual(GameSessionDisposition.Changed, talk.Session.SetDangerState(talk.Session.Version, new SessionDangerRequest { Entity = speaker.Code, State = DangerState.Surrender, Target = SessionCombatant.Hero }).Disposition);
            Assert.AreEqual(ScrDialogState.Surrendered | ScrDialogState.MindControlled, talk.Host.DialogState(speaker));
            Assert.AreEqual(GameSessionDisposition.Changed, talk.Session.Kill(talk.Session.Version, SessionLifeTarget.Critter(speaker.Code, new Arcanum.Simulation.Combat.LifeFacts(), default)).Disposition);
            Assert.AreEqual(ScrDialogState.Dead | ScrDialogState.Surrendered | ScrDialogState.MindControlled, talk.Host.DialogState(speaker));
            Assert.AreEqual(ScrDialogState.Normal, talk.Host.DialogState(talk.Host.Pc));
        });
        WithContent(content =>
        {
            using var talk = Create(content, 800, useHost: false, spellFlags: AreaUseScriptHost.MindControlledFlag);
            var speaker = talk.Host.Object(talk.Speaker);
            Assert.AreEqual(GameSessionDisposition.Changed, talk.Session.Kill(talk.Session.Version, SessionLifeTarget.Critter(speaker.Code, new Arcanum.Simulation.Combat.LifeFacts(), default)).Disposition);
            Assert.AreEqual(ScrDialogState.Dead, talk.Host.DialogState(speaker), "no use host of the map: the dead gate alone");
        });
    }

    [TestMethod]
    public void A_transform_of_the_speaker_makes_a_critter_on_its_tile_and_destroys_the_npc()
    {
        WithContent(content =>
        {
            var made = new List<string>();
            using var talk = Create(content, 801, critter: prototype => prototype == 27368, made: made.Add, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0));
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsTrue(talk.Flag, "the run's other effects apply");
            var critter = talk.Use.LastTransformed ?? throw new AssertFailedException("no critter made");
            Assert.AreEqual(new CellCoord(11, 20), critter.Cell, "on the old NPC's tile");
            Assert.AreEqual(27368 + AreaPrototypeItems.PrototypeOffset, critter.Prototype, "the prototype file is the script's number + 20 (F1)");
            CollectionAssert.AreEqual(new[] { critter.Entity }, made, "the host is told of the made critter");
            Assert.IsTrue(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)), "the old NPC is destroyed");
        });
    }

    [TestMethod]
    public void The_later_requests_of_a_run_after_a_transform_name_the_made_critter()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 805, critter: prototype => prototype == 27368, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0));
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsTrue(ScrTransformedObject.Is(run.Effects.EyeCandies.Single().Object), "the run names the placeholder after the transform");
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            var critter = talk.Use.LastTransformed!;
            Assert.AreEqual(critter.Entity, talk.Host.TakeEyeCandies().Single().Object.Code, "the eye candy plays on the new critter");
            var call = talk.Session.QueryScriptCalls().Single().Call;
            Assert.AreEqual(critter.Entity, call.Attachee?.Entity, "the deferred call names the new critter (F11, F12)");
            Assert.AreEqual(806, call.Script);
        });
    }

    [TestMethod]
    public void The_requests_before_a_transform_apply_before_the_old_npc_is_destroyed()
    {
        WithContent(content =>
        {
            Talk? talk = null;
            var destroyedAtAttack = new List<bool>();
            talk = Create(content, 807, critter: prototype => prototype == 27368, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0),
                attack: (attacker, victim) => { destroyedAtAttack.Add(AreaUseScriptHost.Destroyed(talk!.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row))); return "ok"; });
            using (talk)
            {
                var run = talk.Run();
                Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
                Assert.IsTrue(talk.Host.Apply(run.Effects), talk.Use.TalkStateRefusal(run.Effects));
                CollectionAssert.AreEqual(new[] { false }, destroyedAtAttack, "the attack of the old NPC lands while it still stands");
                Assert.IsTrue(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)));
            }
        });
    }

    // ARC-SCRIPT-2506 (TI5, F2, F3, 0x43D530): the made critter takes the old NPC's hit-point and fatigue percentages through the world's life; the damage is the share of its own maxima, hit points at most maximum - 1.
    [TestMethod]
    public void The_made_critter_carries_the_hit_point_and_fatigue_shares_of_the_old_npc()
    {
        Assert.AreEqual((50, 10), AreaUseScriptHost.TransformDamage(50, 100, 50, 20));
        Assert.AreEqual((0, 0), AreaUseScriptHost.TransformDamage(100, 100, 100, 20), "a full critter carries no damage");
        Assert.AreEqual((0, 0), AreaUseScriptHost.TransformDamage(130, 100, 101, 20), "a percentage above 100 stores no negative damage");
        Assert.AreEqual((99, 20), AreaUseScriptHost.TransformDamage(0, 100, 0, 20), "hit points stop one short of the maximum; fatigue is not clamped");
        Assert.AreEqual((30, 6), AreaUseScriptHost.TransformDamage(70, 100, 66, 20), "the damage rounds down");
        WithContent(content =>
        {
            var carried = new List<(string, int, int)>();
            // 20 of 40 hit points (50 %), 10 of 20 fatigue (50 %); 7 of 9 hit points would be 77 %.
            using var talk = Create(content, 801, critter: prototype => prototype == 27368, vitals: _ => new AreaScriptVitals(20, 40, 10, 20, 0),
                carry: (entity, hitPoints, fatigue) => { carried.Add((entity, hitPoints, fatigue)); return true; });
            var run = talk.Run();
            Assert.IsTrue(talk.Host.Apply(run.Effects), talk.Use.TalkStateRefusal(run.Effects));
            var critter = talk.Use.LastTransformed!;
            CollectionAssert.AreEqual(new[] { (critter.Entity, 50, 50) }, carried);
            Assert.IsTrue(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)));
        });
        WithContent(content =>
        {
            var carried = new List<(string, int, int)>();
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0), carry: (e, h, f) => { carried.Add((e, h, f)); return true; });
            Assert.IsTrue(talk.Host.Apply(talk.Run().Effects));
            Assert.AreEqual(0, carried.Count, "a full NPC needs no carry");
        });
        WithContent(content =>
        {
            // The life refuses the share: the made critter goes again and the old NPC stays.
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(20, 40, 20, 20, 0), carry: (_, _, _) => false);
            Assert.IsFalse(talk.Host.Apply(talk.Run().Effects));
            Assert.IsNull(talk.Use.LastTransformed);
            Assert.IsFalse(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)));
            Assert.AreEqual(0, talk.Session.QueryCreatedObjects(new SessionMapId(Map)).Count, "no second critter is left");
        });
        WithContent(content =>
        {
            // A maximum of 0 is refused (the original divides by it without a guard).
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(0, 0, 20, 20, 0), carry: (_, _, _) => true);
            StringAssert.Contains(talk.Use.TalkStateRefusal(talk.Run().Effects) ?? "", "maximum of 0");
        });
    }

    // ARC-SCRIPT-2506 (F6): the old NPC's destroy script runs first; the default lets the destroy go on, a return of 0 keeps the old NPC while the made critter stands.
    [TestMethod]
    public void The_old_npcs_destroy_script_runs_first_and_a_skip_default_keeps_it()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0), destroyScript: 4003);
            var run = talk.Run();
            Assert.IsNull(talk.Use.TalkStateRefusal(run.Effects));
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsTrue(talk.Host.GlobalFlag(AreaFlag + 1), "the destroy script ran");
            Assert.IsTrue(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)));
            Assert.IsNotNull(talk.Use.LastTransformed);
        });
        WithContent(content =>
        {
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0), destroyScript: 4004);
            Assert.IsTrue(talk.Host.Apply(talk.Run().Effects));
            Assert.IsTrue(talk.Host.GlobalFlag(AreaFlag + 2), "the destroy script ran");
            Assert.IsFalse(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)), "the refusal keeps the old NPC");
            Assert.IsNotNull(talk.Use.LastTransformed, "the made critter stands (F6)");
            Assert.AreEqual(1, talk.Session.QueryCreatedObjects(new SessionMapId(Map)).Count);
        });
    }

    // ARC-SCRIPT-2506 (F5): the destroy takes the old NPC out of its NPC leader's array and frees what it led.
    [TestMethod]
    public void The_destroy_of_a_transform_unlinks_the_old_npc_from_its_leaders_and_followers()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0));
            var old = AreaConversationState.EntityId(talk.Speaker.Row);
            const string leader = "npc-leader", led = "npc-led";
            Assert.AreEqual(GameSessionDisposition.Changed, talk.Session.ForceJoinLeader(talk.Session.Version, old, leader).Disposition);
            Assert.AreEqual(GameSessionDisposition.Changed, talk.Session.ForceJoinLeader(talk.Session.Version, led, old).Disposition);
            var run = talk.Run();
            Assert.IsNull(talk.Use.TalkStateRefusal(run.Effects), "a leader or follower is no longer a refusal");
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            Assert.IsNull(talk.Session.QueryLeader(old));
            Assert.AreEqual(0, talk.Session.QueryFollowers(leader).Count);
            Assert.IsNull(talk.Session.QueryLeader(led));
        });
    }

    [TestMethod]
    public void A_dialog_action_in_the_run_of_a_transform_leaves_the_run_unapplied()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 808, critter: prototype => prototype == 27368, vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0));
            var run = talk.Run();
            Assert.IsNotNull(run.Effects.Dialog, run.Refusal?.ToString());
            Assert.IsFalse(talk.Host.Apply(run.Effects));
            Assert.IsNull(talk.Use.LastTransformed, "nothing was made");
        });
    }

    [TestMethod]
    public void A_transform_the_host_cannot_make_refuses_the_run_whole_before_anything_applies()
    {
        WithContent(content =>
        {
            AreaScriptVitals? Full(string _) => new AreaScriptVitals(40, 40, 20, 20, 0);
            // No critter prototype the world knows.
            using (var talk = Create(content, 801, vitals: Full))
            {
                var run = talk.Run();
                Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
                Assert.IsFalse(talk.Host.Apply(run.Effects));
                Assert.IsFalse(talk.Flag, "the flag before the transform was not applied: the run is refused whole");
                Assert.IsNull(talk.Use.LastTransformed);
            }
            // A speaker that is hurt while the world has no life to carry the share: refused whole.
            using (var talk = Create(content, 801, critter: prototype => true, vitals: _ => new AreaScriptVitals(39, 40, 20, 20, 0)))
            {
                var run = talk.Run();
                Assert.IsFalse(talk.Host.Apply(run.Effects));
                Assert.IsFalse(AreaUseScriptHost.Destroyed(talk.Session, Map, AreaConversationState.EntityId(talk.Speaker.Row)));
            }
            // Vitals the world cannot give: the shares cannot be carried either.
            using (var talk = Create(content, 801, critter: prototype => true))
                Assert.IsFalse(talk.Host.Apply(talk.Run().Effects));
            // A speaker whose own destroy script would run first (the original keeps the old object when that script refuses).
            using (var talk = Create(content, 801, critter: prototype => true, vitals: Full, destroyScript: 4002))
            {
                var run = talk.Run();
                StringAssert.Contains(talk.Use.TalkStateRefusal(run.Effects) ?? "", "destroy script 4002");
                Assert.IsFalse(talk.Host.Apply(run.Effects));
            }
            // Without the shown map's use host the set does not hold the action at all: the run is refused before it runs.
            using (var talk = Create(content, 801, useHost: false))
                Assert.AreEqual(ScrCatalogue.Action.TransformObject, talk.Run().Refusal?.Type);
        });
    }

    [TestMethod]
    public void A_create_item_without_an_item_prototype_refuses_the_run_whole()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 802);
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsFalse(talk.Host.Apply(run.Effects));
            Assert.IsFalse(talk.Flag);
        });
    }

    [TestMethod]
    public void A_movie_and_the_end_of_the_game_reach_the_viewer_through_the_talk_host()
    {
        WithContent(content =>
        {
            using var talk = Create(content, 803);
            var run = talk.Run();
            Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.ToString());
            Assert.IsTrue(talk.Host.Apply(run.Effects));
            CollectionAssert.AreEqual(new[] { 3 }, talk.Host.TakeFades().Select(f => f.Movie).ToArray());
            Assert.IsTrue(talk.Host.TakeEndGame());
        });
    }

    [TestMethod]
    public void The_loop_and_state_actions_are_admitted_only_while_a_use_host_answers()
    {
        WithContent(content =>
        {
            var loop = content.Script(804).Program!;
            Assert.IsTrue(ScrAdmission.Admit(loop, AreaScriptHost.TalkAdmission).IsAdmitted, "the talk set holds a loop");
            Assert.AreEqual(ScrCatalogue.Action.LoopFor, ScrAdmission.Admit(loop, AreaScriptHost.Admission).Refusal?.Type, "the bare set does not");
            using var talk = Create(content, 800, useHost: false);
            Assert.AreSame(AreaScriptHost.Admission, talk.Host.RunAdmission);
            Assert.AreEqual(ScrCatalogue.Action.ChangeScript, talk.Run().Refusal?.Type);
        });
    }
}
