using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Magic;

/// <summary>ARC-MAGIC-017: the parts of an eye-candy art id (0x504AE0): the foreground overlay, the background overlay and the
/// underlay, each its own ART file (<c>_F</c>, <c>_B</c>, <c>_U</c>, table 0x5A11EC).</summary>
public enum EyeCandyVariant { Foreground = 0, Background = 1, Underlay = 2 }

/// <summary>
/// ARC-MAGIC-017: the eye-candy art id (class 14) as the original builds it (0x504AE0) and reads it (ledger EC rows): bits 1..3 the
/// scale index, 4..5 the palette, 6..7 the variant, 8 translucency, 9..11 the rotation, 12..18 the frame, 19..27 the number of
/// <c>art\eye_candy\eye_candy.mes</c>. The file is <c>art\eye_candy\&lt;name&gt;_&lt;F|B|U&gt;.art</c> (0x41DF84).
/// </summary>
public static class EyeCandyArt
{
    public const int Class = 14;
    public const string NamesPath = "art/eye_candy/eye_candy.mes";
    /// <summary>The scale index of 100 % (the parse default and the draw's "unscaled").</summary>
    public const int UnscaledIndex = 4;

    /// <summary>The draw's scale of each index, in percent (0x5A548C; the parse's table differs, <see cref="SpellEyeCandyTable"/>).</summary>
    public static IReadOnlyList<int> DrawScales { get; } = Array.AsReadOnly(new[] { 50, 63, 75, 87, 100, 130, 160, 200 });

    /// <summary>The art id, or null for a part out of range (the original's error 12: number below 512, frame below 128, rotation
    /// below 8, translucency 0 or 1, variant and palette below 4, scale 0..7).</summary>
    [OriginalFunction(0x504AE0, OriginalFunctionScope.Full)]
    public static uint? Make(int number, int frame, int rotation, bool translucent, int variant, int palette, int scale)
    {
        if ((uint)number >= 0x200 || (uint)frame >= 0x80 || rotation is < 0 or >= 8 || (uint)variant >= 4 || (uint)palette >= 4 || scale is < 0 or > 7)
            return null;
        return ((uint)Class << 28) | ((uint)number << 19) | ((uint)frame << 12) | ((uint)rotation << 9) | (translucent ? 1u << 8 : 0)
            | ((uint)variant << 6) | ((uint)palette << 4) | ((uint)scale << 1);
    }

    public static bool IsEyeCandy(uint aid) => aid >> 28 == Class;
    public static int Number(uint aid) => (int)((aid >> 19) & 0x1FF);
    /// <summary>The frame (0x502B50, class 14: bits 12..18).</summary>
    [OriginalFunction(0x502B50, OriginalFunctionScope.Partial)]
    public static int Frame(uint aid) => (int)((aid >> 12) & 0x7F);
    public static int Rotation(uint aid) => (int)((aid >> 9) & 7);
    [OriginalFunction(0x504C00, OriginalFunctionScope.Partial)]
    public static bool Translucent(uint aid) => ((aid >> 8) & 1) != 0;
    /// <summary>The variant (0x504B90).</summary>
    [OriginalFunction(0x504B90, OriginalFunctionScope.Partial)]
    public static EyeCandyVariant Variant(uint aid) => (EyeCandyVariant)((aid >> 6) & 3);
    public static int Palette(uint aid) => (int)((aid >> 4) & 3);
    [OriginalFunction(0x504C60, OriginalFunctionScope.Partial)]
    public static int Scale(uint aid) => (int)((aid >> 1) & 7);

    /// <summary>The same art id with another variant (0x504BC0; the original logs and takes 0 for one of 3 or more).</summary>
    [OriginalFunction(0x504BC0, OriginalFunctionScope.Full)]
    public static uint WithVariant(uint aid, EyeCandyVariant variant)
    {
        if (!IsEyeCandy(aid)) return aid;
        var v = (int)variant is >= 0 and < 3 ? (uint)variant : 0;
        return (aid & ~0xC0u) | (v << 6);
    }

    /// <summary>
    /// ARC-SCRIPT-1829 (WE4, the art rotation reader and setter the script play uses; the setter's row is marked on <c>ProjectileRules.FacedArt</c>): the entry's art turned to an object's rotation. A modifier of 0 leaves the art alone (an object whose art has no rotation, or faces rotation 0); otherwise the rotation replaces bits 9..11 of
    /// a class 14 art id, a value of 8 or more taken as 0 and a negative one as 7 (the setter's clamp). An art of another class passes through (the original also remaps wall art and mirrors portal art there; no eye-candy entry has such art).
    /// </summary>
    public static uint TurnTo(uint aid, int rotation)
    {
        if (rotation == 0 || !IsEyeCandy(aid)) return aid;
        var turned = rotation >= 8 ? 0 : rotation < 0 ? 7 : rotation;
        return (aid & ~(7u << 9)) | ((uint)turned << 9);
    }

    /// <summary>The same art id at another frame (0..127).</summary>
    public static uint WithFrame(uint aid, int frame) =>
        (uint)frame < 0x80 ? (aid & ~(0x7Fu << 12)) | ((uint)frame << 12) : throw new ArgumentOutOfRangeException(nameof(frame));

    /// <summary>The ART file of a variant for its <c>eye_candy.mes</c> name (0x41DF84).</summary>
    public static string FilePath(string name, EyeCandyVariant variant)
    {
        ArgumentNullException.ThrowIfNull(name);
        return $"art/eye_candy/{name}_{"FBU"[(int)variant is >= 0 and < 3 ? (int)variant : 0]}.art";
    }
}

/// <summary>ARC-MAGIC-017: the <c>Flags:</c> of an eye-candy entry (0x5B76A8 with values 0x5B76C8), with the bits the parse adds:
/// 0x8 for a blend or translucency, 0x10 for tinting.</summary>
[Flags]
public enum SpellEyeCandyFlags : uint
{
    None = 0,
    Foreground = 0x1,
    Background = 0x2,
    Underlay = 0x4,
    Translucent = 0x8,
    Tinting = 0x10,
    Animates = 0x20,
    /// <summary><c>anim_forever</c> is 0x60: <see cref="Animates"/> and this bit.</summary>
    Forever = 0x40,
    AutoScale = 0x80,
}

/// <summary>ARC-MAGIC-017: one entry of an eye-candy list (24 bytes in the original): the art id (null for none, the original's −1;
/// translucency, variant, palette and scale in it), the flags, the sound (−1 none), the light art number (null none), its colour
/// (the three values of <c>Light Color:</c>, white without it) and the projectile speed (0 none).</summary>
public sealed record SpellEyeCandyEntry(uint? Art, SpellEyeCandyFlags Flags, int Sound, int? Light, (byte R, byte G, byte B) LightColor, int ProjectileSpeed)
{
    public static SpellEyeCandyEntry Empty { get; } = new(null, SpellEyeCandyFlags.None, -1, null, (0, 0, 0), 0);
}

public sealed record SpellEyeCandyRead(SpellDataStatus Status, SpellEyeCandyTable? Table, SpellDataRefusal? Refusal = null);

/// <summary>
/// ARC-SCRIPT-1592: how one eye-candy list is laid out in its file (the list record the module's loader 0x4CDB70 reads: 0x4CDB30 sets entries per group 1, message stride 1 and no sound offsets, a list's init changes them). The entries of a group are
/// <paramref name="PerGroup"/> messages in a row, the next group starts <paramref name="FileStride"/> messages after the group before; the groups run from message 0 while the group's first message exists (0x4CDCD0). A list with sound offsets gives an entry
/// without <c>Sound:</c> its group's first entry's sound plus its offset (0x4CDF3B).
/// </summary>
public sealed record EyeCandyListShape(string Path, int PerGroup, int FileStride, IReadOnlyList<int> SoundOffsets, int FirstMessage = 0)
{
    /// <summary>The spell list (0x5E7568): six entries for each ten messages, with the sound offsets of 0x5B0DC8.</summary>
    public static EyeCandyListShape Spell { get; } = new(SpellEyeCandyTable.Path, SpellEyeCandyTable.PerSpell, SpellEyeCandyTable.FileStride, SpellEyeCandyTable.SoundOffsets);
    /// <summary>The script list (0x5E2FB0): the loader's defaults (the script module's init 0x4446E0 changes none of them): one entry per message, in a row from message 0, no sound offsets. A script's entry number is the message number.</summary>
    public static EyeCandyListShape Script { get; } = new(SpellEyeCandyTable.ScriptPath, 1, 1, Array.Empty<int>());
    /// <summary>ARC-RENDER-1794: the animation list (0x5DE670, loaded by the animation module's init 0x421B00): <c>rules\AnimEyeCandy.mes</c> with the loader's defaults too (the init presets a count of 11 that the loader's own count 0x4CDCD0 overwrites): one entry per message from message 0, no sound offsets. Entry 0 to 5 are the normal, poison, electrical, fire, fatigue and critical splotches, 6 and 7 the undead and the clean effect, 8 to 10 the damage tiers (the hit's blood, <c>HitGoreRules</c>).</summary>
    public static EyeCandyListShape Anim { get; } = new(SpellEyeCandyTable.AnimPath, 1, 1, Array.Empty<int>());
    /// <summary>ARC-RENDER-1990: the weapon list (0x5DE610, loaded by the same init 0x421B00 with the list's own settings: first message 1, five entries per group, a stride of ten messages, no sound offsets): <c>rules\WeaponEyeCandy.mes</c>, entry <c>5g + k</c> is message <c>10g + 1 + k</c>
    /// (k: 0 begin, 1 swing, 2 projectile speed, 3 hit, 4 never asked), the groups counted while message <c>10g + 1</c> exists (the count 0x4CDCD0 starts at the list's first message).</summary>
    public static EyeCandyListShape Weapon { get; } = new(SpellEyeCandyTable.WeaponPath, 5, 10, Array.Empty<int>(), 1);
}

/// <summary>
/// ARC-MAGIC-017: <c>rules\SpellEyeCandy.mes</c> as the magic-tech init loads it (0x44F07C, list 0x5E7568 of the animation-effect
/// module 0x4CDB70; ledger EC rows): six entries per spell — 0 cast, 1 projectile, 2 destination, 3 secondary destination,
/// 4 secondary cast, 5 unnamed — read from messages <c>10·s + k</c>. The groups run from message 0 while message <c>10·s</c>
/// exists (0x4CDCD0); a missing message, or an empty one, is an entry without art. Each line is parsed as 0x4CDDF0 does: with
/// <c>Art:</c>, then <c>Palette:</c>, <c>Scale:</c>, <c>Blend:</c>, <c>Sound:</c> and <c>Flags:</c>, the art id built from them
/// and each flagged part's ART asked for (a missing one leaves no art and no flags); without <c>Art:</c>, <c>Sound:</c> or else
/// <c>ArtEffect:</c>; then <c>Light:</c>, <c>Light Color:</c> and <c>Proj Speed:</c>. Universal: nothing here names a map.
/// </summary>
public sealed class SpellEyeCandyTable
{
    public const string Path = "rules/SpellEyeCandy.mes";
    /// <summary>ARC-SCRIPT-1592: the script eye-candy list (0x5E2FB0, loaded by the script module's init 0x4446E0) is the same module list over <c>rules\ScriptEyeCandy.mes</c>, laid out as <see cref="EyeCandyListShape.Script"/> (one entry per message):
    /// its entry <c>n</c> is the number a script's play action names (0x44C7A0 passes it as the index).</summary>
    public const string ScriptPath = "rules/ScriptEyeCandy.mes";
    /// <summary>ARC-RENDER-1794: the animation eye-candy list (0x5DE670, the animation module's init 0x421B00), the blood of a hit: laid out as <see cref="EyeCandyListShape.Anim"/>.</summary>
    public const string AnimPath = "rules/AnimEyeCandy.mes";
    /// <summary>ARC-RENDER-1990: the weapon eye-candy list (0x5DE610, the animation module's init 0x421B00): laid out as <see cref="EyeCandyListShape.Weapon"/>.</summary>
    public const string WeaponPath = "rules/WeaponEyeCandy.mes";
    public const int PerSpell = 6, FileStride = 10;
    public const int Cast = 0, Projectile = 1, Destination = 2, SecondaryDestination = 3, SecondaryCast = 4;

    /// <summary>The parse's <c>Scale:</c> percents of each index (0x5B770C; the draw's table differs, <see cref="EyeCandyArt.DrawScales"/>).</summary>
    public static IReadOnlyList<int> ParseScales { get; } = Array.AsReadOnly(new[] { 50, 62, 75, 87, 100, 125, 150, 200 });
    /// <summary>The <c>Blend:</c> names the parse looks up (0x5B76E8, seven of the table's eight: <c>Translucency</c> is not one).</summary>
    public static IReadOnlyList<string?> BlendNames { get; } = Array.AsReadOnly(new string?[] { "none", "add", "sub", "mult", "alphC", "alphA", "alphS" });
    public static IReadOnlyList<string> FlagNames { get; } =
        Array.AsReadOnly(new[] { "overlay_f", "overlay_b", "overlay_fb", "underlay", "tinting", "animates", "anim_forever", "can_autoscale" });
    public static IReadOnlyList<uint> FlagValues { get; } = Array.AsReadOnly(new uint[] { 0x1, 0x2, 0x3, 0x4, 0x10, 0x20, 0x60, 0x80 });
    public static IReadOnlyList<string?> ArtEffectNames { get; } = Array.AsReadOnly(new string?[] { "Translucency", "Tinting" });
    /// <summary>The sound an entry without <c>Sound:</c> takes from its group's first entry: that sound plus this offset by the entry's
    /// place in the group (0x5B0DC8), unless the first entry has none.</summary>
    public static IReadOnlyList<int> SoundOffsets { get; } = Array.AsReadOnly(new[] { 0, 4, 5, 6, 3, 7 });

    private readonly SpellEyeCandyEntry[] _entries;

    private SpellEyeCandyTable(SpellEyeCandyEntry[] entries) => _entries = entries;

    /// <summary>The entries, six per spell group.</summary>
    public IReadOnlyList<SpellEyeCandyEntry> Entries => Array.AsReadOnly(_entries);

    /// <summary>The entry of a spell's place in its group (<see cref="Cast"/> … 5); the empty entry outside the table.</summary>
    public SpellEyeCandyEntry Entry(int spell, int place) =>
        place is >= 0 and < PerSpell && spell >= 0 && (long)spell * PerSpell + place < _entries.Length ? _entries[spell * PerSpell + place] : SpellEyeCandyEntry.Empty;

    /// <summary>The entry at a table index (the original's <c>6·spell + k</c>); the empty entry outside the table.</summary>
    public SpellEyeCandyEntry At(int index) => index >= 0 && index < _entries.Length ? _entries[index] : SpellEyeCandyEntry.Empty;

    /// <summary>Reads the file from the first base source holding it (an init file, ARC-DATA-027), the parts' ART asked from the same
    /// stack through <see cref="ArtExists"/>.</summary>
    [OriginalFunction(0x4CDB70, OriginalFunctionScope.Partial)]
    public static SpellEyeCandyRead Open(ContentStack stack, MesReaderOptions? options = null) => Open(stack, EyeCandyListShape.Spell, options);

    /// <summary>ARC-SCRIPT-1592: <c>rules\ScriptEyeCandy.mes</c> read as the script list (<see cref="EyeCandyListShape.Script"/>: one entry per message).</summary>
    public static SpellEyeCandyRead OpenScript(ContentStack stack, MesReaderOptions? options = null) => Open(stack, EyeCandyListShape.Script, options);

    /// <summary>ARC-RENDER-1794: <c>rules\AnimEyeCandy.mes</c> read as the animation list (<see cref="EyeCandyListShape.Anim"/>: one entry per message), the table of a hit's blood.</summary>
    public static SpellEyeCandyRead OpenAnim(ContentStack stack, MesReaderOptions? options = null) => Open(stack, EyeCandyListShape.Anim, options);

    /// <summary>ARC-RENDER-1990: <c>rules\WeaponEyeCandy.mes</c> read as the weapon list (<see cref="EyeCandyListShape.Weapon"/>), the table of a weapon's own begin, swing, projectile and hit effects.</summary>
    public static SpellEyeCandyRead OpenWeapon(ContentStack stack, MesReaderOptions? options = null) => Open(stack, EyeCandyListShape.Weapon, options);

    private static SpellEyeCandyRead Open(ContentStack stack, EyeCandyListShape shape, MesReaderOptions? options)
    {
        var path = shape.Path;
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source) return Refused(new(SpellDataStatus.FileMissing, path));
        if (source.Length > options.MaxBytes) return Refused(new(SpellDataStatus.FileRefused, path, Token: nameof(MesParseStatus.TooLarge)));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return Refused(new(SpellDataStatus.FileUnreadable, path, Token: read.Status.ToString()));
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return Refused(new(SpellDataStatus.FileRefused, path, parsed.Number, $"{parsed.Status} line {parsed.Line}"));
        var names = ReadNames(stack, options);
        return Read(parsed.File!, aid => ArtExists(stack, names, aid), shape);
    }

    /// <summary>The entries of a parsed file. <paramref name="artExists"/> answers whether the ART of one part's art id loads (the
    /// original's 0x502220); a line whose flagged part does not load keeps no art and no flags (0x4CE1A0).</summary>
    public static SpellEyeCandyRead Read(MesFile file, Func<uint, bool> artExists, EyeCandyListShape? shape = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(artExists);
        shape ??= EyeCandyListShape.Spell;
        var path = shape.Path;
        // The count (0x4CDCD0): the entries of a group for each message the stride puts a group's first at that exists, from the list's first message on.
        var groups = 0;
        while (file.Find(shape.FirstMessage + groups * shape.FileStride) is not null) groups++;
        if (groups == 0) return Refused(new(SpellDataStatus.FileEmpty, path));
        var entries = new SpellEyeCandyEntry[groups * shape.PerGroup];
        for (var index = 0; index < entries.Length; index++)
        {
            var number = shape.FirstMessage + index / shape.PerGroup * shape.FileStride + index % shape.PerGroup;
            var text = SpellTexts.Text(file, number);
            // A missing or empty message is an entry without art (0x4CE2A0, 0x4CDE54).
            if (text is null || text.Length == 0) { entries[index] = SpellEyeCandyEntry.Empty; continue; }
            foreach (var c in text)
                if (c > '\x7f') return Refused(new(SpellDataStatus.NonAsciiByte, path, number, c.ToString()));
            var cursor = new NamedKeyCursor(text);
            var entry = Line(cursor, index, entries, artExists, shape);
            if (cursor.Fault != NamedKeyFault.None)
                return Refused(new(cursor.Fault switch
                {
                    NamedKeyFault.MissingParenthesis => SpellDataStatus.MissingParenthesis,
                    NamedKeyFault.UnknownName => SpellDataStatus.UnknownName,
                    NamedKeyFault.UnknownFlag => SpellDataStatus.UnknownFlag,
                    NamedKeyFault.MissingValue => SpellDataStatus.MissingValue,
                    _ => SpellDataStatus.TrailingSeparator,
                }, path, number, cursor.FaultToken));
            entries[index] = entry;
        }
        return new(SpellDataStatus.Read, new(entries));
    }

    /// <summary>One non-empty line (0x4CDDF0). With <c>Art:</c> the palette (0 without), the scale
    /// (100 % without; a percent not in the table is logged and takes index 0), the blend and the sound are read before the flags;
    /// the blend's value shares the scale's temporary, so a line with <c>Scale:</c> but no <c>Blend:</c> is translucent too
    /// (0x4CDEFC). A flag list always replaces the flags (0x532630 clears its value first).</summary>
    [OriginalFunction(0x4CDDF0, OriginalFunctionScope.Partial)]
    private static SpellEyeCandyEntry Line(NamedKeyCursor cursor, int index, SpellEyeCandyEntry[] entries, Func<uint, bool> artExists, EyeCandyListShape shape)
    {
        uint? art = null; var flags = SpellEyeCandyFlags.None; var sound = -1; int? light = null; (byte, byte, byte) color = (0, 0, 0); var speed = 0;
        if (cursor.NamedInt("Art:", out _) is { } number)
        {
            var palette = cursor.NamedInt("Palette:", out _) ?? 0;
            var shared = 0;
            var scale = EyeCandyArt.UnscaledIndex;
            if (cursor.NamedInt("Scale:", out _) is { } percent)
            {
                shared = percent;
                var at = ParseScales.ToList().IndexOf(percent);
                scale = at < 0 ? 0 : at;
            }
            if (cursor.NamedName("Blend:", BlendNames, out _) is { } blend) shared = blend;
            if (cursor.Fault != NamedKeyFault.None) return SpellEyeCandyEntry.Empty;
            if (cursor.NamedInt("Sound:", out _) is { } named) sound = named;
            else sound = GroupSound(index, entries, shape);
            flags = (SpellEyeCandyFlags)(cursor.NamedFlags("Flags:", FlagNames, FlagValues, out _, out _) ?? 0);
            if (cursor.Fault != NamedKeyFault.None) return SpellEyeCandyEntry.Empty;
            if (shared != 0) flags |= SpellEyeCandyFlags.Translucent;
            var variant = (flags & (SpellEyeCandyFlags.Foreground | SpellEyeCandyFlags.Background)) != 0 ? 0 : 2;
            art = EyeCandyArt.Make(number, 0, 0, (flags & SpellEyeCandyFlags.Translucent) != 0, variant, palette, scale);
            // A failed build (error 12, 0x4CE028) and a part that does not load (0x4CE1A0) both leave no art and no flags.
            if (art is not { } built || !PartsLoad(built, flags, artExists)) { art = null; flags = SpellEyeCandyFlags.None; }
        }
        else if (cursor.NamedInt("Sound:", out _) is { } named) sound = named;
        else if (cursor.NamedName("ArtEffect:", ArtEffectNames, out _) is { } effect)
            flags |= effect == 0 ? SpellEyeCandyFlags.Translucent : SpellEyeCandyFlags.Tinting;
        if (cursor.Fault != NamedKeyFault.None) return SpellEyeCandyEntry.Empty;
        if (cursor.NamedInt("Light:", out _) is { } lightNumber)
        {
            light = lightNumber;
            color = cursor.NamedTriple("Light Color:", out _) is { } c ? ((byte)c.First, (byte)c.Second, (byte)c.Third) : ((byte)255, (byte)255, (byte)255);
        }
        if (cursor.Fault != NamedKeyFault.None) return SpellEyeCandyEntry.Empty;
        if (cursor.NamedInt("Proj Speed:", out _) is { } projectile) speed = projectile;
        return new(art, flags, sound, light, color, speed);
    }

    // An entry without Sound: takes its group's first entry's sound plus its offset (0x4CDF3B; the original then drops a sound file that
    // does not exist, not modelled here).
    private static int GroupSound(int index, SpellEyeCandyEntry[] entries, EyeCandyListShape shape)
    {
        // The original adds the offset only for a list that has sound offsets (0x4CDF3B tests their count) and for an entry that is not its group's first.
        var place = index % shape.PerGroup;
        if (shape.SoundOffsets.Count == 0 || place == 0) return -1;
        var first = entries[index - place].Sound;
        return first == -1 ? -1 : first + shape.SoundOffsets[place];
    }

    // The parts the flags name must each load (0x4CE1A0): the foreground (variant 0), the background (1), the underlay (2).
    [OriginalFunction(0x4CE1A0, OriginalFunctionScope.Full)]
    private static bool PartsLoad(uint aid, SpellEyeCandyFlags flags, Func<uint, bool> artExists) =>
        ((flags & SpellEyeCandyFlags.Foreground) == 0 || artExists(EyeCandyArt.WithVariant(aid, EyeCandyVariant.Foreground)))
        && ((flags & SpellEyeCandyFlags.Background) == 0 || artExists(EyeCandyArt.WithVariant(aid, EyeCandyVariant.Background)))
        && ((flags & SpellEyeCandyFlags.Underlay) == 0 || artExists(EyeCandyArt.WithVariant(aid, EyeCandyVariant.Underlay)));

    /// <summary>The <c>eye_candy.mes</c> names (number to name) as the area owner reads every art name table
    /// (<see cref="Arcanum.Data.Area.AreaObjectArt.ReadNameTable"/>, the draw's reader too); empty when the file is missing or does
    /// not parse.</summary>
    public static IReadOnlyDictionary<int, string> ReadNames(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        var found = stack.Resolve(EyeCandyArt.NamesPath);
        if (found.Winner is not { } winner || winner.Length > options.MaxBytes || stack.Read(winner) is not { Status: ContentReadStatus.Read, Bytes: { } bytes })
            return new Dictionary<int, string>();
        return Arcanum.Data.Area.AreaObjectArt.ReadNameTable(bytes) ?? new Dictionary<int, string>();
    }

    /// <summary>Whether the ART file of an eye-candy art id's variant is in the stack.</summary>
    public static bool ArtExists(ContentStack stack, IReadOnlyDictionary<int, string> names, uint aid)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(names);
        return EyeCandyArt.IsEyeCandy(aid) && names.TryGetValue(EyeCandyArt.Number(aid), out var name)
            && stack.Resolve(EyeCandyArt.FilePath(name, EyeCandyArt.Variant(aid))).Winner is not null;
    }

    private static SpellEyeCandyRead Refused(SpellDataRefusal refusal) => new(refusal.Status, null, refusal);
}
