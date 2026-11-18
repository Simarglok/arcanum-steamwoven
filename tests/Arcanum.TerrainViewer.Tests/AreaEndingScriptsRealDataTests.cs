using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Scripting.Scr;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-RENDER-1879 opt-in (<c>DAT_ROOT</c>, the game folder; docs/research/bink-movies.md section 7, FR-MEDIA-002): the installed dying scripts of Kerghan, 2863 and 3061 (attachment point 12), run through the
/// use-script host's dying run (<see cref="AreaUseScriptHost.RunDying"/>) against a session: both are admitted whole by <see cref="AreaUseScriptHost.DyingAdmission"/>, play their two movies (2, then 5) and ask for the end
/// of the game (script action 132). The end-game script 997 then queues the slides the viewer shows (<see cref="AreaSlideScripts"/>). Numbers only; no picture, movie or text is read or printed.
/// </summary>
[TestClass]
public sealed class AreaEndingScriptsRealDataTests
{
    private static AreaObjectRecord Kerghan(int index, int script) => Scripted(index, ScrPoint.Dying, script);

    // A row with the installed script at one attachment point (the dying point of Kerghan's prototypes, the use point of the Vendigroth underground's object).
    private static AreaObjectRecord Scripted(int index, int point, int script)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(point, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    [TestMethod, TestCategory("RealData")]
    public void Kerghans_dying_scripts_play_movies_2_and_5_and_ask_for_the_end_of_the_game()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        foreach (var script in new[] { 2863, 3061 })
        {
            var read = content.Script(script);
            Assert.AreEqual(Arcanum.Data.Scripts.ScriptContentStatus.Read, read.Status, $"script {script} {read.Detail}");
            var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.DyingAdmission);
            Assert.IsTrue(admission.IsAdmitted, $"script {script} is admitted whole by the dying set: {admission.Refusal}");

            using var session = ConversationSessionFixture.CreateWithHero();
            var kerghan = Kerghan(1, script);
            var host = new AreaUseScriptHost(session, 1, [kerghan], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var ran = host.RunDying(AreaUseScriptHost.EntityId(kerghan), host.Pc, session.Rules.Scripts);
            var fades = host.TakeFades();
            var endGame = host.TakeEndGame();
            Console.WriteLine($"script {script}: outcome {ran.Outcome}, run {ran.Run?.Outcome}/{ran.Run?.Stop}, fades {string.Join(",", fades.Select(f => $"movie {f.Movie} track {f.Track} advance {f.AdvanceMs}"))}, end game {endGame}, eye candy {host.TakeEyeCandies().Count}");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            CollectionAssert.AreEqual(new[] { 2, 5 }, fades.Select(f => f.Movie).ToArray(), $"script {script}: movies in order");
            Assert.IsTrue(endGame, $"script {script} asks for the end of the game");
            Assert.IsFalse(host.TakeEndGame(), "taken once");
        }
    }

    // The item the Vendigroth underground's use script 2862 tests its extra object for: an item row named 6711 (field 21), beside the object that carries the script.
    internal static AreaObjectRecord VendigrothItem(int index)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
        var fields = new List<AreaFieldValue> { new(21, AreaPresence.Value, AreaValueOrigin.Instance, 6711L, [], null) };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    /// <summary>ARC-RENDER-2197: the Vendigroth underground's use script 2862 (use point 1 of a type-14 object) is admitted whole by the use set (float line, 23, included) and runs through the use host (<see cref="AreaUseScriptHost.RunUseScript"/>): used with the item
    /// it tests its extra object for (name 6711) it plays its movies 3 and 5 and asks for the end of the game (script action 132); used with nothing it only floats its line 1 over the object. Numbers only; no picture, movie or text is read or printed.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_Vendigroth_use_script_2862_plays_movies_3_and_5_and_asks_for_the_end_of_the_game_when_used_with_its_item()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var read = content.Script(2862);
        Assert.AreEqual(Arcanum.Data.Scripts.ScriptContentStatus.Read, read.Status, $"script 2862 {read.Detail}");
        var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.Admission);
        Assert.IsTrue(admission.IsAdmitted, $"script 2862 is admitted whole by the use set: {admission.Refusal}");

        using var session = ConversationSessionFixture.CreateWithHero();
        var row = Scripted(1, ScrPoint.Use, 2862);
        var item = VendigrothItem(2);
        var host = new AreaUseScriptHost(session, 1, [row, item], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
        var ran = host.RunUseScript(AreaUseScriptHost.EntityId(row), host.Pc, new ScrObjectRef(1, AreaUseScriptHost.EntityId(item)), session.Rules.Scripts);
        var fades = host.TakeFades();
        var endGame = host.TakeEndGame();
        Console.WriteLine($"script 2862 with the item: outcome {ran.Outcome}, run {ran.Run?.Outcome}/{ran.Run?.Stop}, fades {string.Join(",", fades.Select(f => $"movie {f.Movie} track {f.Track} advance {f.AdvanceMs}"))}, end game {endGame}, floats {host.TakeFloats().Count}");
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
        CollectionAssert.AreEqual(new[] { 3, 5 }, fades.Select(f => f.Movie).ToArray(), "movies in order");
        Assert.IsTrue(endGame, "script 2862 asks for the end of the game");
        Assert.IsFalse(host.TakeEndGame(), "taken once");

        // Used with nothing, the extra object is missing and the script floats its hint line instead: no movie, no end of the game.
        using var plain = ConversationSessionFixture.CreateWithHero();
        var plainHost = new AreaUseScriptHost(plain, 1, [row], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
        var ranPlain = plainHost.Run(row, plain.Rules.Scripts);
        Console.WriteLine($"script 2862 plain: outcome {ranPlain.Outcome}, run {ranPlain.Run?.Outcome}/{ranPlain.Run?.Stop}");
        Assert.AreEqual(0, plainHost.TakeFades().Count, "no movie without the item");
        Assert.IsFalse(plainHost.TakeEndGame(), "no end of the game without the item");
    }

    /// <summary>ARC-SCRIPT-2198: Kerghan's dialog script 1105 (dialog point 9) runs under the dialog host with the shown map's use host answering: its ending lines (88, 91) play movie 2 or 3 through fade and ask for the end of the game,
    /// and the whole script is admitted (loops, change script, disband, create item, transform, call script, call-ex); the movies reach the viewer through <see cref="AreaScriptHost.TakeFades"/> and <see cref="AreaScriptHost.TakeEndGame"/>.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Kerghans_dialog_script_plays_its_ending_movies_and_asks_for_the_end_of_the_game_through_the_dialog_host()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        foreach (var (line, movie) in new[] { (88, 2), (91, 3) })
        {
            using var session = ConversationSessionFixture.CreateWithHero();
            var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
            var fields = new List<AreaFieldValue>
            {
                new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [0, 0, 1105])], null),
                new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
            };
            var row = new AreaObjectRecord(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
            var speaker = new AreaSpeaker(0, row, 1105, row.WorldX!.Value, row.WorldY!.Value);
            var map = new SessionMapId(1);
            var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(row)], 50);
            var use = new AreaUseScriptHost(session, 1, [row], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = () => use };
            Assert.AreSame(AreaScriptHost.SkillTalkAdmission, host.RunAdmission);
            var admission = ScrAdmission.Admit(content.Script(1105).Program!, host.RunAdmission);
            Assert.IsTrue(admission.IsAdmitted, $"1105 is admitted whole: {admission.Refusal}");
            var run = ScrDispatcher.RunObject(new(ScrPoint.Dialog, host.Object(speaker), host.Pc, null, line), host, new() { Rules = session.Rules.Scripts, Admission = host.RunAdmission });
            Console.WriteLine($"1105 from line {line}: {run.Outcome}/{run.Stop}, fades {string.Join(",", run.Effects.Fades.Select(f => $"movie {f.Movie} seconds {f.Seconds}"))}, slides {run.Effects.Slides.Count}");
            Assert.AreNotEqual(ScrOutcome.Refused, run.Outcome, run.Refusal?.ToString());
            Assert.IsTrue(host.Apply(run.Effects), $"line {line}: the host applies the run");
            CollectionAssert.AreEqual(new[] { movie }, host.TakeFades().Select(f => f.Movie).ToArray(), $"line {line}: the movie");
            Assert.IsTrue(host.TakeEndGame(), $"line {line}: the end of the game");
        }
    }

    private static AreaObjectRecord Npc(int index, int dialog, int name)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, dialog == 0 ? [] : [new AreaSparseElement(ScrPoint.Dialog, [0, 0, dialog])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
            new(21, AreaPresence.Value, AreaValueOrigin.Instance, (long)name, [], null),
        };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private sealed record Scene(GameSessionOwner Session, AreaObjectRecord Kerghan, AreaUseScriptHost Use, AreaScriptHost Host, List<string> Made, List<string> Attacks) : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public ScrRunResult Run(int line) => ScrDispatcher.RunObject(new(ScrPoint.Dialog, Host.Object(new AreaSpeaker(0, Kerghan, 1105, Kerghan.WorldX!.Value, Kerghan.WorldY!.Value)), Host.Pc, null, line), Host,
            new() { Rules = Session.Rules.Scripts, Admission = Host.RunAdmission });
    }

    // Kerghan with the NPCs of the vicinity the battle script names (by name number), the PC beside them, and a world that can make a critter and take an attack.
    private static Scene Stage(Arcanum.Data.Scripts.ScriptContent content, params int[] nearNames)
    {
        var session = ConversationSessionFixture.CreateWithHero();
        var kerghan = Npc(1, 1105, 0);
        var rows = new List<AreaObjectRecord> { kerghan };
        for (var i = 0; i < nearNames.Length; i++) rows.Add(Npc(2 + i, 0, nearNames[i]));
        var made = new List<string>(); var attacks = new List<string>();
        var world = new AreaScriptWorld(() => new(12, 20), _ => false, prototype => prototype == 8278 ? new AreaPrototypeItem(new(SessionItemType.Food, 8278, 7001, 8278), 0x1234, new(1)) : null, () => null, Critter: prototype => prototype == 27368, StandardReaction: npc => session.QueryReaction(npc, new(50, Arcanum.Simulation.Character.CharacterRace.Human, false, false), session.QueryHeroReactionFacts(0, 0)!).View?.Effective, CritterMade: made.Add, Vitals: _ => new AreaScriptVitals(40, 40, 20, 20, 0),
            ScriptAttack: (attacker, victim) => { attacks.Add(attacker + ">" + victim); return "ok"; });
        var map = new SessionMapId(1);
        var speaker = new AreaSpeaker(0, kerghan, 1105, kerghan.WorldX!.Value, kerghan.WorldY!.Value);
        var state = new AreaConversationState(session, map, rows.Select(AreaConversationState.EntityId).ToArray(), 50);
        var use = new AreaUseScriptHost(session, 1, rows, [], content, world);
        var host = new AreaScriptHost(session, map, [speaker], state, content) { QueryHost = () => use };
        return new(session, kerghan, use, host, made, attacks);
    }

    /// <summary>ARC-SCRIPT-2440: Kerghan's change of shape (1105 line 80) through the dialog host: the NPC becomes a created critter of prototype 27368 on its tile, is destroyed, and the later lines of the run (three script eye candies, the deferred call of line 86)
    /// name the made critter.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Kerghans_change_of_shape_makes_a_critter_and_the_later_lines_name_it()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        using var scene = Stage(content);
        var run = scene.Run(80);
        Console.WriteLine($"1105 from line 80: {run.Outcome}/{run.Stop}, eye candies {run.Effects.EyeCandies.Count}, calls {run.Effects.Calls.Count}, states {string.Join(",", run.Effects.ObjectStates.Select(r => r.Change))}");
        Assert.AreNotEqual(ScrOutcome.Refused, run.Outcome, run.Refusal?.ToString());
        Assert.IsTrue(scene.Host.Apply(run.Effects), "the host applies the run");
        var critter = scene.Use.LastTransformed ?? throw new AssertFailedException("no critter made");
        CollectionAssert.AreEqual(new[] { critter.Entity }, scene.Made);
        Assert.IsTrue(AreaUseScriptHost.Destroyed(scene.Session, 1, AreaConversationState.EntityId(scene.Kerghan)));
        var eyes = scene.Host.TakeEyeCandies();
        Assert.IsTrue(eyes.Count >= 1 && eyes.All(e => e.Object.Code == critter.Entity), "every eye candy of the later lines plays on the made critter: " + string.Join(",", eyes.Select(e => e.Object.Code)));
        var call = scene.Session.QueryScriptCalls().Single().Call;
        Assert.AreEqual((1105, 86), (call.Script, call.Line));
        Assert.AreEqual(critter.Entity, call.Attachee?.Entity);
    }

    /// <summary>ARC-SCRIPT-2440: the call script 2719 of line 53 (the battle: loops over the vicinity) runs under the dialog host with NPCs of the vicinity named as the script names them; the run is applied, nothing is refused.</summary>
    [TestMethod, TestCategory("RealData")]
    public void Kerghans_battle_callee_runs_over_a_populated_vicinity_through_the_dialog_host()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        using var scene = Stage(content, 6411, 6426, 6557, 6617);
        var run = scene.Run(53);
        Console.WriteLine($"1105 from line 53: {run.Outcome}/{run.Stop}, refusal {run.Refusal}, states {string.Join(",", run.Effects.ObjectStates.Select(r => r.Change))}, floats {run.Effects.Floats.Count}, eye candies {run.Effects.EyeCandies.Count}");
        Assert.AreNotEqual(ScrOutcome.Refused, run.Outcome, run.Refusal?.ToString());
        Assert.IsTrue(run.Effects.ObjectStates.Count > 0, "the callee's loops reached the NPCs of the vicinity");
        Assert.IsTrue(scene.Host.Apply(run.Effects), "the host applies the run");
        Console.WriteLine($"attacks {scene.Attacks.Count}");
    }

    /// <summary>ARC-SCRIPT-2440 (acceptance): every line of Kerghan's dialog script 1105 run as an entry (a reply's script line) under the dialog host with a populated vicinity ends without a refusal, and what it asks for is applied by the host, except line 57 (issue #2505).</summary>
    [TestMethod, TestCategory("RealData")]
    public void Every_entry_line_of_Kerghans_dialog_script_runs_and_applies_without_a_refusal()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var count = content.Script(1105).Program!.Count;
        var failures = new List<string>();
        for (var line = 0; line < count; line++)
        {
            using var scene = Stage(content, 6411, 6426, 6557, 6617);
            var run = scene.Run(line);
            if (run.Outcome == ScrOutcome.Refused || run.Stop is ScrStop.Refused or ScrStop.NotModelled) { failures.Add($"line {line}: {run.Outcome}/{run.Stop} {run.Refusal} stop line {run.StopLine} trace {string.Join(" ", run.Trace.Select(t => $"{t.Line}:c{t.Condition}/a{t.Action}"))}"); continue; }
            if (!scene.Host.Apply(run.Effects)) failures.Add($"line {line}: the host refused the run ({scene.Use.TalkStateRefusal(run.Effects)})");
        }
        // Line 57 (a loop over the vicinity that reads each critter's reaction, action 29) reads an NPC that is no dialog speaker through the use host (ARC-SCRIPT-2505, #2505): nothing refuses.
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }
}
