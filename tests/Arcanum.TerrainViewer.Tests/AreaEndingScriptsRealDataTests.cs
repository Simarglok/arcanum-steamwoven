using Arcanum.Data.Area;
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
    private static AreaObjectRecord Kerghan(int index, int script)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dying, [0, 0, script])], null),
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
}
