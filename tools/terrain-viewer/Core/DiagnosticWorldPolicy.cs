namespace Arcanum.TerrainViewer.Core;

/// <summary>Closed ARC-WORLD-001 project policy, not recovered gameplay or collision.</summary>
public static class DiagnosticWorldPolicy
{
    public const double MinimumCell = 0;
    public const double MaximumCell = 63;
    public const double ProjectedPixelsPerSecond = 240;
    public const int IdleFramesPerSecond = 6;
    public const int WalkFramesPerSecond = 10;
    public static DiagnosticSectorPosition InitialPosition => new(30, 32);
    public static Arcanum.Input.InputSettings DefaultInputSettings => new(InvertCameraX: false, InvertCameraY: false,
        InvertActorX: false, InvertActorY: false);
    public const string Limitation = "Diagnostic free movement — collision not evaluated";
    public const string FacingLimitation = "Facing mapping: project diagnostic octants; source facing numbers opaque";
}
