using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.World;

/// <summary>One decoded frame of an interface art: its size and RGBA pixels (palette index 0 transparent), and its hot point
/// (the frame header's centre, which a cursor puts on the pointer, ARC-UI-056).</summary>
public sealed record AreaInterfaceFrame(int Width, int Height, byte[] Rgba, int HotX = 0, int HotY = 0);
