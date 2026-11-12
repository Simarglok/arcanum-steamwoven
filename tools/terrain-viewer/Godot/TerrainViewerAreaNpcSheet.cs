using Arcanum.Data.Area;
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
// ARC-UI-1127 (E8-g, the Identify window of the critter, ledger XR2): Read Aura (spell 7) and any other cast whose Identify component lands on an NPC open the same read-only sheet (the cast host's IdentifyCritter event, the
// target named by the cast host's id): the cast ends and the sheet opens once nothing else owns input, like the dialog's. The hero as the target keeps the message line (its own sheet is the C key).
public partial class TerrainViewerApp
{
    private (GameSessionOwner Session, AreaConversationHost Host, string Entity, int Speaker)? _npcSheetPending;
    private AreaNpcSheetPages? _sheetNpc;
    private string? _sheetNpcStatus;
    // ARC-UI-1127: the critter an Identify cast named (session, the cast host's id, its map row) waits here until nothing else owns input.
    private (GameSessionOwner Session, string Entity, AreaObjectRecord Row)? _identifySheetPending;
    // ARC-RES-1327 (E8-g, ledger XR3, XR4): the container an Identify cast named waits for its inventory window in mode 4 (See Contents), the item for its description window.
    private (GameSessionOwner Session, AreaInventoryTarget Target)? _identifyContainerPending;
    private (GameSessionOwner Session, string Item)? _identifyItemPending;
    /// <summary>The last Identify container window's entity and whether it opened, for the smoke.</summary>
    internal (string Entity, bool Opened)? LastIdentifyContentsForSelfTest { get; private set; }
    // ARC-UI-1411 (ledger XR4): the portal or scenery an Identify cast named waits for its object window (the hover window's, 0x57CCF0).
    private (GameSessionOwner Session, AreaObjectKey Key)? _identifyObjectPending;
    /// <summary>The last Identify portal or scenery window's object type and whether the object window was drawn for it, for the smoke.</summary>
    internal (AreaObjectKey Key, bool Shown)? LastIdentifyObjectForSelfTest { get; private set; }
    /// <summary>The last Identify item description's item and whether the item window was drawn for it, for the smoke.</summary>
    internal (string Item, bool Shown)? LastIdentifyDescriptionForSelfTest { get; private set; }

    /// <summary>The last Identify sheet's entity and whether it opened, for the smoke.</summary>
    internal (string Entity, bool Opened)? LastIdentifySheetForSelfTest { get; private set; }

    /// <summary>A cast host log entry of an Identify on an NPC (ledger XR2): the read-only sheet is queued for it (the hero as the target has its own C sheet and keeps the message line).</summary>
    private void TakeIdentifyEvent(SpellCastEvent entry)
    {
        if (entry.Kind == SpellCastEventKind.IdentifyContainer && entry.Object is { } container && _areaSession is { } containerSession)
        {
            // ARC-RES-1327 (XR3): the container's own row names its inventory target (the open is the window's mode 4, with no lock test).
            if (_spellCastMap?.Row(container) is not { } containerRow || _inventoryHost?.Target(containerSession, CurrentAreaMap, containerRow.Key) is not { Kind: AreaInventoryTargetKind.Container } target)
            { GD.Print($"ARC-RES-1327 identify container={container} is no standing container of the shown map"); return; }
            GD.Print($"ARC-RES-1327 identify contents queued entity={container}");
            _identifyContainerPending = (containerSession, target);
            return;
        }
        if (entry.Kind == SpellCastEventKind.IdentifyItem && entry.Value is Arcanum.Data.Combat.CombatFieldReader.PortalType or Arcanum.Data.Combat.CombatFieldReader.SceneryType
            && entry.Object is { } named && _areaSession is { } objectSession)
        {
            // ARC-UI-1411 (XR4): a portal or scenery goes to the hover window's object window by its map row.
            if (_spellCastMap?.Row(named) is not { } namedRow) { GD.Print($"ARC-UI-1411 identify object={named} has no map row here"); return; }
            GD.Print($"ARC-UI-1411 identify object window queued object={named}");
            _identifyObjectPending = (objectSession, namedRow.Key);
            return;
        }
        if (entry.Kind == SpellCastEventKind.IdentifyItem && entry.Value is >= 5 and <= 14 && entry.Object is { } item && _areaSession is { } itemSession)
        {
            // ARC-RES-1327 (XR4): an item (types 5..14) goes to the item window of the hover window (0x57CCF0); Divine Magick has marked it identified before this entry.
            GD.Print($"ARC-RES-1327 identify description queued item={item}");
            _identifyItemPending = (itemSession, item);
            return;
        }
        if (entry.Kind != SpellCastEventKind.IdentifyCritter || entry.Object is not { } id || id == SessionSpellCaster.Hero || _areaSession is null) return;
        if (_spellCastMap?.Row(id) is not { } row) { GD.Print($"ARC-UI-1127 identify critter={id} has no map row here"); return; }
        GD.Print($"ARC-UI-1127 identify sheet queued entity={id}");
        _identifySheetPending = (_areaSession, id, row);
    }

    private void TakeIdentifyWindows()
    {
        if (_identifyItemPending is { } described)
        {
            _identifyItemPending = null;
            if (ReferenceEquals(described.Session, _areaSession) && SheetActive)
            {
                DescribeItem(described.Session, described.Item);
                LastIdentifyDescriptionForSelfTest = (described.Item, _messageLog.Shown is { Kind: AreaMessageKind.Item });
            }
        }
        if (_identifyObjectPending is { } named)
        {
            _identifyObjectPending = null;
            if (ReferenceEquals(named.Session, _areaSession) && SheetActive)
            {
                var drawn = DescribeObject(named.Key, _gameBarPanel, pointer: false);
                GD.Print($"ARC-UI-1411 identify object window object={named.Key} drawn={drawn}");
                LastIdentifyObjectForSelfTest = (named.Key, drawn && _messageLog.Shown is { Kind: AreaMessageKind.Object });
            }
        }
        if (_identifyContainerPending is not { } contents) return;
        if (!ReferenceEquals(contents.Session, _areaSession) || !SheetActive) { _identifyContainerPending = null; return; }
        if (WorldModal || SheetOpen) return;
        _identifyContainerPending = null;
        OpenContainerContents(contents.Target);
        LastIdentifyContentsForSelfTest = (contents.Target.Entity, ContentsWindow);
    }

    private void TakeIdentifySheet()
    {
        TakeIdentifyWindows();
        if (_identifySheetPending is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession) || !SheetActive) { _identifySheetPending = null; return; }
        if (WorldModal || SheetOpen) return;
        _identifySheetPending = null;
        OpenNpcSheet(() => BuildNpcSheet(pending.Row, pending.Entity), pending.Entity);
        LastIdentifySheetForSelfTest = (pending.Entity, SheetOpen && _sheetNpc is not null);
    }

    /// <summary>A c-e reply ended the conversation (<see cref="Arcanum.Scripting.Dialog.DialogEndKind.NpcSheetWindow"/>): the sheet opens for the speaker once nothing else owns input.</summary>
    private void QueueDialogNpcSheet(AreaConversationHost host, (string Entity, int Speaker) speaker)
    {
        GD.Print($"ARC-UI-088 dialog npc sheet speaker={speaker.Entity}");
        _npcSheetPending = (_areaSession!, host, speaker.Entity, speaker.Speaker);
    }

    private void TakeDialogNpcSheet()
    {
        TakeIdentifySheet();
        if (_npcSheetPending is not { } pending) return;
        if (!ReferenceEquals(pending.Session, _areaSession) || !SheetActive) { _npcSheetPending = null; return; }
        if (WorldModal) return;
        _npcSheetPending = null;
        OpenNpcSheet(pending.Host, pending.Entity, pending.Speaker);
    }

    /// <summary>The NPC's sheet pages from the speaker's stored fields (null with the reason in <see cref="_sheetNpcStatus"/>).</summary>
    private AreaNpcSheetPages? BuildNpcSheet(AreaConversationHost host, string entity, int speaker)
    {
        if (speaker < 0 || speaker >= host.Speakers.Count) { _sheetNpcStatus = "no data of the speaker"; GD.Print("ARC-UI-088 npc sheet refused: no data of the speaker"); return null; }
        return BuildNpcSheet(host.Speakers[speaker].Row, entity);
    }

    /// <summary>The NPC's sheet pages from the stored fields of its map row (null with the reason in <see cref="_sheetNpcStatus"/>).</summary>
    private AreaNpcSheetPages? BuildNpcSheet(AreaObjectRecord row, string entity)
    {
        AreaNpcSheetPages? Refuse(string why) { _sheetNpcStatus = why; GD.Print($"ARC-UI-088 npc sheet refused: {why}"); return null; }
        var session = _areaSession!;
        if (CombatFieldReader.Read(row).Fields is not { } fields) return Refuse("no data of the speaker");
        if (_areaEffectRules is not { } effects) return Refuse("no effect rules (start with --hero)");
        if (AreaAdvancement.Npc(entity, fields, effects) is not { } loaded) return Refuse("the speaker gives no admissible sheet");
        AdoptSkillTexts();
        var name = AreaHoverIndex.Text(row, _hoverNames?.Catalog) ?? entity;
        var life = _combatHost is { } combat ? AreaLife.Of(session, combat, entity) : null;
        return AreaNpcSheet.Build(session, name, fields, loaded, life, _skillTexts?.Texts, AreaHoverIndex.TextProfile) ?? Refuse("the speaker is not a critter");
    }

    private AreaSpellsPageView? NpcSpells() => _sheetNpc?.Spells(_spellData?.Data, _sheetCollege, _areaSession!.AutoLevel?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower);

    private void OpenNpcSheet(AreaConversationHost host, string entity, int speaker) =>
        OpenNpcSheet(() => BuildNpcSheet(host, entity, speaker), entity);

    private void OpenNpcSheet(Func<AreaNpcSheetPages?> build, string entity)
    {
        if (!SheetActive || !WindowCanOpen(InterfaceMode.Character)) return;
        // The original's open refuses for a dead local PC (0x45D8D0 on the PC, CHW24).
        if (_areaSession!.QueryHeroLife() is not { Status.IsDead: false }) { _sheetNpcStatus = "refused: the hero is dead"; return; }
        AdoptSpellData();
        if (build() is not { } npc) { RefreshAreaStatus(); return; }
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
