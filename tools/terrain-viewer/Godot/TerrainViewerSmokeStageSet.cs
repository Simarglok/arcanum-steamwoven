using System.Diagnostics;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-DEV-399: the stages a map smoke runs (<c>ARC_AREA_MAP_SMOKE_ONLY</c>; none named means the whole battery), with a
/// clock per stage. The smoke chain asks <see cref="Contains"/> (a named stage) or <see cref="Stage"/> (a stage the battery also runs)
/// before each stage in the order it runs them, so the answer "yes" is the moment the stage starts and the next "yes" (or
/// <see cref="Finish"/>) the moment it ended. Each ended stage prints <c>ARC-DEV-399 stage-time NAME ms=N</c>, which the smoke runner
/// (tools/local_dev/smoke.py) reads from the run's log into the queue's stage history. The clock changes no answer: it only measures.</summary>
internal sealed class TerrainViewerSmokeStageSet(HashSet<string>? named)
{
    private readonly Stopwatch _clock = new();
    private string? _running;

    /// <summary>Whether the stage is named (false in a battery run); true starts the stage's clock.</summary>
    public bool Contains(string stage)
    {
        if (named?.Contains(stage) != true) return false;
        Begin(stage);
        return true;
    }

    /// <summary>Whether the stage runs: every stage of a battery run, a named stage otherwise; true starts the stage's clock.</summary>
    public bool Stage(string stage)
    {
        if (named is null) { Begin(stage); return true; }
        return Contains(stage);
    }

    /// <summary>Ends the running stage (the last one, or the one a failure interrupted); calling it again prints nothing.</summary>
    public void Finish()
    {
        if (_running is null) return;
        GD.Print($"ARC-DEV-399 stage-time {_running} ms={_clock.ElapsedMilliseconds}");
        _running = null;
    }

    private void Begin(string stage)
    {
        // A stage the chain asks for twice in a row (a stage that two lines run, such as transfer and follower-home) goes on
        // under the clock it already has.
        if (_running == stage) return;
        Finish();
        _running = stage;
        _clock.Restart();
    }
}
