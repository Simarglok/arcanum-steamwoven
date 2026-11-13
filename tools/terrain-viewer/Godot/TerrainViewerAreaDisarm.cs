using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SKILLS-1156 (E6-a second half, ledger SP3, TT5, TT6): the disarm traps skill in map mode. The hotkey bar's Disarm traps entry arms the
// skill cursor (interface mode 2, skill target, as the pick pocket's); the next left press on a trapped chest, door, scenery or a known trap
// object runs the hero's disarm roll (GameSessionOwner.RollHeroDisarmTrap through AreaTraps.Disarm: the turn-based charge of 4 action points, the
// check with the trap object's difficulty, the master's third roll, success removes the trap, an expert's critical success makes its device in the
// pack, a critical failure sets it off); a right press, a window or another cursor cancels. A trap the hero does not know is no trap to disarm
// (TT5): the press says so and rolls nothing. NOT READY: the follower who acts instead of the hero (0x57A620), the reach of the press (the original's
// skill-bar press builds the invocation at once; no walk is read for it) and the interface hooks' messages (the status line carries the outcome).
public partial class TerrainViewerApp
{
    // True while the armed skill cursor is the disarm traps one (the pick pocket's shares interface mode 2).
    private bool _disarmArmed;

    /// <summary>The last disarm press, for the smoke.</summary>
    private AreaTrapDisarm? _lastDisarm;

    /// <summary>ARC-SKILLS-1156: the armed skill cursor is the disarm traps one.</summary>
    private bool DisarmArmed => _disarmArmed && TargetModeOpen(InterfaceMode.SkillTarget);

    /// <summary>The hotkey bar's Disarm traps entry (the skill bar's start): arms the skill cursor; the next left press on a trap runs the skill.</summary>
    private string ArmDisarmTraps()
    {
        if (_areaSession?.QueryHero() is null || !InventoryActive || AreaTrapsNow() is null) return "disarm traps: no hero, no area inventory or no trap host here";
        CancelArmedLockpicks("disarm traps armed");
        CancelSpellTargeting("disarm traps armed");
        CancelPendingOrder("disarm traps armed");
        if (PickPocketArmed) CancelPickPocket("disarm traps armed");
        if (!OpenTargetMode(InterfaceMode.SkillTarget, _ => _disarmArmed = false)) return "disarm traps: not ready (a window is open)";
        _disarmArmed = true;
        GD.Print("ARC-SKILLS-1156 disarm armed");
        return "disarm traps: press a trapped object";
    }

    private void CancelDisarm(string why)
    {
        if (!DisarmArmed) { _disarmArmed = false; return; }
        CloseTargetMode(InterfaceMode.SkillTarget);
        _disarmArmed = false;
        _areaUseStatus = "disarm traps cancelled: " + why;
    }

    /// <summary>The armed press: the pressed object's known trap (a chest, door, scenery or trap object under the pixel), else a known trap object on the pressed tile,
    /// runs the skill; anything else cancels it. The press leaves the skill's cursor mode.</summary>
    private void DisarmPress(Vector2 screen)
    {
        if (DisarmArmed) CloseTargetMode(InterfaceMode.SkillTarget);
        _disarmArmed = false;
        if (_areaSession is null || _readyArea is not { } area || AreaTrapsNow() is not { } traps || ActorCell() is null)
        {
            _areaUseStatus = "disarm traps cancelled: not ready"; RefreshAreaStatus(); return;
        }
        CancelTalkApproach("a disarm press replaced it"); CancelAreaUseApproach("a disarm press replaced it"); CancelInventoryApproach(null);
        var row = TrapTargetAt(screen, area, traps);
        if (row is null) { _areaUseStatus = "disarm traps: no known trap there"; GD.Print("ARC-SKILLS-1156 disarm press found no known trap"); RefreshAreaStatus(); return; }
        PerformDisarm(row, traps);
    }

    private AreaObjectRecord? TrapTargetAt(Vector2 screen, AreaLoadResult area, AreaTraps traps)
    {
        if (AreaObjectAtScreen(screen) is { } key && area.Snapshot.Objects.FirstOrDefault(o => o.Key == key) is { } hit && traps.KnownTrap(hit)) return hit;
        // A trap object is not drawn until spotted and then has no pixels of its own: the pressed tile's known trap objects are the target.
        var chart = _canvas!.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        return AreaCellPicking.Pick(new(chart.X, chart.Y)) is { } cell ? traps.KnownTrapObjectsOn(cell).FirstOrDefault() : null;
    }

    /// <summary>The skill itself on a row: one roll through the session, its consequences on the status line and in the log.</summary>
    private void PerformDisarm(AreaObjectRecord row, AreaTraps traps)
    {
        var session = _areaSession!;
        ResumeLoadedRound();
        var world = session.QueryCombatRound() is not null ? AreaCombatWorldNow() : null;
        // ARC-COMBAT-1777 (0x57A620 → 0x4C9050): with the follower skills option on and no Ctrl key held, the follower with the best score disarms instead of the hero.
        var byFollower = DisarmByFollower(row, traps, out var refused);
        if (refused) return;
        var disarm = byFollower ?? traps.Disarm(row, world, _inventoryHost!.InsertCheck);
        _lastDisarm = disarm;
        if (disarm is null) { _areaUseStatus = "disarm traps: no known trap there"; RefreshAreaStatus(); return; }
        // ARC-COMBAT-020: the charge's round events (a refused charge's next subturn) go to the round log.
        if (disarm.Roll.Charge is { } charge) LogRound(charge.Events);
        if (disarm.Script is { } ordinary && UseScriptHost(_readyArea!.Snapshot) is { } scriptHost) Awarded(scriptHost, ordinary);
        var line = disarm.Describe();
        GD.Print($"ARC-SKILLS-1156 disarm entity={AreaUseScriptHost.EntityId(row)} {System.Text.Json.JsonSerializer.Serialize(new { disarm.Roll.Disposition, disarm.Roll.Failure, Outcome = disarm.Outcome.ToString(), SecondRoll = disarm.Roll.SecondRoll, Device = disarm.Device?.Item, Removed = disarm.Roll.Disarm?.State.Removed })}");
        _areaUseStatus = "disarm traps: " + line;
        SyncAreaInventory();
        RefreshAreaStatus();
    }
}
