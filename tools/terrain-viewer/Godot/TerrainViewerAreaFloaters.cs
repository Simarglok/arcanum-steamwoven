using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-021 (alpha N4, slice 4 of docs/research/combat-hud.md): floating text in crash-site-area. Every attack the viewer runs through
// the session (the hero's on an NPC or an object, a projectile's strike, an NPC's on the hero or another NPC) makes the original's
// floaters (CombatFloaters, ledger CH12) at the options window's "text floaters" level, with the texts of the player's mes\combat.mes;
// they rise at the "float speed" step every 50 ms or more of wall time and fade (CH11) over their object: the hero, a companion or an
// NPC where it stands now, an object on its cell. The lines belong to the shown map: a new scene drops them.
public partial class TerrainViewerApp
{
    private const string HeroFloater = AiSchedulerRules.Pc;
    private AreaFloaterLayer? _floaters;
    private object? _floaterScene;
    // An owner's cell when the line was made, for an object no live lookup finds.
    private readonly Dictionary<string, (long X, long Y)> _floaterCells = new(StringComparer.Ordinal);
    private string? _combatTextsModule;
    private Task<AreaCombatTexts>? _combatTextsTask;
    private AreaCombatTexts? _combatTexts;
    // Smokes step the lines by their own clock.
    private bool _floaterClockForSelfTest;

    /// <summary>Starts reading the floaters' texts for this module once (a reload, the cave or a map transfer reuses them).</summary>
    private void BindCombatTexts(TerrainViewerArguments arguments)
    {
        if (_combatTextsModule == arguments.ModulePath) return;
        _combatTextsModule = arguments.ModulePath; _combatTexts = null;
        var module = arguments.ModulePath;
        _combatTextsTask = SelfTestBootstrap ? Task.FromResult(AreaCombatTexts.Project("self-test: no game folder")) : Task.Run(() => AreaCombatTexts.Load(module));
    }

    /// <summary>The texts, waiting at most a second for a read still running (the project's text after that, read again next time).</summary>
    private AreaCombatTexts CombatTexts()
    {
        if (_combatTexts is { } read) return read;
        if (_combatTextsTask is not { } task) return AreaCombatTexts.Project("not bound");
        // A wait on the handle never throws for a faulted task (Task.Wait would).
        if (!task.IsCompleted && !((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1))) return AreaCombatTexts.Project("still loading");
        _combatTexts = task.IsCompletedSuccessfully ? task.Result : AreaCombatTexts.Project("load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _combatTextsTask = null;
        GD.Print($"ARC-UI-021 combat texts fromGame={_combatTexts.FromGame} detail={_combatTexts.Detail}");
        return _combatTexts;
    }

    /// <summary>A critter's side for its floater colour: the PC, a companion the PC leads, anyone else.</summary>
    private FloaterSide FloaterSideOf(string entity) =>
        entity == HeroFloater ? FloaterSide.Pc : _areaSession?.QueryCompanion(entity).LedByPc == true ? FloaterSide.PcFollower : FloaterSide.Other;

    /// <summary>The floaters of an attack on a critter by <paramref name="attacker"/> on <paramref name="target"/> (entities, or the
    /// hero's <see cref="HeroFloater"/>).</summary>
    private void FloatCritterAttack(SessionAttackResult result, string attacker, string target)
    {
        var hit = AreaCombatFloaters.Of(result, FloaterSideOf(attacker), FloaterSideOf(target));
        Float(hit, attacker, result.Recipient == Arcanum.Simulation.Combat.DamageRecipient.Attacker ? attacker : target);
    }

    /// <summary>The floaters of the hero's attack on an object (a door, window, container, scenery, trap or item; a wall without a
    /// row), its lines over the object's cell.</summary>
    private void FloatObjectAttack(SessionObjectAttackResult result, string target, AreaObjectRecord? row)
    {
        // A portal's or scenery's entity names no inventory row: its cell comes from its map row.
        if (row is { WorldX: not null, WorldY: not null }) _floaterCells[target] = LiveWorldCell(row);
        var hit = AreaCombatFloaters.Of(result, FloaterSide.Pc);
        Float(hit, HeroFloater, result.Life is null ? target : HeroFloater);
    }

    private void Float(CombatFloaterHit? hit, string attacker, string struck)
    {
        if (hit is null || _readyMode != TerrainViewerMode.CrashSiteArea || _readyArea is null) return;
        var floaters = CombatFloaters.Of(hit, _gameSettings.Floats, CombatTexts().Text);
        if (floaters.Count == 0) return;
        var layer = FloaterLayer();
        DropFloatersOfOldScene(layer);
        foreach (var floater in floaters)
        {
            var owner = floater.Over == FloaterOver.Attacker ? attacker : struck;
            if (FloaterCell(owner) is { } cell) _floaterCells[owner] = cell;
            if (!layer.Field.Add(owner, floater.Text, floater.Color)) GD.Print($"ARC-UI-021 floater refused owner={owner} (field full)");
        }
        GD.Print($"ARC-UI-021 floaters attacker={attacker} struck={struck} level={_gameSettings.Floats} lines={string.Join("|", floaters.Select(f => $"{f.Over}:{f.Color}:{f.Text}"))}");
        layer.Redraw();
    }

    private AreaFloaterLayer FloaterLayer()
    {
        if (_floaters is { } layer) return layer;
        _floaters = new AreaFloaterLayer { Anchor = FloaterAnchor };
        AddChild(_floaters);
        // First among the app's children: on its layer it draws under the diagnostic HUD.
        MoveChild(_floaters, 0);
        _floaterScene = _readyArea;
        return _floaters;
    }

    /// <summary>A new scene drops the lines of the old one (and the cells kept for them).</summary>
    private void DropFloatersOfOldScene(AreaFloaterLayer layer)
    {
        if (ReferenceEquals(_floaterScene, _readyArea)) return;
        layer.Field.Clear(); _floaterCells.Clear(); _floaterScene = _readyArea; layer.Redraw();
    }

    /// <summary>Each frame: a new scene drops the lines; otherwise they rise (every 50 ms or more) and are drawn where their objects are.</summary>
    private void TickFloaters()
    {
        if (_floaters is not { } layer) return;
        DropFloatersOfOldScene(layer);
        layer.Level = _gameSettings.Floats;
        if (layer.Field.Count == 0) return;
        if (!_floaterClockForSelfTest) layer.Field.Advance((long)Time.GetTicksMsec(), _gameSettings.FloatSpeed);
        layer.Redraw();
    }

    /// <summary>The cell an owner stands on now: the hero's committed cell, a companion's live cell, an NPC the AI walked, a map row's
    /// cell; else the cell it had when its line was made.</summary>
    private (long X, long Y)? FloaterCell(string owner)
    {
        if (owner == HeroFloater)
        {
            if (_worldActor.Session is not AreaGridSession grid) return null;
            var at = grid.Chart.ToWorld(grid.State.CommittedCell);
            return (at.X, at.Y);
        }
        if (_areaCompanions is { } group && group.Map.RuntimeMap == CurrentAreaMap && group.Companions.FirstOrDefault(c => c.Entity == owner) is { } companion)
            return group.WorldCell(companion.Speaker);
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(owner, out var row)
            && row.WorldX is not null && row.WorldY is not null)
            return LiveWorldCell(row);
        return _floaterCells.TryGetValue(owner, out var kept) ? kept : null;
    }

    /// <summary>The screen point 90 px (at the view's zoom) above an owner's tile centre, the floater box's bottom centre.</summary>
    private Vector2? FloaterAnchor(string owner)
    {
        if (_canvas is null || _readyArea is not { } area || _readyMode != TerrainViewerMode.CrashSiteArea || FloaterCell(owner) is not { } cell) return null;
        var chart = area.Plan.Chart;
        var projected = TerrainGeometry.Project(checked((int)(cell.X - chart.OriginX)), checked((int)(cell.Y - chart.OriginY)));
        return _canvas.GetGlobalTransformWithCanvas() * new Vector2(projected.X + 40, projected.Y + 20 - FloaterRules.BoxRise);
    }

    internal AreaFloaterLayer? FloatersForSelfTest => _floaters;
}
