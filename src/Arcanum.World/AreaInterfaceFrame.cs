using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.World;

/// <summary>One decoded frame of an interface art: its size and RGBA pixels (palette index 0 transparent), and its hot point
/// (the frame header's centre, which a cursor puts on the pointer, ARC-UI-056).</summary>
/// <param name="Hd">ARC-UI-089: the HD pack question for this frame (<see cref="Arcanum.Data.Hd.HdInterfaceKey"/>); null for a frame that is
/// not interface art read from a file (synthetic frames, composed cursors).</param>
public sealed record AreaInterfaceFrame(int Width, int Height, byte[] Rgba, int HotX = 0, int HotY = 0, Arcanum.Data.Hd.HdSpriteQuery? Hd = null);
