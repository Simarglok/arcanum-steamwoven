using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-015 (magic plan MT20a-2): the hero's casting in the AREA view, through ARC-MAGIC-014's cast host. M shows the spell
// pane (the hero's known spells); a spell pressed there is activated as the original's spell UI does (0x57BC70, ledger SU rows):
// cast at once at the hero's tile, or the targeting cursor opens with the spell's BEGIN area (an aggressive spell spares the
// hero's party unless an Alt key is held), or with Shift an object spell that may target its caster is cast at the hero. In the
// cursor a left press casts at the object under the pointer, else at the hero when its tile is under it, else at the tile, when
// that passes the area test (0x57C110: affordable, line of fire, then the run); the cursor stays open after a cast, as in the
// original, until a right press, another activation, a window or a map change. A started run's casting goal plays: its start
// (4 action points in a turn-based round, the pre-animation check), the release frame (the target re-check and BEGIN), the end.
// The feedback lines are the cast host's log (spell.mes messages, fatigue, damage, healing, resistance, effects not ready). Since
// ARC-MAGIC-017 the goal is timed by the cast entry's eye candy on the hero (TerrainViewerEyeCandy.cs).
public partial class TerrainViewerApp
{
    private SessionSpellRecord[]? _spellRecords;
    private SessionSpellCaster? _spellCaster;
    private AreaSpellCastMap? _spellCastMap;
    private object? _spellCasterStamp;
    private AreaSpellPane? _spellPane;
    private SpellTargeting? _spellTargeting;
    private SpellCastGoal? _spellGoal;
    private string? _spellNotice;
    // The Alt keys held, left and right apart (the original adds the non-party bit back only when the last one is released).
    private bool _spellAltLeft, _spellAltRight;
    private int _spellLogSeen;
    private int _spellCasts;

    /// <summary>The open targeting cursor: the spell, its record and the cursor's mask (the Alt keys change it).</summary>
    private sealed class SpellTargeting(int spell, SessionSpellRecord record, ulong mask)
    {
        public int Spell { get; } = spell;
        public SessionSpellRecord Record { get; } = record;
        public ulong Mask { get; set; } = mask;
    }

    /// <summary>A casting goal playing: its run, spell and target, when it started (the first frame after the click, on the frames'
    /// clock), its eye candy (ARC-MAGIC-017: it times the release frame and the end) and whether its release frame fired.
    /// ARC-MAGIC-018: the target object (null for a tile) and tile its missile flies to.</summary>
    private sealed class SpellCastGoal(SessionSpellCaster caster, int run, int spell, string? targetObject, SpellLocation targetTile)
    {
        public SessionSpellCaster Caster { get; } = caster;
        public int Run { get; } = run;
        public int Spell { get; } = spell;
        public string? TargetObject { get; } = targetObject;
        public SpellLocation TargetTile { get; } = targetTile;
        public string Target => TargetObject ?? $"tile {TargetTile.X},{TargetTile.Y}";
        public long? StartMs { get; set; }
        public CastEyeCandyAnimation? Animation { get; set; }
        public bool Released { get; set; }
        public long? ReleasedMs { get; set; }
    }

    private bool SpellMapActive => AttackMapActive && _readyArea is not null && _worldActor.Session is AreaGridSession;

    /// <summary>The records in the rules' types, once the spell data is there.</summary>
    private SessionSpellRecord[]? SpellRecordsNow()
    {
        AdoptSpellData();
        if (_spellRecords is null && _spellData?.Data is { } data) _spellRecords = AreaSpellCasting.Records(data);
        return _spellRecords;
    }

    /// <summary>The cast host over the shown map, rebuilt when the session, the map or its actor change.</summary>
    private SessionSpellCaster? SpellCasterNow()
    {
        if (!SpellMapActive || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _readyArea is not { } area
            || SpellRecordsNow() is not { } records) return null;
        var stamp = (owner, area, grid, (object?)_areaPortals);
        if (Equals(stamp, _spellCasterStamp) && _spellCaster is not null) return _spellCaster;
        CancelSpellCast("the map changed");
        ClearEyeCandy("the map changed");
        var map = new AreaSpellCastMap(owner, area.Snapshot.RuntimeMap, grid.Snapshot, area.Snapshot.Objects, _combatHost!, HeroWorldCell, AreaLineOfFireNow,
            key => AreaRemovedObjects().Contains(key));
        _spellCaster = new SessionSpellCaster(owner, n => n >= 0 && n < records.Length ? records[n] : null, map, AreaEvents);
        _spellCastMap = map; _spellCasterStamp = stamp; _spellLogSeen = 0;
        GD.Print($"ARC-MAGIC-015 cast-host map={area.Snapshot.RuntimeMap} records={records.Length}");
        return _spellCaster;
    }

    /// <summary>The cast host for the event pump: the shown map's, built as soon as the session holds a run (its ticks and end need
    /// the map's magic world); without runs the host already built, or none (plain passes).</summary>
    private SessionSpellCaster? SpellCasterForEvents()
    {
        if (_areaSession is not { } owner) return null;
        if (owner.QuerySpellRuns().Count > 0) return SpellCasterNow();
        return _spellCaster is not null && _readyArea is { } area && _worldActor.Session is AreaGridSession grid
            && Equals((owner, area, grid, (object?)_areaPortals), _spellCasterStamp) ? _spellCaster : null;
    }

    private CellCoord? HeroWorldCell()
    {
        if (_worldActor.Session is not AreaGridSession grid) return null;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        return new(checked((int)cell.X), checked((int)cell.Y));
    }

    // ---- the pane (M) ----

    /// <summary>The original Spells key (M, acting on its release as the original's scancode handler does, SU14) shows or hides the
    /// spell pane while the hero can cast. Returns true when the key belongs to it, so it is not reported as a reserved,
    /// unimplemented original key.</summary>
    private bool RouteSpellKey(WorldKeyDecision decision)
    {
        if (decision.Key != WorldKey.M || !SpellMapActive || _areaSession?.QueryHero() is null) return false;
        if (decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _spellNotice = "M ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        if (_spellPane?.Shown == true) { _spellPane.HidePane(); _spellNotice = null; }
        else ShowSpellPane();
        RefreshAreaStatus();
        return true;
    }

    private void ShowSpellPane()
    {
        if (_spellPane is null) { _spellPane = new AreaSpellPane(); AddChild(_spellPane); _spellPane.Activated += ActivateSpell; }
        AdoptSpellData();
        _spellPane.Show(AreaSpellCasting.Known(_areaSession!, _spellData?.Data));
        _spellNotice = _spellData?.Data is null ? "spell data loading" : null;
    }

    /// <summary>Per frame: the pane follows the hero's spell book; the casting goal plays; a window, a lost hero or another map
    /// closes the cursor and ends the goal.</summary>
    private void ProcessAreaSpells()
    {
        if (_spellPane?.Shown == true)
        {
            if (!SpellMapActive || _areaSession?.QueryHero() is null) _spellPane.HidePane();
            else _spellPane.Show(AreaSpellCasting.Known(_areaSession, _spellData?.Data));
        }
        if (_spellTargeting is not null && (!SpellMapActive || WorldModal || ConversationOpen)) CancelSpellTargeting("a window opened or the map changed");
        if (_spellGoal is null) return;
        // Another map, actor or session rebuilds the host, which ends the goal through its own host first; no host ends it too.
        if (SpellCasterNow() is null) CancelSpellCast("the map changed");
        if (_spellGoal is not { } goal || _areaSession is not { } owner) return;
        var caster = goal.Caster;
        var now = WorldMilliseconds();
        var started = goal.StartMs ??= now;
        if (!goal.Released && owner.QueryHeroLife() is not { Status.IsActive: true }) { EndSpellGoal(caster, goal, "the hero cannot act"); return; }
        // ARC-MAGIC-017: the cast entry's eye candy times the goal (goal 27): the release at its action frame, the end after it.
        var animation = goal.Animation ??= StartCastEyeCandy(goal.Run, goal.Spell, started);
        animation.Advance(now, () => ReleaseSpellGoal(caster, goal, now));
        if (animation.Ended)
        {
            _castTimingsForSelfTest.Add((SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.Cast), (goal.ReleasedMs ?? now) - started, now - started, animation.Immediate));
            if (_castTimingsForSelfTest.Count > 16) _castTimingsForSelfTest.RemoveAt(0);
            EndSpellGoal(caster, goal, null);
        }
    }

    /// <summary>The release frame (0x429C80): the target re-check and BEGIN; when it passes, the secondary cast entry's eye candy on the
    /// hero (0x456E00, 6·spell + 4). ARC-MAGIC-018: a spell whose projectile eye candy has art launches its missile instead
    /// (0x429C40), which releases when it arrives.</summary>
    private bool ReleaseSpellGoal(SessionSpellCaster caster, SpellCastGoal goal, long now)
    {
        goal.Released = true; goal.ReleasedMs = now;
        if (SpellMissileArt(goal.Spell) is { } missile) return LaunchSpellMissile(caster, goal, missile, now);
        var released = caster.Release(goal.Run);
        GD.Print($"ARC-MAGIC-015 cast-release run={goal.Run} spell={goal.Spell} begun={released?.Disposition.ToString() ?? "re-check failed"}"
            + $" failure={released?.Failure} at={now - (goal.StartMs ?? now)}ms");
        if (released is not null) PlayEyeCandy(SessionSpellCaster.Hero, SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.SecondaryCast), goal.Run, now);
        FlushSpellLog(released is null ? "the target is no longer valid" : null);
        return released is not null;
    }

    private void EndSpellGoal(SessionSpellCaster caster, SpellCastGoal goal, string? why)
    {
        _spellGoal = null;
        // The end node removes the cast eye candy (0x429CD0, 0x431A40).
        _eyeCandies.RemoveAll(e => e.Cast is not null && e.Run == goal.Run);
        var ended = caster.EndGoal(goal.Run);
        GD.Print($"ARC-MAGIC-015 cast-end run={goal.Run} spell={goal.Spell} released={goal.Released} freed={ended?.Disposition.ToString() ?? "nothing"}"
            + (why is null ? "" : " why=" + why));
        FlushSpellLog(why);
        RefreshAreaStatus();
    }

    // ---- activation and the cursor ----

    /// <summary>A spell pressed in the pane (0x57BC70).</summary>
    private void ActivateSpell(int spell)
    {
        if (_spellTargeting is not null) CancelSpellTargeting(null);
        if (_areaSession is not { } owner || SpellCasterNow() is null || _spellRecords is not { } records || spell < 0 || spell >= records.Length)
        { _spellNotice = "no spell can be cast here (needs --hero and the spell data)"; RefreshAreaStatus(); return; }
        var record = records[spell];
        var begin = record.Areas.Length > 0 ? record.Areas[0].Targets : 0;
        var active = owner.QueryHeroLife() is { Status: { IsDead: false, IsUnconscious: false } };
        if (active && SpellUiRules.TargetsInventory(begin)) { _spellNotice = "a spell on an inventory item is not ready"; RefreshAreaStatus(); return; }
        var activation = SpellUiRules.Activate(active, record.Data.Enabled, begin, record.Resist.Aggressive, Godot.Input.IsKeyPressed(Key.Alt),
            Godot.Input.IsKeyPressed(Key.Shift), owner.QueryActionPointCheck(SessionCombatant.Hero, TurnBasedRules.CastSpellCost));
        GD.Print($"ARC-MAGIC-015 activate spell={spell} kind={activation.Kind} mask=0x{activation.Mask:X}");
        switch (activation.Kind)
        {
            case SpellActivationKind.Nothing: _spellNotice = "the hero cannot cast now"; break;
            case SpellActivationKind.NoActionPoints: _spellNotice = "not enough action points"; break;
            case SpellActivationKind.CastAtCaster:
                if (HeroWorldCell() is { } cell) CastSpell(spell, null, AreaSpellCastMap.Location(cell));
                break;
            case SpellActivationKind.Target:
            case SpellActivationKind.CastAtPlayer:
                _spellTargeting = new(spell, record, activation.Mask);
                _spellNotice = $"{AreaSpellsPage.SpellName(_spellData?.Data, spell)}: left-click a target, right click cancels";
                _hoverStamp = null;
                if (activation.Kind == SpellActivationKind.CastAtPlayer) CastSpell(spell, SessionSpellCaster.Hero, SpellLocation.None);
                break;
        }
        RefreshAreaStatus();
    }

    /// <summary>The cursor's cancel (0x57BFF0): the cursor closes; a goal already playing goes on.</summary>
    private void CancelSpellTargeting(string? why)
    {
        if (_spellTargeting is null) return;
        _spellTargeting = null; _hoverStamp = null;
        if (why is not null) { _spellNotice = "targeting cancelled: " + why; GD.Print("ARC-MAGIC-015 targeting-cancel " + why); }
    }

    /// <summary>Closes the cursor and ends a playing goal (a goal ended before its release frame frees its run, 0x429CD0).</summary>
    private void CancelSpellCast(string? why)
    {
        CancelSpellTargeting(why);
        // A goal ends through its own host while its session is the shown one (a map change frees its run, 0x429CD0); a goal of a
        // replaced session (a quick load) is only dropped.
        if (_spellGoal is { } goal && ReferenceEquals(goal.Caster.Owner, _areaSession)) EndSpellGoal(goal.Caster, goal, why ?? "cancelled");
        else if (_spellGoal is { } dropped) _eyeCandies.RemoveAll(e => e.Cast is not null && e.Run == dropped.Run);
        _spellGoal = null;
    }

    /// <summary>The Alt keys while the cursor is open (0x57C0B0, 0x57C0E0): an aggressive spell may then pick the hero's party.</summary>
    private void TrackSpellModifiers(InputEventKey key)
    {
        if (key.Keycode != Key.Alt || key.Echo) return;
        if (key.Location == KeyLocation.Right) _spellAltRight = key.Pressed; else _spellAltLeft = key.Pressed;
        if (_spellTargeting is not { } targeting) return;
        var aggressive = targeting.Record.Resist.Aggressive;
        targeting.Mask = key.Pressed ? SpellUiRules.AltPressed(targeting.Mask, aggressive)
            : SpellAltHeld ? targeting.Mask : SpellUiRules.AltReleased(targeting.Mask, aggressive);
        _hoverStamp = null;
    }

    // A key-up lost to a focus change cannot leave an Alt held: some Alt must still be down (as the attack's left Alt).
    private bool SpellAltHeld => (_spellAltLeft || _spellAltRight) && Godot.Input.IsKeyPressed(Key.Alt);

    /// <summary>What the cursor would cast at under the screen point: the object under it, else the hero when its tile is under it,
    /// else the tile; null when the point is off the map. The answer carries the area test.</summary>
    private (string? Object, SpellLocation Tile, SpellTargetTest Test)? SpellTargetAt(Vector2 screen)
    {
        if (_spellTargeting is not { } targeting || SpellCasterNow() is not { } caster || _spellCastMap is not { } map || _canvas is null
            || _worldActor.Session is not AreaGridSession grid) return null;
        SpellTargetTest last = new(false);
        if (AreaObjectAtScreen(screen) is { } key && _hoverIndex is { } index && index.Contains(key))
        {
            var id = map.Id(index.Row(key));
            last = caster.TestTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, id, SpellLocation.None);
            if (last.Passes) return (id, SpellLocation.None, last);
        }
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return (null, SpellLocation.None, last);
        var world = grid.Chart.ToWorld(cell);
        var tile = new SpellLocation(checked((uint)world.X), checked((uint)world.Y));
        if (cell == grid.State.CommittedCell)
        {
            var hero = caster.TestTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, SessionSpellCaster.Hero, SpellLocation.None);
            if (hero.Passes) return (SessionSpellCaster.Hero, SpellLocation.None, hero);
        }
        var test = caster.TestTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, null, tile);
        return (null, tile, test.Passes || last.Reason == 0 ? test : last);
    }

    /// <summary>A left press while the cursor is open (the mode-1 branch of 0x54DE50): a target that passes is cast at; otherwise the
    /// "not damaged" failure shows spell.mes 606 and the cursor stays.</summary>
    private void SpellTargetClick(Vector2 screen)
    {
        if (_spellTargeting is not { } targeting) return;
        if (SpellTargetAt(screen) is not { } pick) { _spellNotice = "no target here"; RefreshAreaStatus(); return; }
        if (!pick.Test.Passes)
        {
            if (SpellUiRules.NoTargetMessage(pick.Test.Reason) is { } message)
            {
                _spellNotice = AreaSpellCasting.Message(_spellData?.Data?.Texts, message);
                PostSpellMessage(_spellNotice);
            }
            else _spellNotice = "not a valid target";
            GD.Print($"ARC-MAGIC-015 target-refused spell={targeting.Spell} reason=0x{pick.Test.Reason:X}");
            RefreshAreaStatus();
            return;
        }
        CastSpell(targeting.Spell, pick.Object, pick.Tile);
    }

    // ---- the cast and its goal ----

    /// <summary>The cast at a target (0x57C110) and, when the run starts, its casting goal's start (0x429B50).</summary>
    private void CastSpell(int spell, string? target, SpellLocation tile)
    {
        if (SpellCasterNow() is not { } caster) return;
        if (_spellGoal is not null) { _spellNotice = "a cast is still playing"; RefreshAreaStatus(); return; }
        var attempt = caster.CastAt(SessionSpellCaster.Hero, spell, target, tile);
        _spellCasts++;
        GD.Print($"ARC-MAGIC-015 cast spell={spell} target={target ?? $"tile {tile.X},{tile.Y}"} outcome={attempt.Outcome} message={attempt.Message}"
            + $" creation={attempt.Run?.Creation?.Status} check={attempt.Run?.Creation?.Check} failure={attempt.Run?.Failure}");
        if (attempt.Outcome != SpellCastAttemptOutcome.Started || attempt.RunId is not { } run)
        {
            FlushSpellLog(attempt.Outcome switch
            {
                SpellCastAttemptOutcome.Refused => "the cast was refused: " + (attempt.Run?.Creation?.Check?.ToString() ?? attempt.Run?.Failure.ToString()),
                SpellCastAttemptOutcome.Nothing => "the hero cannot cast now",
                _ => null,
            });
            RefreshAreaStatus();
            return;
        }
        // ARC-COMBAT-020: in a turn-based round the casting goal's start charges its 4 action points.
        var start = caster.StartGoal(run, CombatWorldOrNull() ?? NoSpellCombat.Instance);
        if (start.Outcome != SpellCastGoalOutcome.Started)
        {
            var ended = caster.EndGoal(run, refused: true);
            GD.Print($"ARC-MAGIC-015 goal-refused run={run} outcome={start.Outcome} freed={ended?.Disposition}");
            FlushSpellLog(start.Outcome switch
            {
                SpellCastGoalOutcome.NoActionPoints => "not enough action points",
                SpellCastGoalOutcome.CannotAfford => "the hero cannot afford the spell",
                SpellCastGoalOutcome.TargetGone => "the target is gone",
                SpellCastGoalOutcome.CannotTurn => "the hero cannot turn to the target",
                _ => "the cast did not start",
            });
            RefreshAreaStatus();
            return;
        }
        StopWorldRoute("ROUTE_REPLACED_BY_CAST");
        CancelAreaAttack("a cast replaced it");
        _spellGoal = new(caster, run, spell, target, tile);
        // ARC-MAGIC-018: goal 26 turns the caster toward its target object before goal 27 plays the cast (0x42C850).
        TurnHeroForCast(start.Facing);
        _spellNotice = $"casting {AreaSpellsPage.SpellName(_spellData?.Data, spell)}";
        GD.Print($"ARC-MAGIC-015 goal-start run={run} spell={spell} ap={start.ActionPoints?.Charge.ToString() ?? "no round"}");
        RefreshAreaStatus();
    }

    /// <summary>The cast host's new log entries as the status notice and a message window entry.</summary>
    private void FlushSpellLog(string? lead)
    {
        if (_spellCaster is not { } caster) return;
        var lines = new List<string>();
        if (lead is not null) lines.Add(lead);
        var texts = _spellData?.Data?.Texts;
        for (; _spellLogSeen < caster.Log.Count; _spellLogSeen++)
        {
            var entry = caster.Log[_spellLogSeen];
            GD.Print($"ARC-MAGIC-015 log kind={entry.Kind} run={entry.Run} value={entry.Value} object={entry.Object} detail={entry.Detail}");
            TakeEyeCandyEvent(entry);
            if (AreaSpellCasting.Line(entry, texts, SpellObjectName) is { } line) lines.Add(line);
        }
        if (lines.Count == 0) return;
        _spellNotice = string.Join("; ", lines);
        PostSpellMessage(_spellNotice);
    }

    private void PostSpellMessage(string text)
    {
        if (_messageWindow is null) { _messageWindow = new AreaMessageWindow(); AddChild(_messageWindow); }
        _messageWindow.Post(new("Magic", text, ""));
    }

    private string SpellObjectName(string id) =>
        id == SessionSpellCaster.Hero ? "the hero"
        : _spellCastMap?.Row(id) is { } row && _hoverIndex is { } index && index.Contains(row.Key) ? index.Name(row.Key) ?? id : id;

    private string SpellStatus() => !SpellMapActive || _areaSession?.QueryHero() is null ? ""
        : " | M spells" + (_spellTargeting is { } t ? $" (targeting spell {t.Spell})" : "") + (_spellGoal is { } g ? $" (casting run {g.Run})" : "")
          + (_spellNotice is null ? "" : ": " + _spellNotice);

    /// <summary>Outside a turn-based round a casting goal charges no action points: the world it is given then is never asked.</summary>
    private sealed class NoSpellCombat : ISessionCombatWorld
    {
        public static readonly NoSpellCombat Instance = new();

        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }
}
