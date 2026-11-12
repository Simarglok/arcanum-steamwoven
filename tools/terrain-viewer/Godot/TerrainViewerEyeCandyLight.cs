using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-MAGIC-1229 (FR-LIGHT-011, ledger IW2–IW5, LT10, LT14): the live light a lit eye candy (Illuminate, 65) keeps on its object while its run is shown. The light is not an object light of
// the map: the overlay host answers it from the shown runs (AreaEyeCandyHost.Lights), and the carried-light source of the lighting (TerrainViewerAreaLighting.cs, the lights that move with
// a creature) stands it where the object stands now, so the drawn glow and the darkness sampler (AreaCellLightLevel) see the same light and it follows its object. Not ready: the light on an
// object with an offset of its own (the eye candy parts do not use the object's offsets either), a light ART number whose frame the map's lighting did not load (counted, logged once).
public partial class TerrainViewerApp
{
    private readonly HashSet<int> _eyeCandyLightMissing = [];

    /// <summary>How many live eye candy lights the shown overlay runs keep now (0 without a host).</summary>
    private int EyeCandyLightCount() => ShownEyeCandyHost?.Lights().Count() ?? 0;

    /// <summary>A signature of the live eye candy lights (owner, run, slot): it changes when a glow starts, ends or is replaced by another in the same frame (0 for none).</summary>
    private int EyeCandyLightKey()
    {
        var hash = new HashCode(); var any = false;
        foreach (var light in ShownEyeCandyHost?.Lights() ?? []) { hash.Add(light.Owner); hash.Add(light.Handle); hash.Add(light.Slot); any = true; }
        return any ? hash.ToHashCode() | 1 : 0;
    }

    /// <summary>The live eye candy lights where their objects stand now, with the frames of their light ARTs: the hero's at its placement (with its offsets, as the hero's carried light), a map
    /// critter's at its cell with the shift of its pose (LT10), a created critter's at its cell. A light whose object is not here (a companion on another map) or whose ART frame was not loaded is left out.</summary>
    private void AddEyeCandyLights(AreaLightingPlan lighting, AreaLocalLighting local, ActorDiagnosticPlacementInput placement, IReadOnlyDictionary<AreaObjectKey, AreaCritterPose> poses,
        IReadOnlySet<AreaObjectKey>? absent, Dictionary<int, StationaryLightFrame> frames, List<StationaryLight> lights, Func<long, long, int, int, bool> near)
    {
        if (ShownEyeCandyHost is not { } host || _areaSession is not { } session) return;
        foreach (var lit in host.Lights())
        {
            var frame = (lit.ArtNumber == Arcanum.Data.Lighting.CarriedLightRules.ArtNumber ? lighting.CarriedFrame : null) ?? local.Field.Source.Frames.GetValueOrDefault(lit.ArtNumber) ?? local.CritterFrames.GetValueOrDefault(lit.ArtNumber);
            if (frame is null)
            {
                if (_eyeCandyLightMissing.Add(lit.ArtNumber)) GD.Print($"ARC-MAGIC-1229 eye-candy-light-art-missing number={lit.ArtNumber}");
                continue;
            }
            StationaryLight? stood = null;
            if (lit.Owner == SessionSpellCaster.Hero)
            {
                if (placement.MapX is { } x && placement.MapY is { } y)
                    stood = AreaEyeCandyLights.ToLight(lit, checked(local.Chart.OriginX + x), checked(local.Chart.OriginY + y), placement.InstanceOffsetX ?? 0, placement.InstanceOffsetY ?? 0, null);
                else if (HeroWorldCell() is { } cell) stood = AreaEyeCandyLights.ToLight(lit, cell.X, cell.Y, 0, 0, null);
            }
            else if (_spellCastMap?.Row(lit.Owner) is { } row)
            {
                // A companion that stands on another map takes its glow with it.
                if (absent?.Contains(row.Key) == true) continue;
                if (poses.TryGetValue(row.Key, out var pose) && row.WorldX is { } baseX && row.WorldY is { } baseY)
                {
                    var (cx, cy, ox, oy) = AreaCritterLights.Stand(baseX, baseY, 0, 0, pose);
                    stood = AreaEyeCandyLights.ToLight(lit, cx, cy, ox, oy, row.Key);
                }
                else if (EyeCandyCell(session, lit.Owner) is { } live) stood = AreaEyeCandyLights.ToLight(lit, live.X, live.Y, 0, 0, row.Key);
            }
            else if (EyeCandyCell(session, lit.Owner) is { } made) stood = AreaEyeCandyLights.ToLight(lit, made.X, made.Y, 0, 0, null);
            if (stood is not { } light || !near(light.X, light.Y, light.OffsetX, light.OffsetY)) continue;
            frames.TryAdd(lit.ArtNumber, frame);
            lights.Add(light);
        }
    }
}
