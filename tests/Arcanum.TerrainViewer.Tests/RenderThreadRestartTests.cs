using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-3420: the one restart into the engine's separate rendering thread.</summary>
[TestClass]
public sealed class RenderThreadRestartTests
{
    [TestMethod]
    public void Only_a_first_interactive_start_with_the_setting_on_restarts()
    {
        Assert.IsTrue(RenderThreadRestart.Needed(wanted: true, restarted: false, automated: false));
        Assert.IsFalse(RenderThreadRestart.Needed(wanted: false, restarted: false, automated: false), "off by default: no restart");
        Assert.IsFalse(RenderThreadRestart.Needed(wanted: true, restarted: true, automated: false), "the restarted process never restarts again");
        Assert.IsFalse(RenderThreadRestart.Needed(wanted: true, restarted: false, automated: true), "an automated run keeps its process");
    }

    [TestMethod]
    public void The_exported_game_restarts_with_the_engine_option_and_the_mark()
        => CollectionAssert.AreEqual(new[] { "--render-thread", "separate", "--", RenderThreadRestart.Flag },
            RenderThreadRestart.Arguments([], [], projectPath: null));

    [TestMethod]
    public void The_restart_keeps_other_arguments_and_drops_the_launcher_request()
        => CollectionAssert.AreEqual(
            new[] { "--render-thread", "separate", "--path", "/p", "--audio-driver", "Dummy", "--", "--debug-mode", RenderThreadRestart.Flag },
            RenderThreadRestart.Arguments(["--audio-driver", "Dummy", "--"], ["--launcher", "--debug-mode", RenderThreadRestart.Flag], "/p"));

    [TestMethod]
    public void The_mark_is_taken_from_the_arguments()
    {
        var args = new List<string> { RenderThreadRestart.Flag };
        Assert.IsTrue(RenderThreadRestart.Take(args));
        Assert.AreEqual(0, args.Count);
        Assert.IsFalse(RenderThreadRestart.Take(args));
    }

    [TestMethod]
    public void The_restarted_start_goes_past_show_at_every_start_only()
    {
        foreach (var reason in Enum.GetValues<LauncherShowReason>())
        {
            Assert.AreEqual(reason, RenderThreadRestart.Reason(reason, restarted: false));
            Assert.AreEqual(reason == LauncherShowReason.ShowAtStart ? LauncherShowReason.None : reason, RenderThreadRestart.Reason(reason, restarted: true));
        }
    }
}
