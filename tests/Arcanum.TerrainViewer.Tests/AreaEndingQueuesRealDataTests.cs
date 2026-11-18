using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-RENDER-2197 opt-in (<c>DAT_ROOT</c>, the game folder; ledger SS1, SS2, FR-MEDIA-002): which slides the end-game script 997 and the ending scripts under it (2921–2928) queue for the global flags and the hero's quest states that pick the endings,
/// run against a session with a hero (<see cref="AreaSlideScripts"/>). 997 picks slide 1, 3 or 4 by the flags 2632, 2053 and 2050 and otherwise calls 2928, whose chain of quest states and flags queues the slides in front of the common tail 20, 33,
/// 39, 44 (the scripts 2927 down to 2921). Slide numbers only; no picture, voice or text is read or printed. The smoke stage <c>slides</c> walks four of these endings through the viewer with the installed pictures.
/// </summary>
[TestClass]
public sealed class AreaEndingQueuesRealDataTests
{
    private static (int Quest, SessionQuestState State) Quest(int quest, SessionQuestState state) => (quest, state);

    private static int[] Queue(Arcanum.Data.Scripts.ScriptContent content, int[] flags, (int Quest, SessionQuestState State)[] quests)
    {
        using var session = ConversationSessionFixture.CreateWithHero();
        foreach (var flag in flags)
        {
            var receipt = session.Flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), session.Flags.Version, new FlagId(flag), true));
            Assert.AreEqual(GlobalFlagWriteDisposition.Applied, receipt.Disposition, $"flag {flag}: {receipt.Reason}");
        }
        foreach (var (quest, state) in quests)
        {
            var set = session.SetQuestState(session.Version, quest, state);
            Assert.AreEqual(GameSessionDisposition.Changed, set.Disposition, $"quest {quest} {state}: {set.Failure}");
        }
        var result = AreaSlideScripts.Run(AreaSlideKind.EndGame, content, session);
        Assert.IsNotNull(result.Stop, result.Detail);
        return result.Slides.ToArray();
    }

    [TestMethod, TestCategory("RealData")]
    public void The_end_game_script_queues_the_slides_of_the_ending_the_flags_and_quest_states_pick()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in ending check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var cases = new (string Name, int[] Flags, (int Quest, SessionQuestState State)[] Quests, int[] Slides)[]
        {
            // 997 itself: one slide by a flag, the first flag (2632) before the others.
            ("flag 2632", [2632], [], [1]),
            ("flag 2053", [2053], [], [3]),
            ("flag 2050", [2050], [], [4]),
            ("flags 2632 and 2053", [2632, 2053], [], [1]),
            ("flags 2053 and 2050", [2053, 2050], [], [3]),
            // 2928 and the chain under it, with no flag of 997 set (a new game is the first row).
            ("nothing set", [], [], [46, 20, 33, 39, 44]),
            ("quest 1058 completed", [], [Quest(1058, SessionQuestState.Completed)], [6, 46, 20, 33, 39, 44]),
            ("quest 1058 completed, flag 1016", [1016], [Quest(1058, SessionQuestState.Completed)], [6, 10, 20, 33, 39, 44]),
            ("flag 1016", [1016], [], [10, 20, 33, 39, 44]),
            ("flag 1015", [1015], [], [10, 20, 33, 39, 44]),
            ("flag 1014", [1014], [], [7, 20, 33, 39, 44]),
            ("flag 1016, quest 1152 completed", [1016], [Quest(1152, SessionQuestState.Completed)], [9, 20, 33, 39, 44]),
            ("flags 1016 and 2640, quest 1152 completed", [1016, 2640], [Quest(1152, SessionQuestState.Completed)], [8, 20, 33, 39, 44]),
            ("quest 1146 completed", [], [Quest(1146, SessionQuestState.Completed)], [46, 11, 20, 33, 39, 44]),
            ("quest 1146 completed, flag 2430", [2430], [Quest(1146, SessionQuestState.Completed)], [46, 20, 33, 39, 44]),
        };
        var failures = new List<string>();
        foreach (var (name, flags, quests, slides) in cases)
        {
            var queued = Queue(content, flags, quests);
            Console.WriteLine($"{name}: {string.Join(",", queued)}");
            if (!queued.SequenceEqual(slides)) failures.Add($"{name}: {string.Join(",", queued)} instead of {string.Join(",", slides)}");
        }
        Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
    }
}
