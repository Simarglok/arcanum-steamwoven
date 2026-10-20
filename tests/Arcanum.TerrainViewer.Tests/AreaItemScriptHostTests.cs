using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-013: the viewer's item script host over an invented content stack and map (no original data):
/// slots from the area rows, printed lines and their refusals, and the inventory host's transfers with and without it.</summary>
[TestClass]
public sealed class AreaItemScriptHostTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static string Entity(int index) => "oid:" + Key(index).ObjectIdSha256;

    // An item row whose script array holds script on the insert-item point (21) and words that are not a slot on point 0.
    private static AreaObjectRecord Row(int index, int type, long x, long y, int script) =>
        new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null,
                [new AreaSparseElement(0, [1, 2]), new AreaSparseElement(SessionScriptPoint.InsertItem, [4, 0, script])], null)], true, true, true);

    private static SessionItemFacts Facts(int name) => new(new(SessionItemType.Armor, 4000 + name, name, 100 + name), 5, 0, 0, null, [SessionScriptPoint.InsertItem]);

    // Four ground items with insert-item scripts 500 (plain line), 501 (a line naming the NPC), 502 (no DLG), 503 (a line
    // with an action field), and a dead body.
    private static AreaInventoryMap Map() => new(1,
        new([new(Entity(9), SessionHolderKind.Npc, true, false, [], [])],
            [.. Enumerable.Range(0, 4).Select(i => new SessionSeedItem(Entity(i), Facts(10 + i), new CellCoord(10 + i, 20)))]),
        new[] { Row(0, 7, 10, 20, 500), Row(1, 7, 11, 20, 501), Row(2, 7, 12, 20, 502), Row(3, 7, 13, 20, 503), Row(9, 16, 14, 20, 0) }
            .ToDictionary(row => Entity(row.Key.SourceIndex)));

    private static byte[] PrintAndSkip()
    {
        var bytes = new byte[64 + 2 * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 2);
        var print = bytes.AsSpan(64 + 44, 44);
        BinaryPrimitives.WriteInt32LittleEndian(print, ScrCatalogue.Action.PrintLine);
        print[4] = 3; print[5] = 3;
        BinaryPrimitives.WriteInt32LittleEndian(print[12..], 1); BinaryPrimitives.WriteInt32LittleEndian(print[16..], 6);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 132 + 44), ScrCatalogue.Action.ReturnSkipDefault);
        return bytes;
    }

    private static T WithContent<T>(Func<ScriptContent, T> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-013-");
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
            foreach (var name in new[] { "00500plain", "00501named", "00502silent", "00503acting" }) Write($"scr/{name}.scr", PrintAndSkip());
            Write("dlg/00500plain.dlg", Encoding.Latin1.GetBytes("{1}{It hums for @pcname@.}{It hums softly.}{}{}{}{}"));
            Write("dlg/00501named.dlg", Encoding.Latin1.GetBytes("{1}{@npcname@ hums.}{}{}{}{}{}"));
            Write("dlg/00503acting.dlg", Encoding.Latin1.GetBytes("{1}{It hums.}{}{}{}{}{gf 5 1}"));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            return test(content);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void The_host_reads_slots_from_rows_and_prints_only_plain_lines()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var host = new AreaItemScriptHost(session, [Map()], content);
            Assert.AreEqual(new ScrSlot(500, 4, 0), host.Slot(new(1, Entity(0)), SessionScriptPoint.InsertItem));
            Assert.IsNull(host.Slot(new(1, Entity(0)), 0), "an element without three words is no slot");
            Assert.IsNull(host.Slot(new(2, Entity(0)), SessionScriptPoint.InsertItem), "another map");
            Assert.AreEqual(ScrScriptStatus.Found, host.Script(500).Status);
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(600).Status);
            Assert.IsFalse(host.GlobalFlag(5));
            Assert.AreEqual(new ScrObjectRef(session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode), host.Player());
            Assert.IsNull(host.Leader(new(1, Entity(9))));

            // The diagnostic PC is male: the text, with the diagnostic PC name.
            Assert.AreEqual((SessionPrintedLineStatus.Shown, "It hums for [PC]."), Pair(host.PrintedLine(500, 1)));
            Assert.AreEqual((SessionPrintedLineStatus.Shown, " "), Pair(host.PrintedLine(500, 7)), "a missing line prints one space");
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(501, 1).Status, "whose name @npcname@ is on an item run is not read");
            Assert.AreEqual(SessionPrintedLineStatus.NoDialog, host.PrintedLine(502, 1).Status);
            Assert.AreEqual(SessionPrintedLineStatus.Unsupported, host.PrintedLine(503, 1).Status, "the original would run the action field");
            Assert.AreEqual(SessionPrintedLineStatus.NoDialog, new AreaItemScriptHost(session, [Map()], null).PrintedLine(500, 1).Status);
            return 0;
        });
    }

    private static (SessionPrintedLineStatus, string) Pair(SessionPrintedLine line) => (line.Status, line.Text);

    [TestMethod]
    public void The_inventory_host_runs_insert_scripts_only_with_content()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var bare = new AreaInventoryHost([Map()]);
            Assert.AreEqual(GameSessionFailure.None, bare.Attach(session));
            Assert.IsNull(bare.ItemScripts(session));
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, bare.PickUp(session, Entity(0)).Failure);

            var host = new AreaInventoryHost([Map()], scripts: content);
            var plain = host.PickUp(session, Entity(0));
            Assert.AreEqual((GameSessionDisposition.Changed, " — It hums for [PC]."), (plain.Disposition, AreaInventoryHost.Printed(plain)));
            Assert.AreEqual(GameSessionDisposition.Changed, host.PickUp(session, Entity(2)).Disposition, "no DLG: taken, nothing printed");
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, host.PickUp(session, Entity(1)).Failure);
            Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, host.PickUp(session, Entity(3)).Failure);
            Assert.AreEqual(string.Empty, AreaInventoryHost.Printed(default));
            CollectionAssert.AreEqual(new[] { Entity(0), Entity(2) }, session.QueryPcInventory().Select(item => item.Entity).ToArray());
            return 0;
        });
    }
}
