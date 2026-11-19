using Arcanum.Simulation.Ai;
using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
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
        /// <summary>ARC-DEV-183: the play never reaches its action frame (EC7): the action frame releases nothing.</summary>
        public bool NoRelease { get; set; }
        /// <summary>ARC-DEV-183: a queued cast whose table run starts at the next safe point (its start is not made yet).</summary>
        public bool Queued { get; set; }
        /// <summary>ARC-DEV-183: the goal's start (node 1 of goal 26) passed; a goal cut before it frees its run unpaid.</summary>
        public bool Started { get; set; }
        /// <summary>ARC-DEV-183: the waits the table was given (release at, end at, from the goal's start): the smoke's timing record.</summary>
        public long PlannedReleaseMs { get; set; }
        public long PlannedEndMs { get; set; }
        /// <summary>ARC-DEV-318: the play's start found cast eye candy that plays (goal 27 went on to its action frame); without it the goal ends through the end step and drops the queue.</summary>
        public bool Played { get; set; }
        /// <summary>ARC-DEV-318: the holder was rebuilt from a restored run (its play was not timed here: no self-test timing).</summary>
        public bool Restored { get; init; }
    }

    // ARC-DEV-183: why the last start was refused (for the refusal's message).
    private SpellCastGoalOutcome _spellStartOutcome;
    // ARC-DEV-318 (MIG14 S9c): the session whose hero casts this host has seen start; a restored session's casting run is adopted once (the holder and the queue rebuilt from the run),
    // a live run whose holder was cleared never is.
    private GameSessionOwner? _castOwner;

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
        // ARC-MAGIC-1280: a session's first cast host keeps the overlay runs its load restored (their host was built by their first step); a rebuild for a map or its portals does not.
        ClearEyeCandy("the map changed", keepRestoredOverlays: _spellCaster is null || !ReferenceEquals(_spellCaster.Owner, owner));
        var map = new AreaSpellCastMap(owner, area.Snapshot.RuntimeMap, grid.Snapshot, area.Snapshot.Objects, _combatHost!, HeroWorldCell, AreaLineOfFireNow,
            key => AreaRemovedObjects().Contains(key), SpellPlacedCompanions, HeroHolds, SpellVisitingCompanions,
            // ARC-DEV-255: damage to a door, window, container, scenery or trap goes through the object-attack host's baselines.
            row => ObjectTargets()?.Target(owner, row.Key, AreaTrapped),
            // ARC-COMBAT-025: the hero's starting kit, so the items it wears take an acid's damage.
            () => _areaKitDurability,
            // ARC-MAGIC-1232 (DW2): a spell's weapon drop (Disarm) places the item as a critical drop does.
            AreaDropMapNow);
        // ARC-AI-2124 (GW9): a spell's damage on an object runs its hit steps (damage line, break, guard witnesses) inside that damage step, as the original's damage end does, not when the cast log is flushed.
        map.ObjectHitMade = LogObjectHits;
        // ARC-COMBAT-1934 (0x4ADE00): an NPC caster's line of fire starts at its pixel offset, as the AI host reads it for sight.
        map.ParentOffset = id => _aiHost?.ShooterOffset(id) ?? (0, 0);
        var runtimeMap = area.Snapshot.RuntimeMap;
        _spellCaster = new SessionSpellCaster(owner, n => n >= 0 && n < records.Length ? records[n] : null, map, AreaEvents,
            id => HeroItemSpells(id, true) ?? _combatHost!.NpcItemSpells(owner, id) ?? _combatHost.ContainerItemSpells(owner, id));
        // ARC-UI-031: the host's own life steps (a spell's damage, a payment's fatigue, its passes) fire the worn items' spells.
        var castCombat = new AreaCombatWorld(runtimeMap, AreaScanCritters, AreaCombatantFacts);
        _spellCaster.ItemTriggers = new SessionItemTriggers(_spellCaster, castCombat);
        // ARC-MAGIC-049 (E4-e): the goals of the casts a Cast component starts (goal 73) play in the same combat world (the action points of a turn-based round).
        _spellCaster.CastCombat = castCombat;
        // ARC-DEV-254: a script's damage asks the victim's taking-damage script (point 0x17) first.
        _spellCaster.TakingDamage = TakingDamageGoesOn;
        // ARC-RENDER-1794 (OH2): the damage effect's blood of a hit reads the violence filter as the options set it.
        _spellCaster.ViolenceFilter = () => _gameSettings.ViolenceFilter;
        // ARC-MAGIC-1167 (MA-10): a Heal with the raise bit asks a dead target's raise script (point 0x12) first.
        _spellCaster.RaiseScript = RaiseGoesOn;
        // ARC-AI-1680: a spell's, trap's or burn's damage step asks a follower's near-death line (0x4AEAB0) as it lands, as the damage step 0x4B4390 does.
        _spellCaster.DamageStepApplied = AskNearDeathOfCastStep;
        // ARC-MAGIC-048 (E4-c): the Use component has its target run its own use script (the target is the triggerer too).
        _spellCaster.UseObject = UseScriptOfCast;
        // ARC-DEV-281 (B08): an aggressive spell tells the AI of its target that the caster attacked it (the attack event), before the damage lands.
        _spellCaster.AiNotice = (attacker, victim) => _aiHost?.SpellAttacked(attacker, victim);
        // ARC-DEV-292: and a damage that took effect on an NPC gives it its immediate AI pass (0x4B52FC), after the life step.
        _spellCaster.AiStruck = victim => _aiHost?.Struck(victim);
        // ARC-AI-2402: a worn mana store drained to 0 drops the payer's slot of the AI list cache (0x4CCC40), as the original's payment does.
        _spellCaster.ManaStoreEmptied = (payer, item) => _aiHost?.ManaStoreEmptied(payer, item);
        // ARC-TECHNOLOGY-1635 (TT15): a trap's shot flies as a projectile and strikes where it meets the victim.
        _spellCaster.LaunchTrapShot = LaunchTrapShotFlight;
        // ARC-MAGIC-035 (MA-4): the social and AI flags (charm and mind control's reactions and peace, fear's flight) run through the AI host.
        _spellCaster.Social = new AreaSpellSocial(() => _aiHost);
        // ARC-MAGIC-038 (MA-5 second half): the teleport types of the Movement component (the anti-teleport test, the area helper) ask the world-map data and the shown map.
        _spellCaster.Teleport = NewSpellTeleportHost();
        // ARC-DEV-303 (EF9): the Effect component asks the hero's effect tables (effect.mes, the module's gameeffect.mes) for the rule of a number (Entangle's effect 62: speed -5); a rule the session refuses is none.
        _spellCaster.EffectDefinitions = number =>
        {
            try { return _areaEffectRules?.Invoke(number) is { } rule ? SessionEffectDefinition.FromRule(rule) : null; }
            catch (ArgumentException) { return null; }
        };
        // ARC-MAGIC-1644: the TraitIdx rewield (magick and tech points) runs the wield scripts of the NPC's worn items through the inventory host's item script host.
        _spellCaster.ItemScripts = () => _inventoryHost?.ItemScripts(owner);
        // ARC-DEV-352 (E1-b): the Summon component creates on this map from the module's prototypes; the eye candies' callbacks call the runs back.
        WireSummon(_spellCaster);
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
    // ARC-DEV-140: thrownOnMap lets the cast host's lookup see a thrown item lying on the map; item use does not.
    private SessionItemSpells? HeroItemSpells(string id, bool thrownOnMap = false) =>
        _lentItemSpellsForSelfTest is { } lent && lent.Entity == id ? lent
        : _areaSession is { } owner && _inventoryHost is { } host ? host.ItemSpells(owner, id, _areaKitItemSpells, thrownOnMap) : null;

    // ARC-MAGIC-021: the companions' rows where they stand now (the committed cell), none while on another map. ARC-WORLD-102: the NPCs
    // the AI walked stand on their cells too (a caster's range and line of fire, a target's tile).
    private IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> SpellPlacedCompanions()
    {
        var placed = new Dictionary<AreaObjectKey, (long X, long Y)?>();
        if (_aiHost is { } ai && _readyArea is { } shown && ai.Scene.RuntimeMap == shown.Snapshot.RuntimeMap)
            foreach (var mover in ai.Movers.All) placed[mover.Key] = (mover.Cell.X, mover.Cell.Y);
        // ARC-MAGIC-053: a critter the session created on this map (a summoned creature) stands on its creation cell until the AI host moves it.
        foreach (var (made, row) in SpellCreatedCritters()) if (!placed.ContainsKey(row.Key)) placed[row.Key] = (made.Cell.X, made.Cell.Y);
        if (_areaCompanions is not { } group || _readyArea is not { } area || group.Map.RuntimeMap != area.Snapshot.RuntimeMap) return placed;
        foreach (var key in group.Absent) placed[key] = null;
        foreach (var companion in group.Companions) placed[companion.Key] = group.WorldCell(companion.Speaker);
        return placed;
    }

    // ARC-DEV-076 (MIG3): the companions carried here from another map: spell targets and party members through their home map's record.
    private IReadOnlyList<(string Entity, AreaObjectRecord Row)> SpellVisitingCompanions() =>
        [.. (_areaCompanions is { } group && _readyArea is { } area && group.Map.RuntimeMap == area.Snapshot.RuntimeMap
            ? group.Companions.Where(companion => companion.Template.Home.RuntimeMap != area.Snapshot.RuntimeMap)
                .Select(companion => (companion.Entity, companion.Speaker.Row))
            : []).Concat(SpellCreatedCritters().Select(created => (created.Made.Entity, created.Row)))];

    // ARC-MAGIC-053: the critters the session created on the shown map with their rows (built from their prototypes, by identity): spell targets and party members like a map NPC, found by the cast map as
    // visitors (a created critter is no row of the map's file). None without the module's prototypes or an area session.
    private IReadOnlyList<(SessionCreatedObject Made, AreaObjectRecord Row)> SpellCreatedCritters()
    {
        if (_areaSession is not { } session || _createdPrototypeRead is null || _readyArea is null) return [];
        var map = new SessionMapId(CurrentAreaMap);
        var found = new List<(SessionCreatedObject, AreaObjectRecord)>();
        foreach (var made in session.QueryCreatedObjects(map))
            if (made.Kind == SessionCreatedKind.Critter && CreatedCrittersRow(made.Entity) is { } row) found.Add((made, row));
        return found;
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
        // ARC-UI-2304: the spells mode replaces bar mode 8 (0x5506C0 with 1).
        _itemPaneItem = null; _weaponPaneCache = null;
        _spellPane.ListInStrip = SpellListInStrip;
        _spellPane.Show(AreaSpellCasting.Known(_areaSession!, _spellData?.Data), DiagnosticWeaponPane());
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
        // ARC-DEV-338 (B53): a run that has no casting goal begins by its magic-tech event (a script's cast without a caster, 0x455C30: 2 ms later), which the frame step fires: its Begin
        // components (the eye candy of the ghost of Brego, the spoken-with-dead flag) log into the cast host with no call of the viewer's own behind them. Posted here on each world frame.
        if (_spellCaster is { } cast && ReferenceEquals(cast.Owner, _areaSession) && cast.Log.Count > _spellLogSeen) FlushSpellLog(null);
        // ARC-UI-016: the pane leaves the weapon's spells when the weapon leaves its slot, shown or not (0x5570D0 on the removal).
        if (_itemPaneItem is not null && _itemPaneItem != HeroWeapon()) WeaponPaneNow();
        if (_spellPane?.Shown == true)
        {
            if (!SpellMapActive || _areaSession?.QueryHero() is null) _spellPane.HidePane();
            else _spellPane.Show(AreaSpellCasting.Known(_areaSession, _spellData?.Data), DiagnosticWeaponPane());
        }
        if (_spellTargeting is not null && (!SpellMapActive || TargetsDroppedByModal || ConversationOpen)) CancelSpellTargeting("a window opened or the map changed");
        // Another map, actor or session rebuilds the host, which ends the goal through its own host first; no host ends it too.
        if (_spellGoal is not null && SpellCasterNow() is null) CancelSpellCast("the map changed");
        // ARC-DEV-318: a restored session's casting run gets its holder back.
        if (_spellGoal is null) AdoptRestoredCast();
        if (_spellGoal is not { } goal || _areaSession is not { } owner) return;
        // ARC-DEV-183 (MIG13 S12c): the goal is a run of the table (HeroCastNode, HeroAttemptSpellNode); this keeps its guards and the cast eye candy's frames. ARC-DEV-318: a queued cast is a goal 26 beneath it on the table, which starts it when the goals above it pop.
        if (!goal.Released && owner.QueryHeroLife() is not { Status.IsActive: true }) { StopHeroCast("the hero cannot act"); return; }
        // The run is gone (a round's start sweep, an interrupt, the goal's own end): the cast host ends the goal.
        if (HeroRunsHost() is { CastRun: null }) { EndSpellGoal(goal.Caster, goal, "the cast was interrupted"); return; }
        // The overlay's frames follow the clock; the release and the end are the table's.
        if (goal.Animation is { Immediate: false } play) play.Advance(WorldMilliseconds(), () => true);
    }

    /// <summary>The casting run of a goal the cast host built: goal 26 starts on the table. False (and the run freed unpaid) when the request was refused.</summary>
    private bool StartHeroCastRun(SpellCastGoal goal)
    {
        if (HeroRunsHost() is not null && HeroRequests.Apply(new CastRequest(goal.Run, goal.TargetObject)).Outcome == HeroRequestOutcome.Done) return true;
        _spellGoal = null;
        RefuseSpellGoal(goal.Caster, goal.Run, SpellCastGoalOutcome.NoRun);
        return false;
    }

    /// <summary>The hero's cast is stopped (a cancel, a lost hero): its table run is killed, which ends the goal through its end node.</summary>
    private void StopHeroCast(string why)
    {
        try { _heroRunsHost?.EndCast(why); }
        catch (ObjectDisposedException) { /* a replaced session */ }
        if (_spellGoal is { } goal && ReferenceEquals(goal.Caster.Owner, _areaSession)) EndSpellGoal(goal.Caster, goal, why);
    }

    // ---- the casting goals' ports (ARC-DEV-183) ----

    // ARC-DEV-318: the goal of the run, adopted from a restored run when the host has none (by the nodes' ports, which only the shown session's stepper calls; not by an interrupt, which may be
    // a dropped session's, whose run number a restored run of the same save shares). A holder of a replaced session (a quick load) is dropped first.
    private SpellCastGoal? CastGoalOf(int run, bool adopt = true)
    {
        if (_spellGoal is { } stale && !ReferenceEquals(stale.Caster.Owner, _areaSession)) CancelSpellCast("the session was replaced");
        return _spellGoal is { } goal ? goal.Run == run ? goal : null : adopt ? AdoptRestoredCast(run) : null;
    }

    /// <summary>
    /// ARC-DEV-318 (MIG14 S9c): a restored session's hero casting run gets its holder back once: the playing goal from the run's top (its spell and tile from the spell run, its target from the frame) and the queue from the
    /// goals 26 beneath it, first cast first. A goal 27 between its play's start and its action frame replays the cast eye candy from its first frame (the overlay is not saved) and asks it again whether the play releases;
    /// one after its action frame is released (its run freed or begun meanwhile ends nothing more). Null when there is nothing to adopt or no cast host can be built (a cold load's spell data still loading waits for it at
    /// the run's first step, bounded).
    /// </summary>
    private SpellCastGoal? AdoptRestoredCast(int? run = null)
    {
        if (_spellGoal is not null || _areaSession is not { } owner || ReferenceEquals(_castOwner, owner)) return null;
        GoalRun? held;
        try { held = HeroRunsHost()?.CastRun; }
        catch (ObjectDisposedException) { return null; } // a replaced session
        if (held is not { Top.SpellRun: { } top } || run is { } asked && asked != top) return null;
        var caster = SpellCasterNow();
        if (caster is null && run is not null && _spellData is null && _spellDataTask is { } loading)
        {
            try { loading.Wait(TimeSpan.FromSeconds(30)); }
            catch (AggregateException) { /* AdoptSpellData records the failure */ }
            caster = SpellCasterNow();
        }
        if (caster is null) return null;
        _castOwner = owner;
        var runs = owner.QuerySpellRuns();
        SpellCastGoal Holder(GoalFrame frame)
        {
            var id = frame.SpellRun!.Value;
            return new(caster, id, runs[id]?.Spell ?? -1, frame.Target, runs[id]?.TargetTile ?? default) { Restored = true };
        }
        var goal = Holder(held.Top);
        var now = WorldMilliseconds();
        if (held.Top.Goal == GoalType.AttemptSpell)
        {
            goal.Started = true; goal.StartMs = now;
            goal.Played = held.Top.Node >= 2;
            if (held.Top.Node == 2 && goal.Spell >= 0)
            {
                var animation = goal.Animation = StartCastEyeCandy(top, goal.Spell, now, out var probe);
                animation.PaceAfterRelease = probe.PaceAfterRelease = SpellMissileArt(goal.Spell) is not null;
                // A play whose art the body lacks now is not asked: the action frame releases, as a play with a release frame does.
                if (!probe.Immediate) goal.NoRelease = !probe.Advance(long.MaxValue, () => true).Any(step => step.Kind == EyeCandyStepKind.Release);
            }
            // After the action frame the run was released or launched (or freed by its re-check, MG41): the goal's end frees nothing, whoever holds the slot now.
            else if (held.Top.Node >= 3) { goal.Released = true; goal.ReleasedMs = now; goal.FreedAtRelease = true; }
        }
        _spellQueue.Clear();
        for (var i = held.Frames.Length - 2; i >= 0; i--) _spellQueue.Add(Holder(held.Frames[i]));
        _spellGoal = goal;
        // A goal adopted after its release may name a run already freed (an instant spell): its spell is not known (−1) and is not named.
        _spellNotice = (goal.Spell >= 0 ? $"casting {AreaSpellsPage.SpellName(_spellData?.Data, goal.Spell)}" : "casting") + (_spellQueue.Count > 0 ? $" ({_spellQueue.Count} queued)" : "");
        GD.Print($"ARC-DEV-318 cast-adopt run={top} spell={goal.Spell} goal={(int)held.Top.Goal} node={held.Top.Node} released={goal.Released} no-release={goal.NoRelease} queued={_spellQueue.Count}");
        RefreshAreaStatus();
        return goal;
    }

    /// <summary>The casting goal's start (0x429B50 → 0x429BC0): the action points of a round and the pre-animation check through the cast host; the turn it computed is applied here.</summary>
    internal AiCastGoalStart HeroStartCast(int run)
    {
        if (CastGoalOf(run) is not { } goal) return AiCastGoalStart.Refused;
        var start = goal.Caster.StartGoal(run, CombatWorldOrNull() ?? NoSpellCombat.Instance);
        _spellStartOutcome = start.Outcome;
        goal.Started = start.Outcome == SpellCastGoalOutcome.Started;
        if (start.Outcome != SpellCastGoalOutcome.Started) return start.Outcome == SpellCastGoalOutcome.TargetGone ? AiCastGoalStart.TargetGone : AiCastGoalStart.Refused;
        if (goal.Queued) { _spellQueuedStarts++; GD.Print($"ARC-MAGIC-019 queued-start run={run} spell={goal.Spell} outcome={start.Outcome} waiting={_spellQueue.Count}"); }
        // ARC-MAGIC-018: goal 26 turns the caster toward its target (0x42C850).
        TurnHeroForCast(start.Facing);
        _spellNotice = $"casting {AreaSpellsPage.SpellName(_spellData?.Data, goal.Spell)}" + (_spellQueue.Count > 0 ? $" ({_spellQueue.Count} queued)" : "");
        GD.Print($"ARC-MAGIC-015 goal-start run={run} spell={goal.Spell} ap={start.ActionPoints?.Charge.ToString() ?? "no round"}");
        RefreshAreaStatus();
        return AiCastGoalStart.Started;
    }

    internal bool HeroFaceCast(int run) => true;

    /// <summary>The play's start (0x429F00): the target re-check, then the hero's cast eye candy, whose release frame and last frame time the goal (a dry run of the same animation gives the table its waits).</summary>
    internal AiCastSwing HeroBeginCast(int run)
    {
        if (CastGoalOf(run) is not { } goal || _areaSession is not { } owner) return new(false);
        var now = WorldMilliseconds();
        goal.StartMs = now;
        // ARC-MAGIC-019: before the cast eye candy is added the target is checked again (0x429F00 → 0x456A90); a failure releases at once (nothing begins, MG41) and the goal ends through the end step.
        if (owner.QuerySpellRuns()[run] is { } awaiting && !goal.Caster.TargetStillValid(awaiting)) { goal.TargetFailedAtStart = true; _spellTargetFailedAtStart++; return new(false); }
        var animation = goal.Animation = StartCastEyeCandy(run, goal.Spell, now, out var probe);
        if (animation.Immediate) return new(false);
        // ARC-MAGIC-018: a release that launches a missile restarts the goal one pace later (0x4246E0, SM1).
        animation.PaceAfterRelease = probe.PaceAfterRelease = SpellMissileArt(goal.Spell) is not null;
        long? release = null;
        long end = probe.StartMs;
        foreach (var step in probe.Advance(long.MaxValue, () => true))
        {
            if (step.Kind == EyeCandyStepKind.Release) release ??= step.AtMs;
            end = Math.Max(end, step.AtMs);
        }
        if (release is null) goal.NoRelease = true;
        goal.Played = true;
        var swing = release is { } at
            ? new AiCastSwing(true, (int)Math.Clamp(at - probe.StartMs, 1, int.MaxValue), (int)Math.Clamp(end - at, 0, int.MaxValue))
            : new AiCastSwing(true, (int)Math.Clamp(end - probe.StartMs, 1, int.MaxValue));
        goal.PlannedReleaseMs = swing.ActionFrameMs; goal.PlannedEndMs = swing.ActionFrameMs + swing.RestMs;
        return swing;
    }

    /// <summary>The play's action frame (0x42CAA0 → 0x429C40): the missile launches or the run is released; a play that never reaches its action frame releases nothing.</summary>
    internal bool HeroCastActionFrame(int run) =>
        CastGoalOf(run) is { NoRelease: false } goal && ReleaseSpellGoal(goal.Caster, goal, WorldMilliseconds(), fromGate: true);

    /// <summary>The release without a play (0x429C80).</summary>
    internal bool HeroReleaseCast(int run) => CastGoalOf(run) is { } goal && ReleaseSpellGoal(goal.Caster, goal, WorldMilliseconds(), fromGate: false);

    /// <summary>The goal ended on its own (the end node 0x429CD0): the run is freed as it was, and the queue decided.</summary>
    internal void HeroCastEnds(int run, HeroCastEnd how)
    {
        if (CastGoalOf(run) is not { } goal) return;
        switch (how)
        {
            case HeroCastEnd.Refused:
                _spellGoal = null;
                RefuseSpellGoal(goal.Caster, run, _spellStartOutcome);
                DropSpellQueue(goal.Caster, "a cast was refused at its start");
                RefreshAreaStatus();
                return;
            case HeroCastEnd.Popped when goal is { Restored: false, Animation: { Immediate: false } played }:
                {
                    _castTimingsForSelfTest.Add((SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.Cast), goal.PlannedReleaseMs, goal.PlannedEndMs, played.Immediate));
                    if (_castTimingsForSelfTest.Count > 16) _castTimingsForSelfTest.RemoveAt(0);
                    break;
                }
        }
        EndSpellGoal(goal.Caster, goal, null, endStep: how == HeroCastEnd.EndStep);
    }

    /// <summary>A cast run ended by an interrupt or a kill: the cast host frees the run and drops the queue.</summary>
    internal void HeroCastInterrupted(int run, string why)
    {
        if (CastGoalOf(run, adopt: false) is { } goal) EndSpellGoal(goal.Caster, goal, why);
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
        if (released is not null) PlayEyeCandy(SessionSpellCaster.Hero, SpellEyeCandyRules.EntryIndex(goal.Spell, SpellEyeCandyRules.SecondaryCast), goal.Run);
        // MG41 (fixed in both settings): a refused re-check freed the run, so the goal's end must not touch its slot, which another
        // cast may take before the end (a queued hero cast, an NPC's cast through the same host).
        else if (_areaSession?.QuerySpellRuns()[goal.Run] is null) goal.FreedAtRelease = true;
        FlushSpellLog(released is null ? "the target is no longer valid" : null);
        return released is not null;
    }

    private void EndSpellGoal(SessionSpellCaster caster, SpellCastGoal goal, string? why, bool endStep = false)
    {
        _spellGoal = null;
        // The end node removes the cast eye candy (0x429CD0, 0x431A40).
        _eyeCandies.RemoveAll(e => e.Run == goal.Run);
        var ended = goal.FreedAtRelease ? null : caster.EndGoal(goal.Run, refused: !goal.Started);
        GD.Print($"ARC-MAGIC-015 cast-end run={goal.Run} spell={goal.Spell} released={goal.Released} freed={ended?.Disposition.ToString() ?? "nothing"}"
            + (why is null ? "" : " why=" + why));
        FlushSpellLog(why);
        // ARC-MAGIC-019: a goal that played its cast eye candy to the end pops (the overlay's removal answers true, 0x431A40), so the
        // next queued cast's goal starts; without cast eye candy, or after a target that failed before it, the goal ends through the
        // end step (0x90000000), and a goal cut short interrupts the caster's whole stack (0x44E2C0): both drop the queue.
        if (why is not null) DropSpellQueue(caster, why);
        else if (goal.TargetFailedAtStart) DropSpellQueue(caster, "the target failed before the cast eye candy (end step)");
        else if (endStep || !goal.Played) DropSpellQueue(caster, "the cast had no eye candy (end step)");
        else StartQueuedSpellGoal(caster);
        RefreshAreaStatus();
    }

    /// <summary>The first queued cast's goal starts (0x423D10's pop restarts it at its first node). A start refused there ends
    /// through the goal's end and the end step, which ends the caster's stack and drops the casts queued behind it. ARC-DEV-318: the queued goal is the frame
    /// beneath the popping one on the table, which steps it next; this makes it the playing goal.</summary>
    private void StartQueuedSpellGoal(SessionSpellCaster caster)
    {
        if (_spellQueue.Count == 0) return;
        // A caster lost meanwhile (knocked out by the release, say) kills its whole stack (0x44E2C0): no queued goal starts.
        if (_areaSession?.QueryHeroLife() is not { Status.IsActive: true }) { DropSpellQueue(caster, "the hero cannot act"); return; }
        var next = _spellQueue[0];
        _spellQueue.RemoveAt(0);
        if (!ReferenceEquals(next.Caster, caster)) { DropSpellQueue(caster, "the cast host changed"); return; }
        next.Queued = true;
        _spellGoal = next;
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

    /// <summary>ARC-UI-2304: the diagnostic pane's weapon button and pane only while the bottom strip does not draw them.</summary>
    private AreaWeaponSpellPane? DiagnosticWeaponPane() => WeaponPaneInStrip ? null : WeaponPaneNow();

    /// <summary>The diagnostic pane's weapon button (0x556EA0 with no object: the wielded weapon; 0x5506C0: a second press returns to mode 0).</summary>
    private void ToggleWeaponPane()
    {
        if (_itemPaneItem is not null) _itemPaneItem = null;
        else if (WeaponPaneNow() is { ButtonShown: true } pane) _itemPaneItem = pane.Item;
        ClearMessagePaneName();
        GD.Print($"ARC-UI-016 item-pane open={_itemPaneItem ?? "no"}");
        if (_spellPane?.Shown == true && _areaSession is { } owner) _spellPane.Show(AreaSpellCasting.Known(owner, _spellData?.Data), DiagnosticWeaponPane());
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
        // ARC-MAGIC-1128 (SU3): the inventory bit opens the caster's inventory before the cursor's tests; the cursor then stands over the window.
        var inventory = active && record.Data.Enabled && SpellUiRules.TargetsInventory(begin);
        if (inventory && !OpenInventoryForSpell()) { RefreshAreaStatus(); return; }
        var activation = SpellUiRules.Activate(active, record.Data.Enabled, begin, record.Resist.Aggressive, Godot.Input.IsKeyPressed(Key.Alt),
            Godot.Input.IsKeyPressed(Key.Shift), owner.QueryActionPointCheck(SessionCombatant.Hero, TurnBasedRules.CastSpellCost));
        // ARC-MAGIC-1128: the quick cast at the hero (Shift) would skip the pick's area test, which refuses the hero for an inventory spell (it is no item in an inventory): the cursor opens instead.
        if (inventory && activation.Kind == SpellActivationKind.CastAtPlayer) activation = new(SpellActivationKind.Target, activation.Mask);
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
                if (OpenSpellTargeting(new(spell, record, activation.Mask, item), inventory))
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

    /// <summary>
    /// ARC-MAGIC-1128 (0x57BC70, SU3): an activation whose BEGIN area has the inventory bit opens the caster's own inventory first. ARC-MAGIC-1336 (read again in 0x57BC70): the barter (4) and steal (0xB) windows are
    /// closed first (0x573440), a loot window (10) is kept and the cursor stands over it (its holder's items and the hero's are the targets), and the own window opens unless an inventory window is open
    /// then; another window of the stack (the spell pane is a bar panel, the other windows replace it) refuses.
    /// </summary>
    private bool OpenInventoryForSpell()
    {
        if (!InventoryActive) { _spellNotice = "a spell on an inventory item needs the hero's inventory here"; return false; }
        if (WindowOpen(InterfaceMode.Barter)) EndBarter();
        if (WindowOpen(InterfaceMode.Steal)) EndInventory();
        if (!InventoryOpen)
        {
            if (AnyWindowOpen) { _spellNotice = "targeting cancelled: a window is open"; GD.Print("ARC-MAGIC-1128 inventory-target not opened: a window is open"); return false; }
            OpenInventory();
        }
        if (WindowOpen(InterfaceMode.Inventory) || WindowOpen(InterfaceMode.Loot)) return true;
        _spellNotice = "the inventory did not open";
        return false;
    }

    /// <summary>ARC-DEV-110 (MIG16 S3): the targeting cursor opens as interface mode 1 on the viewer's stack (the original's 0x57BC70 pushes
    /// it). ARC-MAGIC-1128: with <paramref name="overInventory"/> (the BEGIN area has the inventory bit) it is pushed over the inventory window, which stays and picks the target (<see cref="InventoryItemPicked"/>);
    /// without it the mode replaces the top. A window open takes the place the cursor would open in: the cursor does not open, as the per-frame window guard used to close it at once.</summary>
    private bool OpenSpellTargeting(SpellTargeting targeting, bool overInventory = false)
    {
        if (AnyWindowOpen && !overInventory)
        {
            _spellNotice = "targeting cancelled: a window opened or the map changed";
            GD.Print("ARC-MAGIC-015 targeting-cancel a window is open");
            return false;
        }
        // One target mode at a time: the other cursors and a pending order leave first, each with its notice.
        CancelArmedLockpicks("a spell's cursor took the next press");
        if (PickPocketArmed) CancelPickPocket("a spell's cursor took the next press");
        CancelDisarm("a spell's cursor took the next press");
        CancelPendingOrder("a spell's cursor took the next press");
        if (!OpenTargetMode(InterfaceMode.SpellTarget, _ => ClearSpellTargeting(), overInventory))
        {
            _spellNotice = "targeting cancelled: the interface mode did not open";
            return false;
        }
        _spellTargeting = targeting;
        if (overInventory && _inventoryPanel is not null) _inventoryPanel.TargetPicking = true;
        return true;
    }

    /// <summary>The leave case of interface mode 1 (SU5): the cursor closes. Runs from the mode's leave step only.</summary>
    private void ClearSpellTargeting()
    {
        _spellTargeting = null; _hoverStamp = null;
        if (_inventoryPanel is not null) _inventoryPanel.TargetPicking = false;
    }

    /// <summary>The cursor's cancel (0x57BFF0): the cursor closes (mode 1 leaves the stack, and the mode below it, if any, goes too, SA38);
    /// a goal already playing goes on.</summary>
    private void CancelSpellTargeting(string? why)
    {
        if (_spellTargeting is null) return;
        // ARC-MAGIC-1336: a cursor over a loot window takes the window with it when it leaves (SA38); the stack only hides the panel, so the loot window's close runs here (the lid, the junk pile, the theft).
        var lootHolder = WindowOpen(InterfaceMode.Loot) ? _inventoryPanel?.Holder : null;
        var lootOpen = WindowOpen(InterfaceMode.Loot);
        CloseTargetMode(InterfaceMode.SpellTarget);
        if (lootOpen && !WindowOpen(InterfaceMode.Loot)) FinishInventoryClose(lootHolder);
        if (why is not null) { _spellNotice = "targeting cancelled: " + why; GD.Print("ARC-MAGIC-015 targeting-cancel " + why); }
    }

    /// <summary>Closes the cursor and ends a playing goal (a goal ended before its release frame frees its run, 0x429CD0).</summary>
    private void CancelSpellCast(string? why)
    {
        CancelSpellTargeting(why);
        // A goal ends through its own host while its session is the shown one (a map change frees its run, 0x429CD0); a goal of a
        // replaced session (a quick load) is only dropped.
        if (_spellGoal is { } goal && ReferenceEquals(goal.Caster.Owner, _areaSession)) StopHeroCast(why ?? "cancelled");
        else if (_spellGoal is { } dropped) _eyeCandies.RemoveAll(e => e.Run == dropped.Run);
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
        // SU9: the game view picks only with no inventory window open; with one open the window's own press picks (ARC-MAGIC-1128).
        if (InventoryOpen) return;
        if (SpellTargetAt(screen) is not { } pick) { _spellNotice = "no target here"; RefreshAreaStatus(); return; }
        // ARC-INPUT-1356: a refused press says what the pointer was over (a report of a missed creature then shows whether it missed the body or hit a corpse).
        if (!pick.Test.Passes) GD.Print("ARC-INPUT-1356 target-refused " + PickRefusalDetail(screen));
        SpellTargetApply(targeting, pick.Object, pick.Tile, pick.Test);
    }

    /// <summary>
    /// ARC-MAGIC-1128 (the mode-1 branch of the inventory window's mouse handler 0x573850, picker 0x575FA0, ledger IW44): a left press on an item of the window with the spell's cursor open takes the item as the object target and
    /// casts at it (0x57C110). The pick runs the same area test as the game view's (<see cref="SessionSpellCaster.PickTarget"/> with the item as the point's object, no tile: an item the test refuses is no target).
    /// </summary>
    private void InventoryItemPicked(string item)
    {
        // The game view's guards of a press (WorldRouteClick): a hero lying down, another participant's turn in a round, a map transfer under way.
        if (HeroLyingDown || AnotherHasTheTurn || AreaTransferPending) { _spellNotice = "the hero cannot act now"; RefreshAreaStatus(); return; }
        if (_itemAiming is { } aim && TargetModeOpen(InterfaceMode.ItemTarget) && SpellUiRules.TargetsInventory(aim.Mask)) { ItemAimInventoryPick(aim, item); return; }
        if (_spellTargeting is not { } targeting || !SpellUiRules.TargetsInventory(targeting.Mask) || SpellCasterNow() is not { } caster) return;
        var pick = caster.PickTarget(SessionSpellCaster.Hero, targeting.Spell, targeting.Mask, item, null);
        GD.Print($"ARC-MAGIC-1128 inventory-pick spell={targeting.Spell} item={item} passes={pick.Test.Passes} reason=0x{pick.Test.Reason:X}");
        SpellTargetApply(targeting, pick.Object?.Id, pick.Tile, pick.Test);
    }

    /// <summary>The click's tail: a target that passes is cast at; otherwise the "not damaged" failure shows spell.mes 606 and the cursor stays.</summary>
    private void SpellTargetApply(SpellTargeting targeting, string? target, SpellLocation tile, SpellTargetTest test)
    {
        if (!test.Passes)
        {
            if (SpellUiRules.NoTargetMessage(test.Reason) is { } message)
            {
                _spellNotice = AreaSpellCasting.Message(_spellData?.Data?.Texts, message);
                PostSpellMessage(_spellNotice);
            }
            else _spellNotice = "not a valid target";
            GD.Print($"ARC-MAGIC-015 target-refused spell={targeting.Spell} reason=0x{test.Reason:X}");
            RefreshAreaStatus();
            return;
        }
        CastSpell(targeting.Spell, target, tile, targeting.Item);
    }

    // ---- the cast and its goal ----

    /// <summary>The cast at a target (0x57C110) and, when the run starts, its casting goal's start (0x429B50).</summary>
    private void CastSpell(int spell, string? target, SpellLocation tile, string? item = null, Func<SessionSpellCaster, SpellCastAttempt>? script = null)
    {
        if (SpellCasterNow() is not { } caster) return;
        // ARC-DEV-318: a restored cast not adopted yet is the playing one; this host has seen the session's casts from now on.
        AdoptRestoredCast();
        _castOwner = _areaSession;
        // ARC-DEV-244: a script's cast by the PC (script) starts the run through the script entry point (no cost test, the free and unresistable flags) and then plays the same casting goal.
        var attempt = script is null ? caster.CastAt(SessionSpellCaster.Hero, spell, target, tile, item) : script(caster);
        _spellCasts++;
        GD.Print($"ARC-MAGIC-015 cast spell={spell} target={target ?? $"tile {tile.X},{tile.Y}"}{(item is null ? "" : " item=" + item)} outcome={attempt.Outcome} message={attempt.Message}"
            + $" creation={attempt.Run?.Creation?.Status} check={attempt.Run?.Creation?.Check} failure={attempt.Run?.Failure}");
        if (attempt.Outcome != SpellCastAttemptOutcome.Started || attempt.RunId is not { } run)
        {
            // A script's refusal is silent (the invocation check shows no message, GT183).
            FlushSpellLog(script is not null ? null : attempt.Outcome switch
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
            // ARC-DEV-318: the queued goal goes beneath the playing one in the table (0x44D9B0); a stack the table refuses has no goal to play the run, which is freed unpaid as a dropped queue's.
            var table = queued == SpellCastQueueOutcome.Queued ? HeroRunsHost()?.QueueCast(run, target) ?? SessionGoalFailure.NoRun : SessionGoalFailure.None;
            GD.Print($"ARC-MAGIC-019 cast-queue run={run} spell={spell} depth={depth} outcome={queued} table={table}");
            if (table != SessionGoalFailure.None)
            {
                caster.DropQueued(run);
                _spellNotice = "the cast could not be queued: " + table;
            }
            else if (queued == SpellCastQueueOutcome.Queued)
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
        // ARC-COMBAT-020: in a turn-based round the casting goal's start charges its 4 action points (GR19: a loaded round's rule first). ARC-DEV-183: the start is node 1 of goal 26 on the table (HeroStartCast).
        ResumeLoadedRound();
        StopWorldRoute("ROUTE_REPLACED_BY_CAST");
        CancelAreaAttack("a cast replaced it");
        _spellGoal = new(caster, run, spell, target, tile);
        _spellNotice = $"casting {AreaSpellsPage.SpellName(_spellData?.Data, spell)}";
        StartHeroCastRun(_spellGoal);
        RefreshAreaStatus();
    }

    /// <summary>
    /// ARC-DEV-244 (MIG13 follow-up of S14f, GT183/GT184): a script's cast (48, 118, 121, 122) whose caster is the PC: the cast host's script entry point (<see cref="SessionSpellCaster.CastByScript"/> with the hero as
    /// source and parent) starts the run, and the casting goal plays as the hero's own cast does (queued under a playing cast, a round's action points). A hero who is down casts nothing (a project choice: the original has no
    /// test). The text says what happened.
    /// </summary>
    private string CastByScriptAsHero(string target, int spell, bool free, bool unresistable)
    {
        if (_areaSession is not { } session || SpellCasterNow() is null) return "no cast host";
        if (session.QueryHeroLife() is not { Status.IsActive: true }) return "the hero is down";
        var before = _spellCasts; var playing = _spellGoal; var waiting = _spellQueue.Count;
        CastSpell(spell, target, SpellLocation.None, null, caster => caster.CastByScript(SessionSpellCaster.Hero, spell, target, free, unresistable));
        if (_spellCasts == before) return "not cast";
        if (_spellGoal is not null && !ReferenceEquals(_spellGoal, playing)) return "cast, the casting goal plays";
        return _spellQueue.Count > waiting ? "cast, queued under the playing cast" : "not started: refused or freed";
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
        LogObjectHits();
        LogItemHits();
    }

    /// <summary>ARC-DEV-255 (GT195): the hits damage that is no hero attack (a spell, a script, a burn, a trap) dealt to a door, window, container,
    /// scenery or trap since the last call, logged; since ARC-DEV-260 the object's trap and its BUST script run for a hit that broke it (no attacker: no
    /// triggerer, no breaker); since ARC-AI-2112 a hit with an actor (a spell's caster) then calls the guard witnesses, and since ARC-AI-2124 that hit is taken the moment the damage step makes it (<see cref="AreaSpellCastMap.ObjectHitMade"/>), the rest at the cast log's flush. A trap's or a script's own damage queues hits in turn, which the next round takes (bounded). The redraw of what a hit destroyed
    /// or busted follows from the session's version (the per-frame inventory sync).</summary>
    private void LogObjectHits()
    {
        // A trap or script of a break is running: the hits its damage queues wait for the loop that runs it (RunObjectBreakSteps).
        if (_objectBreakRunning) return;
        var any = false;
        for (var round = 0; round < MaxObjectBreakRounds && _spellCastMap?.TakeObjectHits() is { Count: > 0 } hits; round++)
        {
            any = true;
            foreach (var hit in hits)
            {
                // ARC-AI-2123: a zero hit on a whole object has nothing to log, float or break; the guards still see it.
                if (hit.WitnessOnly)
                {
                    if (hit.Actor is { } witnessed && _spellCastMap?.Row(hit.Id) is { } wholeRow)
                        WitnessesOfObjectHit(hit.Id, wholeRow, hit.Host, witnessed == SessionSpellCaster.Hero ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : witnessed);
                    continue;
                }
                GD.Print($"ARC-DEV-255 object-damage target={hit.Id} damage={hit.Hit.Damage - hit.Hit.DamageBefore} total={hit.Hit.Damage} broken={hit.Hit.Broken} break={hit.Hit.Break} host={hit.Host}");
                // ARC-COMBAT-1138: the damage function floats the object's damage line whoever dealt it (CH12, CH13).
                FloatObjectDamage(hit.Id, _spellCastMap?.Row(hit.Id), AreaCombatFloaters.Of(hit));
                // ARC-TECHNOLOGY-1760 (OD15): the attacker of a trap's shot is the BUST breaker.
                var breaker = hit.Source is { } source && _spellCastMap?.Row(source) is { } shooterRow ? AreaUseScriptHost.EntityId(shooterRow) : null;
                if (_spellCastMap?.Row(hit.Id) is { } row)
                {
                    RunObjectBreakSteps(row, hit.Host, false, false, breaker);
                    // ARC-AI-2112 (GW10, 0x4B4390 → 0x4AEE50): after the break, the guards see or hear the caster's spell damage on a portal, container or scenery (the host's gate admits the hero and a player character's follower).
                    if (hit.Actor is { } caster) WitnessesOfObjectHit(hit.Id, row, hit.Host, caster == SessionSpellCaster.Hero ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : caster);
                }
            }
        }
        if (!any) return;
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    // ARC-DEV-260: a window breaks again on every hit (ODA3), so a trap or BUST script that damages its own window could chain without end; the hits a
    // run queues are taken in at most this many rounds per call (the rest wait for the next call), and a nested call stands off (_objectBreakRunning).
    private const int MaxObjectBreakRounds = 8;

    /// <summary>ARC-COMBAT-025 (OD1–OD4, OD12): the hits that same damage dealt to items (lying on the ground or worn by a critter since an acid), logged with
    /// what the host owes: the trap and the BUST script of an item lying on the map run as for any broken object (ARC-DEV-260, <see cref="RunObjectBreakSteps"/>); a worn item has
    /// no map row, so its BUST script is not run.</summary>
    private void LogItemHits()
    {
        // A trap or script of a break is running: the hits its damage queues wait for the loop that runs it.
        if (_objectBreakRunning) return;
        var any = false;
        for (var round = 0; round < MaxObjectBreakRounds && _spellCastMap?.TakeItemHits() is { Count: > 0 } hits; round++)
        {
            any = true;
            foreach (var hit in hits)
            {
                GD.Print($"ARC-COMBAT-025 item-damage target={hit.Id} condition={hit.Item.Condition} break={hit.Item.Break} host={hit.Host}");
                // ARC-COMBAT-1138: an item lying on the map floats its damage line over its cell; a worn item has no row and no cell to float over.
                if (_spellCastMap?.Row(hit.Id) is { } row)
                {
                    FloatObjectDamage(hit.Id, row, AreaCombatFloaters.Of(hit));
                    RunObjectBreakSteps(row, hit.Host, false, false);
                }
                // ARC-SCRIPT-2722: an item with no map row has no break steps to run; show the destroy point's run its gate left (the host runs the point only for an item of the shown map's rows, so this is rare and drains a stale run).
                else if (_readyArea is { } itemArea && UseScriptHost(itemArea.Snapshot) is { } itemHost) ShowDestroyPointRuns(itemHost, hit.Id);
            }
        }
        if (!any) return;
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    /// <summary>Log entries as the status notice and a message window entry (ARC-MAGIC-018: also a replaced host's own entries, for a
    /// missile that left with it).</summary>
    private void PostSpellLog(string? lead, IEnumerable<SpellCastEvent> entries)
    {
        var lines = new List<string>();
        if (lead is not null) lines.Add(lead);
        var texts = _spellData?.Data?.Texts;
        // A snapshot: a tile script run by a relocation event may cast and flush the log in turn (ARC-MAGIC-038), and the nested flush owns the entries added meanwhile.
        var taken = entries.ToArray();
        foreach (var entry in taken)
        {
            GD.Print($"ARC-MAGIC-015 log kind={entry.Kind} run={entry.Run} value={entry.Value} object={entry.Object} detail={entry.Detail}");
            TakeEyeCandyEvent(entry);
            TakeSummonEvent(entry);
            TakeKnockbackEvent(entry);
            TakeMovementEvent(entry);
            TakeTeleportEvent(entry);
            TakeHoldEvent(entry);
            // ARC-AI-2084 (0x454A10 → 0x4AEE50, mode 0, loud): the guards near a portal or container whose lock a spell cleared; the alert is logged for a critter caster, which the host's rule limits to the hero and its followers.
            if (entry is { Kind: SpellCastEventKind.LockGuardAlert, Object: { } guarded, Actor: { } caster } && ObjectRowByEntity(guarded) is { } guardedRow)
                WitnessesOfLockSpell(guarded, guardedRow, caster == SessionSpellCaster.Hero ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : caster);
            TakeTraitEvent(entry);
            TakeIdentifyEvent(entry);
            // ARC-MAGIC-1745: a line a wield script of an NPC's rewield printed (the TraitIdx magick and tech points) goes to the message window as any script's printed line.
            if (entry is { Kind: SpellCastEventKind.ItemScriptLine, Text: { } text }) PostScriptLines([new(entry.Detail, 0, entry.Value, text)]);
            // ARC-MAGIC-1830: an eye candy request of the same scripts plays on the object it names, as any script's.
            if (entry is { Kind: SpellCastEventKind.ItemScriptEyeCandy, EyeCandy: { } candy }) QueueScriptEyeCandies([candy]);
            if (AreaSpellCasting.Line(entry, texts, SpellObjectName) is { } line) lines.Add(line);
        }
        // ARC-MAGIC-045: a run wrote or took off an object flag (invisible, translucent, shrunk, don't-draw): the bodies are styled again without waiting for the next selection.
        if (taken.Any(e => e.Kind is SpellCastEventKind.ObjectFlagWritten or SpellCastEventKind.ObjectFlagKept)) SyncAreaBodyStyles();
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
