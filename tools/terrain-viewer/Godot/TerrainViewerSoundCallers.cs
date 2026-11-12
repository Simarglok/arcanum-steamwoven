using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Sound;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-1126: the world events that played no sound before get their callers (docs/audio/positional-sound.md; checked in the 1.0.7.4
// executable, docs/research/audio-evidence.md SN23-SN26): the looping sound of an animated scenery (0x42DA50), the busting and destroying
// sounds of a broken object (0x43F1C0), the footsteps of the NPCs and companions on their walk and run frames 3 and 8 (0x4305D0), the sound
// of an item dropped from the inventory window (the release 0x575930), and the NPCs' barks: fidget (frame 1 of the fidget animation, 0x42CF40),
// attacking (1 attack in 4, 0x432700) and alerted (the combat approach goal, 0x4296D0). The rules are the Godot-free ones of the earlier
// slices (SoundCueRules, SceneryLoopSoundRules); these callers read the object's sound fields and play through PlayCueAt.
public partial class TerrainViewerApp
{
    // The looping voices of the shown map's sceneries, by entity: the begin node starts one only when it has none (0x42DA50 tests its handle).
    private readonly Dictionary<string, ViewerAudioHandle> _sceneryVoices = new(StringComparer.Ordinal);
    // The gait frame each moving body drew last (action and frame), so a footstep sounds once per frame change.
    private readonly Dictionary<AreaObjectKey, (ActorDiagnosticAction Action, int Frame)> _gaitFrames = [];
    // ARC-RENDER-1126 smoke observations: the begin nodes decided and the footsteps and barks of NPCs played.
    private int _sceneryLoopStarts, _sceneryLoopStops, _npcFootsteps, _npcBarks, _breakSounds, _dropSounds;

    // ---- scenery loops (0x42DA50) ----

    /// <summary>
    /// Per frame, after the scenery host stepped: each loop whose begin node ran decides its looping sound (<see cref="SceneryLoopSoundRules"/>):
    /// within the sound's range of the local PC it starts a looping positional voice on the scenery's tile unless one sounds already, beyond it
    /// it stops the one it has. The begins wait in the host while there is no audio backend or catalogue.
    /// </summary>
    private void TickSceneryLoopSounds(AreaSceneryHost host)
    {
        if (_audio is null || AreaSoundCatalog() is not { } catalog) return;
        foreach (var entity in host.TakeSoundBegins()) BeginSceneryLoopSound(host, entity, catalog);
    }

    private void BeginSceneryLoopSound(AreaSceneryHost host, string entity, SoundCatalog catalog)
    {
        if (_audio is null || host.LoopOf(entity)?.Row is not { WorldX: { } x, WorldY: { } y } row || HeroWorldCell() is not { } pc) return;
        if (ObjectSoundReader.Read(row).Fields is not { } fields || SoundCueRules.Misc(fields.Type, fields.SoundEffect, MiscSoundAction.Animating) is not { } cue) return;
        var size = PositionalSoundRules.Size(fields.Type, fields.SceneryFlags);
        var sounding = _sceneryVoices.TryGetValue(entity, out var voice) && _audio.Volume(voice) is not null;
        var range = SceneryLoopSoundRules.RangeTiles(Positional(catalog.Positional), size);
        switch (SceneryLoopSoundRules.Decide(range, SceneryLoopSoundRules.Distance(x, y, pc.X, pc.Y), sounding))
        {
            case SceneryLoopSoundDecision.Start:
                if (PlayCueAt(cue, x, y, size, "scenery", loop: true).Result is { Started: true, Handle: { } handle }) { _sceneryVoices[entity] = handle; _sceneryLoopStarts++; }
                break;
            case SceneryLoopSoundDecision.Stop:
                _audio.Stop(voice);
                _positionalVoices.RemoveAll(v => v.Handle == voice);
                _sceneryVoices.Remove(entity);
                _sceneryLoopStops++;
                break;
        }
    }

    /// <summary>The scenery loops' voices stop with their runs (the loop's end node 0x42DED0 stops the run's sound handle): the window moved, the map left or the loops given up.</summary>
    private void StopSceneryVoices()
    {
        if (_audio is not null)
            foreach (var voice in _sceneryVoices.Values) { _audio.Stop(voice); _positionalVoices.RemoveAll(v => v.Handle == voice); }
        _sceneryVoices.Clear();
    }

    // ---- busting and destroying (0x43F1C0) ----

    /// <summary>
    /// A broken object's sounds (SN25): the busting sound when the break host steps carry <see cref="SessionObjectHostSteps.BustSound"/>, the
    /// destroying sound for <see cref="SessionObjectHostSteps.DestroySound"/>, on the object's tile (none for a portal or an object with no
    /// sound base, and nothing when the id has no file). The original plays it after the BUST script (OD15).
    /// </summary>
    private void PlayObjectBreakSounds(AreaObjectRecord row, SessionObjectHostSteps steps)
    {
        if ((steps & (SessionObjectHostSteps.BustSound | SessionObjectHostSteps.DestroySound)) == 0 || _audio is null) return;
        if (row.WorldX is null || row.WorldY is null || ObjectSoundReader.Read(row).Fields is not { } fields) return;
        var (x, y) = LiveWorldCell(row);
        var size = PositionalSoundRules.Size(fields.Type, fields.SceneryFlags);
        foreach (var (flag, action, kind) in new[] { (SessionObjectHostSteps.BustSound, MiscSoundAction.Busting, "busting"), (SessionObjectHostSteps.DestroySound, MiscSoundAction.Destroying, "destroying") })
        {
            if ((steps & flag) == 0 || SoundCueRules.Misc(fields.Type, fields.SoundEffect, action) is not { } cue) continue;
            if (PlayCueAt(cue, x, y, size, kind).Result?.Started == true) _breakSounds++;
        }
    }

    // ---- footsteps of NPCs and companions (0x4305D0) ----

    /// <summary>
    /// A moving body drew frame <paramref name="frame"/> of its <paramref name="action"/> cycle at the world cell <paramref name="cell"/>: on frames 3 and 8 of a
    /// walk or run its footsteps sound (SN26; the frame number is the cycle's, not the ART's action frame), once per frame change, for a critter that is not
    /// monstrous (critter flags undead, animal, monster or mechanical, 0x45F730), by the ground tile's class and the armour its art shows; no sound while the
    /// local PC is absent or off.
    /// </summary>
    private void NoteGaitFrame(AreaObjectKey key, AreaObjectRecord row, ActorDiagnosticAction action, int frame, CellCoord cell)
    {
        if (action is not (ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run)) { _gaitFrames.Remove(key); return; }
        var now = (action, frame);
        if (_gaitFrames.TryGetValue(key, out var last) && last == now) return;
        _gaitFrames[key] = now;
        if (!SoundCueRules.FootstepFrame(frame) || SoundCueRules.IsMonstrous(CritterFlagsOf(row)) || _audio is null) return;
        if (ObjectSoundReader.Read(row).Fields is not { } fields) return;
        var cue = SoundCueRules.Critter(CritterSoundAction.Footsteps, SessionSoundCues.Critter(fields, TileSoundAt(cell.X, cell.Y)), SessionSoundCues.LocalPc(HeroSounds()));
        if (cue is not null && PlayCueAt(cue, cell.X, cell.Y, PositionalSize.Large, "npc footstep").Result?.Started == true) _npcFootsteps++;
    }

    private const int CritterFlagsSlot = 218;

    /// <summary>The critter flags (field 0xDA) of a map row: the instance's own over its prototype's, 0 when it has none.</summary>
    private static uint CritterFlagsOf(AreaObjectRecord row) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == CritterFlagsSlot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : 0;

    // ---- barks of the AI (0x42CF40, 0x432700, 0x4296D0) ----

    /// <summary>The world cell a grid session's body stands on (its committed chart cell).</summary>
    private static CellCoord WorldCellCoord(AreaGridSession grid)
    {
        var world = grid.Chart.ToWorld(grid.State.CommittedCell);
        return new(checked((int)world.X), checked((int)world.Y));
    }

    private AreaAiNpc? NpcOf(AreaAiHost host, string entity) => host.Scene.Npcs.FirstOrDefault(n => n.Entity == entity);

    /// <summary>The sound id of an NPC's alerted bark (its sound base + 5), stored by the setter when the danger update makes it hostile (<see cref="AreaAiScene.AlertedSound"/>);
    /// null without a sound base, or while the local PC is absent or off.</summary>
    private int? AiAlertedSound(string entity)
    {
        // The danger update asks inside the session's frame step: without a backend nothing is read, and a failing audio content read changes no AI state.
        if (_audio is null || _aiHost is not { } host || NpcOf(host, entity) is not { } npc) return null;
        try
        {
            return ObjectSoundReader.Read(npc.Row).Fields is { } fields
                ? SoundCueRules.Critter(CritterSoundAction.Alerted, SessionSoundCues.Critter(fields), SessionSoundCues.LocalPc(HeroSounds()))?.Id : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or Arcanum.Data.DatException)
        {
            GD.Print($"ARC-RENDER-1126 alerted-sound npc={entity} unread: {error.GetType().Name} {error.Message}");
            return null;
        }
    }

    /// <summary>The fidget the AI host started for an NPC: its fidget bark at its tile (the original plays it on frame 1 of the fidget animation, which is not drawn
    /// for an NPC, so the bark sounds with the fidget's start).</summary>
    private void PlayNpcFidgetBark(AreaAiHost host, string entity) => PlayNpcBark(host, entity, CritterSoundAction.Fidget, "fidget bark");

    /// <summary>An NPC's attack began and its roll for the attacking bark came up 1 (<see cref="AreaAiNotice.AttackBark"/>, 0x432700).</summary>
    private void PlayNpcAttackBark(AreaAiHost host, string entity) => PlayNpcBark(host, entity, CritterSoundAction.Attacking, "attacking bark");

    private void PlayNpcBark(AreaAiHost host, string entity, CritterSoundAction action, string kind)
    {
        if (_audio is null || NpcOf(host, entity) is not { } npc || npc.Row.WorldX is null || ObjectSoundReader.Read(npc.Row).Fields is not { } fields) return;
        if (SoundCueRules.Critter(action, SessionSoundCues.Critter(fields), SessionSoundCues.LocalPc(HeroSounds())) is not { } cue) return;
        var (x, y) = LiveWorldCell(npc.Row);
        if (PlayCueAt(cue, x, y, PositionalSize.Large, kind).Result?.Started == true) _npcBarks++;
    }

    /// <summary>A sound id a goal asked for on an NPC (the alerted bark of the approach goal): played on its tile, nothing without an audio backend.</summary>
    private void PlayNpcSoundId(AreaAiHost host, string entity, int? id, string kind)
    {
        if (_audio is null || id is not { } sound || NpcOf(host, entity) is not { } npc || npc.Row.WorldX is null) return;
        var (x, y) = LiveWorldCell(npc.Row);
        if (PlayCueAt(new SoundCue(sound), x, y, PositionalSize.Large, kind).Result?.Started == true) _npcBarks++;
    }

    // ---- item drop (the release 0x575930) ----

    /// <summary>The sound facts of an item the hero is about to drop (read before the drop moves it): null for the coins, which sound as gold, and for an item whose
    /// facts do not read.</summary>
    private SoundItem? DropSoundFacts(string item) =>
        item == SessionHeroGrid.GoldEntity ? new SoundItem(0, 0, 0, SoundCueRules.GoldDescription) : EntitySounds(item) is { } fields ? SessionSoundCues.Item(fields) : null;

    /// <summary>An item the hero dropped to the ground from the inventory window: its drop sound (5960 + material class, gold 5968), not positional, as the original's
    /// release of a held item plays it (0x41B780).</summary>
    private void PlayDroppedItemSound(SoundItem? sounding)
    {
        if (_audio is null || sounding is null || SoundCueRules.Item(ItemSoundAction.Drop, sounding) is not { } cue) return;
        if (PlayInterfaceSound(cue.Id, "item dropped")) _dropSounds++;
    }
}
