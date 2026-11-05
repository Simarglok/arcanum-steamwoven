using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
// ARC-MAGIC-019 (SU13): a cast while a goal plays is queued beneath it (at most 4, none in a turn-based round) and plays when the
// goals before it are done; a goal refused at its start, or one cut short, drops the casts queued behind it.
// ARC-UI-016: the cast host knows the hero's items (kit and taken), so an item can be a cast's source. The pane's weapon button
// (shown while the wielded weapon qualifies, 0x556EF0) opens the weapon's spell pane (bar mode 8); a slot pressed there is activated
// for the weapon (0x57C040 → 0x57BC70: its holder casts, the weapon is the source and pays from its charges) and the cursor keeps the
// weapon as the cast's source. The pane returns to the spell list when the weapon leaves the weapon slot (the inventory callback
// 0x5570D0).
public partial class TerrainViewerApp
{
    private SessionSpellRecord[]? _spellRecords;
    private SessionSpellCaster? _spellCaster;
    private AreaSpellCastMap? _spellCastMap;
    private object? _spellCasterStamp;
    private AreaSpellPane? _spellPane;
    private SpellTargeting? _spellTargeting;
    private SpellCastGoal? _spellGoal;
    // ARC-UI-016: the item whose spell pane is open (bar mode 8), or null for the spell list.
    private string? _itemPaneItem;
    // The weapon pane last built and what it was built from (the session's revision, the weapon, open, the spell data).
    private ((GameSessionOwner, long, string, bool, object?) Stamp, AreaWeaponSpellPane? Pane)? _weaponPaneCache;
    // ARC-MAGIC-019: the casts queued beneath the playing goal, first cast first (the original inserts each at the bottom of the
    // caster's goal stack, 0x44D9B0).
    private readonly List<SpellCastGoal> _spellQueue = [];
    private string? _spellNotice;
    // The Alt keys held, left and right apart (the original adds the non-party bit back only when the last one is released).
    private bool _spellAltLeft, _spellAltRight;
    private int _spellLogSeen;
    private int _spellCasts, _spellQueuedStarts, _spellTargetFailedAtStart;

    /// <summary>The open targeting cursor: the spell, its record and the cursor's mask (the Alt keys change it); ARC-UI-016: the item
    /// that is the cast's source (null for the hero's own spell).</summary>
    private sealed class SpellTargeting(int spell, SessionSpellRecord record, ulong mask, string? item = null)
    {
        public int Spell { get; } = spell;
        public SessionSpellRecord Record { get; } = record;
        public ulong Mask { get; set; } = mask;
        public string? Item { get; } = item;
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
        // ARC-MAGIC-019: the target failed the re-check before the cast eye candy was added (0x429F00): the goal ends through the end step.
        public bool TargetFailedAtStart { get; set; }
        /// <summary>ARC-WORLD-108: the release's refused re-check freed the run (MG41): the goal's end frees nothing.</summary>
        public bool FreedAtRelease { get; set; }
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
            key => AreaRemovedObjects().Contains(key), SpellPlacedCompanions, HeroHolds, SpellVisitingCompanions);
        var runtimeMap = area.Snapshot.RuntimeMap;
        _spellCaster = new SessionSpellCaster(owner, n => n >= 0 && n < records.Length ? records[n] : null, map, AreaEvents,
            id => HeroItemSpells(id) ?? _combatHost!.NpcItemSpells(owner, id));
        // ARC-UI-031: the host's own life steps (a spell's damage, a payment's fatigue, its passes) fire the worn items' spells.
        _spellCaster.ItemTriggers = new SessionItemTriggers(_spellCaster, new AreaCombatWorld(runtimeMap, AreaScanCritters, AreaCombatantFacts));
        _spellCastMap = map; _spellCasterStamp = stamp; _spellLogSeen = 0;
        GD.Print($"ARC-MAGIC-015 cast-host map={runtimeMap} records={records.Length} item-triggers=on");
        return _spellCaster;
    }

    // ARC-UI-016: the entities the hero holds now (listed or worn), once per session revision: a taken row is no map object then.
    private (GameSessionOwner Owner, long Revision, HashSet<string> Held)? _heroHeld;

    private bool HeroHolds(string id)
    {
        if (_areaSession is not { } owner) return false;
        if (_heroHeld is not { } known || !ReferenceEquals(known.Owner, owner) || known.Revision != owner.Version.Revision)
        {
            var held = owner.QueryPcInventory().Select(item => item.Entity).Concat(owner.QueryEquipment().Worn.Select(worn => worn.Entity))
                .ToHashSet(StringComparer.Ordinal);
            _heroHeld = known = (owner, owner.Version.Revision, held);
        }
        return known.Held.Contains(id);
    }

    // ARC-UI-016: the spell facts of an item the hero holds, kit or taken; the cast host's item lookup asks it first, then the shown
    // map's NPCs (ARC-UI-031).
    private SessionItemSpells? HeroItemSpells(string id) =>
        _lentItemSpellsForSelfTest is { } lent && lent.Entity == id ? lent
        : _areaSession is { } owner && _inventoryHost is { } host ? host.ItemSpells(owner, id, _areaKitItemSpells) : null;

    // ARC-MAGIC-021: the companions' rows where they stand now (the committed cell), none while on another map. ARC-WORLD-102: the NPCs
    // the AI walked stand on their cells too (a caster's range and line of fire, a target's tile).
    private IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> SpellPlacedCompanions()
    {
        var placed = new Dictionary<AreaObjectKey, (long X, long Y)?>();
        if (_aiHost is { } ai && _readyArea is { } shown && ai.Scene.RuntimeMap == shown.Snapshot.RuntimeMap)
            foreach (var mover in ai.Movers.All) placed[mover.Key] = (mover.Cell.X, mover.Cell.Y);
        if (_areaCompanions is not { } group || _readyArea is not { } area || group.Map.RuntimeMap != area.Snapshot.RuntimeMap) return placed;
        foreach (var key in group.Absent) placed[key] = null;
        foreach (var companion in group.Companions) placed[companion.Key] = group.WorldCell(companion.Speaker);
        return placed;
    }

    // ARC-DEV-076 (MIG3): the companions carried here from another map: spell targets and party members through their home map's record.
    private IReadOnlyList<(string Entity, AreaObjectRecord Row)> SpellVisitingCompanions() =>
        _areaCompanions is { } group && _readyArea is { } area && group.Map.RuntimeMap == area.Snapshot.RuntimeMap
            ? group.Companions.Where(companion => companion.Template.Home.RuntimeMap != area.Snapshot.RuntimeMap)
                .Select(companion => (companion.Entity, companion.Speaker.Row)).ToArray()
            : [];

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
        if (_spellPane is null)
        {
            _spellPane = new AreaSpellPane(); AddChild(_spellPane);
            _spellPane.Activated += PressPaneSpell;
            _spellPane.WeaponToggled += ToggleWeaponPane;
            _spellPane.ItemActivated += ActivateItemSlot;
            // ARC-UI-027: a right press there starts a drag onto the hotkey bar.
            _spellPane.SpellDragStarted += HotkeyPaneDrag;
            _spellPane.ItemDragStarted += HotkeyWeaponPaneDrag;
        }
        AdoptSpellData();
        _spellPane.ListInStrip = SpellListInStrip;
        _spellPane.Show(AreaSpellCasting.Known(_areaSession!, _spellData?.Data), WeaponPaneNow());
        _spellNotice = _spellData?.Data is null ? "spell data loading" : null;
    }

    /// <summary>A spell of the pane was pressed (the diagnostic pane's button, or ARC-UI-053's release over its icon in the bottom strip):
    /// ARC-UI-022: the press becomes the latest recent action first (0x57EFA0), then the activation.</summary>
    private void PressPaneSpell(int spell)
    {
        RecordRecentAction(Arcanum.Simulation.Hotkeys.HotkeyEntry.ForSpell(spell));
        ActivateSpell(spell);
    }

    /// <summary>Per frame: the pane follows the hero's spell book; the casting goal plays; a window, a lost hero or another map
    /// closes the cursor and ends the goal.</summary>
    private void ProcessAreaSpells()
    {
        // ARC-UI-016: the pane leaves the weapon's spells when the weapon leaves its slot, shown or not (0x5570D0 on the removal).
        if (_itemPaneItem is not null && _itemPaneItem != HeroWeapon()) WeaponPaneNow();
        if (_spellPane?.Shown == true)
        {
            if (!SpellMapActive || _areaSession?.QueryHero() is null) _spellPane.HidePane();
            else _spellPane.Show(AreaSpellCasting.Known(_areaSession, _spellData?.Data), WeaponPaneNow());
        }
        if (_spellTargeting is not null && (!SpellMapActive || TargetsDroppedByModal || ConversationOpen)) CancelSpellTargeting("a window opened or the map changed");
        if (_spellGoal is null) return;
        // Another map, actor or session rebuilds the host, which ends the goal through its own host first; no host ends it too.
        if (SpellCasterNow() is null) CancelSpellCast("the map changed");
        if (_spellGoal is not { } goal || _areaSession is not { } owner) return;
        var caster = goal.Caster;
        var now = WorldMilliseconds();
        var started = goal.StartMs ??= now;
        if (!goal.Released && owner.QueryHeroLife() is not { Status.IsActive: true }) { EndSpellGoal(caster, goal, "the hero cannot act"); return; }
        // ARC-MAGIC-019: before the cast eye candy is added the target is checked again (0x429F00 → 0x456A90); a failure releases at
        // once (nothing begins, MG41) and the goal ends through the end step, which drops the casts queued behind it.
        if (goal.Animation is null && owner.QuerySpellRuns()[goal.Run] is { } awaiting && !caster.TargetStillValid(awaiting))
        {
            goal.TargetFailedAtStart = true; _spellTargetFailedAtStart++;
            ReleaseSpellGoal(caster, goal, now, fromGate: false);
            EndSpellGoal(caster, goal, null);
            return;
        }
        // ARC-MAGIC-017: the cast entry's eye candy times the goal (goal 27): the release at its action frame, the end after it.
        var animation = goal.Animation ??= StartCastEyeCandy(goal.Run, goal.Spell, started);
        // ARC-MAGIC-018: only the gate of a cast eye candy (0x42CAA0) reaches the missile's launch (0x429C40); without one the
        // release (0x429C80) runs at once.
        animation.Advance(now, () => ReleaseSpellGoal(caster, goal, now, fromGate: !animation.Immediate));
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
    private bool ReleaseSpellGoal(SessionSpellCaster caster, SpellCastGoal goal, long now, bool fromGate)
    {
        goal.Released = true; goal.ReleasedMs = now;
        if (fromGate && SpellMissileArt(goal.Spell) is { } missile)
        {
            var launched = LaunchSpellMissile(caster, goal, missile, now);
            // The launch's true step restarts goal 27 one pace later (0x4246E0, delay −2).
            if (launched && goal.Animation is { } playing) playing.PaceAfterRelease = true;
            return launched;
        }
        var released = caster.Release(goal.Run);
        GD.Print($"ARC-MAGIC-015 cast-release run={goal.Run} spell={goal.Spell} begun={released?.Disposition.ToString() ?? "re-check failed"}"
            + $" failure={released?.Failure} at={now - (goal.StartMs ?? now)}ms");
        if (released is not null) PlayEyeCandy(SessionSpellCaster.Hero, SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.SecondaryCast), goal.Run, now);
        // MG41 (fixed in both settings): a refused re-check freed the run, so the goal's end must not touch its slot, which another
        // cast may take before the end (a queued hero cast, an NPC's cast through the same host).
        else if (_areaSession?.QuerySpellRuns()[goal.Run] is null) goal.FreedAtRelease = true;
        FlushSpellLog(released is null ? "the target is no longer valid" : null);
        return released is not null;
    }

    private void EndSpellGoal(SessionSpellCaster caster, SpellCastGoal goal, string? why)
    {
        _spellGoal = null;
        // The end node removes the cast eye candy (0x429CD0, 0x431A40).
        _eyeCandies.RemoveAll(e => e.Cast is not null && e.Run == goal.Run);
        var ended = goal.FreedAtRelease ? null : caster.EndGoal(goal.Run);
        GD.Print($"ARC-MAGIC-015 cast-end run={goal.Run} spell={goal.Spell} released={goal.Released} freed={ended?.Disposition.ToString() ?? "nothing"}"
            + (why is null ? "" : " why=" + why));
        FlushSpellLog(why);
        // ARC-MAGIC-019: a goal that played its cast eye candy to the end pops (the overlay's removal answers true, 0x431A40), so the
        // next queued cast's goal starts; without cast eye candy, or after a target that failed before it, the goal ends through the
        // end step (0x90000000), and a goal cut short interrupts the caster's whole stack (0x44E2C0): both drop the queue.
        if (why is not null) DropSpellQueue(caster, why);
        else if (goal.TargetFailedAtStart) DropSpellQueue(caster, "the target failed before the cast eye candy (end step)");
        else if (goal.Animation is not { Immediate: false }) DropSpellQueue(caster, "the cast had no eye candy (end step)");
        else StartQueuedSpellGoal(caster);
        RefreshAreaStatus();
    }

    /// <summary>The first queued cast's goal starts (0x423D10's pop restarts it at its first node). A start refused there ends
    /// through the goal's end and the end step, which ends the caster's stack and drops the casts queued behind it.</summary>
    private void StartQueuedSpellGoal(SessionSpellCaster caster)
    {
        if (_spellQueue.Count == 0) return;
        // A caster lost meanwhile (knocked out by the release, say) kills its whole stack (0x44E2C0): no queued goal starts.
        if (_areaSession?.QueryHeroLife() is not { Status.IsActive: true }) { DropSpellQueue(caster, "the hero cannot act"); return; }
        var next = _spellQueue[0];
        _spellQueue.RemoveAt(0);
        if (!ReferenceEquals(next.Caster, caster)) { DropSpellQueue(caster, "the cast host changed"); return; }
        ResumeLoadedRound();
        var start = caster.StartGoal(next.Run, CombatWorldOrNull() ?? NoSpellCombat.Instance);
        GD.Print($"ARC-MAGIC-019 queued-start run={next.Run} spell={next.Spell} outcome={start.Outcome} waiting={_spellQueue.Count}");
        if (start.Outcome == SpellCastGoalOutcome.Started)
        {
            _spellGoal = next; _spellQueuedStarts++;
            // ARC-MAGIC-018: the queued goal 26 turns the caster toward its own target (0x42C850).
            TurnHeroForCast(start.Facing);
            _spellNotice = $"casting {AreaSpellsPage.SpellName(_spellData?.Data, next.Spell)}" + (_spellQueue.Count > 0 ? $" ({_spellQueue.Count} queued)" : "");
            return;
        }
        RefuseSpellGoal(caster, next.Run, start.Outcome);
        DropSpellQueue(caster, "a queued cast was refused at its start");
    }

    /// <summary>The casts queued behind a stack that ended or was interrupted never play (their runs: MG43).</summary>
    private void DropSpellQueue(SessionSpellCaster? caster, string why)
    {
        if (_spellQueue.Count == 0) return;
        foreach (var queued in _spellQueue)
        {
            // A queued cast of a replaced session (a quick load) is only dropped with it.
            var freed = caster is not null && ReferenceEquals(queued.Caster, caster) && ReferenceEquals(caster.Owner, _areaSession) ? caster.DropQueued(queued.Run) : null;
            GD.Print($"ARC-MAGIC-019 queued-drop run={queued.Run} spell={queued.Spell} freed={freed?.Disposition.ToString() ?? "nothing"} why={why}");
        }
        _spellQueue.Clear();
        FlushSpellLog("queued casts dropped: " + why);
    }

    // ---- the weapon's spell pane (ARC-UI-016) ----

    /// <summary>The hero's wielded weapon (slot 1004), or null.</summary>
    private string? HeroWeapon() =>
        _areaSession?.QueryEquipment().Worn.FirstOrDefault(worn => worn.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon)?.Entity;

    /// <summary>The weapon button and the weapon's spell pane now: the pane closes (bar mode 0) once its item is no longer the wielded
    /// weapon (the inventory callback 0x5570D0); null without a weapon the cast host knows.</summary>
    private AreaWeaponSpellPane? WeaponPaneNow()
    {
        var weapon = HeroWeapon();
        if (_itemPaneItem is not null && _itemPaneItem != weapon)
        {
            GD.Print($"ARC-UI-016 item-pane-close item={_itemPaneItem} weapon={weapon ?? "none"}");
            _itemPaneItem = null;
            ClearMessagePaneName();
        }
        if (weapon is null || _areaSession is not { } owner || SpellCasterNow() is not { } caster) return null;
        var stamp = (owner, owner.Version.Revision, weapon, _itemPaneItem == weapon, (object?)_spellData?.Data);
        if (_weaponPaneCache is { } cached && cached.Stamp.Equals(stamp)) return cached.Pane;
        var pane = AreaSpellCasting.WeaponPane(caster.ItemPane(weapon), _itemPaneItem == weapon, _spellData?.Data);
        _weaponPaneCache = (stamp, pane);
        return pane;
    }

    /// <summary>The weapon button (0x556EA0 with no object: the wielded weapon; 0x5506C0: a second press returns to mode 0).</summary>
    private void ToggleWeaponPane()
    {
        if (_itemPaneItem is not null) _itemPaneItem = null;
        else if (WeaponPaneNow() is { ButtonShown: true } pane) _itemPaneItem = pane.Item;
        ClearMessagePaneName();
        GD.Print($"ARC-UI-016 item-pane open={_itemPaneItem ?? "no"}");
        if (_spellPane?.Shown == true && _areaSession is { } owner) _spellPane.Show(AreaSpellCasting.Known(owner, _spellData?.Data), WeaponPaneNow());
        RefreshAreaStatus();
    }

    /// <summary>A slot pressed in the weapon's spell pane (0x57C040): the slot's spell is activated for the weapon when it names a spell
    /// without item triggers; otherwise nothing.</summary>
    private void ActivateItemSlot(int slot)
    {
        if (_itemPaneItem is not { } item || SpellCasterNow() is not { } caster || caster.Item(item) is not { } spells || slot < 0 || slot >= spells.Spells.Count)
            return;
        var spell = spells.Spells[slot];
        var activates = ItemSpellPaneRules.CanActivate(spell, caster.ItemTriggered);
        GD.Print($"ARC-UI-016 item-activate item={item} slot={slot} spell={spell} activates={activates}");
        if (!activates) return;
        ActivateSpell(spell, item);
    }

    // ---- activation and the cursor ----

    /// <summary>A spell pressed in the pane (0x57BC70); ARC-UI-016: with <paramref name="item"/> the item is the cast's source and its
    /// holder, the hero, the caster.</summary>
    private void ActivateSpell(int spell, string? item = null)
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
        GD.Print($"ARC-MAGIC-015 activate spell={spell} kind={activation.Kind} mask=0x{activation.Mask:X}" + (item is null ? "" : " item=" + item));
        switch (activation.Kind)
        {
            case SpellActivationKind.Nothing: _spellNotice = "the hero cannot cast now"; break;
            case SpellActivationKind.NoActionPoints: _spellNotice = "not enough action points"; break;
            case SpellActivationKind.CastAtCaster:
                if (HeroWorldCell() is { } cell) CastSpell(spell, null, AreaSpellCastMap.Location(cell), item);
                break;
            case SpellActivationKind.Target:
            case SpellActivationKind.CastAtPlayer:
                // A window open keeps the cursor from opening, but a spell cast at the hero still casts (before ARC-DEV-110 the cursor
                // opened and the per-frame guard closed it, and the cast ran).
                if (OpenSpellTargeting(new(spell, record, activation.Mask, item)))
                {
                    _spellNotice = $"{AreaSpellsPage.SpellName(_spellData?.Data, spell)}: left-click a target, right click cancels";
                    _hoverStamp = null;
                }
                else if (activation.Kind != SpellActivationKind.CastAtPlayer) break;
                if (activation.Kind == SpellActivationKind.CastAtPlayer) CastSpell(spell, SessionSpellCaster.Hero, SpellLocation.None, item);
                break;
        }
        RefreshAreaStatus();
    }

    /// <summary>ARC-DEV-110 (MIG16 S3): the targeting cursor opens as interface mode 1 on the viewer's stack (the original's 0x57BC70 pushes
    /// it). The viewer's spells never carry the inventory bit (that case is NOT READY, above), so the mode replaces the top. A window open
    /// takes the place the cursor would open in: the cursor does not open, as the per-frame window guard used to close it at once.</summary>
    private bool OpenSpellTargeting(SpellTargeting targeting)
    {
        if (AnyWindowOpen)
        {
            _spellNotice = "targeting cancelled: a window opened or the map changed";
            GD.Print("ARC-MAGIC-015 targeting-cancel a window is open");
            return false;
        }
        // One target mode at a time: the other cursors and a pending order leave first, each with its notice.
        CancelArmedLockpicks("a spell's cursor took the next press");
        if (PickPocketArmed) CancelPickPocket("a spell's cursor took the next press");
        CancelPendingOrder("a spell's cursor took the next press");
        if (!OpenTargetMode(InterfaceMode.SpellTarget, _ => ClearSpellTargeting()))
        {
            _spellNotice = "targeting cancelled: the interface mode did not open";
            return false;
        }
        _spellTargeting = targeting;
        return true;
    }

    /// <summary>The leave case of interface mode 1 (SU5): the cursor closes. Runs from the mode's leave step only.</summary>
    private void ClearSpellTargeting()
    {
        _spellTargeting = null; _hoverStamp = null;
    }

    /// <summary>The cursor's cancel (0x57BFF0): the cursor closes (mode 1 leaves the stack, and the mode below it, if any, goes too, SA38);
    /// a goal already playing goes on.</summary>
    private void CancelSpellTargeting(string? why)
    {
        if (_spellTargeting is null) return;
        CloseTargetMode(InterfaceMode.SpellTarget);
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
        DropSpellQueue(_spellQueue.Count > 0 ? _spellQueue[0].Caster : null, why ?? "cancelled");
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

    /// <summary>ARC-MAGIC-021: while the cursor is open, the map objects its pick sees through (SU15), for the screen pick; else null.</summary>
    private Func<AreaObjectKey, bool>? SpellPickSpared()
    {
        if (_spellTargeting is not { } targeting || SpellCasterNow() is not { } caster) return null;
        var spared = caster.PickSpared(SessionSpellCaster.Hero, targeting.Mask);
        return key => SpellPickId(key) is { } id && spared(id);
    }

    // A visiting companion's row is another map's, so it has no id here (its key could name another row of this map).
    private string? SpellPickId(AreaObjectKey key) => _spellCastMap?.IdOf(key);

    // A key-up lost to a focus change cannot leave an Alt held: some Alt must still be down (as the attack's left Alt).
    private bool SpellAltHeld => (_spellAltLeft || _spellAltRight) && Godot.Input.IsKeyPressed(Key.Alt);

    /// <summary>What the cursor would cast at under the screen point (ARC-MAGIC-021: the pick 0x4F28A0's order, SU15–SU19): the object
    /// the screen pick finds there (seeing through what the mask spares), else the objects on the tile under the point (the hero
    /// first, then the map's rows), else the tile; an object that fails stands for its own tile once more. Null when the cursor is
    /// closed or there is no cast host; with no tile under the point only the object is tried.</summary>
    private (string? Object, SpellLocation Tile, SpellTargetTest Test)? SpellTargetAt(Vector2 screen)
    {
        if (_spellTargeting is not { } targeting || SpellCasterNow() is not { } caster || _spellCastMap is not { } map || _canvas is null
            || _worldActor.Session is not AreaGridSession grid) return null;
        var at = AreaObjectAtScreen(screen, spared: SpellPickSpared()) is { } found ? SpellPickId(found) : null;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        SpellLocation? tile = null;
        if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is { } cell)
        {
            var world = grid.Chart.ToWorld(cell);
            tile = new SpellLocation(checked((uint)world.X), checked((uint)world.Y));
        }
        var pick = caster.PickTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, at, tile);
        return (pick.Object?.Id, pick.Tile, pick.Test);
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
        CastSpell(targeting.Spell, pick.Object, pick.Tile, targeting.Item);
    }

    // ---- the cast and its goal ----

    /// <summary>The cast at a target (0x57C110) and, when the run starts, its casting goal's start (0x429B50).</summary>
    private void CastSpell(int spell, string? target, SpellLocation tile, string? item = null)
    {
        if (SpellCasterNow() is not { } caster) return;
        var attempt = caster.CastAt(SessionSpellCaster.Hero, spell, target, tile, item);
        _spellCasts++;
        GD.Print($"ARC-MAGIC-015 cast spell={spell} target={target ?? $"tile {tile.X},{tile.Y}"}{(item is null ? "" : " item=" + item)} outcome={attempt.Outcome} message={attempt.Message}"
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
        // ARC-MAGIC-019 (SU13): while a goal plays the new cast's goal is queued beneath it, or refused (its run freed, silently).
        if (_spellGoal is { } playing)
        {
            var depth = _spellQueue.Count;
            var queued = ReferenceEquals(playing.Caster, caster) ? caster.Queue(run, depth) : SpellCastQueueOutcome.NoRun;
            GD.Print($"ARC-MAGIC-019 cast-queue run={run} spell={spell} depth={depth} outcome={queued}");
            if (queued == SpellCastQueueOutcome.Queued)
            {
                // An entry whose run was freed meanwhile (cancelled by a run that began, QC6) lost its slot to this one.
                _spellQueue.RemoveAll(q => q.Run == run);
                _spellQueue.Add(new(caster, run, spell, target, tile));
                _spellNotice = $"{AreaSpellsPage.SpellName(_spellData?.Data, spell)} queued ({_spellQueue.Count} waiting)";
            }
            else
            {
                if (queued == SpellCastQueueOutcome.NoRun) caster.Act(new(SessionSpellRunActionKind.Free, run));
                _spellNotice = _areaSession?.QueryCombatRound() is not null ? "no cast is queued in a turn-based round" : "the cast queue is full";
            }
            FlushSpellLog(null);
            RefreshAreaStatus();
            return;
        }
        // ARC-COMBAT-020: in a turn-based round the casting goal's start charges its 4 action points (GR19: a loaded round's rule first).
        ResumeLoadedRound();
        var start = caster.StartGoal(run, CombatWorldOrNull() ?? NoSpellCombat.Instance);
        if (start.Outcome != SpellCastGoalOutcome.Started)
        {
            RefuseSpellGoal(caster, run, start.Outcome);
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

    /// <summary>A goal refused at its start ends through its end (0x429CD0, MG39).</summary>
    private void RefuseSpellGoal(SessionSpellCaster caster, int run, SpellCastGoalOutcome outcome)
    {
        var ended = caster.EndGoal(run, refused: true);
        GD.Print($"ARC-MAGIC-015 goal-refused run={run} outcome={outcome} freed={ended?.Disposition}");
        FlushSpellLog(outcome switch
        {
            SpellCastGoalOutcome.NoActionPoints => "not enough action points",
            SpellCastGoalOutcome.CannotAfford => "the hero cannot afford the spell",
            SpellCastGoalOutcome.TargetGone => "the target is gone",
            SpellCastGoalOutcome.CannotTurn => "the hero cannot turn to the target",
            _ => "the cast did not start",
        });
    }

    /// <summary>The cast host's new log entries as the status notice and a message window entry.</summary>
    private void FlushSpellLog(string? lead)
    {
        if (_spellCaster is not { } caster) return;
        var from = _spellLogSeen;
        _spellLogSeen = caster.Log.Count;
        PostSpellLog(lead, caster.Log.Skip(from));
    }

    /// <summary>Log entries as the status notice and a message window entry (ARC-MAGIC-018: also a replaced host's own entries, for a
    /// missile that left with it).</summary>
    private void PostSpellLog(string? lead, IEnumerable<SpellCastEvent> entries)
    {
        var lines = new List<string>();
        if (lead is not null) lines.Add(lead);
        var texts = _spellData?.Data?.Texts;
        foreach (var entry in entries)
        {
            GD.Print($"ARC-MAGIC-015 log kind={entry.Kind} run={entry.Run} value={entry.Value} object={entry.Object} detail={entry.Detail}");
            TakeEyeCandyEvent(entry);
            if (AreaSpellCasting.Line(entry, texts, SpellObjectName) is { } line) lines.Add(line);
        }
        if (lines.Count == 0) return;
        _spellNotice = string.Join("; ", lines);
        PostSpellMessage(_spellNotice);
    }

    // ARC-UI-023: the cast's lines go to the message log as a plain text entry (no sound), titled with the project's label: the
    // original's own spell messages are not built.
    private void PostSpellMessage(string text) => PostMessage(AreaMessageEntry.Text(AreaMessageKind.Plain, "Magic\n" + text), "magic");

    private string SpellObjectName(string id) =>
        id == SessionSpellCaster.Hero ? "the hero"
        : _spellCastMap?.Row(id) is { } row && _hoverIndex is { } index && index.Contains(row.Key) ? index.Name(row.Key) ?? id : id;

    private string SpellStatus() => !SpellMapActive || _areaSession?.QueryHero() is null ? ""
        : " | M spells" + (_spellTargeting is { } t ? $" (targeting spell {t.Spell})" : "") + (_spellGoal is { } g ? $" (casting run {g.Run}" + (_spellQueue.Count > 0 ? $", {_spellQueue.Count} queued)" : ")") : "")
          + (_spellNotice is null ? "" : ": " + _spellNotice);

    /// <summary>Outside a turn-based round a casting goal charges no action points: the world it is given then is never asked.</summary>
    private sealed class NoSpellCombat : ISessionCombatWorld
    {
        public static readonly NoSpellCombat Instance = new();

        public IReadOnlyList<SessionCombatCandidate> ScanCombatants(SessionMapId map) => [];

        public SessionCombatantFacts? CombatantFacts(SessionMapId map, string entity) => null;
    }
}
