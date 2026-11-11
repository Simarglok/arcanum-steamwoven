using Arcanum.Data.Area;
using Arcanum.Data.Objects;
using Arcanum.TerrainViewer.Core;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-046 (MA-8 second slice; ledger TR4, TR6): the monster body of an NPC the Trait component transformed. The session keeps the species and palette the spell chose (ARC-MAGIC-040); the original builds
// the new art id from the monster class (12), the species, the old art's rotation and animation, the weapon-part bit of the old art and the palette. The body is drawn through the same path as the body of a
// critter the session created (class 12 already reads there, AreaCreatedCritterArt): its stand pose at the NPC's T0 cell as the T0 body of the shown map (NpcT0Body, so the AI movers move it as they move any
// NPC's, AnimateAiMovers) and, for an NPC that never moved, among the moving bodies the canvas draws over the plan's own T0 body. The restore removes the record and the NPC's own body is back. Not done: a
// companion's body (drawn by the companion group from its template), the fallen pose of a transformed NPC (AreaFallenPoses reads the T0 visual), the weapon a monster shows in combat mode (the armed ART repairs
// to bare hands when the monster has none) and the hero (the actor profile is the human's).
public partial class TerrainViewerApp
{
    // The monster bodies of the shown map by NPC row, the session's records they were built from and the map and host they were built for.
    private Dictionary<AreaObjectKey, AreaDrawCommand> _traitBodies = [];
    private IReadOnlyList<SessionTraitArtRecord> _traitBodiesFrom = [];
    private (AreaLoadResult? Area, AreaAiHost? Host, AreaAiScene? Scene) _traitBodiesFor;
    private readonly Dictionary<uint, AreaCreatedCritterArtLoad> _traitArtLoads = [];

    /// <summary>The monster art id of an NPC whose own current art is <paramref name="old"/> (0x452B00 → 0x503CD0): class 12, the species, no armour or shield fields, frame 0, the old art's rotation (0x502880: bits 9 to 11
    /// for the classes 9 and 14, bits 11 to 13 for the others) and animation (0x503E20: bits 6 to 10 for a critter, monster or unique NPC art), the weapon-part bit (1 when the old art's weapon part, bits 0 to 3, is not 0) and the palette.</summary>
    internal static uint TraitArtId(uint old, SessionTraitArt art) =>
        (12u << 28) | ((uint)art.Species << 23) | ((uint)AreaScenePlan.ArtRotation(old) << 11) | ((uint)WeaponArtRules.AnimationOf(old) << 6) | ((old & 0xFu) != 0 ? 1u : 0u) | ((uint)art.Palette << 4);

    /// <summary>
    /// Brings the monster bodies of the shown map up to date with the session's records: built for each hosted NPC that has one, dropped for each that lost it. Returns whether the set changed (the canvas is then
    /// republished). Cheap when no record exists and none was drawn: one query.
    /// </summary>
    private bool SyncTraitBodies()
    {
        var records = _areaSession?.QueryAllTraitArt() ?? [];
        var host = _aiHost;
        var ready = _readyArea is { } area && _areaOverlay is { } overlay && ReferenceEquals(overlay.Area, area) && host is not null && ExtraArt() is not null;
        if (!ready || records.Count == 0)
        {
            if (_traitBodies.Count == 0) { _traitBodiesFrom = []; return false; }
            foreach (var key in _traitBodies.Keys) _bodyCycles.Remove(key);
            _traitBodies = []; _traitBodiesFrom = []; return true;
        }
        var shown = _readyArea!;
        if (_traitBodiesFor.Area == shown && _traitBodiesFor.Host == host && ReferenceEquals(_traitBodiesFor.Scene, host!.Scene) && _traitBodiesFrom.SequenceEqual(records)) return false;
        if (_traitBodiesFor.Area != shown) _traitArtLoads.Clear();
        _traitBodiesFor = (shown, host, host!.Scene);
        _traitBodiesFrom = records;
        var extra = ExtraArt()!;
        var frames = (AreaFrameOverlay)AreaFrames();
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        foreach (var record in records)
        {
            if (host!.Scene.Npcs.FirstOrDefault(n => n.Entity == record.Entity) is not { } npc) continue;
            if (npc.Row.WorldX is not { } x || npc.Row.WorldY is not { } y || Number(npc.Row, CurrentArtSlot) is not { } own) continue;
            var aid = TraitArtId(own, record.Art);
            if (!_traitArtLoads.TryGetValue(aid, out var load))
            {
                _traitArtLoads[aid] = load = AreaCreatedCritterArt.Load(aid, shown.ArtSources, extra.MappedName);
                GD.Print($"ARC-MAGIC-046 trait-art art=0x{aid:X8} species={record.Art.Species} palette={record.Art.Palette} status={load.Status} detail={load.Detail}");
            }
            if (load.Visual is not { } visual || load.Pose is not { } pose) continue;
            frames.Add(pose.Key, pose.Frame);
            _extraVisuals[visual.Key] = visual;
            var key = npc.Row.Key;
            bodies[key] = AreaScenePlan.Place(shown.Plan.Chart, key, visual, x, y, false) with { Frame = pose.Key };
        }
        var changed = bodies.Count != _traitBodies.Count || bodies.Any(pair => !_traitBodies.TryGetValue(pair.Key, out var was) || was != pair.Value);
        // A mover's gait cycle is of the body it was read for: only the NPCs whose body changed (a body added, removed or replaced) start theirs again.
        foreach (var key in bodies.Keys.Union(_traitBodies.Keys).Where(k => !bodies.TryGetValue(k, out var now) || !_traitBodies.TryGetValue(k, out var was) || now != was).ToArray()) _bodyCycles.Remove(key);
        _traitBodies = bodies;
        GD.Print($"ARC-MAGIC-046 trait-bodies records={records.Count} drawn={bodies.Count} changed={changed}");
        return changed;
    }
}
