using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-1117 (ledger OH2, OH4): the coloured rings under and over the hovered critter or trap. The original's hovered-object setter (0x43C270)
// decides them once, when the hovered object changes: the arts (467 and 468, both 555 while the local PC is in combat mode) and the colour
// (white; an NPC's reaction category's from rules\reaction_colors.mes). The two rings then step through their art's frames at the art's own
// rate while the object stays hovered. The canvas draws them (TerrainCanvasArea.AreaRing); this file owns the decision and the clock.
public partial class TerrainViewerApp
{
    private AreaHoverRingContent? _ringContent;
    private string? _ringModule;
    private AreaObjectKey? _ringTarget;
    private AreaLoadResult? _ringArea;
    private (int Under, int Over) _ringArts;
    private (byte R, byte G, byte B) _ringColour = AreaHoverRingRules.White;
    private readonly AreaHoverRingClock _ringClock = new();
    private AreaFrameOverlay? _ringOverlay;
    private readonly HashSet<AreaFrameKey> _ringAdded = [];
    private readonly HashSet<string> _ringSaid = new(StringComparer.Ordinal);

    internal AreaHoverRingDraw? HoverRingForSelfTest => _canvas?.AreaRing;
    internal (int Under, int Over) HoverRingArtsForSelfTest => _ringArts;

    /// <summary>The ring colour table and arts of the module the area was read from, read once (a failure is kept and said once).</summary>
    private AreaHoverRingContent? HoverRingContent()
    {
        var module = _arguments?.ModulePath;
        if (module is null) return null;
        if (_ringModule == module) return _ringContent;
        _ringModule = module;
        var found = AreaContentRoot.Discover(module);
        if (found.Status != AreaContentRootStatus.Ready) _ringContent = AreaHoverRingContent.Empty($"not read ({found.Status})");
        else if (ContentStack.OpenShared(found.Plan!) is { Status: ContentStackStatus.Ready, Stack: { } opened })
        {
            using var stack = opened;
            _ringContent = AreaHoverRingContent.Load(stack);
        }
        else _ringContent = AreaHoverRingContent.Empty("content stack did not open");
        GD.Print($"ARC-UI-1117 hover-ring content {_ringContent.Detail}");
        return _ringContent;
    }

    /// <summary>ARC-BUG-1345: the scene is cleared. The ring's overlay wraps the retired map's whole frame repository and the ring's area is its
    /// load result, so neither may outlive the map: the overlay stayed in its field until a later hovered critter replaced it and kept the retired
    /// generation alive (the resources smoke stage's RETIRED_GENERATION_ALIVE after any earlier stage had hovered a critter).</summary>
    private void RetireHoverRing()
    {
        _ringOverlay = null; _ringArea = null; _ringTarget = null; _ringAdded.Clear();
        if (_canvas is not null) _canvas.AreaRing = null;
    }

    /// <summary>Per frame, after the hover decision: the rings of the hovered critter or trap, none otherwise.</summary>
    private void SyncHoverRing()
    {
        if (_canvas is null) return;
        // A new map (or a reloaded one) decides again even for an equal key.
        if (!ReferenceEquals(_ringArea, _readyArea)) { _ringArea = _readyArea; _ringTarget = null; }
        if (HoveredKeyNow is not { } key || _readyArea is null || _readyMode != TerrainViewerMode.Area || _areaSession is null
            || _hoverIndex is not { } index || !index.Contains(key) || !AreaHoverRingRules.Applies(index.Row(key).Type)
            || _areaSession.QueryHero() is null)
        {
            _ringTarget = null; _canvas.AreaRing = null;
            return;
        }
        var content = HoverRingContent();
        var now = (long)Time.GetTicksMsec();
        if (_ringTarget != key)
        {
            // The setter's decision, once per hovered object: PC combat mode and the NPC's reaction are read now (0x43C270).
            _ringTarget = key;
            var type = index.Row(key).Type;
            int? reaction = type == AreaHoverRingRules.NpcType && HoverEntity(key) is { } entity ? _aiHost?.StandardReaction(entity) : null;
            _ringArts = AreaHoverRingRules.Arts(type, HeroInCombatMode);
            _ringColour = AreaHoverRingRules.Colour(type, reaction, content?.Colours);
            _ringClock.Start(now);
        }
        var under = content?.Art(_ringArts.Under); var over = content?.Art(_ringArts.Over);
        if (content is not null)
            foreach (var number in new[] { _ringArts.Under, _ringArts.Over }.Distinct())
                if (content.Arts.TryGetValue(number, out var load) && load.Art is null && _ringSaid.Add($"{_ringModule}:{number}"))
                    GD.Print($"ARC-UI-1117 hover-ring art {number} {load.Status} {load.Detail}: no ring drawn from it");
        // The periods are the ones 0x43A330 reads from arts 467 and 468 at start; swapping to 555 does not change them (0x43A6D0 never re-reads).
        var (underFrame, overFrame) = _ringClock.Step(now, content?.Art(AreaHoverRingRules.UnderArt)?.FramesPerSecond ?? 0, under?.Frames.Count ?? 0,
            content?.Art(AreaHoverRingRules.OverArt)?.FramesPerSecond ?? 0, over?.Frames.Count ?? 0);
        var frames = (AreaFrameOverlay)AreaFrames();
        if (!ReferenceEquals(_ringOverlay, frames)) { _ringOverlay = frames; _ringAdded.Clear(); }
        AreaHoverRingFrame? Use(AreaHoverRingArt? art, int frame)
        {
            if (art is null || art.Frames.Count == 0) return null;
            var chosen = art.Frames[Math.Min(frame, art.Frames.Count - 1)];
            if (_ringAdded.Add(chosen.Key)) frames.Add(chosen.Key, chosen.Pixels);
            return chosen;
        }
        _canvas.AreaRing = new(key, Use(under, underFrame), Use(over, overFrame), _ringColour);
    }
}
