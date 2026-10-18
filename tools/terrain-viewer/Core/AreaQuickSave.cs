using Arcanum.Input;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The answer to one quick save. <see cref="Status"/> is the HUD line.</summary>
public sealed record AreaQuickSaveResult(bool Saved, SaveSlotWriteResult? Write, GameSessionFailure PlaceFailure, string Status);

/// <summary>The answer to one quick load. On success <see cref="Owner"/> is a new session the caller adopts;
/// on any failure there is no owner and the caller's session was not touched.</summary>
public sealed record AreaQuickLoadResult(GameSessionOwner? Owner, SaveSlotReadResult? Read, GameSessionFailure RestoreFailure, string Status);

/// <summary>
/// ARC-WORLD-011 quick save and quick load (the original F7 and F8, one slot, no save list). A save records the
/// actor's committed cell and facing in the session (<see cref="GameSessionOwner.PlaceActor"/>), captures the whole
/// session and writes it atomically to the slot; the in-flight step, a click route and other host state are not
/// saved. The placement stays recorded when the write then fails: it only mirrors where the actor already stands,
/// and the previous save file is kept. A load reads and decodes the slot, lets the host refuse a placement it cannot stand the actor on, then
/// restores a new owner. The load continues the saved playthrough: it takes the save's world id, while the module
/// id and the exact clock profile must equal the running session's, so a save from another module or other source
/// data is refused before anything changes. The slot and the codec carry every snapshot section, so a domain added
/// to the session snapshot is saved and loaded here unchanged. Caller-serialized; no Godot.
/// </summary>
public static class AreaQuickSave
{
    public static AreaQuickSaveResult Save(GameSessionOwner session, ActorPlacement actor, GameSessionSaveSlot slot)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(slot);
        var placed = session.PlaceActor(session.Version, actor);
        if (placed.Disposition == GameSessionDisposition.Rejected)
            return new(false, null, placed.Failure, $"quick save refused: actor placement {placed.Failure}");
        var write = slot.Write(session.Capture());
        return write.Saved
            ? new(true, write, GameSessionFailure.None,
                $"quick saved: map {actor.Map.RuntimeMap} cell ({actor.Cell.X},{actor.Cell.Y}), session rev {session.Version.Revision}")
            : new(false, write, GameSessionFailure.None, $"quick save FAILED ({write.Failure} {write.Detail}); the previous save is kept");
    }

    /// <param name="placementRefusal">The host's reason it cannot stand the actor at the saved placement
    /// (map not loaded, cell not admitted), or null when it can.</param>
    /// <param name="mapSound">The default music and ambient pair of the saved actor's map (ARC-WORLD-040: the original opens
    /// the map before it loads the sound state), or null when the host has no sound catalogue.</param>
    public static AreaQuickLoadResult Load(GameSessionSaveSlot slot, GameSessionOwner current, Func<ActorPlacement, string?> placementRefusal,
        Func<ActorPlacement, SessionMapSound?>? mapSound = null)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(placementRefusal);
        var read = slot.Read();
        if (read.Snapshot is not { } snapshot) return new(null, read, GameSessionFailure.None, "quick load FAILED: " + Describe(read));
        if (placementRefusal(snapshot.Actor) is { } refusal)
            return new(null, read, GameSessionFailure.None,
                $"quick load REFUSED: saved actor on map {snapshot.Actor.Map.RuntimeMap} cell ({snapshot.Actor.Cell.X},{snapshot.Actor.Cell.Y}): {refusal}");
        var options = new GameSessionRestoreOptions(new(snapshot.Identity.World, current.Flags.Binding.Identity.Module),
            current.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), current.Limits, current.Rules.Mode,
            current.Experience, current.Difficulty, current.AutoLevel, mapSound?.Invoke(snapshot.Actor));
        var restored = GameSessionOwner.Restore(snapshot, options);
        if (restored.Owner is null)
            return new(null, read, restored.Failure, "quick load REFUSED: " + Describe(restored.Failure));
        var actor = restored.Owner.QueryActor();
        return new(restored.Owner, read, GameSessionFailure.None,
            $"quick loaded: map {actor.Map.RuntimeMap} cell ({actor.Cell.X},{actor.Cell.Y}), session rev {restored.Owner.Version.Revision}");
    }

    public static string Describe(SaveSlotReadResult read) => read.Failure switch
    {
        SaveSlotFailure.None => "read",
        SaveSlotFailure.NoSave => "no quick save yet (F7 saves)",
        SaveSlotFailure.TooLarge => "the save file is too large (" + read.Detail + ")",
        SaveSlotFailure.ReadFailed => "the save file could not be read (" + read.Detail + ")",
        SaveSlotFailure.NotUtf8 => "the save file is damaged (not UTF-8)",
        SaveSlotFailure.Corrupt when read.DecodeFailure == SnapshotDecodeFailure.UnsupportedSchema =>
            "the save was written by an incompatible version (" + read.Detail + ")",
        SaveSlotFailure.Corrupt when read.DecodeFailure == SnapshotDecodeFailure.WrongFormat => "the file is not a session save (" + read.Detail + ")",
        SaveSlotFailure.Corrupt => "the save file is damaged (" + read.Detail + ")",
        _ => read.Failure + " " + read.Detail,
    };

    public static string Describe(GameSessionFailure failure) => failure switch
    {
        GameSessionFailure.WrongModule => "the save belongs to another module (WrongModule)",
        GameSessionFailure.IncompatibleClockProfile => "the save was made with other game data (IncompatibleClockProfile)",
        GameSessionFailure.UnsupportedSchema => "the save was written by an incompatible version (UnsupportedSchema)",
        GameSessionFailure.BudgetExceeded => "the save exceeds this session's limits (BudgetExceeded)",
        _ => "the save state was not accepted (" + failure + ")",
    };
}

public enum QuickKeyAction
{
    /// <summary>Not F7/F8: the key keeps its other routing.</summary>
    NotQuickKey,
    /// <summary>F7/F8 with nothing to do now (echo, a press that armed, a release that was not armed).</summary>
    None,
    /// <summary>F7/F8 under the palette, a dialog or UI focus: the HUD says it was ignored.</summary>
    Ignored,
    Save,
    Load,
}

/// <summary>
/// ARC-WORLD-011 F7/F8 key gate. The original acts on key release; here a key acts on its release only when its
/// fresh press and its release both came while world input was allowed (focused window, no palette, dialog or UI
/// focus). Echo repeats do nothing. A press of the other quick key re-arms the gate for that key. Modifiers are
/// not checked (the source's handling of modified F7/F8 is not verified). Caller-serialized; no Godot.
/// </summary>
public sealed class QuickKeyGate
{
    public WorldKey? Armed { get; private set; }

    public QuickKeyAction Route(WorldKey key, bool pressed, bool echo, bool allowed)
    {
        if (key is not (WorldKey.F7 or WorldKey.F8)) return QuickKeyAction.NotQuickKey;
        if (echo) return QuickKeyAction.None;
        if (pressed)
        {
            Armed = allowed ? key : null;
            return allowed ? QuickKeyAction.None : QuickKeyAction.Ignored;
        }
        var armed = Armed == key;
        Armed = null;
        if (!armed) return QuickKeyAction.None;
        if (!allowed) return QuickKeyAction.Ignored;
        return key == WorldKey.F7 ? QuickKeyAction.Save : QuickKeyAction.Load;
    }

    /// <summary>Forget an armed key at a lifecycle boundary.</summary>
    public void Reset() => Armed = null;
}
