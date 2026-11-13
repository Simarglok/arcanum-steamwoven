using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-101: the loot window on a container (the inventory open 0x572240 in loot mode 2, contract docs/world/barter-runtime.md). A
// left press on a standing container walks within reach 1 and charges the use's 2 action points in a turn-based round, as a body's loot
// does; then the window's admission runs (the player's open attempt; a locked, jammed or magically held container plays its locked sound
// and stays shut), a closed container opens with its open sound, a container with flag 0x200 is generated once (GenerateContainerOnce),
// and the window opens, which schedules the container's restock. Closing the window closes a container on frame 1 with its close sound.
public partial class TerrainViewerApp
{
    // The session whose container the loot window shows (its close runs against that session only).
    private GameSessionOwner? _containerLootSession;
    // The runtime map of that container (its close finds it there after a map change).
    private int _containerLootMap;

    /// <summary>The last container open's result, for the smoke.</summary>
    private AreaContainerOpen? _lastContainerOpen;

    // ARC-RES-1327 (ledger XR3, XR16): the container whose See Contents window (inventory window mode 4) is open or about to open; null when the open window is no such one.
    private string? _contentsHolder;
    /// <summary>The open inventory window is the See Contents window of a container: it moves nothing (no take, put, drop, use or wear; the arrange button still lays the hero's grid out).</summary>
    private bool ContentsWindow => _contentsHolder is not null && InventoryOpen;
    /// <summary>The container of the open See Contents window (null none), for the smoke.</summary>
    internal string? ContentsHolderForSelfTest => ContentsWindow ? _contentsHolder : null;

    /// <summary>
    /// ARC-RES-1327 (E8-g, ledger XR3, LW1–LW3, XR16): the Identify component on a container (<see cref="SpellCastEventKind.IdentifyContainer"/>) opens the inventory window on it in mode 4: no open attempt
    /// (a locked container shows its items), no use script, no open frame or sound, a container with flag 0x200 generated first, the window is the loot window's frame (interface mode 10) with nothing to
    /// move, and its close runs the junk-pile check without closing the lid (<see cref="CloseContainer"/>).
    /// </summary>
    private void OpenContainerContents(AreaInventoryTarget target)
    {
        _lastContainerOpen = null;
        if (!InventoryActive || InventoryOpen || !WindowCanOpen(InterfaceMode.Loot) || _inventoryHost!.Map(CurrentAreaMap) is not { } map)
        {
            GD.Print($"ARC-RES-1327 contents entity={target.Entity} not opened (no inventory or another window up)");
            return;
        }
        var open = AreaContainerLoot.Contents(_areaSession!, map, target.Entity, AreaEvents);
        _lastContainerOpen = open;
        GD.Print($"ARC-RES-1327 contents entity={target.Entity} outcome={open.Outcome} {open.Detail}");
        if (open.Outcome != AreaContainerOpenOutcome.Opened)
        {
            _inventoryStatus = $"{_inventoryHost.Name(target.Entity, _hoverNames?.Catalog)}: {Refusal(open.Outcome)}";
            RefreshAreaStatus();
            return;
        }
        if (open.Generation?.Disposition == GameSessionDisposition.Changed) SyncAreaInventory();
        _containerLootSession = _areaSession;
        _containerLootMap = map.RuntimeMap;
        _contentsHolder = target.Entity;
        OpenInventory(target.Entity);
    }

    private void OpenContainer(AreaInventoryTarget target)
    {
        _lastContainerOpen = null;
        // ARC-DEV-109: another window on the stack refuses the open before the session part below runs (the container would stay open there).
        if (!InventoryActive || InventoryOpen || !WindowCanOpen(InterfaceMode.Loot) || _inventoryHost!.Map(CurrentAreaMap) is not { } map) return;
        var frames = _readyArea?.Snapshot.Visuals.FirstOrDefault(v => v.Key.Layer == AreaLayerKind.Body && v.Key.Object == target.Row.Key)?.FrameCount ?? 0;
        var open = AreaContainerLoot.Open(_areaSession!, map, target.Entity, frames, AreaEvents, ContainerUseScript);
        _lastContainerOpen = open;
        var done = open.Generation?.Regeneration;
        if (open.UseScript is { } ran)
            GD.Print($"ARC-WORLD-101 container use script outcome={ran.Outcome} run={ran.Run?.Outcome.ToString() ?? "-"} lines={ran.Lines.Count} detail={ran.Detail}");
        if (open.UseScript?.Trap is { } met) NoteTrapMeeting(met);
        GD.Print($"ARC-WORLD-101 container open entity={target.Entity} outcome={open.Outcome} {open.Detail} " +
            $"removed={done?.Removed ?? 0} coins={done?.Coins ?? 0} created={done?.Created.Count ?? 0} refused={done?.Refused ?? 0} skipped={done?.Skipped ?? 0}");
        if (open.LockedSound) PlayContainerSound(target.Row, OpenableSoundAction.Locked);
        if (open.Outcome != AreaContainerOpenOutcome.Opened)
        {
            _inventoryStatus = $"{_inventoryHost.Name(target.Entity, _hoverNames?.Catalog)}: {Refusal(open.Outcome)}" +
                (open.UseScript is { Lines.Count: > 0 } said ? " — \"" + string.Join(" / ", said.Lines) + "\"" : "") +
                (open.UseScript?.Trap is { } trap ? $" ({trap.Describe()})" : "");
            RefreshAreaStatus();
            return;
        }
        if (open.Opening) PlayContainerSound(target.Row, OpenableSoundAction.Open);
        if (open.Generation?.Disposition == GameSessionDisposition.Changed) SyncAreaInventory();
        _containerLootSession = _areaSession;
        _containerLootMap = map.RuntimeMap;
        OpenInventory(target.Entity);
    }

    /// <summary>The container's use script (point 1) through the shown map's use-script host, the PC as the triggerer; a teleport it asks
    /// for is refused (the run is then not applied).</summary>
    private AreaScriptedUse ContainerUseScript(AreaObjectRecord row) =>
        _readyArea is { } area && UseScriptHost(area.Snapshot) is { } host
            ? Awarded(host, host.Run(row, _areaSession!.Rules.Scripts, _ => "a container's use script does not teleport here"))
            : new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no use-script host");

    /// <summary>The close of a container's window (0x573440): a container on frame 1 closes with its close sound.</summary>
    private void CloseContainer(string? holder)
    {
        var session = _containerLootSession;
        _containerLootSession = null;
        // ARC-RES-1327 (LW3): the See Contents window (mode 4) closes no lid: only the loot counter and the junk-pile check follow.
        var contents = holder is not null && holder == _contentsHolder;
        _contentsHolder = null;
        if (holder is null || session is null || !ReferenceEquals(session, _areaSession) || _inventoryHost?.Map(_containerLootMap) is not { } map) return;
        var closed = !contents && AreaContainerLoot.Close(session, map, holder);
        GD.Print($"ARC-WORLD-101 container close entity={holder} closed={closed}{(contents ? " contents" : "")}");
        if (closed && map.TryGetRow(holder, out var row)) PlayContainerSound(row, OpenableSoundAction.Close);
        // ARC-DEV-310 (B04, JP4, JP5): after the lid, the close runs the dissolve check; a junk pile of the map's data (the Crash Site cave's)
        // left with no item goes, one left with one item goes and drops it again, as a pile the session made does (EndInventory).
        if (AreaContainerLoot.DissolveJunkPile(session, map, holder) is { } dissolved)
        {
            GD.Print($"ARC-DEV-310 map-pile close entity={holder} disposition={dissolved.Disposition} failure={dissolved.Failure} piles={session.QueryJunkPiles().Count}");
            if (dissolved.Disposition == GameSessionDisposition.Changed) SyncAreaInventory();
        }
    }

    /// <summary>Whether the loot window shows a container of the map (an item's decay then waits, ledger ID6).</summary>
    private bool ContainerLootOpen => InventoryOpen && _containerLootSession is not null && ReferenceEquals(_containerLootSession, _areaSession);

    /// <summary>The loot window shows a container: a container of the map (also the See Contents window) or a junk pile the session made. The original's pickup switch (0x5E8800) is lowered while it
    /// does (0x5727B0, 0x573440): an item's decay waits (ID6) and no NPC starts a pickup (ARC-AI-1486).</summary>
    private bool LootWindowOnContainer => InventoryOpen && _inventoryPanel is { } panel && SessionJunkPile.Number(panel.Holder) is not null || ContainerLootOpen;

    private void PlayContainerSound(AreaObjectRecord row, OpenableSoundAction action)
    {
        if (row is not { WorldX: { } x, WorldY: { } y } || ObjectSoundReader.Read(row).Fields is not { } fields) return;
        if (SoundCueRules.Container(fields.Type, fields.SoundEffect, action) is { } cue) PlayCueAt(cue, x, y, PositionalSize.Large, "container");
    }

    private static string Refusal(AreaContainerOpenOutcome outcome) => outcome switch
    {
        AreaContainerOpenOutcome.Locked => "locked",
        AreaContainerOpenOutcome.Jammed => "jammed",
        AreaContainerOpenOutcome.MagicallyHeld => "magically held",
        AreaContainerOpenOutcome.UnlockScriptUnsupported => "not opened: its unlock script is not run here",
        AreaContainerOpenOutcome.UseScriptStopped => "its use script kept it shut",
        AreaContainerOpenOutcome.TrapScriptUnsupported => "not opened: it is trapped (no cast host to set its trap off)",
        AreaContainerOpenOutcome.TrapSpotted => "not opened: you spotted a trap on it",
        AreaContainerOpenOutcome.TrapFired => "a trap went off",
        AreaContainerOpenOutcome.TrapRefused => "not opened: the session refused its trap",
        AreaContainerOpenOutcome.UseScriptUnsupported => "not opened: its use script cannot run here",
        AreaContainerOpenOutcome.GenerationRefused => "not opened: its goods cannot be generated yet",
        _ => "not opened",
    };
}
