using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-347 (E1-a): the module's prototype reader (set with the extra art when the content stack opens; null while it does not read) and the art and flags of each prototype a created
    // scenery or eye-candy object names, read once per module (null: the prototype does not read).
    private Func<int, Arcanum.Data.Characters.PrototypeFile>? _createdPrototypeRead;
    private IReadOnlyList<int> _createdPrototypeNumbers = [];
    private readonly Dictionary<int, (uint Art, bool Flat)?> _createdPrototypes = [];

    /// <summary>ARC-DEV-347: the current art and the flat flag (OF_FLAT, 0x4) of the object prototype <paramref name="prototype"/> of the module's content stack, or null when it does not read.</summary>
    private (uint Art, bool Flat)? CreatedPrototype(int prototype)
    {
        if (_createdPrototypes.TryGetValue(prototype, out var known)) return known;
        if (ExtraArt() is null || _createdPrototypeRead is not { } read) return null;
        var fields = ObjectSoundReader.ReadPrototype(read, prototype).Fields;
        // A wall's or portal's art class (1, 3) is no body AreaScenePlan.Place draws (it refuses it): a save naming such a prototype is said once and the object not drawn.
        (uint, bool)? facts = fields is null || AreaDataOwner.NamesNoArt(fields.CurrentArt) || fields.CurrentArt >> 28 is 1 or 3 ? null : (fields.CurrentArt, (fields.Flags & 0x4) != 0);
        _createdPrototypes[prototype] = facts;
        return facts;
    }

    /// <summary>
    /// ARC-DEV-347 (E1-a): each created scenery and eye-candy object lying on the shown map, drawn at its cell with its pixel offsets from its prototype's current art (flat when the prototype's
    /// flags say so), under the key a placed item gets. A scenery is picked like a junk pile (its row joins the hover index; a click on it is no pickup, <see cref="AreaInventoryTargetOf"/>); an eye-candy
    /// object is drawn and never picked. A prototype that does not read is said once and not drawn.
    /// </summary>
    private void AddCreatedObjectBodies(AreaLoadResult area, Dictionary<AreaObjectKey, AreaDrawCommand> bodies, Dictionary<AreaObjectKey, (string, CellCoord, AreaObjectRecord)> only,
        Func<uint, string, AreaExtraFrame?> frameOf)
    {
        if (_areaSession is not { } session) return;
        foreach (var made in session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap)))
        {
            if (CreatedPrototype(made.Prototype) is not { } facts || frameOf(facts.Art, "created") is not { } frame)
            {
                if (_groundReported.Add("nocreated:" + made.Entity)) GD.Print($"ARC-DEV-347 created-object entity={made.Entity} prototype={made.Prototype} not drawn: no art");
                continue;
            }
            var key = PlacedKey(area.Snapshot.RuntimeMap, made.Entity);
            var command = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, made.Cell.X, made.Cell.Y, facts.Flat);
            var at = command.Destination;
            bodies[key] = command with { Destination = new(checked(at.X + made.OffsetX), checked(at.Y + made.OffsetY), at.Width, at.Height) };
            if (made.Kind == SessionCreatedKind.Scenery)
                only[key] = (made.Entity, made.Cell, new AreaObjectRecord(key, 0, SceneryType, made.Prototype, "", "", "", "", AreaScope.Core, null, made.Cell.X, made.Cell.Y,
                    Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true));
        }
    }

    /// <summary>
    /// ARC-DEV-347 (E1-a) native observation (stage "created-scenery", only when named, with --hero, on a map window with the inventory active): the session creates a scenery and an eye-candy object by
    /// prototype next to the hero (two scenery prototypes of the module's data with art, found by a scan); the scenery is drawn at its cell and picked (its hover row is admitted, a click on it is no pickup),
    /// the eye-candy object is drawn and not picked; both are residents of the open map when its residency is open, are in the save, and are gone from the picture, the directory and the save once destroyed.
    /// A refused creation (a prototype that is none) leaves the session as it was. The map's own state is left as it was.
    /// </summary>
    private async Task RunAreaCreatedSceneryStageAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || !InventoryActive || ExtraArt() is not { } art || _createdPrototypeRead is null)
        { GD.Print("ARC-DEV-347 native-smoke created-scenery SKIP (needs --hero, an area session, the inventory and the module's content)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-347 created-scenery smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CREATED_SCENERY_SMOKE_FAILED");
        }
        var runtimeMap = area.Snapshot.RuntimeMap;
        var map = new SessionMapId(CurrentAreaMap);
        var prototypes = new List<int>();
        foreach (var number in _createdPrototypeNumbers)
        {
            if (ObjectSoundReader.ReadPrototype(_createdPrototypeRead, number).Fields is { Type: SceneryType } fields && !AreaDataOwner.NamesNoArt(fields.CurrentArt) && fields.CurrentArt >> 28 is not (1 or 3) && art.Load(fields.CurrentArt).Frame is not null)
                prototypes.Add(number);
            if (prototypes.Count == 2) break;
        }
        if (prototypes.Count < 2) { GD.Print($"ARC-DEV-347 native-smoke created-scenery NONE (the module's data holds {prototypes.Count} scenery prototypes with art, 2 needed)"); return; }
        if (HeroPoolPlace() is not { } hero) { GD.Print("ARC-DEV-347 native-smoke created-scenery SKIP (the hero has no committed cell)"); return; }

        async Task Settle()
        {
            for (var i = 0; i < 3; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); SyncAreaInventory(); }
        }
        var heldBefore = session.QueryCreatedObjects(map).Count;
        // A refused creation: no prototype (number 0). The session is as it was.
        var bad = session.CreateObject(session.Version, SessionCreatedKind.Scenery, 0, map, hero.Cell);
        Require(bad.Disposition == GameSessionDisposition.Rejected && bad.Failure == GameSessionFailure.InvalidCreatedObject, $"a creation with no prototype is refused: {bad.Disposition} {bad.Failure}");
        Require(session.QueryCreatedObjects(map).Count == heldBefore, "a refused creation leaves nothing");

        var scenery = session.CreateObject(session.Version, SessionCreatedKind.Scenery, prototypes[0], map, new CellCoord(hero.Cell.X + 2, hero.Cell.Y), 0, 0).Object;
        var candy = session.CreateObject(session.Version, SessionCreatedKind.EyeCandy, prototypes[1], map, new CellCoord(hero.Cell.X + 3, hero.Cell.Y), 5, -3).Object;
        try
        {
            Require(scenery is not null && candy is not null, "the session created a scenery and an eye-candy object");
            await Settle();
            var sceneryKey = PlacedKey(runtimeMap, scenery!.Entity);
            var candyKey = PlacedKey(runtimeMap, candy!.Entity);
            Require(_groundBodies.TryGetValue(sceneryKey, out var sceneryBody) && sceneryBody.MapX == scenery.Cell.X && sceneryBody.MapY == scenery.Cell.Y, "the scenery is drawn at its cell");
            Require(_groundBodies.TryGetValue(candyKey, out var candyBody) && candyBody.MapX == candy.Cell.X && candyBody.MapY == candy.Cell.Y, "the eye-candy object is drawn at its cell");
            Require(_groundOnly.TryGetValue(sceneryKey, out var picked) && picked.Entity == scenery.Entity && picked.Row.Type == SceneryType && picked.Row.PrototypeNumber == scenery.Prototype, "the scenery is picked under its row");
            Require(!_groundOnly.ContainsKey(candyKey), "the eye-candy object is not picked");
            Require(HoverIndex() is { } index && index.Contains(sceneryKey) && !index.Contains(candyKey), "the hover index holds the scenery's row and not the eye-candy object's");
            Require(AreaInventoryTargetOf(sceneryKey) is null, "a click on the scenery is no pickup");
            var directory = session.QueryObject(scenery.Entity);
            Require(directory is { Origin: SessionObjectOrigin.Scenery, Place: SessionObjectPlace.Ground } && directory.Cell == scenery.Cell && directory.Map == map, "the directory finds the scenery");
            var residents = session.QueryResidents(map);
            GD.Print($"ARC-DEV-347 created-scenery residency={residents.Status}");
            if (residents.Status == ResidencyStatus.Open)
                Require(residents.Residents.Any(r => r.Entity == scenery.Entity && r.Origin == SessionResidentOrigin.Created) && residents.Residents.Any(r => r.Entity == candy.Entity),
                    "the created objects are residents of the open map");
            var saved = GameSessionSnapshotCodec.Encode(session.Capture());
            Require(saved.Contains($"\"entity\":\"{scenery.Entity}\",\"kind\":0,\"prototype\":{scenery.Prototype}", StringComparison.Ordinal)
                && saved.Contains($"\"entity\":\"{candy.Entity}\",\"kind\":1,\"prototype\":{candy.Prototype}", StringComparison.Ordinal), "the save holds both objects");
            GD.Print($"ARC-DEV-347 created-scenery scenery={scenery.Entity}/{scenery.Prototype} candy={candy.Entity}/{candy.Prototype} cell={scenery.Cell.X},{scenery.Cell.Y}");

            Require(session.DestroyCreatedObject(session.Version, scenery.Entity).Disposition == GameSessionDisposition.Changed, "the scenery is destroyed");
            Require(session.DestroyCreatedObject(session.Version, candy.Entity).Disposition == GameSessionDisposition.Changed, "the eye-candy object is destroyed");
            await Settle();
            Require(!_groundBodies.ContainsKey(sceneryKey) && !_groundBodies.ContainsKey(candyKey) && !_groundOnly.ContainsKey(sceneryKey), "neither is drawn or picked once destroyed");
            Require(session.QueryObject(scenery.Entity) is null && session.QueryObject(candy.Entity) is null, "the directory knows neither once destroyed");
            Require(!GameSessionSnapshotCodec.Encode(session.Capture()).Contains($"\"entity\":\"{scenery.Entity}\"", StringComparison.Ordinal), "the save holds neither once destroyed");
            GD.Print("ARC-DEV-347 native-smoke created-scenery PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = runtimeMap, Scenery = prototypes[0], EyeCandy = prototypes[1],
                Residency = residents.Status.ToString() }));
        }
        finally
        {
            foreach (var made in new[] { scenery, candy })
                if (made is not null && session.QueryCreatedObject(made.Entity) is not null) session.DestroyCreatedObject(session.Version, made.Entity);
        }
    }
}
