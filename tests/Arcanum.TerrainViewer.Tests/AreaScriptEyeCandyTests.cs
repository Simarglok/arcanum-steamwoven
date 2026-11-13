using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Magic;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Magic;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1592 (ledger WE4, WE5): the script action 120 (play script eye candy) reaches the eye-candy host: the use host keeps each run's requests in action order for the viewer (<see cref="AreaUseScriptHost.TakeEyeCandies"/>), and
/// <see cref="AreaEyeCandyHost.PlayScript"/> plays the entry of the script list as an overlay run whose entry reference names that list, so a spell's entry of the same index is a different one.
/// </summary>
[TestClass]
public sealed class AreaScriptEyeCandyTests
{
    private const string Hero = SessionSpellCaster.Hero;
    private const byte Attachee = 1, Number = 3, Extra = 12;
    private const int PlayScript = 820, RefusedScript = 821;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
    private static AreaObjectRecord Row(int index, int type, int script) => new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new(ScrPoint.Use, [0, 0, script])], null), new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null)], true, true, true);

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
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
        var root = Directory.CreateTempSubdirectory("arc-script-1592-");
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
            // 820: entry 7 on the attachee, entry 9 on the extra object (the use's target), entry 7 on the attachee again; the run goes on after each.
            Write("scr/00820play.scr", Program(
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(9), (Extra, -1))),
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            // 821: plays, then destroys the PC (the extra object), which the use host does not model (only an NPC of the shown map): the run is refused whole before anything applies and shows nothing.
            Write("scr/00821refused.scr", Program(
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.Destroy, (Extra, -1))),
                Do(new(ScrCatalogue.Action.ReturnRunDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    [TestMethod]
    public void A_use_script_hands_its_play_requests_to_the_viewer_in_action_order_once()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var scenery = Row(2, 2, PlayScript);
            var host = Host(session, content, scenery);
            var pc = host.Pc;
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(scenery), pc, pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            var taken = host.TakeEyeCandies();
            CollectionAssert.AreEqual(new[] { 7, 9, 7 }, taken.Select(t => t.Number).ToArray());
            Assert.AreEqual(AreaUseScriptHost.EntityId(scenery), taken[0].Object.Code, "the attachee");
            Assert.AreEqual(pc, taken[1].Object, "the extra object, the PC the use passed");
            Assert.AreEqual(0, host.TakeEyeCandies().Count, "taken once");
        });
    }

    [TestMethod]
    public void A_run_the_host_refuses_whole_shows_no_eye_candy()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var scenery = Row(3, 2, RefusedScript);
            var host = Host(session, content, scenery);
            var pc = host.Pc;
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(scenery), pc, pc, session.Rules.Scripts);
            Assert.AreNotEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, "the run is not applied");
            Assert.AreEqual(0, host.TakeEyeCandies().Count);
        });
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public AreaEyeCandyHost Host { get; }
        public Dictionary<int, SpellEyeCandyEntry> Entries = [];

        public Fixture()
        {
            Host = new AreaEyeCandyHost(Session, i => Entries.GetValueOrDefault(i), (art, parts) => new(4, 2, 100, 0, true, true), (low, high) => low);
        }

        public void Dispose() => Session.Dispose();
    }

    private static SpellEyeCandyEntry Entry(uint art, SpellEyeCandyFlags flags = SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Animates) => new(art, flags, -1, null, (0, 0, 0), 0);

    [TestMethod]
    public void A_play_runs_the_entry_of_the_script_list_and_not_the_spell_entry_of_the_same_index()
    {
        using var f = new Fixture();
        var script = EyeCandyEntryRef.Make(EyeCandyList.Script, 3)!.Value;
        f.Entries[3] = Entry(0x3000);
        f.Entries[script] = Entry(0x4000);
        Assert.IsTrue(f.Host.PlayScript(Hero, 3));
        var run = f.Session.QueryGoals().RunsOf(Hero).Single();
        Assert.AreEqual(GoalType.EyeCandy, run.Bottom.Goal);
        Assert.AreEqual((0x4000u, script, -1), (run.Top.EyeCandy!.Art, run.Top.EyeCandy.Entry, run.Top.EyeCandy.Tag), "the script entry, no tag");
        Assert.AreEqual((EyeCandyList.Script, 3), (EyeCandyEntryRef.ListOf(run.Top.EyeCandy.Entry), EyeCandyEntryRef.IndexOf(run.Top.EyeCandy.Entry)));
        // A spell's add of the same index is its own entry, and the script list's is not removed by it.
        Assert.IsTrue(f.Host.Play(Hero, 3, 0, 5));
        Assert.AreEqual(0, f.Host.Remove(Hero, 4, 5, false), "another index");
        Assert.AreEqual(1, f.Host.Remove(Hero, 3, 5, false), "the spell entry only");
        Assert.AreEqual(1, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done), "the script entry's run stays");
    }

    [TestMethod]
    public void A_play_of_a_number_past_the_list_or_of_an_entry_that_runs_nothing_starts_no_run()
    {
        using var f = new Fixture();
        f.Entries[EyeCandyEntryRef.Make(EyeCandyList.Script, 1)!.Value] = Entry(0x1000, SpellEyeCandyFlags.Foreground);
        f.Entries[EyeCandyEntryRef.Make(EyeCandyList.Script, 2)!.Value] = Entry(0x2000, SpellEyeCandyFlags.Background | SpellEyeCandyFlags.Animates);
        Assert.IsFalse(f.Host.PlayScript(Hero, 0), "no entry");
        Assert.IsFalse(f.Host.PlayScript(Hero, 1), "a static entry");
        Assert.IsFalse(f.Host.PlayScript(Hero, 2), "a background only");
        Assert.IsFalse(f.Host.PlayScript(Hero, -1), "a number below the list");
        Assert.IsFalse(f.Host.PlayScript(Hero, EyeCandyEntryRef.MaxIndex + 1), "a number past the entry range");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void An_entry_reference_names_its_list_and_keeps_a_spell_entry_as_it_was()
    {
        Assert.AreEqual(1338, EyeCandyEntryRef.Make(EyeCandyList.Spell, 1338), "list 0: the number a cast host and a save already hold");
        var script = EyeCandyEntryRef.Make(EyeCandyList.Script, 55)!.Value;
        Assert.AreEqual((EyeCandyList.Script, 55), (EyeCandyEntryRef.ListOf(script), EyeCandyEntryRef.IndexOf(script)));
        Assert.AreNotEqual(55, script);
        Assert.IsNull(EyeCandyEntryRef.Make(EyeCandyList.Script, -1));
        Assert.IsNull(EyeCandyEntryRef.Make(EyeCandyList.Script, EyeCandyEntryRef.MaxIndex + 1));
        Assert.IsFalse(Enum.IsDefined(EyeCandyEntryRef.ListOf(0x06000005)), "a value outside the lists is not read as the spell list");
    }
}
