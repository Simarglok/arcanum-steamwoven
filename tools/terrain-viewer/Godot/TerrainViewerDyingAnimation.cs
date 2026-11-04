using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-040: what each NPC lying on the shown map shows. Wanted, Dead and DeathMs are what the session's life state asks for
    // (the kill's dying animation or the fall, and the death they belong to); Load the pose drawn (the fall kept when the dying start refused to replay); StartMs the world time its
    // frames started (null: its last frame held, as for a body already lying when the map and session were first shown); EndsAtMs the
    // world time its dying goal ends and leaves its pool (null for a fall or a held body).
    private sealed record LyingShow(int Wanted, bool Dead, long? DeathMs, AreaFallenLoad Load, long? StartMs, int DelayMs, long? EndsAtMs, bool FallKept = false)
    {
        public int Frames => Load.Frames.Count;
        // A clock set back before the start (a self-test restoring its time) shows the last frame, not the animation again.
        public int FrameAt(long now) => StartMs is { } start && now >= start ? DyingAnimationRules.Frame(now - start, Frames, DelayMs) : Frames - 1;
        public bool Playing(long now) => FrameAt(now) < Frames - 1;
    }

    private readonly Dictionary<(int Map, string Entity), LyingShow> _lyingShows = [];
    private object? _lyingBaseline;
    private string? _lyingFramesDrawn;

    /// <summary>
    /// ARC-RENDER-040: what an NPC the session knocked out or killed shows now, as the original plays it (ledger DA1–DA6). A body lying
    /// when the map and session are first seen holds its last frame (the original's load sets the dead art's last frame). A new fall or
    /// death plays from now: frame 1 at once, one more per 1000 / rate ms, the last kept (<see cref="DyingAnimationRules.Frame"/>); a
    /// death's goal ends one delay after its last frame shows. A death of an NPC lying in its fall (knocked out) is not played (0x45D730):
    /// it keeps the fall (also after a map change or load: the kill's animation becomes the fall) and its goal ends at once. A new death
    /// or knockout (another death time, or dead again after standing) plays again.
    /// </summary>
    private LyingShow ObserveLying((int Map, string Entity) key, int wanted, bool dead, long? deathMs, AreaFallenLoad load, long now, bool firstSight)
    {
        if (_lyingShows.TryGetValue(key, out var shown) && shown.Wanted == wanted && shown.Dead == dead && shown.DeathMs == deathMs) return shown;
        LyingShow next;
        if (firstSight) next = new(wanted, dead, deathMs, load, null, 0, null);
        else if (dead && shown is { Dead: false } && DyingAnimationRules.LiesFallen(shown.Load.Pose!.Animation))
        {
            next = shown with { Wanted = wanted, Dead = true, DeathMs = deathMs, EndsAtMs = now, FallKept = true };
            _deathAnimations[key] = AreaFallenPoses.FallDown;
        }
        else
        {
            // 0x42BC10 has set frame 0 of the dying animation, so the start (0x42D570) plays it.
            var delay = dead ? DyingAnimationRules.Start(load.Pose!.Animation, 0, load.FramesPerSecond)!.Value : DyingAnimationRules.Delay(load.FramesPerSecond);
            next = new(wanted, dead, deathMs, load, now, delay, dead ? now + DyingAnimationRules.EndMs(load.Frames.Count, delay) : null);
        }
        _lyingShows[key] = next;
        GD.Print($"ARC-RENDER-040 lying entity={key.Entity} wanted={wanted} dead={dead} shows={next.Load.Pose!.Animation} frames={next.Frames}"
            + $" delay={next.DelayMs} {(next.StartMs is null ? "held" : ReferenceEquals(next.Load, shown?.Load) ? "kept" : "plays")}"
            + (next.EndsAtMs is { } end ? $" ends-in={end - now}" : ""));
        return next;
    }

    /// <summary>The frame of each lying body still playing its frames, for the fallen bodies' stamp (null when none plays).</summary>
    private string? LyingFramesStamp(long now)
    {
        string? stamp = null;
        foreach (var (key, show) in _lyingShows)
            if (show.Playing(now)) stamp += $"{key.Map}:{key.Entity}:{show.FrameAt(now)}|";
        return stamp;
    }

    /// <summary>Whether the critter's dying goal has not ended yet: its pool of blood waits (0x42FED0 runs when the frames have played).
    /// An NPC with no played dying (its pose does not read, or it is not drawn) ends at once; the hero's dying is its fall
    /// (ARC-COMBAT-021), so where the hero's knockdown runs its pool waits until the dead hero lies down (standing before the fall
    /// starts, or falling).</summary>
    private bool DyingPending(int map, string critter, long now)
    {
        if (critter == Arcanum.Session.SessionEventKinds.PlayerEntity)
            return _readyMode == TerrainViewerMode.CrashSiteArea && _worldActor.Session is AreaGridSession
                && _heroKnockdown.Stage is AreaKnockdownStage.Standing or AreaKnockdownStage.Falling;
        return _lyingShows.TryGetValue((map, critter), out var show) && show.EndsAtMs is { } end && end > now;
    }

    /// <summary>The dying goals still to end, for the pools' stamp: a goal that ends changes it.</summary>
    private string? DyingPendingStamp(int map, long now)
    {
        string? stamp = null;
        foreach (var (key, show) in _lyingShows)
            if (key.Map == map && show.EndsAtMs is { } end && end > now) stamp += key.Entity + "|";
        if (_areaSession?.QueryHeroLife()?.Status.IsDead == true && DyingPending(map, Arcanum.Session.SessionEventKinds.PlayerEntity, now)) stamp += "pc|";
        return stamp;
    }

    /// <summary>Per world frame: while a lying body plays its frames, the fallen bodies are redrawn when a frame changes (the last one
    /// included).</summary>
    private void ProcessLyingAnimations()
    {
        if (_lyingShows.Count == 0 || LyingFramesStamp(WorldMilliseconds()) == _lyingFramesDrawn) return;
        var before = _fallenBodies;
        SyncAreaCombatBodies();
        if (!ReferenceEquals(before, _fallenBodies)) _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }
    /// <summary>ARC-RENDER-040 smoke: the NPC the stage just killed plays its dying animation (its frames on the world clock, its last
    /// frame kept) and leaves no pool of blood before its dying goal ends; the world clock is stepped until it has.</summary>
    private void RequireDyingAnimation(Arcanum.Session.GameSessionOwner session, int runtimeMap, string npc, Arcanum.Data.Area.AreaObjectKey key)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-RENDER-040 dying smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DYING_SMOKE_FAILED");
        }
        Require(_lyingShows.TryGetValue((runtimeMap, npc), out var show) && show.StartMs is not null && show.EndsAtMs is not null,
            "the death started its dying animation: " + show);
        // A death of an NPC the attacks had already knocked out (the staff's stun can do that before the killing blow) is not played (0x45D730):
        // it keeps its fall and its dying goal ends at once (FallKept), so there are no dying frames to see; the rule is what is checked then
        // (the goal ended, no pool before it, and the fall's last frame is kept once the fall has played, below).
        var fallKept = show!.FallKept;
        var frames = new SortedSet<int>();
        var poolBeforeEnd = false;
        var steps = 0;
        for (; steps < 400 && (DyingPending(runtimeMap, npc, WorldMilliseconds()) || (fallKept && show.Playing(WorldMilliseconds()))); steps++)
        {
            frames.Add(_fallenBodies[key].Frame.Frame);
            _areaSmokeTime++;
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            if (DyingPending(runtimeMap, npc, WorldMilliseconds()))
            {
                SyncAreaInventory();
                poolBeforeEnd |= session.QueryBloodPools(new Arcanum.Session.SessionMapId(runtimeMap)).Any(p => p.Critter == npc);
            }
        }
        SyncAreaInventory();
        frames.Add(_fallenBodies[key].Frame.Frame);
        show = _lyingShows[(runtimeMap, npc)];
        GD.Print($"ARC-RENDER-040 smoke dying npc={npc} animation={show.Load.Pose!.Animation} frames={show.Frames} delay={show.DelayMs} drawn={string.Join(",", frames)} steps={steps}");
        Require(!DyingPending(runtimeMap, npc, WorldMilliseconds()), $"the dying goal ended within {steps} world frames");
        Require(!poolBeforeEnd, "no pool of blood before the dying animation has played");
        if (fallKept) GD.Print($"ARC-RENDER-040 smoke dying npc={npc} fall-kept (the target lay knocked out when it was killed)");
        // The killing attack's own last frames already moved the clock on: the frames drawn here are the rest of the animation.
        Require(fallKept || show.Frames <= 4 || frames.Count > 1, $"the dying animation's frames were drawn: {string.Join(",", frames)}");
        Require(frames.Max == show.Frames - 1 && _fallenBodies[key].Frame.Frame == show.Frames - 1, "the last frame is kept");
    }
}
