using Arcanum.Data.Combat;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-088: the character sheet of the dialog's NPC (the reply action c-e, ledger WN1, WN2, CHW24). The talk ends, the dialog text clears and the
// sheet opens for the NPC in the original's mode 2: the same window as the hero's C sheet over the NPC's values, with no + or − button, no editor
// snapshot, the next spell grey, no pause. The sheet closes as the hero's does (C, a release in the close rectangle); the dialog does not resume.
public partial class TerrainViewerApp
{
    private (GameSessionOwner Session, AreaConversationHost Host, string Entity, int Speaker)? _npcSheetPending;
    private AreaNpcSheetPages? _sheetNpc;
    private string? _sheetNpcStatus;

    /// <summary>A c-e reply ended the conversation (<see cref="Arcanum.Scripting.Dialog.DialogEndKind.NpcSheetWindow"/>): the sheet opens for the speaker once nothing else owns input.</summary>
    private void QueueDialogNpcSheet(AreaConversationHost host, (string Entity, int Speaker) speaker)
    {
        GD.Print($"ARC-UI-088 dialog npc sheet speaker={speaker.Entity}");
        _npcSheetPending = (_areaSession!, host, speaker.Entity, speaker.Speaker);
    }

    private void TakeDialogNpcSheet()
    {
        if (_npcSheetPending is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession) || !SheetActive) { _npcSheetPending = null; return; }
        if (WorldModal) return;
        _npcSheetPending = null;
        OpenNpcSheet(pending.Host, pending.Entity, pending.Speaker);
    }

    /// <summary>The NPC's sheet pages from the speaker's stored fields (null with the reason in <see cref="_sheetNpcStatus"/>).</summary>
    private AreaNpcSheetPages? BuildNpcSheet(AreaConversationHost host, string entity, int speaker)
    {
        AreaNpcSheetPages? Refuse(string why) { _sheetNpcStatus = why; GD.Print($"ARC-UI-088 npc sheet refused: {why}"); return null; }
        var session = _areaSession!;
        if (speaker < 0 || speaker >= host.Speakers.Count || CombatFieldReader.Read(host.Speakers[speaker].Row).Fields is not { } fields) return Refuse("no data of the speaker");
        if (_areaEffectRules is not { } effects) return Refuse("no effect rules (start with --hero)");
        if (AreaAdvancement.Npc(entity, fields, effects) is not { } loaded) return Refuse("the speaker gives no admissible sheet");
        AdoptSkillTexts();
        var name = AreaHoverIndex.Text(host.Speakers[speaker].Row, _hoverNames?.Catalog) ?? entity;
        var life = _combatHost is { } combat ? AreaLife.Of(session, combat, entity) : null;
        return AreaNpcSheet.Build(session, name, fields, loaded, life, _skillTexts?.Texts, AreaHoverIndex.TextProfile) ?? Refuse("the speaker is not a critter");
    }

    private AreaSpellsPageView? NpcSpells() => _sheetNpc?.Spells(_spellData?.Data, _sheetCollege, _areaSession!.AutoLevel?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower);

    private void OpenNpcSheet(AreaConversationHost host, string entity, int speaker)
    {
        if (!SheetActive || !WindowCanOpen(InterfaceMode.Character)) return;
        // The original's open refuses for a dead local PC (0x45D8D0 on the PC, CHW24).
        if (_areaSession!.QueryHeroLife() is not { Status.IsDead: false }) { _sheetNpcStatus = "refused: the hero is dead"; return; }
        AdoptSpellData();
        if (BuildNpcSheet(host, entity, speaker) is not { } npc) { RefreshAreaStatus(); return; }
        EnsureSheetPanel();
        AttachSheetArt(_sheetPanel!);
        _sheetPanel!.SetPortrait(null);
        QuarantineWorld(); _quickKeys.Reset();
        _sheetNpc = npc; _sheetPanel.ReadOnly = true;
        if (!OpenWindowMode(InterfaceMode.Character, () => _sheetPanel!.Open(npc.View, npc.Skills, NpcSpells(), npc.Stats, npc.Tech), () => { _sheetPanel!.CloseView(); ResetNpcSheet(); }))
        {
            ResetNpcSheet();
            return;
        }
        _sheetStamp = (_areaSession!, _areaSession!.Version.Revision); _sheetStatus = null; _sheetNpcStatus = null;
        GD.Print($"ARC-UI-088 npc sheet open entity={entity} name={npc.View.Name} level={npc.View.Level} unspent={npc.View.UnspentPoints}");
        RefreshReadyStatus();
    }

    /// <summary>The next open is the hero's (or another NPC's): the panel shows its buttons again.</summary>
    private void ResetNpcSheet()
    {
        _sheetNpc = null;
        if (_sheetPanel is not null) _sheetPanel.ReadOnly = false;
    }
}
