using Arcanum.Data;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-039: the AREA hero is drawn with its own body (race and gender, HeroBody), not the fixed human woman every map load
// publishes. Each frame the session hero's body is compared with the drawn one; on a change (a new game, a loaded save, a map
// publication that brought the fixed human back) the body's ARTs are read from the actor archive once a run, uploaded, and the
// actor, its gait timing and its plan are swapped (NPCs and companions keep the human timing). A body that cannot be read or
// swapped is drawn as the fixed human, with its reason in the log, and is not tried again that run.
public partial class TerrainViewerApp
{
    private readonly Dictionary<HeroBody, HeroBodyLoad> _heroBodies = new();
    private readonly Dictionary<HeroBody, string> _heroBodyFailures = new();
    private string _heroBodyStatus = "fixed human";
    private IReadOnlyList<ActorDiagnosticFrame>? _humanActorFrames;

    /// <summary>The body the session hero should be drawn with: its race and gender's, the fixed human without a hero.</summary>
    private HeroBody WantedHeroBody() =>
        _areaSession?.QueryHero() is { } hero ? HeroBody.For(hero.Race, hero.Gender) ?? HeroBody.HumanFemale : HeroBody.HumanFemale;

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
        var body = HeroBody.For(hero.Race, hero.Gender) ?? HeroBody.HumanFemale;
        var profile = _worldActor.Profile;
        Require(!_heroBodyFailures.ContainsKey(body), "the body did not load: " + _heroBodyStatus);
        Require(ReferenceEquals(_readyActorPlan?.Profile, profile), "the plan draws the actor's profile");
        Require(profile.Actions.All(action => profile.Resource(action).Selector == body.Selector(ActorDiagnosticProfile.HumanFixedArea.Resource(action).Selector)),
            $"{hero.Race} {hero.Gender}: every action is the {body.Label} body");
        Require(_readyActor is { } drawn && (drawn.TextureKey.Selector & 0x0FF00030u) == body.Selector(0) && _cache.TryGet(drawn.TextureKey, out _),
            "the drawn frame is the body's and uploaded: " + _readyActor?.TextureKey);
        Require(_worldActor.AreaGaitTiming is { } timing && timing.RateBonus == body.GaitRateBonus
            && (_worldActor.Session as AreaGridSession)?.Timing == timing, "the steps take the body's gait timing");
        GD.Print($"ARC-UI-039 native-smoke hero body PASS race={hero.Race} gender={hero.Gender} body={body.Label} profile={profile.Id} "
            + $"idle=0x{profile.Idle.Selector:x8} walkRate={_worldActor.AreaGaitTiming!.ArtRate(MovementGait.Walk)}");
    }

    private void NoteHeroBodyFailure(HeroBody body, string reason)
    {
        _heroBodyFailures[body] = reason;
        _heroBodyStatus = $"fixed human ({body.Label} not drawn: {reason})";
        GD.Print($"ARC-UI-039 hero body={body.Label} FAILED {reason}; the fixed human is drawn");
    }
}
