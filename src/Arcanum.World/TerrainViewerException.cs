namespace Arcanum.World;

public sealed class TerrainViewerException(TerrainViewerError error) : Exception(error.Code.ToString())
{
    public TerrainViewerError Error { get; } = error;
}
