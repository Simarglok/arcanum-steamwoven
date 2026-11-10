using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-315 (B36, B01; follows ARC-DEV-305): which prototype an item the session made stands on. An object the original makes is a copy of its
/// prototype, so a <c>made:n</c> item (made by the hero from a schematic, created by a script, added to an NPC) or one of those lying on the ground has no
/// object row in any map and every reader that looks an object row up by entity must read its prototype instead. This is the one place that answers
/// "is this entity a made item and which prototype is it": the session's saved kind (<see cref="SessionItemKind.Prototype"/>) of the hero's made items,
/// of the made and created items placed on the ground, and (when the holder is named) of the made items added to that NPC. Pure over the session's
/// queries; the readers (projectile art, thrown art, audio cues, the NPC armour read) pass it their prototype reader.
/// </summary>
public static class AreaMadeItems
{
    /// <summary>
    /// The saved kind of <paramref name="entity"/> when it is an item the session made: first the hero's made items, then the made and created
    /// items placed on the ground (<see cref="SessionPlacedSource.Made"/> and <see cref="SessionPlacedSource.Created"/>), then, when
    /// <paramref name="holder"/> is named, the made items added to that NPC. Null for anything else (a taken or kit item, an item with a row, an unknown
    /// entity), so a reader tries its row first and this second.
    /// </summary>
    public static SessionItemKind? Kind(GameSessionOwner session, string entity, string? holder = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!entity.StartsWith("made:", StringComparison.Ordinal)) return null;
        if (session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity) is { Entity: not null } made) return made.Kind;
        if (session.QueryPlacedItems().FirstOrDefault(item => item.Entity == entity && item.Source is SessionPlacedSource.Made or SessionPlacedSource.Created) is { } placed)
            return placed.Kind;
        if (string.IsNullOrEmpty(holder)) return null;
        try
        {
            return session.QueryNpcInventory(holder).FirstOrDefault(item => item.Entity == entity) is { Entity: not null } added ? added.Kind : null;
        }
        catch (ArgumentException)
        {
            return null; // a holder that is no valid entity id lists nothing
        }
    }

    /// <summary>The prototype file number of <paramref name="entity"/> when it is an item the session made (<see cref="Kind"/>), else null.</summary>
    public static int? Prototype(GameSessionOwner session, string entity, string? holder = null) => Kind(session, entity, holder)?.Prototype;

    /// <summary>The art of a made item from its prototype (<paramref name="prototypeArt"/> gives a prototype's current art by file number), or
    /// null when <paramref name="entity"/> is no made item or the prototype has no art.</summary>
    public static uint? Art(GameSessionOwner session, string entity, Func<int, uint?> prototypeArt, string? holder = null)
    {
        ArgumentNullException.ThrowIfNull(prototypeArt);
        return Prototype(session, entity, holder) is { } prototype ? prototypeArt(prototype) : null;
    }
}
