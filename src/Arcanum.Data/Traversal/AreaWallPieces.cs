namespace Arcanum.Data.Traversal;

/// <summary>What a wall piece is to a step or a line through its edge: a window piece, a doorway piece, or any other (solid) piece.</summary>
public enum AreaWallPieceKind { Solid, Window, Doorway }

/// <summary>
/// A wall or portal's place on its tile and a wall's piece, read from its ART id as the step evidence and the original's step cost
/// (0x43FE00) read them (docs/research/cave-transition-evidence.md T7, docs/research/first-building-evidence.md; ARC-COMBAT-012 opens
/// them to the line of fire).
/// </summary>
public static class AreaWallPieces
{
    private static readonly int[] WindowPieces = [10, 13, 14, 17, 18, 19];
    private static readonly int[] DoorwayPieces = [22, 25, 26, 29, 30, 31, 32];

    /// <summary>The tile edge the object stands on: its ART rotation (bits 11–13) rounded up to odd (1, 3, 5 or 7).</summary>
    public static int Edge(uint art) => (int)((art >> 11) & 7) | 1;

    /// <summary>A wall's piece (ART bits 14–19).</summary>
    public static int Piece(uint art) => (int)((art >> 14) & 63);

    /// <summary>The kind of a wall piece.</summary>
    public static AreaWallPieceKind Kind(int piece) =>
        WindowPieces.Contains(piece) ? AreaWallPieceKind.Window : DoorwayPieces.Contains(piece) ? AreaWallPieceKind.Doorway : AreaWallPieceKind.Solid;
}
