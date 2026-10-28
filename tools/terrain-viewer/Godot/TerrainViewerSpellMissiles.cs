using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-018: a spell's missile in the AREA view (goals 28 and 78, ledger SM rows). When the casting goal reaches its release frame
// and the spell's projectile eye candy (6·spell + 1) has art, the hero's cast launches a missile instead of releasing (0x429C40): the
// secondary cast eye candy plays on the hero, and the missile flies the combat projectile's line from the hero's tile to the target's
// (its art faced in the screen direction, one frame per step) in ticks of 35 ms, testing nothing on the way. When a leg's steps run
// out it waits one pace of its art; then on its destination with its target there (or a tile target) it arrives: the target re-check
// and the BEGIN (SessionSpellCaster.ArriveMissile); a target that moved is followed on a new leg; a refused line, another map or
// another session loses it (LoseMissile, MG44: its run is freed unpaid). The casting goal ends on its own eye candy meanwhile.
public partial class TerrainViewerApp
{
    /// <summary>One spell missile in flight: its run and host, its target (an object, or none for a tile), its destination, its faced
    /// art, its leg (the line, the time the leg started and the ticks taken), the wait after a leg and the frames it stepped.</summary>
    private sealed class SpellMissile
    {
        public required SessionSpellCaster Caster { get; init; }
        public required int Run { get; init; }
        public required int Spell { get; init; }
        public required int Map { get; init; }
        public required string? Target { get; init; }
        public required CellCoord Destination { get; set; }
        public required uint Art { get; init; }
        public required int StepsPerTick { get; init; }
        public required int PaceMs { get; init; }
        public required ProjectileFlight Flight { get; set; }
        public required long LegStartMs { get; set; }
        public long Ticks { get; set; }
        public long? WaitUntilMs { get; set; }
        public long Steps { get; set; }
        public int Frames { get; set; } = 1;
    }

    private readonly List<SpellMissile> _spellMissiles = [];
    private Dictionary<AreaObjectKey, AreaDrawCommand> _spellMissileBodies = [];
    // Smoke seams: each missile's end (the run, how it ended, the legs it flew and the steps it took).
    private readonly List<(int Run, string End, int Legs, long Steps)> _spellMissileEndsForSelfTest = [];
    private readonly Dictionary<int, int> _spellMissileLegs = [];
    // Smoke seam: the missile arts drawn (the faced art ids at frame 0).
    private readonly HashSet<uint> _spellMissilesDrawnForSelfTest = [];

    /// <summary>The spell's missile art (the projectile entry's, 0x458B70), or null when it has none: the cast then releases.</summary>
    private uint? SpellMissileArt(int spell) => EyeCandyTable?.Entry(spell, SpellEyeCandyTable.Projectile) is { Art: { } art } ? art : null;

    /// <summary>
    /// The release frame of a spell with a missile (0x429C40): the run is marked released without a BEGIN, the secondary cast eye
    /// candy plays on the hero (0x456E00) and the missile leaves the hero's tile for the target's (0x4248A0, goal 28). False when the
    /// run could not launch (released or begun already): the goal then ends.
    /// </summary>
    private bool LaunchSpellMissile(SessionSpellCaster caster, SpellCastGoal goal, uint art, long now)
    {
        if (_areaSession is not { } owner || HeroWorldCell() is not { } start || !caster.LaunchMissile(goal.Run)) return false;
        // A missile of an earlier run in the same slot (freed meanwhile) is gone: the run id is the missile's identity.
        foreach (var stale in _spellMissiles.Where(m => m.Run == goal.Run).ToArray()) DropSpellMissile(stale, "its slot was reused");
        PlayEyeCandy(SessionSpellCaster.Hero, SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.SecondaryCast), goal.Run, now);
        var destination = goal.TargetObject is { } target ? EyeCandyCell(owner, target) ?? start : new CellCoord(unchecked((int)goal.TargetTile.X), unchecked((int)goal.TargetTile.Y));
        var direction = ProjectileRules.Direction(start, destination);
        var faced = ProjectileRules.FacedArt(art, direction) ?? art;
        var entry = EyeCandyTable!.Entry(goal.Spell, SpellEyeCandyTable.Projectile);
        var fps = ExtraArt()?.Load(faced).Frame?.FramesPerSecond;
        var leg = SpellMissileRules.Leg(start, destination);
        GD.Print($"ARC-MAGIC-018 missile-launch run={goal.Run} spell={goal.Spell} from={start.X},{start.Y} to={destination.X},{destination.Y}"
            + $" target={goal.TargetObject ?? "tile"} direction={direction} art=0x{faced:X8} speed={entry.ProjectileSpeed} leg={leg.Kind}");
        _spellMissileLegs[goal.Run] = 1;
        var missile = new SpellMissile
        {
            Caster = caster, Run = goal.Run, Spell = goal.Spell, Map = CurrentAreaMap, Target = goal.TargetObject, Destination = destination, Art = faced,
            StepsPerTick = SpellMissileRules.StepsPerTick(entry.ProjectileSpeed), PaceMs = SpellMissileRules.PaceMs(fps),
            // A missile that does not fly (already there, or a refused line) ends below and is never stepped or drawn.
            Flight = leg.Flight ?? new(start, start, [(0, 0)], 1, start, 0, 0, 0), LegStartMs = now,
        };
        switch (leg.Kind)
        {
            // The goal finds the missile on its destination before it builds a line (0x424D90): it arrives at once.
            case SpellMissileLegKind.AtDestination: ArriveSpellMissile(missile, "launched on its target"); break;
            case SpellMissileLegKind.Refused: LoseSpellMissile(missile, "the line is too long"); break;
            default: _spellMissiles.Add(missile); SyncSpellMissileBodies(); break;
        }
        return true;
    }

    /// <summary>Per frame: each missile takes its ticks' steps (stopping where a leg's steps run out), waits its pace after a leg and
    /// then arrives, follows its target or flies its line again; a missile of another map or session is lost.</summary>
    private void ProcessSpellMissiles()
    {
        if (_spellMissiles.Count == 0) return;
        var now = WorldMilliseconds();
        var moved = false;
        var lineMap = _areaSession is null || !SpellMapActive ? null : AreaLineOfFireNow();
        foreach (var missile in _spellMissiles.ToArray())
        {
            // The host may be rebuilt on the same map (a door's state changes its stamp): the missile keeps the host it left with.
            if (lineMap is null || missile.Map != CurrentAreaMap || !ReferenceEquals(missile.Caster.Owner, _areaSession))
            {
                LoseSpellMissile(missile, "the map or session changed");
                continue;
            }
            // Its run was freed meanwhile (a run of the same spell that began cancelled it, QC6): the missile goes with it.
            if (!missile.Caster.MissileFlies(missile.Run)) { DropSpellMissile(missile, "its run was freed"); moved = true; continue; }
            while (_spellMissiles.Contains(missile))
            {
                if (missile.WaitUntilMs is { } wait)
                {
                    if (now < wait) break;
                    EndSpellMissileLeg(missile, wait);
                    moved = true;
                    continue;
                }
                var due = Math.Max(0, (now - missile.LegStartMs) / ProjectileRules.TickMs);
                if (due <= missile.Ticks) break;
                missile.Ticks++;
                moved = true;
                var tick = SpellMissileRules.Tick(missile.Flight, missile.StepsPerTick, lineMap.Contains);
                missile.Steps += tick.Flight.Steps - missile.Flight.Steps;
                missile.Flight = tick.Flight;
                if (tick.OffMap) { LoseSpellMissile(missile, "it left the map"); break; }
                // The tick that ends a leg pops to goal 28 with a pace's wait (goal 78's false step, delay code −2).
                if (tick.LegEnded) missile.WaitUntilMs = missile.LegStartMs + missile.Ticks * ProjectileRules.TickMs + missile.PaceMs;
            }
        }
        if (moved) SyncSpellMissileBodies();
    }

    /// <summary>Goal 28 after a leg (0x424D90, 0x42A180, 0x42BEC0): arrive, follow the target from here, or fly the line again.</summary>
    private void EndSpellMissileLeg(SpellMissile missile, long at)
    {
        missile.WaitUntilMs = null;
        var target = missile.Target is { } id && _areaSession is { } owner ? EyeCandyCell(owner, id) : null;
        var end = SpellMissileRules.LegEnd(missile.Flight.Tile, missile.Destination, missile.Target is not null, target);
        switch (end)
        {
            case SpellMissileLegEnd.Arrive: ArriveSpellMissile(missile, "arrived"); return;
            case SpellMissileLegEnd.Replay: missile.Flight = SpellMissileRules.Replay(missile.Flight); break;
            default:
                var leg = SpellMissileRules.Leg(missile.Flight.Tile, target!.Value);
                GD.Print($"ARC-MAGIC-018 missile-retarget run={missile.Run} from={missile.Flight.Tile.X},{missile.Flight.Tile.Y} to={target.Value.X},{target.Value.Y} leg={leg.Kind}");
                if (leg.Kind == SpellMissileLegKind.Refused) { LoseSpellMissile(missile, "the line to the moved target is too long"); return; }
                missile.Destination = target.Value;
                missile.Flight = leg.Flight!;
                break;
        }
        _spellMissileLegs[missile.Run] = _spellMissileLegs.GetValueOrDefault(missile.Run) + 1;
        missile.LegStartMs = at; missile.Ticks = 0;
    }

    /// <summary>The missile arrived (goal 28's release, 0x429C80): the target re-check and the BEGIN; the missile is destroyed
    /// (0x424D00).</summary>
    private void ArriveSpellMissile(SpellMissile missile, string why)
    {
        _spellMissiles.Remove(missile);
        var mark = missile.Caster.Log.Count;
        var begun = missile.Caster.ArriveMissile(missile.Run);
        Ended(missile, why, $"begun={begun?.Disposition.ToString() ?? "re-check failed"} failure={begun?.Failure}");
        FlushMissileLog(missile, mark, begun is null ? "the target is no longer valid" : null);
        RefreshAreaStatus();
    }

    /// <summary>The missile's goal ended without its release (0x429E70): the missile is destroyed and its run freed unpaid (MG44, fixed
    /// in both rule modes).</summary>
    private void LoseSpellMissile(SpellMissile missile, string why)
    {
        _spellMissiles.Remove(missile);
        var mark = missile.Caster.Log.Count;
        var freed = missile.Caster.LoseMissile(missile.Run);
        Ended(missile, why, $"freed={freed?.Disposition.ToString() ?? "nothing"}");
        FlushMissileLog(missile, mark, "the missile was lost: " + why);
    }

    /// <summary>A missile whose run is gone (freed, or its slot reused) is only removed: nothing of the run is touched.</summary>
    private void DropSpellMissile(SpellMissile missile, string why)
    {
        _spellMissiles.Remove(missile);
        Ended(missile, why, "dropped");
    }

    /// <summary>The log lines of a missile's end: the shown host's new entries, or, for a host replaced on the same map and session
    /// (a door changed its stamp), that host's own entries since <paramref name="mark"/>.</summary>
    private void FlushMissileLog(SpellMissile missile, int mark, string? lead)
    {
        if (!ReferenceEquals(missile.Caster.Owner, _areaSession)) return;
        if (ReferenceEquals(missile.Caster, _spellCaster)) FlushSpellLog(lead);
        else PostSpellLog(lead, missile.Caster.Log.Skip(mark));
    }

    private void Ended(SpellMissile missile, string why, string detail)
    {
        var legs = _spellMissileLegs.GetValueOrDefault(missile.Run);
        _spellMissileLegs.Remove(missile.Run);
        _spellMissileEndsForSelfTest.Add((missile.Run, why, legs, missile.Steps));
        if (_spellMissileEndsForSelfTest.Count > 16) _spellMissileEndsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-018 missile-end run={missile.Run} spell={missile.Spell} why={why} at={missile.Flight.Tile.X},{missile.Flight.Tile.Y}"
            + $" legs={legs} steps={missile.Steps} {detail}");
        SyncSpellMissileBodies();
    }

    /// <summary>Every missile is lost (a map change, a quick load, a replaced session).</summary>
    private void ClearSpellMissiles(string why)
    {
        foreach (var missile in _spellMissiles.ToArray()) LoseSpellMissile(missile, why);
        _spellMissileBodies = [];
    }

    /// <summary>The missiles' draw commands: the faced art at its step's frame on the tile it is on, moved by its pixel offset from that
    /// tile's centre (the combat projectile's draw); additive when translucent, unlit (the projectile object is OF_DONTLIGHT), at the
    /// art id's draw scale.</summary>
    private void SyncSpellMissileBodies()
    {
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        if (_readyArea is { } area && _spellMissiles.Count > 0 && ExtraArt() is { } extra)
        {
            var frames = (AreaFrameOverlay)AreaFrames();
            foreach (var missile in _spellMissiles)
            {
                var aid = EyeCandyArt.WithFrame(missile.Art, (int)(missile.Steps % Math.Max(1, missile.Frames)));
                var load = extra.Load(aid);
                if (load.Frame is not { } frame)
                {
                    if (_groundReported.Add($"spell-missile:{aid}")) GD.Print($"ARC-MAGIC-018 missile-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
                    continue;
                }
                missile.Frames = Math.Clamp(frame.FramesPerGroup, 1, 127);
                frames.Add(frame.Key, frame.Frame);
                _extraVisuals[frame.Visual.Key] = frame.Visual;
                var key = new AreaObjectKey("spell-missile", area.Snapshot.RuntimeMap, AreaSourceKind.Mobile, null, 0, 0,
                    missile.Run.ToString(System.Globalization.CultureInfo.InvariantCulture), 0);
                var tile = missile.Flight.Tile;
                var command = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, tile.X, tile.Y, flat: false);
                var start = LineOfFireRules.Screen(missile.Flight.Start);
                var here = LineOfFireRules.Screen(tile);
                var dx = checked((int)(start.X + missile.Flight.X - here.X));
                var dy = checked((int)(start.Y + missile.Flight.Y - here.Y));
                var percent = EyeCandyArt.DrawScales[EyeCandyArt.Scale(aid)];
                var at = command.Destination;
                var anchorX = at.X + frame.Visual.HotX + dx; var anchorY = at.Y + frame.Visual.HotY + dy;
                var destination = new Arcanum.Data.Terrain.TerrainRect(anchorX - frame.Visual.HotX * percent / 100, anchorY - frame.Visual.HotY * percent / 100,
                    Math.Max(1, frame.Visual.Width * percent / 100), Math.Max(1, frame.Visual.Height * percent / 100));
                var translucent = EyeCandyArt.Translucent(aid);
                bodies[key] = command with { Destination = destination, Additive = translucent, Unlit = true };
                _spellMissilesDrawnForSelfTest.Add(missile.Art);
            }
        }
        if (bodies.Count == 0 && _spellMissileBodies.Count == 0) return;
        _spellMissileBodies = bodies;
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>ARC-MAGIC-018: the casting goal's facing turn (0x42C850): the hero turns at once toward its target object, drawn while
    /// it stands on its cell.</summary>
    private void TurnHeroForCast(SpellFacing? facing)
    {
        if (facing is not { Kind: SpellFacingKind.Turned } turned || _worldActor.Session is not AreaGridSession grid) return;
        var octant = DiagnosticGridActor.Octant((NeighborDirection)turned.Rotation);
        _worldActor.TurnedFacing = (octant, grid.State.Facing);
        GD.Print($"ARC-MAGIC-018 caster-turn rotation={turned.Rotation} octant={octant}");
        PublishWorldSelection();
    }
}
