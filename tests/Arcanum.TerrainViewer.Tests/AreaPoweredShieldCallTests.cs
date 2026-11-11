using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-MAGIC-041 (ledger TI8): a deferred call whose script uses ammunition units (135) through the viewer's script host: it takes the cell from the hero's stack,
/// ignores a hero with none (the original ignores the unit use's answer) and goes on to its next call; a session without a hero refuses the effects. The script is
/// invented (the installed shield's script is checked on the data in ScrPoweredShieldTests); entity ids and numbers are invented.
/// </summary>
[TestClass]
public sealed class AreaPoweredShieldCallTests
{
    private const int Spender = 795;
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    // 795: use one unit of ammunition from the triggerer, then call itself again in 15 s with the same roles.
    private static byte[] SpenderProgram() => Program((135, [(3, 1), (0, 0)]), (54, [(3, Spender), (3, 0), (0, 0), (1, 0), (3, 15)]), (1, []));

    private static SessionMapInventorySeed Seed(int units) => new([], [new("cells", new(new(SessionItemType.Ammo, 7060, 100, 200), 0, 0, 0, units, []) { AmmoType = 2 }, new CellCoord(101, 200))]);

    private static void With(bool hero, int cells, Action<AreaConversationHost, GameSessionOwner, SessionScriptRole, SessionScriptRole> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-magic-041-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr"));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00795spender.scr"), SpenderProgram());
            using var content = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat")).Content!;
            using var source = new AreaOwnerFixture(motionBodies: true);
            var snapshot = source.Owner.LoadStrict();
            using var session = hero ? ConversationSessionFixture.CreateWithHero(snapshot.RuntimeMap) : ConversationSessionFixture.Create(snapshot.RuntimeMap);
            if (cells > 0)
            {
                Assert.AreEqual(GameSessionDisposition.Changed, session.AttachMapInventory(new(snapshot.RuntimeMap), Seed(cells)).Disposition);
                Assert.AreEqual(GameSessionDisposition.Changed, session.PickUpItem(session.Version, "cells", Accept).Disposition);
            }
            var host = new AreaConversationHost(snapshot, content, session);
            test(host, session, new(new(snapshot.RuntimeMap), SessionEventKinds.PlayerEntity), new(new(snapshot.RuntimeMap), "made:1"));
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void A_call_takes_one_cell_from_the_hero_and_schedules_the_next_call_until_the_cells_are_gone_and_after()
    {
        With(hero: true, cells: 2, (host, session, pc, shield) =>
        {
            var call = new SessionScriptCall(Spender, 0, pc, shield);
            Assert.IsTrue(host.RunCall(call, out var detail), detail);
            Assert.AreEqual(1, session.QueryHeroAmmo().Single().Units);
            Assert.AreEqual((call, session.QueryClockSnapshot().GameTimestampMs + 15 * 8000L), (session.QueryScriptCalls().Single().Call, session.QueryScriptCalls().Single().DeadlineMs));

            // The last unit deletes the stack; a call with no cell left changes no stack and still schedules the next (the original ignores the answer).
            Assert.IsTrue(host.RunCall(call, out detail), detail);
            Assert.AreEqual(0, session.QueryHeroAmmo().Count);
            Assert.IsTrue(host.RunCall(call, out detail), detail);
            Assert.AreEqual(0, session.QueryHeroAmmo().Count);
            Assert.AreEqual(3, session.QueryScriptCalls().Count, "each run scheduled its next call; this host runs only what the pump hands it");
        });
    }

    [TestMethod]
    public void A_session_without_a_hero_refuses_the_call_s_effects()
    {
        With(hero: false, cells: 0, (host, session, pc, shield) =>
        {
            Assert.IsFalse(host.RunCall(new(Spender, 0, pc, shield), out var detail));
            StringAssert.Contains(detail, "refused its effects");
            Assert.AreEqual(0, session.QueryScriptCalls().Count, "the cell step came first and stopped the later steps");
        });
    }

    // A synthetic SCR file: one action per line, each with (kind, value) operands.
    private static byte[] Program(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
        {
            var clause = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(clause, lines[line].Action);
            for (var i = 0; i < lines[line].Operands.Length; i++)
            {
                clause[4 + i] = lines[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(clause[(12 + 4 * i)..], lines[line].Operands[i].Value);
            }
        }
        Assert.AreEqual(ScrReadStatus.Read, ScrProgram.Read(bytes).Status);
        return bytes;
    }
}
