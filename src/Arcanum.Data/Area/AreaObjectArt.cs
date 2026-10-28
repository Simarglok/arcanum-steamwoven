namespace Arcanum.Data.Area;

/// <summary>The ART file and frame selection an art id of a scenery (class 4), item (class 6), container (class 7) or eye candy
/// (class 14, ARC-MAGIC-017) names.</summary>
/// <param name="Path">The content path, under <c>art/scenery/</c>, <c>art/item/</c> or <c>art/container/</c>.</param>
/// <param name="Lookup">The art id the selection is keyed by (an item's without its two palette-free bits).</param>
public sealed record AreaObjectArtPath(string Path, uint Lookup, int Nominal, int Group, int Frame, int Palette);

public enum AreaObjectArtStatus { Resolved, OtherClass, Unsupported, UnnamedNumber, UnsafeName }

/// <summary>The resolution of one art id (<see cref="AreaObjectArt.Resolve"/>): the path when <see cref="Status"/> is Resolved.</summary>
public sealed record AreaObjectArtResolution(AreaObjectArtStatus Status, AreaObjectArtPath? Path, string Detail);

/// <summary>
/// ARC-RENDER-033: the original's art names of the object classes a map draws from name tables (the rules the area owner applies
/// to every body, here for an art id a body does not draw at T0: a scenery's destroyed ART, an item lying in an inventory). The
/// name tables are the plan's mapping files (<see cref="MappingPath"/>), read by the caller in content-stack order.
/// </summary>
public static class AreaObjectArt
{
    /// <summary>The name table of a mapping role a class-4, -6 or -7 art id reads (the area plans bind the same paths).</summary>
    public static string MappingPath(AreaMappingRole role) => role switch
    {
        AreaMappingRole.Scenery => "art/scenery/scenery.mes",
        AreaMappingRole.Container => "art/container/container.mes",
        AreaMappingRole.ItemGround => "art/item/item_ground.mes",
        AreaMappingRole.EyeCandy => Arcanum.Data.Magic.EyeCandyArt.NamesPath,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>The ART an art id of class 4 (scenery), 6 (item) or 7 (container) names, through <paramref name="name"/> (the
    /// role's table entry for a number, null when the table has none).</summary>
    public static AreaObjectArtResolution Resolve(uint aid, Func<AreaMappingRole, int, string?> name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var cls = aid >> 28;
        if (cls == Arcanum.Data.Magic.EyeCandyArt.Class) return EyeCandy(aid, name);
        if (cls is not (4 or 6 or 7)) return new(AreaObjectArtStatus.OtherClass, null, $"class {cls}");
        var frame = cls == 6 ? 0 : (int)((aid >> 14) & 31); var nominal = cls == 6 ? 0 : (int)((aid >> 11) & 7);
        var palette = (int)((aid >> 4) & 3);
        AreaMappingRole role; int key; string folder; var lookup = aid;
        if (cls is 4 or 7)
        {
            role = cls == 4 ? AreaMappingRole.Scenery : AreaMappingRole.Container;
            key = (int)(1000 * ((aid >> 6) & 31) + ((aid >> 19) & 511));
            folder = cls == 4 ? "art/scenery/" : "art/container/";
        }
        else
        {
            if (((aid >> 12) & 3) != 0) return new(AreaObjectArtStatus.Unsupported, null, "item art bits 12..13");
            var type = aid & 15; var subtype = (aid >> 6) & 15; var number = (aid >> 17) & 2047; var coverage = (aid >> 14) & 7;
            key = checked((int)(number + 20 * (subtype + 50 * type) + (type == 2 && coverage != 0 ? 20 * (5 * coverage + 10) : 0)));
            role = AreaMappingRole.ItemGround; folder = "art/item/";
            lookup = aid & ~0x30U;
        }
        if (name(role, key) is not { } entry) return new(AreaObjectArtStatus.UnnamedNumber, null, $"{role} {key}");
        if (!Safe(entry)) return new(AreaObjectArtStatus.UnsafeName, null, $"{role} {key}");
        return new(AreaObjectArtStatus.Resolved, new(folder + entry, lookup, nominal, nominal, frame, palette), $"{role} {key}");
    }

    // ARC-MAGIC-017: an eye-candy art id (class 14, ledger EC rows) names art\eye_candy\<name>_<F|B|U>.art by its number in
    // eye_candy.mes (0x41DF84); its frame, rotation and palette select the frame.
    private static AreaObjectArtResolution EyeCandy(uint aid, Func<AreaMappingRole, int, string?> name)
    {
        var number = Arcanum.Data.Magic.EyeCandyArt.Number(aid);
        if (name(AreaMappingRole.EyeCandy, number) is not { } entry) return new(AreaObjectArtStatus.UnnamedNumber, null, $"{AreaMappingRole.EyeCandy} {number}");
        if (!Safe(entry)) return new(AreaObjectArtStatus.UnsafeName, null, $"{AreaMappingRole.EyeCandy} {number}");
        var variant = Arcanum.Data.Magic.EyeCandyArt.Variant(aid);
        if ((int)variant > 2) return new(AreaObjectArtStatus.Unsupported, null, "eye-candy variant 3");
        var rotation = Arcanum.Data.Magic.EyeCandyArt.Rotation(aid);
        return new(AreaObjectArtStatus.Resolved, new(Arcanum.Data.Magic.EyeCandyArt.FilePath(entry, variant), aid, rotation, rotation,
            Arcanum.Data.Magic.EyeCandyArt.Frame(aid), Arcanum.Data.Magic.EyeCandyArt.Palette(aid)), $"{AreaMappingRole.EyeCandy} {number}");
    }

    /// <summary>A mapping file's name table in the original MES grammar, with the owner's limits; null when it does not parse to a
    /// non-empty table (a duplicate number, a limit, malformed text).</summary>
    public static IReadOnlyDictionary<int, string>? ReadNameTable(ReadOnlySpan<byte> bytes) =>
        Arcanum.Data.Messages.MesTable.Read(bytes, AreaDataOwner.MappingMesOptions, out var table) == Arcanum.Data.Messages.MesParseStatus.Parsed && table.Count > 0
            ? table : null;

    /// <summary>A table entry the owner admits as a file name: printable ASCII without path characters, not blank or padded.</summary>
    public static bool Safe(string token) =>
        !string.IsNullOrWhiteSpace(token) && token == token.Trim() && !token.Any(c => c < 32 || c >= 127 || c is '/' or '\\' or ':') && token is not "." and not "..";
}
