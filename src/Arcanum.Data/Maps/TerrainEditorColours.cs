using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Maps;

/// <summary>One terrain type's editor colour.</summary>
public readonly record struct TerrainColour(byte R, byte G, byte B);

public sealed record TerrainColourLoad(IReadOnlyList<TerrainColour?>? Colours, string? Failure);

/// <summary>
/// ARC-RENDER-034: the terrain types' editor colours of <c>terrain\terrain.mes</c>: message 100 + type is <c>R,G,B</c> (each
/// 0..255, white space around the numbers allowed). The original's terrain initialisation names them editor data and the game
/// does not read them (the generated-wilderness study); the viewer draws its terrain overview beyond the loaded window with
/// them, a Steamwoven viewer feature with no original counterpart. A message that is missing or not three such numbers gives
/// no colour for its type.
/// </summary>
public static class TerrainEditorColours
{
    public const int FirstMessage = 100;

    /// <summary>The colours of types 0..<paramref name="typeCount"/>−1, in type order (null where a type has none).</summary>
    public static IReadOnlyList<TerrainColour?> Read(MesFile file, int typeCount)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (typeCount is < 0 or > TerrainTypeTable.MaxTypes) throw new ArgumentOutOfRangeException(nameof(typeCount));
        var colours = new TerrainColour?[typeCount];
        for (var type = 0; type < typeCount; type++)
            if (file.Find(FirstMessage + type) is { } message) colours[type] = Parse(Encoding.Latin1.GetString(message.Bytes.Span));
        return Array.AsReadOnly(colours);
    }

    /// <summary>The colours of the stack's <c>terrain.mes</c> (the same base source the type table is read from) for the types
    /// of <paramref name="types"/>; a failure names why none were read.</summary>
    public static TerrainColourLoad Open(ContentStack stack, TerrainTypeTable types, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(types);
        options ??= new(); options.Validate();
        if (ContentPhase.Candidate(stack, TerrainTypeTable.MesPath, ContentLoadPhase.GameStart) is not { } candidate) return new(null, "Missing");
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) return new(null, read.Status.ToString());
        var parsed = MesFile.Parse(read.Bytes, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(null, parsed.Status.ToString());
        return new(Read(parsed.File!, types.Types.Count), null);
    }

    // "R,G,B": three decimal numbers 0..255 separated by commas, white space allowed around each.
    private static TerrainColour? Parse(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 3) return null;
        var values = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            var part = parts[i].Trim(' ', '\t');
            if (part.Length is 0 or > 3 || !part.All(char.IsAsciiDigit) || int.Parse(part) is var value && value > 255) return null;
            values[i] = (byte)int.Parse(part);
        }
        return new(values[0], values[1], values[2]);
    }
}
