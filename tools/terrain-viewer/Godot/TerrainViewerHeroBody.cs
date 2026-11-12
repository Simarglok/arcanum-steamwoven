using Arcanum.Data;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-039: the AREA hero is drawn with its own body (race and gender, HeroBody), not the fixed human woman every map load
// publishes. Each frame the session hero's body is compared with the drawn one; on a change (a new game, a loaded save, a map
// publication that brought the fixed human back) the body's ARTs are read from the actor archive once a run, uploaded, and the
// actor, its gait timing and its plan are swapped (NPCs and companions keep the human timing). A body that cannot be read or
// swapped is drawn as the fixed human, with its reason in the log, and is not tried again that run. ARC-WORLD-112: the body wears
// the hero's worn body armour (its use art's armour part and palette, ledger BA1–BA2); a worn body whose ARTs do not read is drawn bare.
// ARC-RENDER-037: in combat mode the body shows the wielded weapon and the shield (0x465020, ledger WA1): it stands and attacks with
// them; an armed body whose ARTs do not read is drawn as the unarmed one.
public partial class TerrainViewerApp
{
    private readonly Dictionary<HeroBody, HeroBodyLoad> _heroBodies = new();
    private readonly Dictionary<HeroBody, string> _heroBodyFailures = new();
    private string _heroBodyStatus = "fixed human";
    // ARC-COMBAT-021: why the human woman's own body (for a human or elf woman hero) did not read or swap; the fixed human draws her then.
    private string? _heroWomanFailure;
    private IReadOnlyList<ActorDiagnosticFrame>? _humanActorFrames;

    // ARC-WORLD-112: the use art of each worn body armour (since ARC-RENDER-037 also weapon), by item entity, for one session and kit
    // (an item's art never changes).
    private readonly Dictionary<string, uint?> _bodyArmorUseArts = new();
    private (GameSessionOwnerIdentity? Owner, IReadOnlyList<SessionWearableItem?>? Kit) _bodyArmorUseArtsFor;

    /// <summary>The body the session hero should be drawn with: its race and gender's wearing its body armour (bare when that body
    /// did not read, or the item's subtype has no art) and, since ARC-RENDER-037, showing its weapon and shield in combat mode (without
    /// the shield when that body did not read, unarmed when neither did); the fixed human without a hero.</summary>
    private HeroBody WantedHeroBody()
    {
        if (_areaSession is not { } session || session.QueryHero() is not { } hero || HeroBody.For(hero.Race, hero.Gender) is not { } bare)
            return HeroBody.HumanFemale;
        // ARC-MAGIC-052 (TR4): a monster body the Trait component put on the hero (Polymorph, the body spells) is drawn instead of the race's body, worn armour and weapon (the original's new art id has none of them);
        // a species whose ARTs do not read is drawn as the hero's own body. ARC-MAGIC-1219 (PR2): its weapon part is 1 in combat mode and 0 out of it, so a combat-mode change swaps the body as the human's does.
        if (session.QueryTraitArt(SessionSpellCaster.Hero) is { IsValid: true } trait && HeroBody.Monster(trait.Species, trait.Palette).MonsterInCombat(HeroInCombatMode) is var monster && !_heroBodyFailures.ContainsKey(monster)) return monster;
        var worn = session.QueryEquipment().Worn;
        var dressed = bare.Wearing(WornUseArt(session, worn, EquipmentSlot.Armor)) is { } armour && !_heroBodyFailures.ContainsKey(armour) ? armour : bare;
        if (ArmedBody(session, worn, dressed) is not { } armed) return dressed;
        // A weapon and shield pair without its ARTs (the session's wear check does not test the art, WA8) still shows the weapon.
        return !_heroBodyFailures.ContainsKey(armed) ? armed : !_heroBodyFailures.ContainsKey(armed with { Shield = 0 }) ? armed with { Shield = 0 } : dressed;
    }

    /// <summary>ARC-RENDER-037: <paramref name="body"/> with the weapon and shield the hero shows (WA1): none out of combat mode (the
    /// body itself); in it the wielded weapon's use-art subtype (bare hands without a weapon, or for one whose use art is not read here:
    /// NOT READY) and the shield bit for an armour item in the shield slot.</summary>
    private HeroBody? ArmedBody(GameSessionOwner session, IReadOnlyList<SessionWornItem> worn, HeroBody body)
    {
        if (!HeroInCombatMode) return body;
        var shield = worn.FirstOrDefault(item => item.Slot == EquipmentSlot.Shield);
        return body.Armed(true, WornUseArt(session, worn, EquipmentSlot.Weapon),
            shield is null ? null : shield.Item.Kind == EquipmentItemKind.Armor ? WeaponArtRules.ArmorItemType : 0);
    }

    /// <summary>The use art of the hero's worn body armour: null with none, or when its facts are not read here (a made item, a taken
    /// item whose map rows the inventory host does not hold: NOT READY, drawn bare). Only a read use art is kept, so an item read later is
    /// drawn then.</summary>
    private uint? WornBodyArmorUseArt(GameSessionOwner session) => WornUseArt(session, session.QueryEquipment().Worn, EquipmentSlot.Armor);

    private uint? WornUseArt(GameSessionOwner session, IReadOnlyList<SessionWornItem> worn, EquipmentSlot slot)
    {
        if (worn.FirstOrDefault(item => item.Slot == slot) is not { } item) return null;
        if (_bodyArmorUseArtsFor != (session.Version.Owner, _areaKitWear)) { _bodyArmorUseArts.Clear(); _bodyArmorUseArtsFor = (session.Version.Owner, _areaKitWear); }
        if (_bodyArmorUseArts.TryGetValue(item.Entity, out var use)) return use;
        use = _inventoryHost?.Wearable(session, item.Entity, _areaKitWear)?.UseArt;
        if (use is not null) _bodyArmorUseArts[item.Entity] = use;
        return use;
    }

    private void SyncHeroBody()
    {
        if (_readyArea is null || _readyMode != TerrainViewerMode.Area || _readyActorPlan is null || _worldActor.Session is not AreaGridSession) return;
        // ARC-RENDER-037: a playing attack keeps its body (its clip has that body's attack ART's frames); a weapon change swaps after it.
        if (_attack?.Player is { Playing: true }) return;
        var wanted = WantedHeroBody();
        // A body that did not read or swap is drawn as the fixed human, with the human's profile and timing.
        if (_heroBodyFailures.ContainsKey(wanted)) wanted = HeroBody.HumanFemale;
        // ARC-COMBAT-021: a hero drawn with the human woman's body (a human or elf woman) reads that body's ARTs as any other, so she
        // has the fall and get-up the fixed human lacks; without a hero, or when they do not read, the fixed human stays.
        if (wanted == HeroBody.HumanFemale && (_areaSession?.QueryHero() is null || _heroWomanFailure is not null))
        {
            // The fixed human is the published plan's own profile; a plan published since a swap already carries it.
            if (ReferenceEquals(_worldActor.Profile, ActorDiagnosticProfile.HumanFixedArea) && ReferenceEquals(_readyActorPlan.Profile, ActorDiagnosticProfile.HumanFixedArea)) return;
            if (_humanActorFrames is not { } human || _heroBodyFailures.ContainsKey(HeroBody.HumanFemale)) return;
            SwapHeroBody(wanted, ActorDiagnosticProfile.HumanFixedArea, GaitTiming.HumanFixed, human, null);
            return;
        }
        if (_heroBodies.TryGetValue(wanted, out var known) && ReferenceEquals(_worldActor.Profile, known.Profile) && ReferenceEquals(_readyActorPlan.Profile, known.Profile)) return;
        if (known is null)
        {
            if (!wanted.IsMonster && _arguments?.ActorResourcePath is null) { NoteHeroBodyFailure(wanted, "no actor archive", textures: true); return; }
            // ARC-DEV-368 (O3): the body a worker is already reading (PrefetchHeroBody) is waited for, else it is read here as before. ARC-MAGIC-052: a monster body is read here, from the window's art archives.
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var prefetched = wanted.IsMonster ? null : TakeHeroBodyRead(wanted, _arguments!.ActorResourcePath!);
            var read = wanted.IsMonster ? ReadHeroMonsterBody(wanted) : prefetched ?? ReadHeroBody(_arguments!.ActorResourcePath!, wanted);
            GD.Print($"ARC-DEV-368 hero body={wanted.Label} armor={wanted.Armor} weapon={wanted.Weapon} shield={wanted.Shield} read={(read.Load is null ? "failed" : "ok")} prefetched={prefetched is not null} waited-ms={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} worker-ms={read.Milliseconds}");
            if (read.Load is null) { NoteHeroBodyFailure(wanted, read.Failure!, textures: true); return; }
            known = _heroBodies[wanted] = read.Load;
        }
        SwapHeroBody(wanted, known.Profile, known.Timing, known.Frames, known.Textures);
    }

    /// <summary>ARC-DEV-368 (O3): one hero body read from the actor archive: its decoded ARTs, or the reason it did not read. Pure (its own archive
    /// handle, nothing of the viewer's state), so a worker thread runs it.</summary>
    private sealed record HeroBodyRead(HeroBodyLoad? Load, string? Failure, long Milliseconds);

    private static HeroBodyRead ReadHeroBody(string path, HeroBody body)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        long Elapsed() => (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        try
        {
            using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
            return new(HeroBodyArt.Load(archive, body), null, Elapsed());
        }
        catch (Exception exception) when (exception is TerrainViewerException or DatException or IOException or UnauthorizedAccessException)
        { return new(null, exception is TerrainViewerException viewer ? viewer.Error.Code.ToString() : exception.GetType().Name, Elapsed()); }
    }

    // ARC-MAGIC-052: the monster body of the hero from the art archives of the shown window (the monster ARTs are there, as the created critters' are), by the monster name tables of the loaded module.
    private HeroBodyRead ReadHeroMonsterBody(HeroBody body)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        long Elapsed() => (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var opened = new List<DatArchive>();
        try
        {
            if (_readyArea is not { } area || ExtraArt() is not { } extra) return new(null, "no window or monster name tables", Elapsed());
            foreach (var path in area.ArtSources.Values.Where(File.Exists))
                opened.Add(DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 }));
            return new(HeroBodyArt.LoadMonster(opened, body, extra.MappedName), null, Elapsed());
        }
        catch (Exception exception) when (exception is TerrainViewerException or DatException or IOException or UnauthorizedAccessException)
        { return new(null, exception is TerrainViewerException viewer ? viewer.Error.Code.ToString() : exception.GetType().Name, Elapsed()); }
        finally { foreach (var archive in opened) archive.Dispose(); }
    }

    // ARC-DEV-368 (O3): the body being read on a worker for the session's hero, by body and the archive it reads. Main thread only.
    private (string Path, HeroBody Body, Task<HeroBodyRead> Read)? _heroBodyPrefetch;

    /// <summary>ARC-DEV-368 (O3, ARC-RES-102): starts reading, on a worker, the body the session's hero will be drawn with, as soon as the session and its
    /// inventory exist (the publication binds the other hosts meanwhile) so the publication's <see cref="SyncHeroBody"/> finds it read instead of
    /// reading the actor archive and decoding its ARTs on the main thread. Nothing is started when the wanted body is known, failed, the fixed human, or
    /// has no actor archive; a body that is not the one wanted later (a weapon drawn, armour changed) is dropped unused. Main thread; the worker touches no viewer state.</summary>
    private void PrefetchHeroBody()
    {
        _heroBodyPrefetch = null;
        if (_areaSession?.QueryHero() is null || _arguments?.ActorResourcePath is not { } path) return;
        var wanted = WantedHeroBody();
        if (wanted.IsMonster) return;
        if (_heroBodies.ContainsKey(wanted) || _heroBodyFailures.ContainsKey(wanted) || (wanted == HeroBody.HumanFemale && _heroWomanFailure is not null)) return;
        _heroBodyPrefetch = (path, wanted, Task.Run(() => ReadHeroBody(path, wanted)));
    }

    /// <summary>The prefetched read of <paramref name="body"/> from <paramref name="path"/> (waited for when it still runs), or null when none was started for it.</summary>
    private HeroBodyRead? TakeHeroBodyRead(HeroBody body, string path)
    {
        if (_heroBodyPrefetch is not { } started || started.Body != body || started.Path != path) return null;
        _heroBodyPrefetch = null;
        return started.Read.GetAwaiter().GetResult();
    }

    // The swap is all or nothing: a plan that cannot be built puts the actor's previous profile and timing back and records
    // the body as failed (the next frame then draws the fixed human).
    private void SwapHeroBody(HeroBody body, ActorDiagnosticProfile profile, GaitTiming timing, IReadOnlyList<ActorDiagnosticFrame> frames,
        IReadOnlyDictionary<ActorDiagnosticTextureKey, DecodedActorTexture>? textures)
    {
        var (previousProfile, previousTiming) = (_worldActor.Profile, _worldActor.AreaGaitTiming ?? GaitTiming.HumanFixed);
        ActorDiagnosticScenePlan plan; ActorDiagnosticPlacementInput input;
        try
        {
            // ARC-DEV-380: the swap of the publication is timed (steps print as hero-swap.NAME); the lap ends with this block, before the selection below.
            using var lap = _perfFirstSelection && PerfProbe.Enabled ? PerfProbe.Lap("hero-swap") : null;
            lap?.Step("upload");
            if (textures is not null) _cache.UploadActor(textures);
            lap?.Step("profile");
            _worldActor.UseProfile(profile, timing);
            input = _worldActor.Placement();
            lap?.Step("plan");
            plan = HeroBodyLoad.PlanOf(profile, frames, input);
            lap?.Step("select");
        }
        catch (Exception exception) when (exception is TerrainViewerException or ArgumentException or InvalidOperationException)
        {
            _worldActor.UseProfile(previousProfile, previousTiming);
            NoteHeroBodyFailure(body, exception is TerrainViewerException viewer ? viewer.Error.Code.ToString() : exception.GetType().Name, textures is not null);
            return;
        }
        _readyActorPlan = plan; _readyActor = plan.Current; _readyActorInput = input;
        _heroBodyStatus = textures is null ? "fixed human" : $"{body.Label} ({profile.Provenance})";
        GD.Print($"ARC-UI-039 hero body={body.Label} profile={profile.Id} idle=0x{profile.Idle.Selector:x8} walkRate={timing.ArtRate(MovementGait.Walk)} runRate={timing.ArtRate(MovementGait.Run)}");
        PublishWorldSelection();
    }

    // Native observation in the hero smoke: the actor, its plan and the drawn frame use the session hero's body (its race and
    // gender's art id bits on every action), the frame's texture is uploaded, and the steps take that body's gait timing.
    private void RunHeroBodySmoke(Arcanum.Session.SessionHeroState hero)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-039 hero body smoke: " + why); }
        SyncHeroBody();
        // ARC-WORLD-112: the body wears the hero's body armour (the pre-made start wields the kit's best items).
        var body = WantedHeroBody();
        var bare = HeroBody.For(hero.Race, hero.Gender) ?? HeroBody.HumanFemale;
        Require(bare.Wearing(_areaSession is { } session ? WornBodyArmorUseArt(session) : null) is not { } worn || worn == body with { Weapon = 0, Shield = 0 },
            $"the worn body armour's body is drawn: {_heroBodyStatus}");
        var profile = _worldActor.Profile;
        Require(!_heroBodyFailures.ContainsKey(body) && (body != HeroBody.HumanFemale || _heroWomanFailure is null),
            "the body did not load: " + (_heroWomanFailure ?? _heroBodyStatus));
        Require(ReferenceEquals(_readyActorPlan?.Profile, profile), "the plan draws the actor's profile");
        Require(profile.Actions.All(action => body.Candidates(ActorDiagnosticProfile.HumanSelector(action)).Contains(profile.Resource(action).Selector)),
            $"{hero.Race} {hero.Gender}: every action is the {body.Label} body");
        // ARC-COMBAT-021: the body reads its fall-down and get-up ARTs.
        Require(profile.FallDown is not null && profile.GetUp is not null, $"{body.Label}: the fall-down and get-up ARTs read");
        Require(_readyActor is { } drawn && (drawn.TextureKey.Selector & 0x0FF00030u) == body.BodyBits && _cache.TryGet(drawn.TextureKey, out _),
            "the drawn frame is the body's and uploaded: " + _readyActor?.TextureKey);
        Require(_worldActor.AreaGaitTiming is { } timing && timing.RateBonus == body.GaitRateBonus
            && (_worldActor.Session as AreaGridSession)?.Timing == timing, "the steps take the body's gait timing");
        GD.Print($"ARC-UI-039 native-smoke hero body PASS race={hero.Race} gender={hero.Gender} body={body.Label} armor={body.Armor} palette={body.Palette} profile={profile.Id} "
            + $"idle=0x{profile.Idle.Selector:x8} walkRate={_worldActor.AreaGaitTiming!.ArtRate(MovementGait.Walk)}");
        RunBodyArmorSmoke(bare, body);
    }

    // ARC-WORLD-112 native observation: taking the worn body armour off draws the bare body (underwear), wearing it again draws the
    // armour's body; each change reaches the drawn frame at the next sync. A hero without worn body armour reports it and checks nothing.
    private void RunBodyArmorSmoke(HeroBody bare, HeroBody dressed)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-WORLD-112 body armour smoke: " + why); }
        var session = _areaSession!;
        if (session.QueryEquipment().Worn.FirstOrDefault(worn => worn.Slot == EquipmentSlot.Armor) is not { } armor)
        { GD.Print($"ARC-WORLD-112 native-smoke body armour SKIP no worn body armour body={bare.Label}"); return; }
        if (dressed == bare) { GD.Print($"ARC-WORLD-112 native-smoke body armour SKIP {armor.Entity} draws the bare body={bare.Label}"); return; }
        void Drawn(HeroBody body, string when)
        {
            SyncHeroBody();
            Require(WantedHeroBody() == body && !_heroBodyFailures.ContainsKey(body), $"{when}: {body.Label} armour {body.Armor} is wanted: {_heroBodyStatus}");
            Require(_readyActor is { } drawn && (drawn.TextureKey.Selector & 0x0FF00030u) == body.BodyBits && _cache.TryGet(drawn.TextureKey, out _),
                $"{when}: the drawn frame is {body.Label} armour {body.Armor}: {_readyActor?.TextureKey}");
        }
        var triggers = ItemTriggersNow();
        var off = session.Unequip(session.Version, EquipmentSlot.Armor, _inventoryHost!.InsertCheck, _inventoryHost.ItemScripts(session), triggers);
        Require(off.Disposition == GameSessionDisposition.Changed, $"take-off {off.Disposition} {off.Failure} {off.Refusal}");
        Drawn(bare, "taken off");
        var wearable = _inventoryHost.Wearable(session, armor.Entity, _areaKitWear);
        Require(wearable is not null, "the armour's wear facts");
        var on = session.Equip(session.Version, armor.Entity, EquipmentSlot.Armor, wearable!, (_, _) => true, _inventoryHost.InsertCheck,
            _inventoryHost.ItemScripts(session), ItemTriggersNow());
        Require(on.Disposition == GameSessionDisposition.Changed && session.QueryEquipment().Worn.Any(worn => worn.Slot == EquipmentSlot.Armor && worn.Entity == armor.Entity),
            $"wear {on.Disposition} {on.Failure} {on.Refusal}: the armour is back in its slot");
        Drawn(dressed, "worn again");
        GD.Print($"ARC-WORLD-112 native-smoke body armour PASS item={armor.Entity} use=0x{wearable!.UseArt:x8} bare={bare.Label} "
            + $"worn={dressed.Label} armor={dressed.Armor} palette={dressed.Palette}");
    }

    /// <param name="textures">The failure is of a body read from its ARTs (not of the fixed human's swap back).</param>
    private void NoteHeroBodyFailure(HeroBody body, string reason, bool textures)
    {
        if (body == HeroBody.HumanFemale && textures) _heroWomanFailure = reason;
        else _heroBodyFailures[body] = reason;
        _heroBodyStatus = $"{body.Label} armour {body.Armor} weapon {body.Weapon} shield {body.Shield} not drawn: {reason}";
        GD.Print($"ARC-UI-039 hero body={body.Label} armor={body.Armor} weapon={body.Weapon} shield={body.Shield} FAILED {reason}; the next body without it is drawn (the fixed human last)");
    }
}
