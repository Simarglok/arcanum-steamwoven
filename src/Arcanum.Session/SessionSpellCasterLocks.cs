using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-037 (magic audit MA-6, ledger <see href="../../docs/research/ledger/spell-runs.md">spell-runs</see> LM1–LM5; 0x454A10, 0x4551C0, 0x45A520, 0x4AEE50, 0x451C40 re-read for this slice): the ObjFlag
/// component on the lock flags of a portal (field 46, 0x2E) and a container (field 56, 0x38), for Unlocking Cantrip (1: locked off on both fields), Magelock (75: magically held on at BEGIN, off at END) and
/// the Dispel component's clear of the held bit. The lock state is the session's (<see cref="GameSessionOwner.QueryOpenable"/>); the writes are plain field writes
/// (<see cref="GameSessionOwner.WriteOpenableFlag"/>), so unlike a key or pick locks they schedule **no Lock event**: a door a spell unlocked stays unlocked.
/// <list type="bullet">
/// <item>**The clear of the locked bit** (<c>param_6 &amp; 1</c> on field 0x2E or 0x38, the only branch with side effects) first asks the unlock gate (0x4551C0): it passes without a parent, for a parent that is no
/// PC or NPC, and while the PC's armed fate "spell at maximum" is used for the pass; otherwise a lock level above 19 needs the parent's captured aptitude (run +0x8C) to reach it. A refused gate leaves the lock,
/// plays the "resisted" feedback (0x45A520: eye candy 643); the guard alert (0x4AEE50, loudness 0, kind 2; logged for a PC or NPC parent) follows either way, and the screen refresh.</item>
/// <item>**Every other bit** (the held bit set or cleared, the locked bit set) is only the write. There is no reference count for these fields (0x455100 counts only the spell flags): the first of two
/// Magelocks to end clears the bit.</item>
/// </list>
/// A field the object's type lacks (field 46 on a container, 56 on a portal) is left not ready: the original writes it all the same (the area masks of the records keep the two apart).
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object field numbers of the portal flags (46, 0x2E) and the container flags (56, 0x38): the lock flags (<see cref="LockRules"/> bits).</summary>
    public const int PortalFlagsField = 46, ContainerFlagsField = 56;

    /// <summary>The unlock gate lets a lock of level 19 or less through whatever the caster's aptitude (0x4551C0: <c>0x13 &lt; level</c> first).</summary>
    public const int UnlockGateFreeLevel = 19;

    // The ObjFlag component on a portal's or container's lock flags (the lock-clearing branch of 0x454A10, which WriteObjectFlag is marked for); false when it is no such component or target
    // (the caller goes on to the critter flags, or logs it not ready).
    private bool LockFlagComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.ObjectFlag is not { Field: PortalFlagsField or ContainerFlagsField } data || target.Object is not { } obj) return false;
        if (_dataMap.Openable(obj.Id) is not { } openable) return false;
        var container = openable.Baseline.Kind == OpenableKind.Container;
        if (container != (data.Field == ContainerFlagsField)) return false;
        var mask = unchecked((int)data.Flag);
        if (mask == 0) return false;
        var refused = false;
        if (!data.On && (mask & LockRules.LockedBit) != 0)
        {
            refused = !UnlockGate(run, openable);
            // The guard alert comes in both cases, after the resisted feedback of a refused gate; only a passed gate goes on to the write.
            if (refused)
            {
                _log.Add(new(SpellCastEventKind.UnlockRefused, run.Id, openable.LockLevel, obj.Id));
                _log.Add(new(SpellCastEventKind.Resisted, run.Id, 0, obj.Id));
                if (IsLocalPlayer(run.Parent)) _log.Add(new(SpellCastEventKind.Message, run.Id, ResistedMessage));
            }
            if (run.Parent is { IsCritter: true } caster) _log.Add(new(SpellCastEventKind.LockGuardAlert, run.Id, 2, obj.Id, refused ? 1 : 0) { Actor = caster.Id });
            if (refused) return true;
        }
        WriteLockFlag(run, obj.Id, openable, mask, data.On);
        return true;
    }

    // The unlock gate 0x4551C0 for the run's parent (source +0x58) and the object's lock level (field 47 of a portal, 57 of a container).
    private bool UnlockGate(SpellRun run, SpellOpenable openable)
    {
        if (run.Parent is not { IsCritter: true } parent || _spellAtMaximum) return true;
        return openable.LockLevel <= UnlockGateFreeLevel || parent.Aptitude >= openable.LockLevel;
    }

    // One plain write of a lock flag bit through the session, with its log line under the id the cast knows the object by; a write that changes nothing (already so, a junk pile) logs nothing.
    private void WriteLockFlag(SpellRun run, string id, SpellOpenable openable, int mask, bool on)
    {
        var written = _owner.WriteOpenableFlag(_owner.Version, openable.Openable, openable.Baseline, mask, on);
        var detail = (on ? 1 : 0) | (openable.Baseline.Kind == OpenableKind.Container ? 2 : 0);
        if (written.Disposition == GameSessionDisposition.Rejected) _log.Add(new(SpellCastEventKind.OpenableFlagNotSaved, run.Id, (int)written.Failure, id, detail));
        else if (written.Disposition == GameSessionDisposition.Changed) _log.Add(new(SpellCastEventKind.OpenableFlagWritten, run.Id, mask, id, detail));
    }

    // The Dispel component's half for a portal or container (0x451C40 → 0x406D40): its magically held bit is cleared by a plain write, without the gate or the count.
    private bool DispelOpenable(SpellRun run, string id)
    {
        if (_dataMap.Openable(id) is not { } openable) return false;
        if ((_owner.QueryOpenable(openable.Openable, openable.Baseline).Flags & LockRules.MagicallyHeldBit) != 0) WriteLockFlag(run, id, openable, LockRules.MagicallyHeldBit, false);
        return true;
    }
}
