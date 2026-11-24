using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-UI-3033: a kit part a window's content draws over its kit background, with its size in original UI units (the frame draws it at its unit position in its own unit scale).</summary>
public sealed record AreaKitOverlay(Texture2D Texture, double UnitWidth, double UnitHeight);

/// <summary>
/// ARC-UI-3033: the window's background is the kit's (a complete one shows under the window in layer 3, ARC-UI-2461), so the window frame draws no background art of its own. The overlays the kit has for the frame's content
/// replace the window's own art for them: the doll view and the hero's doll cover; a null overlay keeps the window's own art.
/// </summary>
public sealed record AreaKitBackdrop(AreaKitOverlay? Doll, AreaKitOverlay? HeroDollCover);
