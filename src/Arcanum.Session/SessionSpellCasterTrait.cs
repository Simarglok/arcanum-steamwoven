using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-040 (magic audit MA-8, first slice; the executable read TR1–TR6, ledger spell-runs: 0x452AD0, 0x452B00, 0x452CD0, with the art helpers 0x502700 to 0x503E50 and the item helpers
/// 0x464C50, 0x464C80, 0x4654F0): the Trait component (type 10, 0x452AD0 → 0x452B00) of art type (<c>art_num</c>, field 1) in Body of Air (13), Body of Stone (18), Body of Fire (23), Body of Water (28)
/// and Polymorph (49). The original acts on a PC or an NPC only (a run whose current target is another object, or none, does nothing); the component's value is the species, and **−1 restores**:
/// <list type="bullet">
/// <item>the **apply** takes every worn item off (the nine slots 1000 to 1008, each unwielded to the inventory, an item that stays is dropped on the holder's tile), builds the monster body (art class 12
/// with the species, the old art's rotation and animation, the weapon-part bit and the component's <c>Palette:</c>; a species of 32 or more or a palette of 4 or more aborts the original, here the component
/// is not ready) and sets it as the current art; for the species 26 to 28 (fire, water and air) it then **assigns** the critter flags 2 and the blit flags a single bit each (MG10). The session keeps the choice
/// (<see cref="GameSessionOwner.WriteTraitArt"/>, schema 89); the body, the unwield and the elemental flags are the host's, told by the <see cref="SpellCastEventKind.TraitApplied"/> line;</item>
/// <item>the **restore** takes the elemental bits off when the current body is an elemental one, builds the art again from the stored art (field 24) with the current rotation and animation at frame 0
/// and chooses the best items for every slot again (0x4654F0). The session removes its record; the rest is the host's, told by <see cref="SpellCastEventKind.TraitRestored"/>.</item>
/// </list>
/// There is no reference count: a second apply puts its body over the first, and the first END's restore takes the body off while a second run still holds it. ARC-MAGIC-047: the critter flags 2 and the blit
/// flags of the elemental bodies are the session's (<see cref="GameSessionOwner.AssignObjectFlag"/>, schema 90; MG10's switch is <see cref="MagicAnomalyProfile.ElementalFlagsAssigned"/>). Not modelled: the other
/// Trait fields (any field but <c>art_num</c> is not ready), and the restore's test of the **current** art for the elemental bits when the critter has no record of the session's (a map NPC whose data body is an
/// elemental one keeps the bits).
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The Trait component's data field of the current art (<c>art_num</c>: the first of the test fields) and the value that restores.</summary>
    public const int ArtNumberField = 1, TraitRestoreValue = -1;

    /// <summary>The detail bits of a <see cref="SpellCastEventKind.TraitApplied"/> or <see cref="SpellCastEventKind.TraitRestored"/> line: for an apply the palette is in the low two bits;
    /// <see cref="TraitElemental"/> says the body is (an apply) or was (a restore) one of the three elemental ones (MG10's flags), <see cref="TraitStateChanged"/> that the session's record changed.</summary>
    public const int TraitPaletteMask = 3, TraitElemental = 4, TraitStateChanged = 8;

    // The Trait component on its target object (0x452AD0 → 0x452B00): the data field and the target's type, then the restore (value −1) or the apply.
    private SpellComponentOutcome? TraitComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Trait is not { } data) return null;
        // No current target object (0x5E6D48 and 0x5E6D4C both 0): the component does nothing, and it is not "not ready".
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        // Another field of the original's Trait data is another component this slice does not model.
        if (data.Field != ArtNumberField) return null;
        // A PC or an NPC only (the cached target type 0xF or 0x10); any other object type does nothing.
        if (!obj.IsCritter) return SpellComponentOutcome.None;
        var id = obj.Id;
        // A critter the session holds no body for and this host does not know is not ready (the hero, a critter on the map or one the session created is known).
        var old = _owner.QueryTraitArt(id);
        if (old is null && id != Hero && !CritterKnown(id) && SessionCreatedObject.KindOf(id) is not SessionCreatedKind.Critter) return null;
        return data.Value == TraitRestoreValue ? TraitRestore(run, id, old) : TraitApply(run, id, new SessionTraitArt(data.Value, data.Palette));
    }

    // 0x452B00, the apply branch: a species or palette the art id cannot hold aborts the original (the builder's range tests); it is not ready here.
    [OriginalFunction(0x452B00, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? TraitApply(SpellRun run, string id, SessionTraitArt art)
    {
        if (!art.IsValid) return null;
        var written = _owner.WriteTraitArt(_owner.Version, id, art);
        if (written.Disposition == GameSessionDisposition.Rejected) return null;
        // TR5 (MG10): the elemental bodies write the critter flags 2 and the blit flags (assigned in the original's mode, turned on in Steamwoven's).
        if (art.IsElemental) ElementalFlags(run, id, on: true);
        _log.Add(new(SpellCastEventKind.TraitApplied, run.Id, art.Species, id,
            art.Palette | (art.IsElemental ? TraitElemental : 0) | (written.Disposition == GameSessionDisposition.Changed ? TraitStateChanged : 0)));
        return SpellComponentOutcome.None;
    }

    // TR5 and TR6 (0x452B00, 0x452CD0): the critter flags 2 and the blit flags of an elemental body. With MG10's Original profile the apply assigns the two words (every other bit is lost); Steamwoven turns the
    // bits on and keeps the rest. The restore clears the two bits in both modes (a wiped word stays wiped: the original does not give the other bits back).
    private void ElementalFlags(SpellRun run, string id, bool on)
    {
        if (on && Profile.ElementalFlagsAssigned)
        {
            _owner.AssignObjectFlag(_owner.Version, id, SessionObjectFlagField.Critter2, ElementalCritterFlags2);
            _owner.AssignObjectFlag(_owner.Version, id, SessionObjectFlagField.Blit, ElementalBlitFlag);
            return;
        }
        _owner.WriteObjectFlag(_owner.Version, id, SessionObjectFlagField.Critter2, ElementalCritterFlags2, on, DataHas(id, SessionObjectFlagField.Critter2, ElementalCritterFlags2));
        _owner.WriteObjectFlag(_owner.Version, id, SessionObjectFlagField.Blit, ElementalBlitFlag, on, DataHas(id, SessionObjectFlagField.Blit, ElementalBlitFlag));
    }

    // 0x452CD0, the restore: the original rebuilds the art from the stored one even for a critter that has none of the spell's making.
    [OriginalFunction(0x452CD0, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? TraitRestore(SpellRun run, string id, SessionTraitArt? old)
    {
        var restored = _owner.WriteTraitArt(_owner.Version, id, null);
        if (restored.Disposition == GameSessionDisposition.Rejected) return null;
        // TR6: the restore clears only the two elemental bits, and only for a body that is an elemental one (the original tests the current art; the session knows its own record).
        if (old is { IsElemental: true }) ElementalFlags(run, id, on: false);
        _log.Add(new(SpellCastEventKind.TraitRestored, run.Id, old?.Species ?? TraitRestoreValue, id,
            (old is { IsElemental: true } ? TraitElemental : 0) | (restored.Disposition == GameSessionDisposition.Changed ? TraitStateChanged : 0)));
        return SpellComponentOutcome.None;
    }
}
