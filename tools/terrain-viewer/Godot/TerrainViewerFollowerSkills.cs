using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Skills;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-1777 (Follower Skills slice 2, ledger OH4, PL5, SP3; the chooser 0x4C9050): with the "follower skills" option on (default on), the hero's lockpick use on a door, window or container (0x571CB0; left Ctrl skips it) and the skill bar's
// Disarm Traps press (0x57A620; either Ctrl key skips it) let the follower with the strictly best effectiveness minus difficulty (the hero scored first, ties keep it) do the job instead, and a hand-over line (4700..4799 of its dialog override or of the
// gender pair's file) floats over the follower. The lock pick is the follower's own skill goal (it walks within range 1 and picks with its own lockpicks if it holds any, AreaAiHost.StartPickLocks); the disarm is made at once from where it stands.
// ARC-COMBAT-1878: the lockpick use is skipped by the left Ctrl key only, the Disarm Traps press by either Ctrl key (the key events' location tells them apart). NOT READY: the lock pick's witnesses are ARC-AI-2084's, the others #1860.
public partial class TerrainViewerApp
{
    /// <summary>Self-test seam: the left Ctrl state the chooser reads instead of the keyboard (null: the keyboard).</summary>
    internal bool? FollowerSkillsCtrlForSelfTest { get; set; }

    /// <summary>Self-test seam: the right Ctrl state the Disarm Traps chooser reads instead of the keyboard (null: the keyboard).</summary>
    internal bool? FollowerSkillsRightCtrlForSelfTest { get; set; }

    // ARC-COMBAT-1878: the Ctrl keys as the key events report them (the mouse event says only that some Ctrl is held); an unspecified location counts as the left key, as left Alt does.
    private bool _leftCtrlHeld, _rightCtrlHeld;

    /// <summary>Tracks the Ctrl keys from the key events.</summary>
    private void TrackFollowerSkillModifiers(InputEventKey key)
    {
        if (key.Keycode != Key.Ctrl) return;
        if (key.Location == KeyLocation.Right) _rightCtrlHeld = key.Pressed;
        else _leftCtrlHeld = key.Pressed;
    }

    /// <summary>The last hand-over (the follower's entity), for the smoke and the log.</summary>
    internal string? LastFollowerSkillHandOver { get; private set; }

    /// <summary>Self-test seam: the score the chooser reads for a user instead of the session's (null: the session's).</summary>
    internal Func<Arcanum.Session.SessionNpcSkillUser?, int?>? FollowerSkillsScoreForSelfTest { get; set; }

    // A key-up lost to a focus change cannot leave a Ctrl key held: some Ctrl must still be down.
    // A Ctrl key held that no key event told the side of (its press was dropped: a pending load, a mode that takes no keys) counts as the left one, the safe default; a stale side with no Ctrl down is cleared.
    private bool FollowerSkillsLeftCtrlHeld()
    {
        if (FollowerSkillsCtrlForSelfTest is { } forced) return forced;
        if (!Godot.Input.IsKeyPressed(Key.Ctrl)) { _leftCtrlHeld = _rightCtrlHeld = false; return false; }
        return _leftCtrlHeld || !_rightCtrlHeld;
    }

    private bool FollowerSkillsRightCtrlHeld() => FollowerSkillsRightCtrlForSelfTest ?? _rightCtrlHeld && Godot.Input.IsKeyPressed(Key.Ctrl);

    // The chooser's gate: the option on, no Ctrl key it tests (the lockpick use tests the left one, the Disarm Traps press either, 0x571CB0, 0x57A620), a host that runs followers.
    private bool FollowerSkillsAllowed(bool eitherCtrl = false) =>
        _gameSettings.FollowerSkills && !FollowerSkillsLeftCtrlHeld() && !(eitherCtrl && FollowerSkillsRightCtrlHeld()) && AreaAiHostNow() is not null && _areaSession is not null;

    /// <summary>
    /// The lockpick press on a door, window or container (<paramref name="target"/> is the goal's target string): when the chooser picks a follower, it starts the follower's pick and the hand-over line floats. False: the hero does it (the
    /// caller starts the hero's own skill goal); a follower whose start the table refused leaves the press handled (0x571CB0 returns after the starter).
    /// </summary>
    private bool HandOverLockpick(string target, string name)
    {
        if (!FollowerSkillsAllowed()) return false;
        var session = _areaSession!; var host = AreaAiHostNow()!;
        // 0x571CB0 returns before the chooser for a hero that is gone or down.
        if (session.QueryHeroLife() is not { Status.IsActive: true }) return false;
        // ARC-COMBAT-1878 (0x571CB0 → 0x462760): every candidate is scored with the PC's first lockpicks in inventory order (none: without), not with the ones the hero armed.
        if (AiPickLockTarget(target) is not { } lockTarget || _inventoryHost is null) return false;
        var lockpick = _inventoryHost.FirstLockpicks(session, _areaKitLockpicks);
        if (host.ChooseSkillUser(FollowerSkillsScoreForSelfTest ?? (user => session.QueryPickLockScore(user, lockTarget, lockpick))) is not { } chosen) return false;
        var follower = chosen.Loaded.Entity;
        // 0x571CB0 returns after the follower's starter whatever it answers: a start the table or the gate refuses (the hero's own turn in a round) leaves the press handled with nothing started, and the hand-over line was already said.
        var started = host.StartPickLocks(follower, target, host.FirstLockpicks(follower));
        LastFollowerSkillHandOver = follower;
        SayFollowerLine(follower, FollowerSkillRules.HandOverLines);
        if (!started)
        {
            GD.Print($"ARC-COMBAT-1777 lockpick hand-over refused follower={follower} target={target}");
            _portalNotice = $"{name}: {AiName(follower)} cannot pick the lock now";
            RefreshAreaStatus();
            return true;
        }
        _portalNotice = $"{name}: {AiName(follower)} picks the lock";
        GD.Print($"ARC-COMBAT-1777 lockpick handed over follower={follower} target={target}");
        RefreshAreaStatus();
        return true;
    }

    /// <summary>
    /// The Disarm Traps press on <paramref name="row"/>: the chooser (either Ctrl skips it) may pick a follower; the disarm is then its roll through the session, the hero's press having paid its action points, and the hand-over line floats over it. Null: the
    /// hero does it.
    /// </summary>
    private AreaTrapDisarm? DisarmByFollower(Arcanum.Data.Area.AreaObjectRecord row, AreaTraps traps, out bool refused)
    {
        refused = false;
        if (!FollowerSkillsAllowed(eitherCtrl: true) || !traps.NpcDisarmReady || _inventoryHost is null) return null;
        var session = _areaSession!; var host = AreaAiHostNow()!;
        if (traps.DisarmScoreInputs(row) is not { } inputs) return null;
        if (host.ChooseSkillUser(FollowerSkillsScoreForSelfTest ?? (user => session.QueryDisarmScore(user, inputs.Trapped, inputs.Baseline, inputs.Difficulty, inputs.Light))) is not { } chosen) return null;
        var follower = chosen.Loaded.Entity;
        // The press's own charge (0x57A320: 4 action points of the PC in a turn-based round) is the hero's, whoever acts.
        ResumeLoadedRound();
        if (!ChargeHeroAction(Arcanum.Simulation.Combat.TurnBasedRules.UseSkillCost, "disarm traps")) { _areaUseStatus = "disarm traps: " + _turnBasedNotice; RefreshAreaStatus(); refused = true; return null; }
        var disarm = traps.DisarmByNpc(row, chosen, _inventoryHost.InsertCheck);
        if (disarm is null) return null;
        LastFollowerSkillHandOver = follower;
        SayFollowerLine(follower, FollowerSkillRules.HandOverLines);
        GD.Print($"ARC-COMBAT-1777 disarm handed over follower={follower} entity={AreaUseScriptHost.EntityId(row)} outcome={disarm.Outcome}");
        return disarm;
    }
}
