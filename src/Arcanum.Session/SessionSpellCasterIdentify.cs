using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-032 (MA-7 of the magic audit ARC-RES-103, decision D5(a); ledger XR1–XR5): the Identify component (0x4522A0) and Divine Magick's identified-flag line. The original's Identify
/// <b>writes nothing</b>: it opens a window or a description for the casting <b>local player</b> (the wrappers 0x4EE4C0, 0x4EE430, 0x4EE3A0 act only when the run's parent is the player; a
/// single-player game does nothing else, and so an NPC caster's Identify has no effect). The host builds the answer as a typed event (<see cref="SpellCastEventKind.IdentifyCritter"/>,
/// <see cref="SpellCastEventKind.IdentifyContainer"/>, <see cref="SpellCastEventKind.IdentifyItem"/>) that the viewer shows as a text line; the windows themselves (the read-only NPC sheet,
/// the container's inventory in mode 4, the item description window) are the identify-window slice E8's. The item's identification is the session's mark (<see cref="GameSessionOwner.MarkItemIdentified"/>).
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object field number of <c>obj_f_item_flags</c> (0x57) and its <c>OIF_IDENTIFIED</c> bit (0x1), the one Divine Magick's ObjFlag line sets (XR4).</summary>
    public const int ItemFlagsField = 87, IdentifiedItemFlag = 0x1;

    // The Identify component: taken whatever the target (it has no data: the positional word after "Type: Identify" is never read, SC11). The wrapper is chosen by the cached target's object
    // type (0x5E75AC): a PC or NPC goes to the character sheet (XR2), a container to the inventory window in mode 4 (XR3), any other type to the item description (XR4). Nothing happens without
    // a target, and nothing when the run's parent is not the local player: an NPC caster's Identify has no effect (XR1).
    [OriginalFunction(0x4522A0, OriginalFunctionScope.Partial)]
    private bool IdentifyComponent(SpellRun run, SpellRunTarget target)
    {
        if (target.Object is not { } obj || run.Parent?.Id != Hero) return true;
        var kind = obj.Type is SpellTargetObject.Pc or SpellTargetObject.Npc ? SpellCastEventKind.IdentifyCritter
            : obj.Type == SpellTargetObject.Container ? SpellCastEventKind.IdentifyContainer : SpellCastEventKind.IdentifyItem;
        _log.Add(new(kind, run.Id, obj.Type, obj.Id));
        return true;
    }

    // The ObjFlag component for the identified item flag (Divine Magick, spell 9, area Tgt_Obj_Inven): the plain write 0x454A10 gives an item flag (no side effect, no reference count, XR4,
    // XR5). The session keeps it as an identification mark. Taken only for a set of that one bit on an item; a clear, another bit or field, and a target that is no item are not ready.
    private bool IdentifiedItemFlagComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.ObjectFlag is not { Field: ItemFlagsField, Flag: IdentifiedItemFlag, On: true } || target.Object is not { Type: >= 5 and <= 14 } item) return false;
        var marked = _owner.MarkItemIdentified(_owner.Version, item.Id);
        _log.Add(marked.Disposition == GameSessionDisposition.Rejected
            ? new(SpellCastEventKind.ItemIdentifyNotSaved, run.Id, (int)marked.Failure, item.Id)
            : new(SpellCastEventKind.ItemIdentified, run.Id, IdentifiedItemFlag, item.Id));
        return true;
    }
}
