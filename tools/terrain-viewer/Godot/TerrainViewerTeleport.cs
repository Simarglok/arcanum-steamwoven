using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-038 (MA-5 second half; ledger TP1–TP5): the viewer's part of the Movement component's teleport types.
// Type 3 (the area portal, record 218, helper 0x452650): the cast host logs TeleportPortal; the hero is sent to the start map at the area's location by the session's change of place (the teleport
// spell's request, flags 0x120), as the debug teleport lands (LandWorldMap, the start map loads around the landing). An NPC is not carried by this host (NOT READY: the destination map is not opened for a
// mover with no player character, and the shown group holds companions only).
// Type 2 (Teleportation, spell 4, 0x4608B0 → 0x560790): the cast host logs TeleportMap; the world map opens in its teleport mode (from any map, 0x5607E0 with the mode flag set): a click picks the nearest
// KNOWN area within 320 tiles (0x4CB2A0), a click that finds none does nothing; the area's location is measured by the anti-teleport test (a refusal shows message 640 and keeps the map open);
// otherwise the spell is paid (the run's cost, 0x4507D0, only when the source is the local player), the area leaves the last-discovered slot when it was in it (0x4CB160) and the PC lands there
// (0x561860, the world map's landing: flags 0x20) and the window closes. The window closed any other way pays nothing and moves nothing. The waypoint journey is not offered in this mode.
public partial class TerrainViewerApp
{
    // The teleport spell the world map is open for: the run's source and the spell number (0x5C9220, -1 when none), null outside the mode.
    private (string Source, int Spell)? _teleportMode;

    // The teleport host the cast host asks: the anti-teleport test and the area helper over the world-map data and the shown map.
    private AreaSpellTeleport NewSpellTeleportHost() => new(() => _worldMapData?.Data, () => _readyArea is null ? 0 : CurrentAreaMap, WhereForTeleport,
        _areaSession?.Rules.Travel);

    private CellCoord? WhereForTeleport(string id) => id == SessionSpellCaster.Hero ? HeroWorldCell() : AiFrameHost()?.Tile(id);

    /// <summary>A cast host log entry of a teleport (<see cref="SpellCastEventKind.TeleportPortal"/> or <see cref="SpellCastEventKind.TeleportMap"/>) is carried out here.</summary>
    private void TakeTeleportEvent(SpellCastEvent entry)
    {
        if (entry.Kind == SpellCastEventKind.TeleportPortal && entry.Object is { } target) PortalTeleport(target, new(entry.Value, entry.Detail));
        else if (entry.Kind == SpellCastEventKind.TeleportMap && entry.Object is { } source) OpenTeleportWorldMap(source, entry.Value);
    }

    private readonly List<string> _teleportsForSelfTest = [];

    private void PortalTeleport(string target, TravelLocation landing)
    {
        if (target != SessionSpellCaster.Hero)
        {
            GD.Print($"ARC-MAGIC-038 portal of {target} not ready: the host carries no mover without a player character");
            _teleportsForSelfTest.Add($"portal {target} not ready");
            return;
        }
        if (_worldMapData?.Data is not { } data || _readyArea is null) { GD.Print("ARC-MAGIC-038 portal refused: no world-map data"); return; }
        var onStart = CurrentAreaMap == data.StartRuntime;
        GD.Print($"ARC-MAGIC-038 portal hero to start map at ({landing.X},{landing.Y}) from map={CurrentAreaMap}");
        _teleportsForSelfTest.Add($"portal {landing.X},{landing.Y}");
        LandWorldMap(landing, "area portal", teleport: true, ChangeOfPlaceCaller.TeleportSpell, ChangeOfPlaceRules.TeleportSpellFlags, onStart);
    }

    /// <summary>Opens the world map in its teleport mode for the spell (0x560790); the original's refusals leave the spell unpaid and the mode off.</summary>
    private void OpenTeleportWorldMap(string source, int spell)
    {
        _teleportMode = (source, spell);
        _teleportsForSelfTest.Add($"map open spell {spell}");
        OpenWorldMap();
        if (!WorldMapOpen) { _teleportMode = null; GD.Print($"ARC-MAGIC-038 teleport world map refused: {_worldMapStatus}"); }
        else GD.Print($"ARC-MAGIC-038 teleport world map open spell={spell} source={source}");
    }

    /// <summary>A click or an area marker of the world map in the teleport mode (0x5618B0): <paramref name="click"/> is the clicked location. Returns false outside the mode.</summary>
    private bool TeleportPick(TravelLocation click)
    {
        if (_teleportMode is not { } mode || !WorldMapOpen || _worldMapData?.Data is not { } data || _areaSession is not { } owner) return false;
        var area = WorldTravelGeometry.NearestKnownArea(data.TravelAreas, owner.IsAreaKnown, click, WorldJourney.SnapDistance);
        // No known area near, an area with no location, or no spell: the original's click does nothing.
        if (area <= 0 || data.TravelAreas[area].Location is { X: 0, Y: 0 } || mode.Spell == -1) return true;
        var location = data.TravelAreas[area].Location;
        var caster = _spellCaster;
        if (caster is null || NewSpellTeleportHost().Allowed(SessionSpellCaster.Hero, new(unchecked((uint)location.X), unchecked((uint)location.Y))) is false)
        {
            GD.Print($"ARC-MAGIC-038 teleport to area {area} refused by the anti-teleport test");
            _teleportsForSelfTest.Add($"area {area} refused");
            RenderWorldMap("teleportation is not allowed there (the original's message 640)");
            return true;
        }
        // 0x5618B0 calls the cost (0x4507B0 → 0x4507D0) and ignores its answer: a hero who cannot pay still lands.
        if (!caster.PayTeleportCost(mode.Source, mode.Spell)) GD.Print("ARC-MAGIC-038 teleport not paid (the original lands anyway)");
        if (owner.QueryLastDiscoveredArea() == area) owner.ClearLastDiscoveredArea(owner.Version);
        GD.Print($"ARC-MAGIC-038 teleport to area {area} at ({location.X},{location.Y}) spell={mode.Spell}");
        _teleportsForSelfTest.Add($"area {area} landing {location.X},{location.Y}");
        // Pixels name sector centres; the area's own location is the landing (0x561860 with the area's location).
        LandWorldMap(location, $"teleportation to area {area}", teleport: true);
        return true;
    }
}
