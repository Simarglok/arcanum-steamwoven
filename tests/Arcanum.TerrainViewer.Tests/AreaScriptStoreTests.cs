using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-100 (MIG8 slice 4, GR29, ledger GV1–GV4): the use-script host answers and applies the value kinds global variable (1), PC variable (5)
/// and PC flag (6) from the session's stores (<see cref="IScrStoreQueries"/>, <see cref="GameSessionOwner.ApplyScriptStores"/>), so a heartbeat that reads
/// or writes them runs instead of being refused (the shape of Dernholm's 1015). An invented content stack and map; no original data.</summary>
[TestClass]
public sealed class AreaScriptStoreTests
{
    private const byte GlobalVariable = 1, PcVariable = 5, PcFlag = 6, Number = 3, Player = 21;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int type, int script)
    {
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, new List<AreaSparseElement> { new(ScrPoint.Heartbeat, [0, 0, script]) }, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (byte, int) G(int index) => (GlobalVariable, index);
    private static (byte, int) Pc(byte kind, int index) => (kind, (Player << 16) | index);
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
        var root = Directory.CreateTempSubdirectory("arc-dev-100-");
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
            // 790: writes global variable 100 and counts in 101, copies 100 to the hero's variable 9 and sets the hero's flag 40.
            Write("scr/00790stores.scr", Program(
                Do(new(ScrCatalogue.Action.Assign, G(100), N(7))),
                Do(new(ScrCatalogue.Action.Add, G(101), G(101), N(1))),
                Do(new(ScrCatalogue.Action.Assign, Pc(PcVariable, 9), G(100))),
                Do(new(ScrCatalogue.Action.Assign, Pc(PcFlag, 40), N(1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 791: the shape of a heartbeat that waits for a global variable (1015's first refusal): skip the default while it is 3.
            Write("scr/00791waits.scr", Program(
                If(new(ScrCatalogue.Condition.Equal, G(100), N(3)), new(ScrCatalogue.Action.ReturnSkipDefault), new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static GameSessionOwner Session(bool hero) => hero
        ? AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), 1,
            AreaNewCharacterTests.Generic()).Owner!
        : ConversationSessionFixture.Create();

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, AreaObjectRecord npc) =>
        new(session, 1, [npc], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, null));

    [TestMethod]
    public void A_heartbeat_writes_the_global_variables_and_the_hero_s_variable_and_flag_into_the_session()
    {
        WithContent(content =>
        {
            using var session = Session(hero: true);
            var npc = Row(1, 16, 790);
            var host = Host(session, content, npc);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.RunDefault, ran.Run!.Outcome);
            Assert.AreEqual((7, 1, 7, true), (session.QueryGlobalVariable(100), session.QueryGlobalVariable(101), session.QueryPcVariable(9), session.QueryPcFlag(40)));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(2, session.QueryGlobalVariable(101), "the next tick reads what the first left");
            using var loaded = ConversationSessionFixture.SaveAndLoad(session);
            Assert.AreEqual((7, 2, 7, true), (loaded.QueryGlobalVariable(100), loaded.QueryGlobalVariable(101), loaded.QueryPcVariable(9), loaded.QueryPcFlag(40)),
                "the stores are saved with the session");
        });
    }

    [TestMethod]
    public void Without_a_hero_the_global_variables_are_written_and_the_PC_s_are_ignored()
    {
        WithContent(content =>
        {
            using var session = Session(hero: false);
            var npc = Row(1, 16, 790);
            var ran = Host(session, content, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual((7, 1, 0, false), (session.QueryGlobalVariable(100), session.QueryGlobalVariable(101), session.QueryPcVariable(9), session.QueryPcFlag(40)),
                "SA14: a write to an object that is not a PC does nothing, and nothing makes the run fail");
        });
    }

    [TestMethod]
    public void A_heartbeat_reads_the_session_s_global_variable()
    {
        WithContent(content =>
        {
            using var session = Session(hero: true);
            var npc = Row(1, 16, 791);
            var host = Host(session, content, npc);
            Assert.AreEqual(ScrOutcome.RunDefault, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Run!.Outcome, "variable 100 is 0 at a new game");
            Assert.AreEqual(GameSessionDisposition.Changed, session.SetGlobalVariable(session.Version, 100, 3).Disposition);
            var ran = host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(ScrOutcome.SkipDefault, ran.Run!.Outcome);
        });
    }

    // ---- opt-in: the installed Dernholm castle heartbeat ----

    [TestMethod, TestCategory("RealData")]
    public void The_installed_dernholm_heartbeat_1015_is_admitted_now_that_the_global_variable_kind_is()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in heartbeat check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var read = content.Script(1015);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, read.Detail);
        var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.HeartbeatAdmission);
        Assert.IsTrue(admission.IsAdmitted, $"the first refusal is {admission.Refusal}");
    }
}
