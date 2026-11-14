using System.Buffers.Binary;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-042 (T-m1, ledger TI4–TI6): the host of a thrown item's hit and miss scripts over invented scripts and an invented map (no original data): the item becomes a created critter on its cell and is gone, a pet joins the PC's
/// party with the follow check skipped, an aggressor's attack events go to everyone in the vicinity, and anything the host cannot take refuses the whole run with nothing applied.
/// </summary>
[TestClass]
public sealed class AreaThrownItemScriptsTests
{
    private const int Map = 1, PetScript = 700, AggressorScript = 701, FlagScript = 702, LedPetScript = 703, PetPrototype = 28460, AggressorPrototype = 27391;
    private static readonly SessionItemKind Gadget = new(SessionItemType.Generic, 15086, 7001, 15086);
    private static readonly SessionItemKind Plain = new(SessionItemType.Generic, 15087, 7002, 15087);

    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        public bool HasCritter(CellCoord cell) => false;
    }

    // Lines of (action type, operands (kind, value)): focus kind 0 triggerer, 1 attachee, 8 everyone in the vicinity; value kind 3 a number.
    private static byte[] Script(params (int Type, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var slot = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(slot, lines[line].Type);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                slot[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        return bytes;
    }

    private static T WithContent<T>(Func<ScriptContent, T> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-magic-042-");
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
            Write($"scr/{PetScript:D5}pet.scr", Script((ScrCatalogue.Action.TransformObject, [(3, PetPrototype)]),
                (ScrCatalogue.Action.MakeFollow, [(1, 0), (0, 0)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            Write($"scr/{AggressorScript:D5}aggressor.scr", Script((ScrCatalogue.Action.TransformObject, [(3, AggressorPrototype)]),
                (ScrCatalogue.Action.Attack, [(8, 0), (1, 0)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            Write($"scr/{FlagScript:D5}flag.scr", Script((ScrCatalogue.Action.TransformObject, [(3, PetPrototype)]),
                (ScrCatalogue.Action.SetGlobalFlag, [(3, 3)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            // ARC-SCRIPT-1842: the pet follows the throw's target (the extra object), an NPC.
            Write($"scr/{LedPetScript:D5}ledpet.scr", Script((ScrCatalogue.Action.TransformObject, [(3, PetPrototype)]),
                (ScrCatalogue.Action.MakeFollow, [(1, 0), (12, 0)]), (ScrCatalogue.Action.ReturnRunDefault, [])));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            return test(content);
        }
        finally { root.Delete(true); }
    }

    // A session with the item on the ground at (300, 400), the script at both points; the world of the run records what it was told.
    private sealed class Rig(GameSessionOwner session, ScriptContent content, string item, CellCoord cell)
    {
        public List<string> Made { get; } = [];
        public List<(string Attacker, string Victim)> Attacks { get; } = [];
        public bool CritterPrototypes { get; set; } = true;
        public IReadOnlyList<string> Near { get; set; } = [];
        public CellCoord? PcCell { get; set; }
        public AreaThrownItemRun Run(int point = ScrPoint.Hit, string? target = null)
        {
            var world = new AreaThrownItemWorld(_ => CritterPrototypes, (_, _) => Near, () => PcCell, entity => Made.Add(entity), (attacker, victim) => { Attacks.Add((attacker, victim)); return "None"; });
            return new AreaThrownItemScripts(session, new AreaItemScriptHost(session, [], content), world, Map).Run(point, item, cell, target);
        }
    }

    private static T WithRig<T>(int script, Func<GameSessionOwner, Rig, T> test, SessionItemKind? kind = null) =>
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create(Map);
            session.AttachItemScripts(prototype => prototype == Gadget.Prototype ? [new(ScrPoint.Hit, script, 0, 0), new(ScrPoint.Miss, script, 0, 0)] : null);
            var created = session.CreateGroundItem(session.Version, kind ?? Gadget, new(Map), new(300, 400), new OpenMap());
            Assert.AreEqual(GameSessionDisposition.Changed, created.Disposition, created.Failure.ToString());
            return test(session, new Rig(session, content, created.Item!, session.QueryPlacedItems().Single().Cell));
        });

    [TestMethod]
    public void A_pet_item_becomes_a_critter_on_its_cell_that_follows_the_PC_with_no_follow_check()
    {
        WithRig(PetScript, (session, rig) =>
        {
            var cell = session.QueryPlacedItems().Single().Cell;
            var ran = rig.Run(ScrPoint.Hit, "npc.9");
            Assert.AreEqual(AreaThrownItemOutcome.Applied, ran.Outcome, ran.Detail);
            Assert.AreEqual("oid:critter:1", ran.Critter);
            Assert.IsTrue(ran.Followed);
            CollectionAssert.AreEqual(new[] { "oid:critter:1" }, rig.Made, "the host is told the critter at once");
            Assert.AreEqual((SessionCreatedKind.Critter, PetPrototype + AreaPrototypeItems.PrototypeOffset, cell), (session.QueryCreatedObject("oid:critter:1")!.Kind,
                session.QueryCreatedObject("oid:critter:1")!.Prototype, session.QueryCreatedObject("oid:critter:1")!.Cell), "the prototype file is the script's number + 20");
            Assert.AreEqual(0, session.QueryPlacedItems().Count, "the item is gone");
            Assert.IsTrue(session.QueryCompanion("oid:critter:1").LedByPc, "the follow joined the party");
            Assert.AreEqual(0, ran.Attacks.Count);
            return 0;
        });
    }

    [TestMethod]
    public void A_pet_item_that_follows_the_throw_s_target_npc_joins_under_that_npc_and_not_the_party()
    {
        WithRig(LedPetScript, (session, rig) =>
        {
            // ARC-SCRIPT-1842 (#1322): the original's join (0x45DE90) takes any leader; the session holds the NPC leader beside the hero's party. The leader is a critter the world names near the landing tile.
            rig.Near = ["npc.9"];
            var ran = rig.Run(ScrPoint.Hit, "npc.9");
            Assert.AreEqual(AreaThrownItemOutcome.Applied, ran.Outcome, ran.Detail);
            Assert.IsTrue(ran.Followed);
            Assert.AreEqual("npc.9", session.QueryLeader("oid:critter:1"));
            CollectionAssert.AreEqual(new[] { "oid:critter:1" }, session.QueryFollowers("npc.9").ToArray());
            Assert.IsFalse(session.QueryCompanion("oid:critter:1").LedByPc);
            return 0;
        });
        // A target the world does not name as a critter near the tile (an object that is no critter) is refused whole: nothing is made.
        WithRig(LedPetScript, (session, rig) =>
        {
            var ran = rig.Run(ScrPoint.Hit, "obj.7");
            Assert.AreEqual(AreaThrownItemOutcome.Refused, ran.Outcome);
            StringAssert.Contains(ran.Detail, "a critter near the landing tile");
            Assert.AreEqual(0, rig.Made.Count);
            Assert.AreEqual(1, session.QueryPlacedItems().Count, "the item still lies there");
            return 0;
        });
        // With no target the follow names no leader: nothing joins, the transform stays.
        WithRig(LedPetScript, (session, rig) =>
        {
            var ran = rig.Run(ScrPoint.Miss);
            Assert.AreEqual(AreaThrownItemOutcome.Applied, ran.Outcome, ran.Detail);
            Assert.IsFalse(ran.Followed);
            Assert.IsNull(session.QueryLeader("oid:critter:1"));
            return 0;
        });
    }

    [TestMethod]
    public void The_miss_point_runs_the_same_script_and_a_flight_that_ended_short_has_no_target()
    {
        WithRig(PetScript, (session, rig) =>
        {
            var ran = rig.Run(ScrPoint.Miss);
            Assert.AreEqual((AreaThrownItemOutcome.Applied, ScrPoint.Miss, "oid:critter:1"), (ran.Outcome, ran.Point, ran.Critter));
            return 0;
        });
    }

    [TestMethod]
    public void An_aggressor_item_attacks_everyone_in_the_vicinity_the_PC_included_and_does_not_follow()
    {
        WithRig(AggressorScript, (session, rig) =>
        {
            var cell = session.QueryPlacedItems().Single().Cell;
            rig.Near = ["npc.2", "npc.3"];
            rig.PcCell = new(cell.X + 3, cell.Y - 2);
            var ran = rig.Run();
            Assert.AreEqual(AreaThrownItemOutcome.Applied, ran.Outcome, ran.Detail);
            Assert.IsFalse(ran.Followed);
            Assert.IsFalse(session.QueryCompanion("oid:critter:1").LedByPc);
            CollectionAssert.AreEqual(new[] { ("oid:critter:1", "npc.2"), ("oid:critter:1", "npc.3"), ("oid:critter:1", "pc") }, rig.Attacks, "the NPCs in list order, then the PC");
            return 0;
        });
    }

    [TestMethod]
    public void A_PC_beyond_the_vicinity_is_not_attacked()
    {
        WithRig(AggressorScript, (session, rig) =>
        {
            var cell = session.QueryPlacedItems().Single().Cell;
            rig.PcCell = new(cell.X + ScrWorld.VicinityReach + 1, cell.Y);
            var ran = rig.Run();
            Assert.AreEqual(AreaThrownItemOutcome.Applied, ran.Outcome, ran.Detail);
            Assert.AreEqual(0, rig.Attacks.Count);
            return 0;
        });
    }

    [TestMethod]
    public void An_item_with_no_script_at_the_point_does_nothing()
    {
        WithRig(PetScript, (session, rig) =>
        {
            Assert.AreEqual(AreaThrownItemOutcome.NoScript, rig.Run().Outcome);
            Assert.AreEqual(1, session.QueryPlacedItems().Count);
            Assert.AreEqual(0, rig.Made.Count);
            return 0;
        }, kind: Plain);
    }

    [TestMethod]
    public void A_prototype_that_is_no_critter_refuses_the_whole_run_and_changes_nothing()
    {
        WithRig(PetScript, (session, rig) =>
        {
            rig.CritterPrototypes = false;
            var revision = session.Version.Revision;
            var ran = rig.Run();
            Assert.AreEqual(AreaThrownItemOutcome.Refused, ran.Outcome);
            StringAssert.Contains(ran.Detail, "no critter prototype");
            Assert.AreEqual(revision, session.Version.Revision);
            Assert.AreEqual(1, session.QueryPlacedItems().Count, "the item still lies there");
            Assert.IsNull(session.QueryCreatedObject("oid:critter:1"));
            return 0;
        });
    }

    [TestMethod]
    public void A_script_that_writes_a_global_flag_is_refused_whole()
    {
        WithRig(FlagScript, (session, rig) =>
        {
            var revision = session.Version.Revision;
            var ran = rig.Run();
            Assert.AreEqual(AreaThrownItemOutcome.Refused, ran.Outcome);
            StringAssert.Contains(ran.Detail, "global state");
            Assert.AreEqual(revision, session.Version.Revision);
            Assert.AreEqual(1, session.QueryPlacedItems().Count);
            return 0;
        });
    }

    [TestMethod]
    public void An_item_that_does_not_lie_on_the_ground_is_refused()
    {
        WithRig(PetScript, (session, rig) =>
        {
            var item = session.QueryPlacedItems().Single().Entity;
            session.TransformPlacedItem(session.Version, item, PetPrototype + AreaPrototypeItems.PrototypeOffset);
            var ran = rig.Run();
            Assert.AreNotEqual(AreaThrownItemOutcome.Applied, ran.Outcome);
            return 0;
        });
    }
}
