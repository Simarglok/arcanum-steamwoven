using Arcanum.Data.Area;
using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
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
// ARC-WORLD-105: an NPC's casting goal (the AI host) launches at the same node; its missile leaves the NPC's live tile and flies,
// arrives and is lost the same way.
public partial class TerrainViewerApp
{
    // ARC-DEV-139 (MIG13 S8): a missile is a created object of the session that owns its run (goal 28, 78 per leg), stepped by the frame step through the router; the host is per session. The seams below are
    // the smoke's: each missile's end (the run, how it ended, the legs it flew and the steps it took).
    private AreaMissileHost? _missileHost;
    private Dictionary<AreaObjectKey, AreaDrawCommand> _spellMissileBodies = [];
    private readonly List<(int Run, string End, int Legs, long Steps)> _spellMissileEndsForSelfTest = [];
    // Smoke seam: the missile arts drawn (the faced art ids at frame 0).
    private readonly HashSet<uint> _spellMissilesDrawnForSelfTest = [];
    // The steps the host took when the bodies were last drawn: a frame draws again only when a tick ran.
    private int _missileDrawnSteps;

    /// <summary>The missiles in flight now (the host's, in the order they left); none without a host.</summary>
    private IReadOnlyList<SpellMissile> _spellMissiles => _missileHost?.Missiles ?? [];

    /// <summary>The missile host of the shown session: a new session (a reload, a carried session) loses the old host's missiles first.</summary>
    private AreaMissileHost MissileHost()
    {
        if (_missileHost is { } host && ReferenceEquals(host.Session, _areaSession)) return host;
        _missileHost?.LoseAll("the session changed");
        var session = _areaSession!;
        return _missileHost = new(session, MissileLineMap, id => EyeCandyCell(session, id), OnMissileFinished);
    }

    // The cells of the shown map for a missile that flew on it (null when the shown map, or the session, is another one).
    private Func<CellCoord, bool>? MissileLineMap(SpellMissile missile) =>
        _areaSession is null || !ReferenceEquals(missile.Caster.Owner, _areaSession) || missile.Map != CurrentAreaMap || !SpellMapActive || AreaLineOfFireNow() is not { } lines ? null : lines.Contains;

    /// <summary>The spell's missile art (the projectile entry's, 0x458B70), or null when it has none: the cast then releases.</summary>
    private uint? SpellMissileArt(int spell) => EyeCandyTable?.Entry(spell, SpellEyeCandyTable.Projectile) is { Art: { } art } ? art : null;

    /// <summary>
    /// The release frame of a spell with a missile (0x429C40): the run is marked released without a BEGIN, the secondary cast eye
    /// candy plays on the hero (0x456E00) and the missile leaves the hero's tile for the target's (0x4248A0, goal 28). False when the
    /// run could not launch (released or begun already): the goal then ends.
    /// </summary>
    private bool LaunchSpellMissile(SessionSpellCaster caster, SpellCastGoal goal, uint art, long now)
    {
        if (_areaSession is null || HeroWorldCell() is not { } start || !caster.LaunchMissile(goal.Run)) return false;
        FlySpellMissile(caster, SessionSpellCaster.Hero, start, goal.Run, goal.Spell, goal.TargetObject, goal.TargetTile, art, now);
        return true;
    }

    /// <summary>
    /// ARC-WORLD-105: an NPC's launch, which the AI host's casting goal made at its play's action frame (0x429C40, the same node as the
    /// hero's, AC8): the missile flies from the NPC's live tile as the hero's does. A missile that cannot fly here (no cell, no art, a
    /// run gone) is lost at once (MG44: its run freed unpaid).
    /// </summary>
    private void LaunchNpcSpellMissile(AreaAiNotice notice, long now)
    {
        if (notice.Caster is not { } caster || !caster.MissileFlies(notice.Run)) return;
        if (ReferenceEquals(caster.Owner, _areaSession) && _areaSession is { } owner && EyeCandyCell(owner, notice.Npc) is { } start
            && SpellMissileArt(notice.Spell) is { } art && owner.QuerySpellRuns()[notice.Run] is { } run)
        {
            FlySpellMissile(caster, notice.Npc, start, notice.Run, notice.Spell, run.Target?.Id, run.TargetTile, art, now);
            return;
        }
        var freed = caster.LoseMissile(notice.Run);
        GD.Print($"ARC-WORLD-105 missile-not-flown npc={notice.Npc} run={notice.Run} spell={notice.Spell} freed={freed?.Disposition.ToString() ?? "nothing"}");
    }

    // The launch after the session's (0x429C40): the secondary cast eye candy on the caster (0x456E00) and the missile from the caster's
    // tile to its target's (0x4248A0, goal 28): the missile host creates the object and starts its run.
    private void FlySpellMissile(SessionSpellCaster caster, string casterObject, CellCoord start, int run, int spell, string? targetObject,
        SpellLocation targetTile, uint art, long now)
    {
        var owner = caster.Owner;
        PlayEyeCandy(casterObject, SpellEyeCandyRules.EntryIndex(spell, SpellEyeCandyRules.SecondaryCast), run, now);
        var destination = targetObject is { } target ? EyeCandyCell(owner, target) ?? start : new CellCoord(unchecked((int)targetTile.X), unchecked((int)targetTile.Y));
        var direction = ProjectileRules.Direction(start, destination);
        var faced = ProjectileRules.FacedArt(art, direction) ?? art;
        var entry = EyeCandyTable!.Entry(spell, SpellEyeCandyTable.Projectile);
        var fps = ExtraArt()?.Load(faced).Frame?.FramesPerSecond;
        var leg = SpellMissileRules.Leg(start, destination);
        GD.Print($"ARC-MAGIC-018 missile-launch run={run} spell={spell} caster={casterObject} from={start.X},{start.Y} to={destination.X},{destination.Y}"
            + $" target={targetObject ?? "tile"} direction={direction} art=0x{faced:X8} speed={entry.ProjectileSpeed} leg={leg.Kind}");
        var launched = MissileHost().Launch(caster, casterObject, CurrentAreaMap, start, destination, run, spell, targetObject, faced,
            SpellMissileRules.StepsPerTick(entry.ProjectileSpeed), SpellMissileRules.PaceMs(fps));
        if (launched) SyncSpellMissileBodies();
    }

    /// <summary>Per frame: the missiles' runs step in the frame step (the router hands them to the host); the draw follows a tick.</summary>
    private void ProcessSpellMissiles()
    {
        if (_missileHost is not { } host) return;
        if (host.Steps == _missileDrawnSteps) return;
        _missileDrawnSteps = host.Steps;
        SyncSpellMissileBodies();
    }

    // A missile's end (arrived, lost, dropped), after the host acted: the diagnostic line, the caster's log and, for an NPC's, the AI's notice.
    private void OnMissileFinished(MissileFinished end)
    {
        var missile = end.Missile;
        Ended(missile, end.Why, end.Detail);
        switch (end.Outcome)
        {
            case MissileOutcome.Arrived:
                FlushMissileLog(missile, end.LogMark, end.Begun is null ? "the target is no longer valid" : null);
                // ARC-WORLD-105: an NPC's missile hits as its release would.
                if (missile.CasterObject != SessionSpellCaster.Hero && end.Begun is not null) { _aiMissileArrivals++; AiSpellTook(); }
                else RefreshAreaStatus();
                break;
            case MissileOutcome.Lost:
                FlushMissileLog(missile, end.LogMark, "the missile was lost: " + end.Why);
                break;
        }
    }

    // The end nodes of a missile's run the session ended by itself (an interrupt, a map's close): the missile host's.
    private void MissileRunEnded(GoalRunEnd end) => _missileHost?.EndNodes(end);

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
        _spellMissileEndsForSelfTest.Add((missile.Run, why, missile.Legs, missile.Steps));
        if (_spellMissileEndsForSelfTest.Count > 16) _spellMissileEndsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-MAGIC-018 missile-end run={missile.Run} spell={missile.Spell} why={why} at={missile.Flight.Tile.X},{missile.Flight.Tile.Y}"
            + $" legs={missile.Legs} steps={missile.Steps} {detail}");
        SyncSpellMissileBodies();
    }

    /// <summary>Every missile is lost (a map change, a quick load, a replaced session).</summary>
    private void ClearSpellMissiles(string why)
    {
        _missileHost?.LoseAll(why);
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
