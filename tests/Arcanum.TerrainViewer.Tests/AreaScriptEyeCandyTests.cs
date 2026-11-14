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
    private const string Hero = SessionSpellCaster.Hero, Npc = "oid:npc";
    private const byte Attachee = 1, Number = 3, Extra = 12;
    private const int PlayScript = 820, RefusedScript = 821, StopScript = 822;

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
            // 822 (ARC-SCRIPT-1828): play entry 7 on the attachee, stop entry 7 on it, play 9 on the extra object, stop 9 on it, stop 4 on the attachee, play 7 again; the run goes on after each.
            Write("scr/00822stop.scr", Program(
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.StopScriptEyeCandy, N(7), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(9), (Extra, -1))),
                Do(new(ScrCatalogue.Action.StopScriptEyeCandy, N(9), (Extra, -1))),
                Do(new(ScrCatalogue.Action.StopScriptEyeCandy, N(4), (Attachee, -1))),
                Do(new(ScrCatalogue.Action.PlayScriptEyeCandy, N(7), (Attachee, -1))),
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

    // ARC-MAGIC-1830, ARC-SCRIPT-1828: the requests the session hands over for an NPC's rewield or wield scripts are turned into script-list requests in order: the plays (120) and the stops (124); play spell names the spell list and is neither.
    [TestMethod]
    public void Only_the_script_list_plays_and_stops_of_a_hosts_requests_are_kept_in_order()
    {
        var map = 1;
        var npc = new ScrObjectRef(map, "oid:npc");
        var requests = AreaScriptEyeCandy.Of(
        [
            new ScrEyeCandy(7, npc), new ScrEyeCandy(23, npc, ScrEyeCandyKind.PlaySpell), new ScrEyeCandy(9, new(map, "ring")), new ScrEyeCandy(30, npc, ScrEyeCandyKind.StopScript),
        ]).ToArray();
        CollectionAssert.AreEqual(new[] { new AreaScriptEyeCandy(npc, 7), new AreaScriptEyeCandy(new(map, "ring"), 9), new AreaScriptEyeCandy(npc, 30, Stop: true) }, requests);
        Assert.AreEqual(0, AreaScriptEyeCandy.Of(Array.Empty<ScrEyeCandy>()).Count());
    }

    [TestMethod]
    public void A_use_script_hands_its_stop_requests_with_its_plays_in_action_order()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var scenery = Row(4, 2, StopScript);
            var host = Host(session, content, scenery);
            var pc = host.Pc;
            var ran = host.RunUseScript(AreaUseScriptHost.EntityId(scenery), pc, pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            var attachee = new ScrObjectRef(1, AreaUseScriptHost.EntityId(scenery));
            CollectionAssert.AreEqual(new[] { new AreaScriptEyeCandy(attachee, 7), new AreaScriptEyeCandy(attachee, 7, Stop: true), new AreaScriptEyeCandy(pc, 9), new AreaScriptEyeCandy(pc, 9, Stop: true),
                new AreaScriptEyeCandy(attachee, 4, Stop: true), new AreaScriptEyeCandy(attachee, 7) }, host.TakeEyeCandies().ToArray());
            Assert.AreEqual(0, host.TakeEyeCandies().Count, "taken once");
        });
    }

    [TestMethod]
    public void The_dialog_and_use_sets_admit_stop_script_eye_candy_beside_play()
    {
        foreach (var set in new[] { AreaScriptHost.Admission, AreaUseScriptHost.Admission, AreaUseScriptHost.HeartbeatAdmission, AreaUseScriptHost.DyingAdmission })
            Assert.IsTrue(set.Actions.Contains(ScrCatalogue.Action.StopScriptEyeCandy), set.Id);
        Assert.IsTrue(AreaUseScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.PlayScriptEyeCandy), "the use set holds the play too");
    }

    [TestMethod]
    public void A_stop_takes_the_first_script_entry_run_off_and_leaves_the_spell_entry_and_other_numbers()
    {
        using var f = new Fixture();
        var script = EyeCandyEntryRef.Make(EyeCandyList.Script, 3)!.Value;
        f.Entries[3] = Entry(0x3000);
        f.Entries[script] = Entry(0x4000);
        f.Entries[EyeCandyEntryRef.Make(EyeCandyList.Script, 4)!.Value] = Entry(0x5000);
        Assert.AreEqual(0, f.Host.StopScript(Hero, 3), "nothing runs yet");
        Assert.IsTrue(f.Host.PlayScript(Hero, 3));
        Assert.IsTrue(f.Host.PlayScript(Hero, 3), "the script's play has no duplicate test");
        Assert.IsTrue(f.Host.PlayScript(Hero, 4));
        Assert.IsTrue(f.Host.Play(Hero, 3, 0, 5), "the spell list's entry of the same index");
        Assert.AreEqual(4, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done));
        Assert.AreEqual(1, f.Host.StopScript(Hero, 3), "the first run of the entry only (0x4243E0 returns after the first interrupt)");
        Assert.AreEqual(3, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done));
        Assert.AreEqual(1, f.Host.StopScript(Hero, 3), "a second stop takes the second");
        Assert.AreEqual(0, f.Host.StopScript(Hero, 3), "none left; the spell entry (tag 5) is not the script list's");
        Assert.AreEqual(2, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done));
        Assert.AreEqual(0, f.Host.StopScript(Npc, 4), "another owner has none");
        Assert.AreEqual(1, f.Host.StopScript(Hero, 4));
        Assert.AreEqual(1, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done), "the spell run stays");
        Assert.AreEqual(0, f.Host.StopScript(Hero, -1) + f.Host.StopScript(Hero, EyeCandyEntryRef.MaxIndex + 1), "a number outside the entry range ends nothing");
    }

    [TestMethod]
    public void A_stop_leaves_a_run_another_tag_holds_and_a_looping_entry_played_as_a_stack_run()
    {
        using var f = new Fixture();
        var script = EyeCandyEntryRef.Make(EyeCandyList.Script, 3)!.Value;
        f.Entries[script] = Entry(0x4000);
        // A run of the same entry that carries a tag (a spell's or a goal's add) is not the script's (run id −1).
        Assert.IsTrue(f.Host.Play(Hero, script, 0, 9));
        Assert.AreEqual(0, f.Host.StopScript(Hero, 3));
        Assert.IsTrue(f.Host.PlayScript(Hero, 3));
        Assert.AreEqual(1, f.Host.StopScript(Hero, 3));
        Assert.AreEqual(1, f.Session.QueryGoals().RunsOf(Hero).Count(r => !r.Done), "the tagged run stays");
    }

    // ARC-SCRIPT-1828 (WE12): the 22 installed scripts that stop a script eye candy (the ground rune tile scripts 2210 to 2230 and the heartbeat 2231 of the main map) are admitted whole where their other commands are: the use, placed and dying sets admit 18 of them; the other
    // four (2213, 2217, 2221, 2225) are refused at line 15 for the focus kind of their loop for (action 18), every portal in the vicinity (19), which another slice holds (the dialog set, which no installed stop script is attached to, admits none of them).
    [TestMethod, TestCategory("RealData")]
    public void The_installed_scripts_that_stop_script_eye_candy_are_admitted_whole_where_their_other_commands_are()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in stop eye-candy check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var content = ScriptContent.Over(opened.Stack!);
        var sets = new (string Name, ScrAdmissionSet Set)[] { ("dialog", AreaScriptHost.Admission), ("use", AreaUseScriptHost.Admission), ("placed", AreaUseScriptHost.PlacedAdmission),
            ("heartbeat", AreaUseScriptHost.HeartbeatAdmission), ("dying", AreaUseScriptHost.DyingAdmission) };
        var counts = sets.ToDictionary(s => s.Name, _ => 0);
        var withStop = new List<int>();
        var refusedByUse = new List<string>();
        var heartbeat = new List<int>();
        foreach (var number in Enumerable.Range(0, ScrCatalogue.MaxScriptNumber + 1))
        {
            if (content.Script(number) is not { Status: ScriptContentStatus.Read, Program: { } program }) continue;
            if (!program.Lines.Any(l => l.OnTrue.Type == ScrCatalogue.Action.StopScriptEyeCandy || l.OnFalse.Type == ScrCatalogue.Action.StopScriptEyeCandy)) continue;
            withStop.Add(number);
            foreach (var (name, set) in sets)
            {
                var result = ScrAdmission.Admit(program, set);
                if (result.IsAdmitted) { counts[name]++; if (name == "heartbeat") heartbeat.Add(number); }
                else if (name == "use") refusedByUse.Add($"{number}:{result.Refusal!.Code}@{result.Refusal.Line}/{result.Refusal.Type}/{result.Refusal.Kind}");
            }
        }
        Console.WriteLine($"STOP-EYE-CANDY-ADMISSION|scripts {withStop.Count}|admitted whole: " + string.Join(", ", counts.Select(c => $"{c.Key} {c.Value}")) + "|use refused: " + string.Join(" ", refusedByUse));
        Assert.AreEqual(22, withStop.Count);
        Assert.AreEqual((0, 18, 18, 18), (counts["dialog"], counts["use"], counts["placed"], counts["dying"]));
        CollectionAssert.AreEqual(new[] { 2231 }, heartbeat);
        CollectionAssert.AreEqual(new[] { "2213:FocusKindNotAdmitted@15/18", "2217:FocusKindNotAdmitted@15/18", "2221:FocusKindNotAdmitted@15/18", "2225:FocusKindNotAdmitted@15/18" }, refusedByUse.Select(r => string.Join('/', r.Split('/').Take(2))).ToArray());
    }
}
