using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Content;
using Arcanum.Presentation.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-042: positional event sounds in the AREA viewer (docs/audio/positional-sound.md). The event rules of ARC-WORLD-041
// choose a cue; the cue plays on the object's tile with the original's attenuation and pan around a listener at the tile in
// the centre of the view, and every live positional sound is recalculated when that tile changes. Wired events: the hero's
// footsteps, a pickup from the ground, the hero's attack (use, attacking bark, hit, critical, miss) and the target's death.
public partial class TerrainViewerApp
{
    private sealed record PositionalVoice(ViewerAudioHandle Handle, SoundPoint Point, PositionalSize Size);

    private readonly List<PositionalVoice> _positionalVoices = [];
    private SoundPoint? _soundListener;
    // The view-centre tile the listener last followed: an effects volume change moves the listener to the PC (SN7) until the
    // view's tile changes again.
    private SoundPoint? _viewListenerTile;
    private (string Module, ContentPrototypes? Prototypes, TileSoundTable? Tiles, string Detail)? _cueContent;
    private (AreaSnapshot Snapshot, Dictionary<(long X, long Y), uint> Art)? _tileArt;
    private readonly Dictionary<int, ObjectSoundFields?> _prototypeSounds = [];
    private readonly Dictionary<int, SoundPayload> _cuePayloads = [];
    private readonly Dictionary<string, int> _cuePlays = new(StringComparer.Ordinal);
    private (int Frame, ActorDiagnosticAction Action)? _stepFrame;
    private string? _cueNotice;
    private const int MaxCuePayloads = 256;

    /// <summary>The prototype index and the tile sound classes of the area's module, opened once over the catalogue's stack.</summary>
    private (ContentPrototypes? Prototypes, TileSoundTable? Tiles) CueContent()
    {
        if (_arguments?.ModulePath is not { } module || SoundCatalogFor(module) is null || _soundContent?.Stack is not { } stack) return (null, null);
        if (_cueContent is { } cached && cached.Module == module) return (cached.Prototypes, cached.Tiles);
        var prototypes = ContentPrototypes.Open(stack);
        var tiles = TileSoundTable.Open(stack);
        var detail = $"{prototypes.Numbers.Count} prototypes, tiles {tiles.Status}";
        GD.Print($"ARC-UI-042 cue content {detail}");
        _cueContent = (module, prototypes, tiles.Table, detail);
        return (prototypes, tiles.Table);
    }

    private void ReleaseCueContent()
    {
        _cueContent = null; _tileArt = null; _prototypeSounds.Clear(); _cuePayloads.Clear();
    }

    private void StopPositionalSound() { _positionalVoices.Clear(); _soundListener = null; _viewListenerTile = null; _stepFrame = null; }

    private static PositionalProfile Positional(SoundPositionalProfile profile)
    {
        static PositionalRange Range(SoundSizeParameters size) => new(size.MinRadius, size.MaxRadius, size.MaxVolumePercent);
        return new(Range(profile.Small), Range(profile.Medium), Range(profile.Large), Range(profile.ExtraLarge), profile.MinPanDistance, profile.MaxPanDistance);
    }

    private ObjectSoundFields? PrototypeSounds(int prototype)
    {
        if (_prototypeSounds.TryGetValue(prototype, out var cached)) return cached;
        var fields = CueContent().Prototypes is { } prototypes ? ObjectSoundReader.ReadPrototype(prototypes.Read, prototype).Fields : null;
        _prototypeSounds[prototype] = fields;
        return fields;
    }

    /// <summary>The hero's sound fields from its prototype; without a hero (or an unreadable one), a PC with no sound base and
    /// no armour, so its footsteps still follow the ground.</summary>
    private ObjectSoundFields HeroSounds() =>
        (_areaSession?.QueryHero() is { } hero ? PrototypeSounds(hero.Prototype) : null) ?? new(SoundCueRules.PcType, -1, 0, 4, 0, 0);

    /// <summary>The sound fields of an item or NPC by entity: a kit item from its prototype, anything else from its map row.</summary>
    private ObjectSoundFields? EntitySounds(string entity)
    {
        if (_areaSession is { } session && session.QueryHeroKit().FirstOrDefault(k => k.Entity == entity) is { Entity: not null } kit)
            return PrototypeSounds(kit.Kind.Prototype);
        return EntityRow(entity) is { } row ? ObjectSoundReader.Read(row).Fields : null;
    }

    private AreaObjectRecord? EntityRow(string entity) =>
        _inventoryHost?.Maps.Select(map => map.TryGetRow(entity, out var row) ? row : null).FirstOrDefault(row => row is not null);

    /// <summary>The footstep class of the ground tile at a world cell (the area's terrain), or null.</summary>
    private int? TileSoundAt(long x, long y)
    {
        if (_readyArea is not { } area || CueContent().Tiles is not { } tiles) return null;
        if (_tileArt is not { } art || !ReferenceEquals(art.Snapshot, area.Snapshot))
        {
            var map = new Dictionary<(long, long), uint>();
            foreach (var tile in area.Snapshot.Terrain) map.TryAdd((tile.WorldX, tile.WorldY), tile.RawArt);
            _tileArt = art = (area.Snapshot, map);
        }
        return art.Art.TryGetValue((x, y), out var raw) ? tiles.SoundClass(raw) : null;
    }

    /// <summary>The listener: the tile at the centre of the view (SN7), in world cells.</summary>
    private SoundPoint? ViewListener()
    {
        if (_camera is null || _worldActor.Session is not AreaGridSession grid) return null;
        var cell = AreaSoundListener.ChartCell(_camera.State.X, _camera.State.Y);
        var world = grid.Chart.ToWorld(cell);
        return PositionalSoundRules.Point(world.X, world.Y);
    }

    /// <summary>Per frame: the listener follows the view; when its tile changes every live positional sound takes the new volume
    /// (scaled by the channel / 127, the original's recalculation) and pan.</summary>
    private void TickPositionalSound()
    {
        if (_audio is null || AreaSoundCatalog() is not { } catalog) return;
        _positionalVoices.RemoveAll(v => _audio.Volume(v.Handle) is null);
        TickFootsteps();
        if (ViewListener() is not { } listener || listener == _viewListenerTile) return;
        _viewListenerTile = listener;
        MoveSoundListener(listener, catalog);
    }

    private void MoveSoundListener(SoundPoint listener, SoundCatalog catalog)
    {
        _soundListener = listener;
        if (_audio is null) return;
        var profile = Positional(catalog.Positional);
        var effects = AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects);
        foreach (var voice in _positionalVoices)
        {
            var level = PositionalSoundRules.Level(voice.Point, listener, profile, voice.Size);
            _audio.Reposition(voice.Handle, AudioVolumeRules.AdjustedVolume(level.Volume, effects), PositionalSoundRules.Pan(level.Balance));
        }
    }

    /// <summary>Plays <paramref name="cue"/> on the world cell (<paramref name="x"/>, <paramref name="y"/>): the variant drawn
    /// with the audio generator, the material fallback of a weapon hit (an empty record falls back only in the Steamwoven
    /// setting, rule SN23), the start volume and pan from the listener. Returns the id and what the backend did.</summary>
    private (int Id, ViewerAudioResult? Result) PlayCueAt(SoundCue cue, long x, long y, PositionalSize size, string kind)
    {
        if (_audio is null || AreaSoundCatalog() is not { } catalog) return (cue.Id, null);
        var emptyFallsBack = _areaSession?.Rules.Sound.EmptyHitRecordSilent != true;
        var id = SoundCueRules.Choose(cue, i => catalog.ResolveSfx(i).Status switch
        {
            SfxStatus.Named => SoundIdState.Named,
            SfxStatus.EmptyRecord => SoundIdState.EmptyRecord,
            _ => SoundIdState.Unknown,
        }, _audioRandom.Next, emptyFallsBack);
        var sfx = catalog.ResolveSfx(id);
        if (!sfx.Playable) { _cueNotice = $"{kind} {id}: {sfx.Status}"; return (id, null); }
        if (!_cuePayloads.TryGetValue(id, out var payload))
        {
            payload = catalog.OpenPayload(sfx.File!);
            if (_cuePayloads.Count >= MaxCuePayloads) _cuePayloads.Clear();
            _cuePayloads[id] = payload;
        }
        var point = PositionalSoundRules.Point(x, y);
        var listener = _soundListener ??= ViewListener() ?? point;
        var level = PositionalSoundRules.Level(point, listener, Positional(catalog.Positional), size);
        var volume = AudioVolumeRules.StartVolume(level.Volume, AudioVolumeRules.ChannelVolume(_audioVolumes, AudioChannel.Effects));
        var result = _audio.Play(payload, AudioChannel.Effects, volume, pan: PositionalSoundRules.Pan(level.Balance));
        if (result.Handle is { } handle) _positionalVoices.Add(new(handle, point, size));
        if (result.Started) _cuePlays[kind] = _cuePlays.GetValueOrDefault(kind) + 1;
        _cueNotice = $"{kind} {id}: {result.Status} volume {level.Volume} balance {level.Balance}";
        return (id, result);
    }

    /// <summary>The hero's footsteps on frames 3 and 8 of its walk or run (SN26).</summary>
    private void TickFootsteps()
    {
        if (_worldActor.Session is not AreaGridSession grid) { _stepFrame = null; return; }
        var current = (_worldActor.CurrentFrame, _worldActor.State.Action);
        if (_stepFrame == current) return;
        _stepFrame = current;
        if (current.Action is not (ActorDiagnosticAction.Walk or ActorDiagnosticAction.Run) || !SoundCueRules.FootstepFrame(current.CurrentFrame)) return;
        PlayHeroFootstep(grid);
    }

    private (int Id, ViewerAudioResult? Result)? PlayHeroFootstep(AreaGridSession grid)
    {
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var hero = HeroSounds();
        var cue = SoundCueRules.Critter(CritterSoundAction.Footsteps, SessionSoundCues.Critter(hero, TileSoundAt(cell.X, cell.Y)), SessionSoundCues.LocalPc(hero));
        return cue is null ? null : PlayCueAt(cue, cell.X, cell.Y, PositionalSize.Large, "footstep");
    }

    /// <summary>A pickup from the ground: the item's pickup sound on the picker (the local PC).</summary>
    private void PlayPickupSound(string item)
    {
        if (ActorCell() is not { } cell || EntitySounds(item) is not { } fields) return;
        if (SoundCueRules.Item(ItemSoundAction.Pickup, SessionSoundCues.Item(fields)) is { } cue) PlayCueAt(cue, cell.X, cell.Y, PositionalSize.Large, "pickup");
    }

    private ObjectSoundFields? HeroWeaponSounds() =>
        _areaSession?.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is { Entity: { } entity } ? EntitySounds(entity) : null;

    /// <summary>An attack starts: the weapon's use sound on the hero and, when the attack sound roll is 1, its attacking bark.</summary>
    private void PlayAttackStartSounds(int? attackSoundRoll)
    {
        if (ActorCell() is not { } cell) return;
        var weapon = HeroWeaponSounds();
        if (SoundCueRules.AttackStart(false, weapon is null ? null : SessionSoundCues.Item(weapon)) is { } start) PlayCueAt(start.Cue, cell.X, cell.Y, PositionalSize.Large, "attack");
        var hero = HeroSounds();
        if (attackSoundRoll == 1 && SoundCueRules.Critter(CritterSoundAction.Attacking, SessionSoundCues.Critter(hero), SessionSoundCues.LocalPc(hero)) is { } bark)
            PlayCueAt(bark, cell.X, cell.Y, PositionalSize.Large, "bark");
    }

    /// <summary>The attack's result sounds (critical bark on the target, hit, critical or miss on the hero) and the target's
    /// dying bark when it died.</summary>
    private void PlayAttackResultSounds(SessionAttackResult result, AreaObjectRecord targetRow, string targetEntity)
    {
        if (ActorCell() is not { } cell || ObjectSoundReader.Read(targetRow).Fields is not { } target || targetRow.WorldX is not { } tx || targetRow.WorldY is not { } ty) return;
        AttackSoundOutcome? outcome = result.Resolution?.Refusal == Arcanum.Simulation.Combat.AttackRefusal.None ? result.Resolution.Outcome switch
        {
            Arcanum.Simulation.Combat.AttackOutcome.Hit => AttackSoundOutcome.Hit,
            Arcanum.Simulation.Combat.AttackOutcome.CriticalHit => AttackSoundOutcome.CriticalHit,
            Arcanum.Simulation.Combat.AttackOutcome.Miss or Arcanum.Simulation.Combat.AttackOutcome.CriticalMiss => AttackSoundOutcome.Miss,
            _ => null,
        } : null;
        if (outcome is not { } kind) return;
        var weapon = HeroWeaponSounds();
        var armorRow = _areaSession is { } armorSession ? _combatHost?.WornArmor(armorSession, CurrentAreaMap, targetEntity) : null;
        var armor = armorRow is null ? null : ObjectSoundReader.Read(armorRow).Fields;
        var local = SessionSoundCues.LocalPc(HeroSounds());
        foreach (var placed in SoundCueRules.AttackResult(kind, weapon is null ? null : SessionSoundCues.Item(weapon), SessionSoundCues.Critter(target),
            SessionSoundCues.HitTarget(target, armor), local))
        {
            var (x, y) = placed.Source == SoundCueSource.Target ? (tx, ty) : (cell.X, cell.Y);
            PlayCueAt(placed.Cue, x, y, PositionalSize.Large, "attack");
        }
        if (result.Life?.Events.HasFlag(Arcanum.Simulation.Combat.LifeEvents.Died) == true && result.Recipient != Arcanum.Simulation.Combat.DamageRecipient.Attacker
            && SoundCueRules.Critter(CritterSoundAction.Dying, SessionSoundCues.Critter(target), local) is { } dying)
            PlayCueAt(dying, tx, ty, PositionalSize.Large, "dying");
    }

    private string CueStatus() => _cuePlays.Count == 0 && _cueNotice is null ? ""
        : $" | event sounds: {string.Join(", ", _cuePlays.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} {p.Value}"))}{(_cueNotice is null ? "" : $" (last {_cueNotice})")}";
}
