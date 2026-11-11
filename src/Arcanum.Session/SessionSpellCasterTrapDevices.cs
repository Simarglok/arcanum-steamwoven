using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-124: what the cast host made of one trap fuse that came due: the firing and the run its spell started (or why none).</summary>
public sealed record SessionTrapDeviceCast(SessionTrapDeviceFiring Firing, SpellCastAttemptOutcome Outcome, int? Run);

public sealed partial class SessionSpellCaster
{
    private readonly List<SessionTrapDeviceCast> _trapDeviceCasts = [];

    /// <summary>ARC-WORLD-124: the casts of the trap fuses the last passes ran, oldest first (a host or test reads and clears them with <see cref="TakeTrapDeviceCasts"/>).</summary>
    public IReadOnlyList<SessionTrapDeviceCast> TakeTrapDeviceCasts()
    {
        var taken = _trapDeviceCasts.ToArray();
        _trapDeviceCasts.Clear();
        return taken;
    }

    /// <summary>
    /// ARC-WORLD-124 (0x4BC690 with delay 0, ledger TP3, TP7): the fuse's spell at the tile with no parent and no source (0x455A20, then 0x455AC0): the invocation check and the run; the run's BEGIN
    /// comes as an event (a run without a parent has no casting goal), so the tile is the run's target tile and nothing else.
    /// </summary>
    public (SpellCastAttemptOutcome Outcome, int? Run) CastAtTileWithoutCaster(int spell, SpellLocation tile)
    {
        if (_records(spell) is not { } record) return (SpellCastAttemptOutcome.Nothing, null);
        var invocation = new SpellInvocation(spell, null, null, null, tile);
        var started = _owner.StartSpellRun(_owner.Version, new(invocation), record.Run, InvocationWorld(), _owner.SpellRolls);
        if (started.Disposition != GameSessionDisposition.Changed || started.Creation?.Run is not { } run) return (SpellCastAttemptOutcome.Refused, null);
        _itemChains.Remove(run.Id);
        return (SpellCastAttemptOutcome.Started, run.Id);
    }

    // The pass's trap fuses that ran (not the postponed ones): each one's spell is cast at its tile after the pass, in firing order.
    private void RunTrapDevices(SessionEventPassResult pass)
    {
        foreach (var firing in pass.TrapDevices.Where(f => !f.Postponed))
        {
            var (outcome, run) = CastAtTileWithoutCaster(firing.Spell, new(checked((uint)firing.Cell.X), checked((uint)firing.Cell.Y)));
            _trapDeviceCasts.Add(new(firing, outcome, run));
        }
    }

    /// <summary>
    /// ARC-WORLD-124 (E6-b, ledger IA13, TP2): the hero's use of the trap device <paramref name="item"/> whose slot 1 holds a spell, on the tile <paramref name="tile"/> (the tile form, 0x4BC480) or on
    /// an object standing there (the object form, 0x4BC2E0): <see cref="GameSessionOwner.PlaceTrapDevice"/> with the plan of <see cref="TrapDeviceRules.Plan"/>. A device whose slot 1 is empty is the script
    /// trap (ARC-WORLD-125) and changes nothing here (<see cref="SessionTrapDeviceOutcome.Script"/>).
    /// </summary>
    public SessionTrapDeviceUse UseTrapDevice(string item, SpellLocation tile, bool onTile)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Item(item) is not { Holder: Hero } spells || spells.Spells.Count <= TrapDeviceRules.SpellSlot) return new(SessionTrapDeviceOutcome.Unknown, default, null);
        var plan = TrapDeviceRules.Plan(spells.Spells[TrapDeviceRules.SpellSlot], onTile);
        if (plan.Kind == TrapDeviceKind.Script) return new(SessionTrapDeviceOutcome.Script, plan, null);
        if (plan.Kind == TrapDeviceKind.Invalid) return new(SessionTrapDeviceOutcome.Invalid, plan, null);
        var cell = new Arcanum.Simulation.Movement.CellCoord(checked((int)tile.X), checked((int)tile.Y));
        var placed = _owner.PlaceTrapDevice(_owner.Version, item, _owner.QueryActor().Map, cell, plan.Spell, plan.MarkerPrototype);
        if (placed.Disposition != GameSessionDisposition.Changed) return new(SessionTrapDeviceOutcome.Refused, plan, null, placed.Failure);
        Act(new(SessionSpellRunActionKind.ClearSources, Object: item));
        return new(SessionTrapDeviceOutcome.Placed, plan, placed.Marker);
    }

    /// <summary>
    /// ARC-WORLD-125 (E6-c, ledger IA13, TP2): the hero's use of the script trap device <paramref name="item"/> (slot 1 empty) on <paramref name="target"/> (the object form, 0x4BC2E0: a portal or a container whose
    /// use slot is empty takes it; a wall takes the tile form) or on the tile <paramref name="tile"/> (0x4BC480; <paramref name="trapObjectOnTile"/>: the map data holds a trap object there, which with a script trap
    /// marker of the session refuses it). <paramref name="script"/> is the device's own use slot and <paramref name="kind"/> its trap class from the script file's header (<see cref="TrapRules.Classify"/>): they
    /// choose the marker. The refusals keep the device and answer the line of <c>mes\trap.mes</c> the player gets (2 or 3); an armed trap is <see cref="GameSessionOwner.PlaceScriptTrapDevice"/>.
    /// </summary>
    public SessionScriptTrapUse UseScriptTrapDevice(string item, SessionTrapScript script, TrapClass kind, SpellLocation tile, SessionScriptTrapTarget? target, bool trapObjectOnTile)
    {
        ArgumentNullException.ThrowIfNull(item);
        var onTile = target is null or { Type: TrapDeviceRules.ObjectTypeWall };
        if (Item(item) is not { Holder: Hero } spells || spells.Spells.Count <= TrapDeviceRules.SpellSlot) return new(SessionScriptTrapOutcome.Unknown, default);
        if (TrapDeviceRules.Plan(spells.Spells[TrapDeviceRules.SpellSlot], onTile).Kind != TrapDeviceKind.Script) return new(SessionScriptTrapOutcome.Invalid, default);
        var map = _owner.QueryActor().Map;
        var cell = new Arcanum.Simulation.Movement.CellCoord(checked((int)tile.X), checked((int)tile.Y));
        var plan = onTile
            ? TrapDeviceRules.ScriptOnTile(trapObjectOnTile || _owner.QueryScriptTrapMarkers(map, cell).Count > 0, script.Script, kind)
            : TrapDeviceRules.ScriptOnObject(target!.Type, target.UseScript);
        if (!plan.Armed) return new(plan.RefusalLine == TrapDeviceRules.TargetRefusedLine ? SessionScriptTrapOutcome.TargetRefused : SessionScriptTrapOutcome.TileRefused, plan);
        var placed = _owner.PlaceScriptTrapDevice(_owner.Version, item, map, cell, onTile ? null : target!.Entity, script, plan.MarkerPrototype);
        if (placed.Disposition != GameSessionDisposition.Changed) return new(SessionScriptTrapOutcome.Refused, plan, null, null, placed.Failure);
        Act(new(SessionSpellRunActionKind.ClearSources, Object: item));
        return new(SessionScriptTrapOutcome.Armed, plan, placed.Marker, placed.Target);
    }
}

/// <summary>The object a script trap device is used on, as the host reads it: its entity, object type (field 0x14F: 0 wall, 1 portal, 2 container) and the use script now in its slot 1 (0 for none).</summary>
public sealed record SessionScriptTrapTarget(string Entity, int Type, int UseScript);

/// <summary>What <see cref="SessionSpellCaster.UseScriptTrapDevice"/> did.</summary>
public enum SessionScriptTrapOutcome
{
    /// <summary>The item is not a carried device of the hero's.</summary>
    Unknown,
    /// <summary>Slot 1 holds a spell (a timed device, <see cref="SessionSpellCaster.UseTrapDevice"/>) or a number no record has: nothing happens.</summary>
    Invalid,
    /// <summary>The target takes no script trap (not a portal or container, or it holds a script): the device stays, <c>mes\trap.mes</c> line 2.</summary>
    TargetRefused,
    /// <summary>The tile holds a trap object already: the device stays, <c>mes\trap.mes</c> line 3.</summary>
    TileRefused,
    /// <summary>The script is written on the target or on a marker made at the tile and the device is destroyed.</summary>
    Armed,
    /// <summary>The session refused (<see cref="SessionScriptTrapUse.Failure"/>): the device stays.</summary>
    Refused,
}

/// <summary>The outcome of one script trap device use: what happened, the plan, the marker made on a tile, the armed target object and the session's refusal.</summary>
public sealed record SessionScriptTrapUse(SessionScriptTrapOutcome Outcome, ScriptTrapPlan Plan, SessionCreatedObject? Marker = null, string? Target = null, GameSessionFailure Failure = GameSessionFailure.None);


/// <summary>What <see cref="SessionSpellCaster.UseTrapDevice"/> did.</summary>
public enum SessionTrapDeviceOutcome
{
    /// <summary>The item is not a carried item of the hero's with a slot 1.</summary>
    Unknown,
    /// <summary>The device is destroyed, its marker made on a tile and its fuse set.</summary>
    Placed,
    /// <summary>Slot 1 is empty: a script trap (not placed by this slice, the device stays).</summary>
    Script,
    /// <summary>Slot 1 holds a number no spell record has: nothing happens.</summary>
    Invalid,
    /// <summary>The session refused (<see cref="SessionTrapDeviceUse.Failure"/>): the device stays.</summary>
    Refused,
}

/// <summary>The outcome of one trap device use: what happened, the plan read from the device, the marker made on a tile and the session's refusal.</summary>
public sealed record SessionTrapDeviceUse(SessionTrapDeviceOutcome Outcome, TrapDevicePlan Plan, SessionCreatedObject? Marker, GameSessionFailure Failure = GameSessionFailure.None);
