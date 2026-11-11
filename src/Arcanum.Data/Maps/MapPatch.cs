using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Maps;

public enum MapPatchIssueCode
{
    /// <summary>A line whose object ID does not parse; the original logs it and goes on.</summary>
    MalformedObjectId,
    /// <summary>An ID of another kind than a GUID (<c>A</c>, <c>B</c>, <c>N</c>, <c>P</c>); its lookup is not modelled.</summary>
    UnsupportedObjectId,
    /// <summary>A byte the file reader treats differently by source (NUL, a lone CR, 0x1A); the file is not modelled.</summary>
    UnsupportedByte,
    /// <summary>A read of 4,096 characters or more: the original's line buffer is 4,096 bytes but it reads up to 4,195
    /// characters into it, so such a line overruns its stack; the rest of the file is not modelled.</summary>
    LineOverflowsBuffer,
}

/// <param name="Line">1-based line number.</param>
public sealed record MapPatchIssue(MapPatchIssueCode Code, int Line);

/// <summary>One line naming an object by GUID: the attribute text after the line's first <c>:</c>, or null when it has none
/// (the original then dereferences a null pointer once the object is found).</summary>
public sealed record MapPatchLine(int Line, Guid ObjectId, string? Attributes);

/// <summary>
/// ARC-DATA-028: <c>Rules\map.patch</c> as the original reads it (checked in the 1.0.7.4 executable, ledger "Map patch").
/// The game reads the file each time a map opens, after the map's mobile objects are loaded and before any sector, and
/// changes the loaded objects each line names; so only mobile objects of the opening map (and objects carried into it) can
/// be changed, never sector objects. Lines are read in text mode, up to 4,195 characters into a 4,096-byte buffer (a longer
/// read overruns it: not modelled); a line starting with <c>#</c> or empty is skipped, and the line's last character (its
/// newline, or the last character of a final line without one) is dropped. A <c>G_</c> GUID is parsed in the Windows memory layout, which is how the object
/// files store it. Parsed here once; <see cref="MapPatchApplier"/> applies the attributes.
/// </summary>
public sealed class MapPatchFile
{
    /// <summary>The size the original passes to <c>fgets</c>, which reads at most one byte less.</summary>
    public const int OriginalLineBuffer = 0x1064;
    /// <summary>The size of the buffer it reads into.</summary>
    public const int OriginalLineStorage = 4096;
    public const int MaxBytes = 1 << 20;
    private readonly IReadOnlyDictionary<Guid, IReadOnlyList<MapPatchLine>> _byObject;

    private MapPatchFile(IReadOnlyList<MapPatchLine> lines, IReadOnlyList<MapPatchIssue> issues)
    {
        Lines = lines; Issues = issues;
        _byObject = new ReadOnlyDictionary<Guid, IReadOnlyList<MapPatchLine>>(lines.GroupBy(l => l.ObjectId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MapPatchLine>)Array.AsReadOnly(g.ToArray())));
    }

    /// <summary>The GUID lines in file order.</summary>
    public IReadOnlyList<MapPatchLine> Lines { get; }
    public IReadOnlyList<MapPatchIssue> Issues { get; }
    /// <summary>True when a line or byte makes the file's effect unknown: an ID kind other than a GUID, or a byte the
    /// reader treats by source. Consumers then cannot claim the patch is modelled.</summary>
    public bool HasUnmodelledLines => Issues.Any(i => i.Code is MapPatchIssueCode.UnsupportedObjectId or MapPatchIssueCode.UnsupportedByte or MapPatchIssueCode.LineOverflowsBuffer);

    /// <summary>The lines naming <paramref name="objectId"/>, in file order (the original applies each in turn).</summary>
    public IReadOnlyList<MapPatchLine> For(Guid objectId) => _byObject.TryGetValue(objectId, out var lines) ? lines : [];

    public static MapPatchFile Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes) throw new ArgumentOutOfRangeException(nameof(bytes), "map.patch exceeds 1 MiB.");
        var lines = new List<MapPatchLine>(); var issues = new List<MapPatchIssue>();
        // Text mode reads CR LF as LF; a lone CR, a NUL or 0x1A is read differently by archive and loose file.
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] is 0 or 0x1A || bytes[i] == '\r' && (i + 1 == bytes.Length || bytes[i + 1] != '\n'))
                return new([], [new(MapPatchIssueCode.UnsupportedByte, 1 + bytes[..i].Count((byte)'\n'))]);
        var text = Encoding.Latin1.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        var at = 0; var number = 0;
        while (at < text.Length)
        {
            number++;
            // fgets: up to the buffer less one, stopping after a newline.
            var newline = text.IndexOf('\n', at);
            var take = Math.Min(newline < 0 ? text.Length - at : newline - at + 1, OriginalLineBuffer - 1);
            var line = text.Substring(at, take); at += take;
            if (take >= OriginalLineStorage) { issues.Add(new(MapPatchIssueCode.LineOverflowsBuffer, number)); break; }
            if (line[0] is '#' or '\n') continue;
            // (A line without a newline before the end would stop the file as too long, but only a read of the full
            // buffer can lack one, and that read has already overrun the storage above.)
            line = line[..^1];
            // A one-character final line is empty after the cut: no ID kind, a malformed line to the original.
            if (line.Length == 0) { issues.Add(new(MapPatchIssueCode.MalformedObjectId, number)); continue; }
            switch (line[0])
            {
                case 'G':
                    if (TryGuid(line, out var guid)) lines.Add(new(number, guid, line.IndexOf(':') is var colon and >= 0 ? line[(colon + 1)..] : null));
                    else issues.Add(new(MapPatchIssueCode.MalformedObjectId, number));
                    break;
                case 'A' or 'B' or 'N' or 'P': issues.Add(new(MapPatchIssueCode.UnsupportedObjectId, number)); break;
                default: issues.Add(new(MapPatchIssueCode.MalformedObjectId, number)); break;
            }
        }
        return new(lines.AsReadOnly(), issues.AsReadOnly());
    }

    // G_XXXXXXXX_XXXX_XXXX_XXXX_XXXXXXXXXXXX: the 8- and 4-digit groups are each one unit and the last two groups
    // two-digit units, each read by strtoul in base 16 over exactly its width after a check of its first character only,
    // so "0x" may open an 8- or 4-character unit (a two-character unit cannot hold it); the first three groups are stored
    // as a 32-bit and two 16-bit numbers (Windows memory layout), the rest byte by byte. Text after the GUID is not checked.
    private static bool TryGuid(string line, out Guid guid)
    {
        guid = default;
        int[] groups = [8, 4, 4, 4, 12];
        var bytes = new byte[16]; var at = 2; var written = 0;
        if (line.Length < 2 || line[1] != '_') return false;
        for (var g = 0; g < groups.Length; g++)
        {
            if (g > 0) { if (at >= line.Length || line[at] != '_') return false; at++; }
            if (at + groups[g] > line.Length || !HexGroup(line.Substring(at, groups[g]), prefixed: g <= 2, out var hexText)) return false;
            var hex = hexText.AsSpan(); at += groups[g];
            if (g == 0) { BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)); written = 4; }
            else if (g is 1 or 2) { BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(written), ushort.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)); written += 2; }
            else for (var b = 0; b < hex.Length; b += 2) bytes[written++] = byte.Parse(hex.Slice(b, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        guid = new Guid(bytes);
        return true;
    }

    private static bool HexGroup(string group, bool prefixed, out string digits)
    {
        digits = group;
        if (!char.IsAsciiHexDigit(group[0])) return false;
        if (prefixed && group[0] == '0' && group[1] is 'x' or 'X') digits = group[2..].PadLeft(group.Length, '0');
        return digits.All(char.IsAsciiHexDigit);
    }

    /// <summary>The script numbers the <c>script</c> attributes name (the plan resolves their headers).</summary>
    internal IReadOnlyCollection<int> ScriptNumbers()
    {
        var numbers = new SortedSet<int>();
        foreach (var line in Lines)
        {
            var tokens = (line.Attributes ?? "").Split((char[])[' ', '\t', '\n', '\v', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 2 < tokens.Length; i++)
                if (string.Equals(tokens[i], "script", StringComparison.OrdinalIgnoreCase) && WorldCatalog.StrToLong(tokens[i + 2]) is var n and > 0 and <= int.MaxValue)
                    numbers.Add((int)n);
        }
        return numbers;
    }

    /// <summary>The 16 GUID bytes of a permanent object ID as the wire reader keeps them.</summary>
    internal static Guid ObjectGuid(ObjectEntityId entity)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, entity.First);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), entity.Second);
        return new Guid(bytes);
    }
}

/// <summary>The 8-byte header the original copies from the start of a script file into an attachment (flags, counters).</summary>
public readonly record struct MapPatchScriptHeader(uint Flags, uint Counters);

/// <summary>What the patch did to one object: the lines applied and the changes not modelled (the object's patched state is
/// then not fully known).</summary>
public sealed record MapPatchApplication(IReadOnlyList<int> Lines, IReadOnlyList<string> Unmodelled);

/// <summary>
/// ARC-DATA-028: applies <see cref="MapPatchLine"/> attributes to one object's wire fields as the original's
/// <c>map_apply_obj_patch</c> (0x411940) does: keys compared ignoring ASCII case, values are the next whitespace-separated
/// token (absent reads 0), an unknown key stops the line. A field the object's type lacks is not written. Writing one element
/// of an array the instance does not hold first copies the prototype's whole array. <c>training</c> (sequential tiers gated
/// by the computed skill level) and a matched line without <c>:</c> (a crash in the original) are not modelled.
/// </summary>
internal static class MapPatchApplier
{
    private const int NameSlot = 21, DescriptionSlot = 22, FlagsSlot = 18, ScriptsSlot = 31;
    private const int PortalType = 1, ContainerType = 2, WeaponType = 5, PcType = 15, NpcType = 16;
    private const int AlignmentStat = 19, AlignmentMin = -1000, AlignmentMax = 1000;

    // Flag names in bit order, from the executable's tables (OF 0x5BA10C, OPF 0x5BA218, OIF 0x5BA284, OCF 0x5BA348,
    // ONF 0x5BA44C).
    private static readonly string[] ObjectFlags = Names("DESTROYED OFF FLAT TEXT SEE_THROUGH SHOOT_THROUGH TRANSLUCENT SHRUNK DONTDRAW INVISIBLE NO_BLOCK CLICK_THROUGH INVENTORY DYNAMIC PROVIDES_COVER HAS_OVERLAYS HAS_UNDERLAYS WADING WATER_WALKING STONED DONTLIGHT TEXT_FLOATER INVULNERABLE EXTINCT TRAP_PC TRAP_SPOTTED DISALLOW_WADING MULTIPLAYER_LOCK FROZEN ANIMATED_DEAD TELEPORTED", "OF_");
    private static readonly string[] PortalFlags = Names("LOCKED JAMMED MAGICALLY_HELD NEVER_LOCKED ALWAYS_LOCKED LOCKED_DAY LOCKED_NIGHT BUSTED STICKY", "OPF_");
    private static readonly string[] ItemFlags = Names("IDENTIFIED WONT_SELL IS_MAGICAL TRANSFER_LIGHT NO_DISPLAY NO_DROP HEXED CAN_USE_BOX NEEDS_TARGET LIGHT_SMALL LIGHT_MEDIUM LIGHT_LARGE LIGHT_XLARGE PERSISTENT MT_TRIGGERED STOLEN USE_IS_THROW NO_DECAY UBER NO_NPC_PICKUP NO_RANGED_USE VALID_AI_ACTION MP_INSERTED", "OIF_");
    private static readonly string[] CritterFlags = Names("IS_CONCEALED MOVING_SILENTLY UNDEAD ANIMAL FLEEING STUNNED PARALYZED BLINDED CRIPPLED_ARMS_ONE CRIPPLED_ARMS_BOTH CRIPPLED_LEGS_BOTH UNUSED SLEEPING MUTE SURRENDERED MONSTER SPELL_FLEE ENCOUNTER COMBAT_MODE_ACTIVE LIGHT_SMALL LIGHT_MEDIUM LIGHT_LARGE LIGHT_XLARGE UNREVIVIFIABLE UNRESSURECTABLE DEMON FATIGUE_IMMUNE NO_FLEE NON_LETHAL_COMBAT MECHANICAL ANIMAL_ENSHROUD FATIGUE_LIMITING", "OCF_");
    private static readonly string[] NpcFlags = Names("FIGHTING WAYPOINTS_DAY WAYPOINTS_NIGHT AI_WAIT_HERE AI_SPREAD_OUT JILTED CHECK_WIELD CHECK_WEAPON KOS WAYPOINTS_BED FORCED_FOLLOWER KOS_OVERRIDE WANDERS WANDERS_IN_DARK FENCE FAMILIAR CHECK_LEADER ALOOF CAST_HIGHEST GENERATOR GENERATED GENERATOR_RATE1 GENERATOR_RATE2 GENERATOR_RATE3 DEMAINTAIN_SPELLS LOOK_FOR_WEAPON LOOK_FOR_ARMOR LOOK_FOR_AMMO BACKING_OFF NO_ATTACK", "ONF_");

    private static readonly string[] TrainingTiers = ["st_untrained", "st_beginner", "st_expert", "st_master"];

    // A value written as zero: an optional sign and one or more zero digits.
    private static bool LiteralZero(string value)
    {
        var digits = value.AsSpan(value.Length > 0 && value[0] is '+' or '-' ? 1 : 0);
        return digits.Length > 0 && !digits.ContainsAnyExcept('0');
    }

    private static string[] Names(string list, string prefix) => list.Split(' ').Select(n => prefix + n).ToArray();

    internal sealed record Result(ObjectWireRecord Record, MapPatchApplication Application);

    /// <param name="scriptZeroClears">ARC-DATA-037 (ledger MP7): <c>script N 0</c> clears attachment N (an empty attachment: no
    /// flags, no counters, script 0) instead of attaching nothing as the original does.</param>
    [OriginalFunction(0x411940, OriginalFunctionScope.Partial)]
    internal static Result Apply(ObjectWireRecord record, ObjectWireRecord prototype, IReadOnlyList<MapPatchLine> lines, Func<int, MapPatchScriptHeader?> scriptHeader,
        bool scriptZeroClears = false)
    {
        ArgumentNullException.ThrowIfNull(record); ArgumentNullException.ThrowIfNull(prototype); ArgumentNullException.ThrowIfNull(lines); ArgumentNullException.ThrowIfNull(scriptHeader);
        var fields = new SortedDictionary<int, ObjectWireValue>(record.Fields.ToDictionary(p => p.Key, p => p.Value));
        var unmodelled = new List<string>();
        var type = record.Type;
        bool Critter() => type is PcType or NpcType;
        bool Item() => type is >= 5 and <= 14;
        ObjectWireValue? Effective(int slot) => fields.TryGetValue(slot, out var own) ? own : prototype.Fields.GetValueOrDefault(slot);
        int Int(int slot) => Effective(slot)?.Number is { } n ? unchecked((int)n) : 0;
        void SetInt(int slot, int value) => fields[slot] = new(ObjectWirePresence.Value, value, []);
        void SetElement(int slot, int key, IReadOnlyList<long> words)
        {
            // Copy-on-write: the instance's own array, else the prototype's whole array.
            var source = fields.TryGetValue(slot, out var own) ? own : prototype.Fields.GetValueOrDefault(slot);
            var elements = (source?.Elements ?? []).Where(e => e.Key != key).Append(new ObjectWireElement(key, words)).OrderBy(e => e.Key).ToArray();
            fields[slot] = new(ObjectWirePresence.Array, null, Array.AsReadOnly(elements));
        }

        var applied = new List<int>();
        foreach (var line in lines)
        {
            if (line.Attributes is not { } text) { unmodelled.Add($"line {line.Line}: no ':' (the original fails here)"); continue; }
            applied.Add(line.Line);
            var tokens = new Tokens(text);
            while (tokens.Next() is { } key)
            {
                string Value() => tokens.Next() ?? "";
                // atoi: a value outside int32 has no defined result in the original.
                int Atoi(string value)
                {
                    var parsed = StrToLong(value);
                    if (parsed is < int.MinValue or > int.MaxValue) unmodelled.Add($"line {line.Line}: {key} value outside int32");
                    return unchecked((int)parsed);
                }
                switch (key.ToUpperInvariant())
                {
                    case "INTERNAL_NAME": SetInt(NameSlot, Atoi(Value())); break;
                    case "KNOWN_NAME": SetInt(DescriptionSlot, Atoi(Value())); break;
                    case "ALIGNMENT":
                        var alignment = Atoi(Value());
                        if (Critter()) SetElement(38, AlignmentStat, [unchecked((uint)Math.Clamp(alignment, AlignmentMin, AlignmentMax))]);
                        // A player character's alignment change also makes its followers reconsider: not modelled.
                        if (type == PcType) unmodelled.Add($"line {line.Line}: player alignment");
                        break;
                    case "ORIGIN": { var v = Atoi(Value()); if (type == NpcType) SetInt(80, v); break; }
                    case "NOTIFY_NPC":
                        {
                            var v = Atoi(Value());
                            if (type == PortalType) SetInt(39, v); else if (type == ContainerType) SetInt(42, v);
                            break;
                        }
                    case "MAGIC_HIT_ADJUST": { var v = Atoi(Value()); if (type == WeaponType) SetInt(62, v); break; }
                    case "OBJ_FLAG": Flag(FlagsSlot, true, ObjectFlags); break;
                    case "PORTAL_FLAG": Flag(36, type == PortalType, PortalFlags); break;
                    case "ITEM_FLAG": Flag(36, Item(), ItemFlags); break;
                    case "CRITTER_FLAG": Flag(36, Critter(), CritterFlags); break;
                    case "NPC_FLAG":
                        {
                            // The executable's table ends in a null entry, so a name it does not hold crashes the comparison.
                            var known = Flag(69, type == NpcType, NpcFlags);
                            if (!known) unmodelled.Add($"line {line.Line}: unknown npc flag (the original fails here)");
                            break;
                        }
                    case "TRAINING":
                        // A tier word takes the skill name after it; any other word leaves that token to be read as a key.
                        if (TrainingTiers.Contains(Value(), StringComparer.OrdinalIgnoreCase))
                        {
                            Value();
                            if (Critter()) unmodelled.Add($"line {line.Line}: training");
                        }
                        break;
                    case "SCRIPT":
                        {
                            var sap = Atoi(Value()); var numberText = Value(); var number = Atoi(numberText);
                            if (number == 0)
                            {
                                // MP7: only a written zero clears (an absent or non-numeric value attaches nothing in both modes).
                                if (scriptZeroClears && LiteralZero(numberText))
                                {
                                    if (sap < 0) unmodelled.Add($"line {line.Line}: script attachment {sap}");
                                    else SetElement(ScriptsSlot, sap, [0, 0, 0]);
                                }
                            }
                            else if (scriptHeader(number) is { } header)
                            {
                                if (sap < 0) unmodelled.Add($"line {line.Line}: script attachment {sap}");
                                else SetElement(ScriptsSlot, sap, [header.Flags, header.Counters, unchecked((uint)number)]);
                            }
                            break;
                        }
                    case "DAYSTAND": Stand(78); break;
                    case "NIGHTSTAND": Stand(79); break;
                    case "MAGIC_TECH_COMPLEXITY": { var v = Atoi(Value()); if (Item()) SetInt(45, v); break; }
                    case "LEVEL_SCHEME": { var v = Atoi(Value()); if (Critter()) SetInt(63, v); break; }
                    case "FACTION": { var v = Atoi(Value()); if (type == NpcType) SetInt(81, v); break; }
                    default: goto EndOfLine;
                }

                bool Flag(int slot, bool valid, string[] names)
                {
                    var flags = valid ? Int(slot) : 0;
                    var value = Value();
                    var clear = value.StartsWith('!');
                    var index = Array.FindIndex(names, n => string.Equals(n, clear ? value[1..] : value, StringComparison.OrdinalIgnoreCase));
                    if (index >= 0) flags = clear ? flags & ~(1 << index) : flags | (1 << index);
                    if (valid) SetInt(slot, flags);
                    return index >= 0;
                }
                void Stand(int slot)
                {
                    // _atoi64 wraps where this reader saturates: a value at either 64-bit end is not modelled.
                    var x = StrToLong(Value()); var y = StrToLong(Value());
                    if (x is long.MaxValue or long.MinValue || y is long.MaxValue or long.MinValue) unmodelled.Add($"line {line.Line}: {key} value outside int64");
                    if (type == NpcType) fields[slot] = new(ObjectWirePresence.Value, unchecked(x | (y << 32)), []);
                }
            }
            EndOfLine:;
        }
        var patched = record with { Fields = new ReadOnlyDictionary<int, ObjectWireValue>(fields) };
        return new(patched, new(applied.AsReadOnly(), unmodelled.AsReadOnly()));
    }

    private static long StrToLong(string text) => WorldCatalog.StrToLong(text);

    /// <summary>The original's token reader: skips C white space, then takes the run up to the next white space.</summary>
    private sealed class Tokens(string text)
    {
        private int _at;
        [OriginalFunction(0x411880, OriginalFunctionScope.Partial)]
        public string? Next()
        {
            while (_at < text.Length && IsSpace(text[_at])) _at++;
            if (_at == text.Length) return null;
            var start = _at;
            while (_at < text.Length && !IsSpace(text[_at])) _at++;
            return text[start.._at];
        }
        private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
    }
}
