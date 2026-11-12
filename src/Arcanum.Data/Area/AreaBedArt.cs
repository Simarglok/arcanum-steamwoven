namespace Arcanum.Data.Area;

/// <summary>
/// ARC-WORLD-1162 (ledger SWB14): the art test of a bed. A bed is scenery whose art id is of art type 4 (bits 28 to 31) with sub-type 0x16 (bits 6 to 10; 0x504300). Its occupied look is the art's next
/// frame (SWB15: the art id's frame number, bits 14 to 18, steps on), so the area builder decodes the other frames of a bed's art too, whatever its scenery flags say.
/// </summary>
public static class AreaBedArt
{
    /// <summary>Scenery art type (the art id's top four bits) and the sub-type a bed has.</summary>
    public const uint SceneryArtType = 4;
    public const int BedSubType = 0x16;

    /// <summary>Whether an art id is a bed's.</summary>
    public static bool IsBed(uint art) => art >> 28 == SceneryArtType && ((art >> 6) & 0x1f) == BedSubType;
}
