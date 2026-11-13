using Arcanum.Data.Area;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Hotkeys;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string DisarmCode = "ARC-SKILLS-1156";

    // ARC-SKILLS-1156 native observation (stage "disarm", map mode with --hero, a map with a container drawn in its window): the hotkey bar's Disarm traps
    // entry arms the skill cursor; the player's data supplies the target, a container the smoke makes trapped with the native trap script 30001 (the use slot's script
    // written as the session's map change, the original object keeps its other data) and known to the hero (the spot mark). Steps:
    //  1. Armed on a trap the hero does not know, a press on the container says so, rolls nothing and closes the cursor.
    //  2. The hotkey bar's entry arms mode 2 (skill target) as the disarm cursor (the pick pocket button's arming, ARC-UI-1113, replaces it and the other way round); a right press cancels it.
    //  3. With the trap known, a fated press (fate point armed: a critical success) removes the trap in one revision; the container is no trapped object any more.
    // NONE without a container the hero can stand beside with a pixel to press.
    private async Task RunAreaDisarmSmokeAsync()
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{DisarmCode} disarm smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DISARM_SMOKE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid || _inventoryHost is null)
        { GD.Print($"{DisarmCode} native-smoke disarm NONE map={_readyMapName} (needs --hero and an area session with its inventory)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true; // the pick pocket's arming (step 2) asks the map's AI host
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            for (var i = 0; i < 1800 && (AreaTrapsNow() is null || SpellCasterNow() is null); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaTrapsNow() is not null && SpellCasterNow() is not null, "the trap host and the cast host of the map");
            for (var i = 0; i < 600 && AreaAiHostNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow() is not null, "the AI host is up");
            if (InventoryOpen) EndInventory();
            var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
            var mapId = new SessionMapId(CurrentAreaMap);
            var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
            // A container drawn in the window that the hero can stand beside, with a pixel where it is the frontmost pick.
            AreaObjectRecord? target = null; Vector2 point = default;
            foreach (var row in area.Snapshot.Objects.Where(r => r.Scope == AreaScope.Core && r.Type == 2 && r.WorldX is not null && commands.ContainsKey(r.Key))
                .OrderBy(r => AreaUseScriptHost.EntityId(r), StringComparer.Ordinal).Take(12))
            {
                var home = SmokeChartCellOf(row);
                CellCoord stand;
                try { stand = AreaSmokeStandable(map, home, 1); } catch (InvalidOperationException) { continue; }
                AreaSmokePlace(map, stand);
                if (InventoryOpen) EndInventory();
                for (var settle = 0; settle < 10; settle++) AreaSmokeFrame();
                if (await HoverPointAsync(this, commands[row.Key]) is { } found) { target = row; point = found; break; }
            }
            if (target is null) { GD.Print($"{DisarmCode} native-smoke disarm NONE map={_readyMapName} (no container with a pixel to press beside a standable cell)"); return; }
            var entity = AreaUseScriptHost.EntityId(target);
            var traps = AreaTrapsNow()!;
            // The container is trapped by the native script 30001 and unknown to the hero.
            var scriptKey = MapChangeKey.ForEntity(entity, AreaUseScriptHost.SlotProperty(ScrPoint.Use, "script"));
            Require(session.ApplyChanges(session.Version, null, [new MapChangeEdit(mapId, scriptKey, 30001)]).Disposition == GameSessionDisposition.Changed, "the trap script is written");
            var host = UseScriptHost(area.Snapshot)!;
            if (host.TrapState(target) is not { Trapped: true })
            { GD.Print($"{DisarmCode} native-smoke disarm NONE map={_readyMapName} (the content's script 30001 is no trap script)"); return; }
            Require(!traps.KnownTrap(target), "the new trap is unknown to the hero");

            // 1. A press on a trap the hero does not know.
            var hotkey = HotkeyEntry.ForSkill(SkillBarButton.DisarmTraps);
            ActivateHotkey(hotkey, "smoke");
            Require(DisarmArmed && !PickPocketArmed && TargetModeOpen(InterfaceMode.SkillTarget) && TargetsMatchStack, $"the hotkey bar's entry armed the disarm cursor: {StackText()} {_hotkeyNotice}");
            var (revision, draws) = (session.Version.Revision, session.QueryRandom().Draws);
            WorldRouteClick(point);
            Require(!DisarmArmed && !TargetModeOpen(InterfaceMode.SkillTarget), "the press left the cursor mode");
            Require((revision, draws) == (session.Version.Revision, session.QueryRandom().Draws), "an unknown trap rolls and changes nothing");
            Require(host.TrapState(target) is { Trapped: true, Removed: false }, "the trap is still there");

            // 2. Arming, the pick pocket's arming replacing it, and a right press cancelling.
            ActivateHotkey(hotkey, "smoke");
            Require(DisarmArmed, "armed again");
            // ARC-UI-1113: the skill bar's Pick pocket button (the hotkey bar's entry, as the skill pane's button) arms it.
            ActivateHotkey(HotkeyEntry.ForSkill(SkillBarButton.PickPocket), "smoke");
            Require(!DisarmArmed && PickPocketArmed && _hotkeyNotice == "hotkey smoke: pick pocket: press an NPC", $"the pick pocket's button replaced the disarm cursor: {_hotkeyNotice}");
            ActivateHotkey(hotkey, "smoke");
            Require(DisarmArmed && !PickPocketArmed, "the disarm cursor replaced the pick pocket's");
            CancelDisarm("the smoke's right press");
            Require(!DisarmArmed && !TargetModeOpen(InterfaceMode.SkillTarget) && TargetsMatchStack, "the right press cancelled the cursor");

            // 3. The trap known: a fated press removes it in one revision.
            Require(session.MarkTrapSpotted(session.Version, AreaUseScriptHost.Trapped(target), host.TrapBaseline(target)!.Value, TrapObserver.Player).Disposition == GameSessionDisposition.Changed, "the spot mark");
            Require(traps.KnownTrap(target), "the hero knows the trap now");
            Require(session.GrantHeroFatePoint(session.Version).Disposition == GameSessionDisposition.Changed, "a fate point");
            Require(session.ArmHeroFate(session.Version, FateKind.DisarmTraps).Disposition == GameSessionDisposition.Changed, "the disarm fate armed");
            ActivateHotkey(hotkey, "smoke");
            Require(DisarmArmed, "armed for the press");
            WorldRouteClick(point);
            Require(!DisarmArmed && _lastDisarm is { Rejected: false } disarm && disarm.Roll.Check is { Success: true, Critical: true } && disarm.Outcome is TrapDisarm.Removed or TrapDisarm.RemovedWithDevice,
                $"the fated press disarmed the trap: {_lastDisarm?.Describe()}");
            Require(host.TrapState(target) is { Removed: true } && !traps.KnownTrap(target), "the trap is removed");
            Require(!session.HeroFateArmed(FateKind.DisarmTraps), "the fate point was used");
            GD.Print($"{DisarmCode} disarm press {_lastDisarm!.Describe()} status={_areaUseStatus}");

            // A second press finds no trap.
            ActivateHotkey(hotkey, "smoke");
            revision = session.Version.Revision;
            WorldRouteClick(point);
            Require(session.Version.Revision == revision && _areaUseStatus.Contains("no known trap", StringComparison.Ordinal), $"a second press finds no trap: {_areaUseStatus}");
            // 4. ARC-COMBAT-1777 (0x57A620 → 0x4C9050): a following NPC of the map is the better follower (the score seam), the press hands the disarm over to it and the hand-over line is asked; Ctrl and the option off leave it to the hero.
            await DisarmHandOverSmokeAsync(session, area, map, target, host, traps, hotkey, mapId);
            GD.Print("ARC-SKILLS-1156 native-smoke disarm PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Container = entity }));
        }
        finally
        {
            CancelDisarm("the smoke ended");
            if (PickPocketArmed) CancelPickPocket("the smoke ended");
            if (InventoryOpen) EndInventory();
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }

    // ARC-COMBAT-1777: the hand-over step of the disarm stage (see the call). It uses a second container (the first one's trap was removed by step 3, and a removal cannot be undone): trapped and known to the hero, a following NPC of the map that
    // the score seam makes the best, one press; Ctrl and the option off are checked at the chooser's gate.
    private async Task DisarmHandOverSmokeAsync(GameSessionOwner session, AreaLoadResult area, AreaMapRuntime map, AreaObjectRecord first, AreaUseScriptHost host, AreaTraps traps, HotkeyEntry hotkey, SessionMapId mapId)
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{DisarmCode} disarm hand-over smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_DISARM_HANDOVER_SMOKE_FAILED");
        }
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        AreaObjectRecord? second = null; Vector2 point = default;
        foreach (var row in area.Snapshot.Objects.Where(r => r.Scope == AreaScope.Core && r.Type == 2 && r.WorldX is not null && r.Key != first.Key && commands.ContainsKey(r.Key))
            .OrderBy(r => AreaUseScriptHost.EntityId(r), StringComparer.Ordinal).Take(12))
        {
            var home = SmokeChartCellOf(row);
            CellCoord stand;
            try { stand = AreaSmokeStandable(map, home, 1); } catch (InvalidOperationException) { continue; }
            AreaSmokePlace(map, stand);
            if (InventoryOpen) EndInventory();
            for (var settle = 0; settle < 10; settle++) AreaSmokeFrame();
            if (await HoverPointAsync(this, commands[row.Key]) is { } found) { second = row; point = found; break; }
        }
        if (second is null) { GD.Print($"{DisarmCode} native-smoke disarm-handover NONE map={_readyMapName} (no second container with a pixel to press)"); return; }
        var entity = AreaUseScriptHost.EntityId(second);
        var scriptKey = MapChangeKey.ForEntity(entity, AreaUseScriptHost.SlotProperty(ScrPoint.Use, "script"));
        Require(session.ApplyChanges(session.Version, null, [new MapChangeEdit(mapId, scriptKey, 30001)]).Disposition == GameSessionDisposition.Changed, "the second trap script is written");
        if (host.TrapState(second) is not { Trapped: true }) { GD.Print($"{DisarmCode} native-smoke disarm-handover NONE map={_readyMapName} (the second container takes no trap)"); return; }
        Require(session.MarkTrapSpotted(session.Version, AreaUseScriptHost.Trapped(second), host.TrapBaseline(second)!.Value, TrapObserver.Player).Disposition == GameSessionDisposition.Changed && traps.KnownTrap(second), "the second trap is known to the hero");
        var before = _smokeLedNpc;
        JoinLedMapNpcForSmoke(session, area, map, string.Empty, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
        if (_smokeLedNpc is not { } follower || follower == before)
        { GD.Print($"{DisarmCode} native-smoke disarm-handover NONE map={_readyMapName} (no NPC of the map could join as a follower)"); return; }
        var settings = _gameSettings;
        try
        {
            for (var i = 0; i < 900 && !(AreaAiHostNow()?.FollowerSkillUsers().Any(u => u.Loaded.Entity == follower) ?? false); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(AreaAiHostNow()?.FollowerSkillUsers().Any(u => u.Loaded.Entity == follower) == true, "the AI host runs the follower and lists it as a skill user");
            // The gate: on and no Ctrl allows the chooser; Ctrl and the option off do not.
            _gameSettings = settings with { FollowerSkills = true }; FollowerSkillsCtrlForSelfTest = false;
            Require(FollowerSkillsAllowed(), "the option on and no Ctrl: the chooser runs");
            FollowerSkillsCtrlForSelfTest = true;
            Require(!FollowerSkillsAllowed(), "Ctrl skips the chooser");
            FollowerSkillsCtrlForSelfTest = false; _gameSettings = settings with { FollowerSkills = false };
            Require(!FollowerSkillsAllowed(), "the option off skips the chooser");
            _gameSettings = settings with { FollowerSkills = true };
            // The score seam: the follower scores higher than the hero, so the press hands the disarm over to it.
            FollowerSkillsScoreForSelfTest = user => user is null ? 10 : 90;
            LastFollowerSkillHandOver = null; _lastDisarm = null;
            // The follower walked up since the pixel was found: find the container's pixel again.
            for (var settle = 0; settle < 10; settle++) AreaSmokeFrame();
            point = await HoverPointAsync(this, commands[second.Key]) ?? point;
            Require(traps.KnownTrap(second), "the second trap is still known");
            ActivateHotkey(hotkey, "smoke");
            Require(DisarmArmed, "armed for the press");
            WorldRouteClick(point);
            Require(!DisarmArmed && LastFollowerSkillHandOver == follower, $"the follower disarmed: {LastFollowerSkillHandOver} {_areaUseStatus}");
            Require(_lastDisarm is { Roll.Check: not null, Roll.Charge: null }, "its roll is the session's own, with no action points charged to it");
            GD.Print("ARC-COMBAT-1777 native-smoke disarm-handover PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Follower = follower, Outcome = _lastDisarm!.Outcome.ToString() }));
        }
        finally
        {
            FollowerSkillsScoreForSelfTest = null; FollowerSkillsCtrlForSelfTest = null; _gameSettings = settings;
            if (DisarmArmed) CancelDisarm("the smoke ended");
            session.LeaveParty(session.Version, follower, new PartyLeave(Force: true));
            _smokeLedNpc = before;
        }
    }
}
