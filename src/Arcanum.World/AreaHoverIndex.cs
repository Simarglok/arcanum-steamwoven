using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.World;

/// <summary>
/// Hover picking and shown names for one published area map. The pick follows the original's screen target rule
/// (the source's 0x43D9F0): the frontmost drawn body with an opaque pixel under the pointer, skipping bodies flagged
/// <c>OF_CLICK_THROUGH</c>. Names come from <see cref="ObjectDisplayName"/>; walls and projectiles are picked but
/// show no text, as in the original's message window.
/// </summary>
public sealed class AreaHoverIndex
{
    /// <summary>Effective slot of <c>OBJ_F_FLAGS</c> and the <c>OF_CLICK_THROUGH</c> bit.</summary>
    public const int FlagsSlot = 18;
    public const uint ClickThroughFlag = 0x800;
    public const int WallType = 0, ProjectileType = 4, NpcType = 16;
    /// <summary>The English original's description tables are ASCII; the NPC conversation declares Windows-1252 too.</summary>
    public const MesTextProfile TextProfile = MesTextProfile.Windows1252;

    private readonly Dictionary<AreaObjectKey, AreaObjectRecord> _rows;
    private readonly Dictionary<(AreaObjectKey Key, CritterRecognition Recognition), string?> _names = [];
    // ARC-RENDER-3418: the hover picks at every moving frame, so the merged order is built in reused lists (one thread, and
    // the opacity test does not pick again).
    private readonly List<AreaDrawCommand> _drawn = [];
    private readonly HashSet<AreaObjectKey?> _movingKeys = [];

    /// <param name="objects">The map's object rows (<see cref="AreaSnapshot.Objects"/>).</param>
    /// <param name="commands">The map's plan commands in draw order (<see cref="AreaScenePlan.Commands"/>).</param>
    /// <param name="visitors">ARC-WORLD-017: rows of objects from another map that may stand here (companions); they are
    /// named and picked only where a moving command draws them, never from this map's plan.</param>
    public AreaHoverIndex(IEnumerable<AreaObjectRecord> objects, IReadOnlyList<AreaDrawCommand> commands, ObjectDescriptionCatalog? catalog,
        IEnumerable<AreaObjectRecord>? visitors = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(commands);
        _rows = objects.ToDictionary(row => row.Key);
        Catalog = catalog;
        // Plan order is draw order (the upright actor marker is inserted elsewhere), so the last hit is frontmost.
        Bodies = Array.AsReadOnly(commands.Where(command => command.Identity is { } key && _rows.TryGetValue(key, out var row) && !ClickThrough(row)).ToArray());
        foreach (var row in visitors ?? []) _rows.TryAdd(row.Key, row);
    }

    public ObjectDescriptionCatalog? Catalog { get; }
    /// <summary>The pickable drawn bodies in draw order.</summary>
    public IReadOnlyList<AreaDrawCommand> Bodies { get; }
    public AreaObjectRecord Row(AreaObjectKey key) => _rows[key];

    /// <summary>ARC-RENDER-033: a row the map's snapshot does not hold (a placed item without a row of this map), named and picked
    /// only where a moving command draws it, like a visitor.</summary>
    public void Admit(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows[row.Key] = row;
        _names.Remove((row.Key, CritterRecognition.Recognized)); _names.Remove((row.Key, CritterRecognition.NotRecognized));
    }

    /// <summary>ARC-DEV-172: whether the local hero recognizes an NPC row (the examine function's recognition test, 0x45F970:
    /// the NPC remembers the hero, "met before" 0x4C0C40, which is true outside the first conversation only). Null (the new-game
    /// state) shows every NPC as not recognized. Read at every <see cref="Name"/>, so a meeting changes the shown name at once.</summary>
    public Func<AreaObjectRecord, CritterRecognition>? Recognition { get; set; }
    public bool Contains(AreaObjectKey key) => _rows.ContainsKey(key);

    public static bool ClickThrough(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var flags = row.EffectiveFields.SingleOrDefault(field => field.Slot == FlagsSlot && field.Presence == AreaPresence.Value)?.Number;
        return flags is { } value && (unchecked((uint)value) & ClickThroughFlag) != 0;
    }

    /// <summary>The frontmost pickable body for which <paramref name="opaqueAt"/> reports a drawn opaque pixel.</summary>
    public AreaObjectKey? Pick(Func<AreaDrawCommand, bool> opaqueAt)
    {
        ArgumentNullException.ThrowIfNull(opaqueAt);
        for (var index = Bodies.Count - 1; index >= 0; index--)
            if (opaqueAt(Bodies[index])) return Bodies[index].Identity;
        return null;
    }

    /// <summary>ARC-WORLD-016: the same pick over the bodies as drawn, with <paramref name="moving"/> (companions where
    /// they stand) in place of their T0 commands, each at the place plan order gives it.</summary>
    public AreaObjectKey? Pick(Func<AreaDrawCommand, bool> opaqueAt, IReadOnlyList<AreaDrawCommand>? moving)
    {
        if (moving is null || moving.Count == 0) return Pick(opaqueAt);
        ArgumentNullException.ThrowIfNull(opaqueAt);
        _movingKeys.Clear(); _drawn.Clear();
        foreach (var command in moving) _movingKeys.Add(command.Identity);
        foreach (var body in Bodies) if (!_movingKeys.Contains(body.Identity)) _drawn.Add(body);
        foreach (var command in moving)
        {
            if (command.Identity is not { } key || !_rows.TryGetValue(key, out var row) || ClickThrough(row)) continue;
            // After the last body drawn before it.
            var at = _drawn.Count;
            while (at > 0 && !(_drawn[at - 1].Phase < command.Phase
                || (_drawn[at - 1].Phase == command.Phase && AreaScenePlan.CompareUpright(_drawn[at - 1], command) <= 0))) at--;
            _drawn.Insert(at, command);
        }
        for (var index = _drawn.Count - 1; index >= 0; index--)
            if (opaqueAt(_drawn[index])) return _drawn[index].Identity;
        return null;
    }

    /// <summary>The text shown for a hovered object, or null when the original shows none (walls, projectiles, an
    /// empty description). Without a catalog the object type is named instead. An NPC the hero has not met is shown by its
    /// not-yet-met description, one it has met by its own (<see cref="Recognition"/>, ARC-DEV-172).</summary>
    public string? Name(AreaObjectKey key)
    {
        var row = _rows[key];
        var recognition = row.Type == NpcType && Recognition is { } recognize ? recognize(row) : CritterRecognition.NotRecognized;
        if (_names.TryGetValue((key, recognition), out var cached)) return cached;
        var text = Text(row, Catalog, recognition);
        _names.Add((key, recognition), text);
        return text;
    }

    /// <summary>The hero's recognition of an NPC row from the session (ARC-DEV-172): recognized when the NPC's reaction record
    /// holds a remembered slot for the hero (<c>SessionReaction.MetBefore</c>); any other row is not recognized (the examine
    /// function reads recognition for NPCs only).</summary>
    /// <param name="record">The session's reaction record of an entity id, or null when it has none.</param>
    public static CritterRecognition RecognitionFrom(AreaObjectRecord row, Func<string, SessionReaction?> record)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(record);
        return row.Type == NpcType && record(AreaConversationState.EntityId(row)) is { MetBefore: true }
            ? CritterRecognition.Recognized : CritterRecognition.NotRecognized;
    }

    /// <summary>The shown text of one object row (see <see cref="Name"/>), uncached; the inventory window (ARC-UI-004)
    /// names items by the same rule.</summary>
    public static string? Text(AreaObjectRecord row, ObjectDescriptionCatalog? catalog, CritterRecognition recognition = CritterRecognition.NotRecognized)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Type is WallType or ProjectileType) return null;
        if (catalog is null) return $"[{TypeName(row.Type)}: name unavailable]";
        var resolution = ObjectDisplayName.Resolve(row, catalog, recognition);
        return resolution.Status switch
        {
            ObjectNameStatus.Resolved => MesText.Decode(resolution.Message!, TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } decoded }
                ? decoded.Length == 0 ? null : decoded : $"[{TypeName(row.Type)}: name not decodable]",
            ObjectNameStatus.NoText => null,
            _ => $"[{TypeName(row.Type)}: {resolution.Status}]",
        };
    }

    public static string TypeName(int type) => type switch
    {
        0 => "wall", 1 => "portal", 2 => "container", 3 => "scenery", 4 => "projectile", 5 => "weapon", 6 => "ammo",
        7 => "armor", 8 => "gold", 9 => "food", 10 => "scroll", 11 => "key", 12 => "key ring", 13 => "written",
        14 => "generic item", 15 => "player character", 16 => "NPC", 17 => "trap", _ => "object",
    };
}
