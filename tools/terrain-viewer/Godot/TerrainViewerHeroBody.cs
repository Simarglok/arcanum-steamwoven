using Arcanum.Data;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-039: the AREA hero is drawn with its own body (race and gender, HeroBody), not the fixed human woman every map load
// publishes. Each frame the session hero's body is compared with the drawn one; on a change (a new game, a loaded save, a map
// publication that brought the fixed human back) the body's ARTs are read from the actor archive once a run, uploaded, and the
// actor, its gait timing and its plan are swapped (NPCs and companions keep the human timing). A body that cannot be read or
// swapped is drawn as the fixed human, with its reason in the log, and is not tried again that run. ARC-WORLD-112: the body wears
// the hero's worn body armour (its use art's armour part and palette, ledger BA1–BA2); a worn body whose ARTs do not read is drawn bare.
public partial class TerrainViewerApp
{
    private readonly Dictionary<HeroBody, HeroBodyLoad> _heroBodies = new();
    private readonly Dictionary<HeroBody, string> _heroBodyFailures = new();
    private string _heroBodyStatus = "fixed human";
    private IReadOnlyList<ActorDiagnosticFrame>? _humanActorFrames;

    // ARC-WORLD-112: the use art of each worn body armour, by item entity, for one session and kit (an item's art never changes).
    private readonly Dictionary<string, uint?> _bodyArmorUseArts = new();
    private (GameSessionOwnerIdentity? Owner, IReadOnlyList<SessionWearableItem?>? Kit) _bodyArmorUseArtsFor;

    /// <summary>The body the session hero should be drawn with: its race and gender's wearing its body armour (bare when that body
    /// did not read, or the item's subtype has no art), the fixed human without a hero.</summary>
    private HeroBody WantedHeroBody()
    {
        if (_areaSession is not { } session || session.QueryHero() is not { } hero || HeroBody.For(hero.Race, hero.Gender) is not { } bare)
            return HeroBody.HumanFemale;
        return bare.Wearing(WornBodyArmorUseArt(session)) is { } worn && !_heroBodyFailures.ContainsKey(worn) ? worn : bare;
    }

    /// <summary>The use art of the hero's worn body armour: null with none, or when its facts are not read here (a made item, a taken
    /// item whose map rows the inventory host does not hold: NOT READY, drawn bare). Only a read use art is kept, so an item read later is
    /// drawn then.</summary>
    private uint? WornBodyArmorUseArt(GameSessionOwner session)
    {
        if (session.QueryEquipment().Worn.FirstOrDefault(worn => worn.Slot == EquipmentSlot.Armor) is not { } armor) return null;
        if (_bodyArmorUseArtsFor != (session.Version.Owner, _areaKitWear)) { _bodyArmorUseArts.Clear(); _bodyArmorUseArtsFor = (session.Version.Owner, _areaKitWear); }
        if (_bodyArmorUseArts.TryGetValue(armor.Entity, out var use)) return use;
        use = _inventoryHost?.Wearable(session, armor.Entity, _areaKitWear)?.UseArt;
        if (use is not null) _bodyArmorUseArts[armor.Entity] = use;
        return use;
    }

    private void SyncHeroBody()
    {
        if (_readyArea is null || _readyMode != TerrainViewerMode.CrashSiteArea || _readyActorPlan is null || _worldActor.Session is not AreaGridSession) return;
        var wanted = WantedHeroBody();
        // A body that did not read or swap is drawn as the fixed human, with the human's profile and timing.
        if (_heroBodyFailures.ContainsKey(wanted)) wanted = HeroBody.HumanFemale;
        if (wanted == HeroBody.HumanFemale)
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
            if (_arguments?.ActorResourcePath is not { } path) { NoteHeroBodyFailure(wanted, "no actor archive"); return; }
            try
            {
                using var archive = DatArchive.Open(path, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 });
                known = _heroBodies[wanted] = HeroBodyArt.Load(archive, wanted);
            }
            catch (Exception exception) when (exception is TerrainViewerException or DatException or IOException or UnauthorizedAccessException)
            { NoteHeroBodyFailure(wanted, exception is TerrainViewerException viewer ? viewer.Error.Code.ToString() : exception.GetType().Name); return; }
        }
        SwapHeroBody(wanted, known.Profile, known.Timing, known.Frames, known.Textures);
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
            if (textures is not null) _cache.UploadActor(textures);
            _worldActor.UseProfile(profile, timing);
            input = _worldActor.Placement();
            plan = HeroBodyLoad.PlanOf(profile, frames, input);
        }
        catch (Exception exception) when (exception is TerrainViewerException or ArgumentException or InvalidOperationException)
        {
            _worldActor.UseProfile(previousProfile, previousTiming);
            NoteHeroBodyFailure(body, exception is TerrainViewerException viewer ? viewer.Error.Code.ToString() : exception.GetType().Name);
            return;
        }
        _readyActorPlan = plan; _readyActor = plan.Current; _readyActorInput = input;
        _heroBodyStatus = body == HeroBody.HumanFemale ? "fixed human" : $"{body.Label} ({profile.Provenance})";
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
        Require(bare.Wearing(_areaSession is { } session ? WornBodyArmorUseArt(session) : null) is not { } worn || worn == body,
            $"the worn body armour's body is drawn: {_heroBodyStatus}");
        var profile = _worldActor.Profile;
        Require(!_heroBodyFailures.ContainsKey(body), "the body did not load: " + _heroBodyStatus);
        Require(ReferenceEquals(_readyActorPlan?.Profile, profile), "the plan draws the actor's profile");
        Require(profile.Actions.All(action => profile.Resource(action).Selector == body.Selector(ActorDiagnosticProfile.HumanFixedArea.Resource(action).Selector)),
            $"{hero.Race} {hero.Gender}: every action is the {body.Label} body");
        Require(_readyActor is { } drawn && (drawn.TextureKey.Selector & 0x0FF00030u) == body.Selector(0) && _cache.TryGet(drawn.TextureKey, out _),
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
            Require(_readyActor is { } drawn && (drawn.TextureKey.Selector & 0x0FF00030u) == body.Selector(0) && _cache.TryGet(drawn.TextureKey, out _),
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

    private void NoteHeroBodyFailure(HeroBody body, string reason)
    {
        _heroBodyFailures[body] = reason;
        _heroBodyStatus = $"fixed human ({body.Label} not drawn: {reason})";
        GD.Print($"ARC-UI-039 hero body={body.Label} FAILED {reason}; the fixed human is drawn");
    }
}
