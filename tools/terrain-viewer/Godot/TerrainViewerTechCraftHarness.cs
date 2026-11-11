using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-362: the technology smoke harness. The steps a technology check on the player's data needs, written once so the stage "tech-craft" and the
// stages and checks that follow it (the status table of the technology audit, ARC-RES-104 T-b) share them: wait for the schematic data of the module, give
// the hero unspent points, buy a degree on the sheet's tech page (the sheet's own + press), put a component item in the pack (the session creates it by
// prototype, the hero picks it up), assemble a learned schematic through the schematic window (its own button, its own session call), list what the hero
// holds, and damage or heal the hero. Every step Requires its own effect, so a stage built from them cannot pass on a step that did nothing. Prints no game text.
public partial class TerrainViewerApp
{
    internal const int TechDisciplineHerbology = 0, TechDisciplineGunSmithy = 4;
    internal const string TechCraftCode = "ARC-DEV-362";

    internal static void TechSmokeRequire(bool value, string why)
    {
        if (value) return;
        GD.Print($"{TechCraftCode} tech-craft smoke FAIL: " + why);
        throw new InvalidOperationException("AREA_TECH_CRAFT_SMOKE_FAILED");
    }

    /// <summary>The schematic data of the module (catalogue, texts, the prototypes' facts and kinds), loaded in the background at the area's start.</summary>
    internal async Task<AreaSchematicData> TechSmokeDataAsync()
    {
        for (var i = 0; i < 1800 && _schematicData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptSchematicData(); }
        TechSmokeRequire(_schematicData is { Status: AreaSchematicDataStatus.Ready, Data: not null }, $"schematic data {_schematicData?.Status} {_schematicData?.Detail}");
        return _schematicData!.Data!;
    }

    /// <summary>The hero has at least <paramref name="need"/> unspent points: an experience award while its auto-level scheme is off (a scheme would spend the points).</summary>
    internal static void TechSmokeEnsurePoints(GameSessionOwner session, int need)
    {
        var scheme = session.QueryHero()!.Advancement.AutoLevelScheme;
        if (session.QueryHeroExperience() is { UnspentPoints: var have } && have >= need) return;
        if (scheme != 0) session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, 0);
        for (var round = 0; round < 12 && session.QueryHeroExperience() is { } now && now.UnspentPoints < need; round++)
            session.AwardHeroExperience(session.Version, Math.Max(2000, (now.ToNextLevel ?? 0) * 4 / 3 + 1));
        if (scheme != 0) session.SetAutoLevelScheme(session.Version, SessionSchemeTarget.Hero, scheme);
        TechSmokeRequire(session.QueryHeroExperience() is { } after && after.UnspentPoints >= need, $"an experience award buys {need} points ({session.QueryHeroExperience()?.UnspentPoints})");
    }

    /// <summary>One frame of a technology stage's clock (twenty ticks to the second): the spell caster's tick, the hero's runs and one world frame with the stick released.</summary>
    internal void TechSmokeFrame()
    {
        _areaSmokeTime++;
        SpellSmokeTick(); SmokeTickHeroRuns();
        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
    }

    /// <summary>The hero's degree in a discipline.</summary>
    internal static int TechSmokeDegree(GameSessionOwner session, int discipline) => session.QueryHeroTechnology()!.Value[discipline].Degree;

    /// <summary>One + press of the sheet's tech page (the sheet must be open: its editor takes the points); the degrees it bought.</summary>
    internal int TechSmokeBuyDegree(int discipline)
    {
        var session = _areaSession!;
        TechSmokeRequire(SheetOpen && session.HeroEditorOpen, "the sheet is open with its editor for a tech page press");
        var before = TechSmokeDegree(session, discipline);
        PressTech(discipline, spend: true);
        return TechSmokeDegree(session, discipline) - before;
    }

    /// <summary>Everything the hero holds (kit, pack and made items) with its kind, without the made items that still lie on the ground.</summary>
    internal static List<(string Entity, SessionItemKind Kind)> TechSmokeHeldKinds(GameSessionOwner session)
    {
        var ground = session.QueryPlacedItems().Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        return session.QueryHeroKit().Select(item => (item.Entity, item.Kind)).Concat(session.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .Concat(session.QueryMadeItems().Select(item => (item.Entity, item.Kind))).Where(item => !ground.Contains(item.Entity)).DistinctBy(item => item.Entity).ToList();
    }

    /// <summary>The entities the hero holds.</summary>
    internal static HashSet<string> TechSmokeHeld(GameSessionOwner session) => TechSmokeHeldKinds(session).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);

    /// <summary>The multiset of what the hero holds as "prototype|units|place" lines (units of an ammunition stack, 1 otherwise; worn slot or bag), sorted.</summary>
    internal static string[] TechSmokeHeldLines(GameSessionOwner session)
    {
        var worn = session.QueryEquipment().Worn.ToDictionary(item => item.Entity, item => item.Slot);
        return TechSmokeHeldKinds(session).Select(item => $"{item.Kind.Prototype}|{session.QueryAmmo(item.Entity)?.Units ?? 1}|{(worn.TryGetValue(item.Entity, out var at) ? "worn:" + at : "bag")}")
            .OrderBy(line => line, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The prototype of an item the hero holds, or null.</summary>
    internal static int? TechSmokePrototype(GameSessionOwner session, string entity) =>
        TechSmokeHeldKinds(session).Where(item => item.Entity == entity).Select(item => (int?)item.Kind.Prototype).FirstOrDefault();

    /// <summary>The prototype's item is created next to the hero (by the session, as a script's create object does) and picked up through the inventory host.</summary>
    internal string TechSmokeGive(AreaSchematicData data, int prototype)
    {
        var session = _areaSession!;
        TechSmokeRequire(data.Kind(prototype) is not null && _inventoryHost is not null && AreaDropMapNow() is not null, $"prototype {prototype} has a kind and the host a drop map");
        session.AttachItemWeights(data.WeightOf);
        var created = session.CreateGroundItem(session.Version, data.Kind(prototype)!.Value, new SessionMapId(CurrentAreaMap), session.QueryActor().Cell, AreaDropMapNow()!, DropOwner.PcSide);
        TechSmokeRequire(created.Disposition == GameSessionDisposition.Changed && created.Item is not null, $"prototype {prototype} was created: {created.Disposition} {created.Failure}");
        var pick = _inventoryHost!.PickUp(session, created.Item!);
        TechSmokeRequire(AreaInventoryHost.Moved(pick) && TechSmokeHeld(session).Contains(created.Item!), $"prototype {prototype} was picked up: {pick.Disposition} {pick.Failure}");
        return created.Item!;
    }

    /// <summary>The record's alternative of the list with the smallest complexity (the one a degree-1 hero can use), as a prototype number.</summary>
    internal static int TechSmokeCheapest(AreaSchematicData data, IEnumerable<int> bases)
    {
        var prototypes = bases.Select(b => b + SchematicAlternatives.PrototypeOffset).Where(p => data.Facts(p) is not null).ToArray();
        TechSmokeRequire(prototypes.Length > 0, "a component alternative has facts");
        return prototypes.OrderBy(p => Math.Abs(data.Facts(p)!.Value.Complexity)).ThenBy(p => p).First();
    }

    /// <summary>The result of one assembly through the window: what the hero's pack gained and lost, the product's prototype and the window's message.</summary>
    internal sealed record TechSmokeCraftResult(int Record, int[] ComponentPrototypes, string[] Made, string[] Consumed, string Message);

    /// <summary>
    /// A learned record of a discipline is assembled as the player does it: the two components (the cheapest alternative of each list) are put in the pack, the
    /// schematic window opens (T), shows the discipline's page of the record and its assemble button works. Requires the window's button to allow it, the products to
    /// be new made items of the product's prototype, and both components to be gone from the pack. The window is closed again.
    /// </summary>
    internal TechSmokeCraftResult TechSmokeCraft(AreaSchematicData data, int discipline, int record)
    {
        var session = _areaSession!;
        var read = data.Catalog.Read(record).Record;
        TechSmokeRequire(read is not null, $"the catalogue reads record {record}");
        var components = new[] { TechSmokeCheapest(data, read!.Component1.Bases), TechSmokeCheapest(data, read.Component2.Bases) };
        var heldBefore = TechSmokeHeld(session);
        var given = components.Select(prototype => TechSmokeGive(data, prototype)).ToArray();
        var afterGive = TechSmokeHeld(session);
        TechSmokeRequire(given.All(afterGive.Contains), "the components are in the pack");

        OpenSchematics();
        TechSmokeRequire(SchematicsOpen, "the schematic window opens: " + _schematicsStatus);
        SelectSchematicDiscipline(discipline);
        for (var turns = 0; turns < 8 && _schematicsPanel!.View is { } view && view.Record != record; turns++) TurnSchematicPage(1);
        var shown = _schematicsPanel!.View;
        TechSmokeRequire(shown is { Placeholder: false } && shown.Record == record, $"the window shows record {record} (shows {shown?.Record})");
        TechSmokeRequire(shown!.Button == AssemblyButton.CanAssemble, $"record {record} can be assembled (button {shown.Button})");
        AssembleSchematic();
        var message = _schematicMessage ?? "";
        TechSmokeRequire(!message.StartsWith("not assembled", StringComparison.Ordinal), $"record {record} was assembled: {message}");
        EndSchematics(byPlayer: true);
        TechSmokeRequire(!SchematicsOpen, "the schematic window closed");

        var heldAfter = TechSmokeHeld(session);
        var made = heldAfter.Except(afterGive).OrderBy(e => e, StringComparer.Ordinal).ToArray();
        var consumed = given.Where(e => !heldAfter.Contains(e)).ToArray();
        TechSmokeRequire(made.Length == read.Quantity, $"record {record} made {read.Quantity} product(s) ({made.Length})");
        TechSmokeRequire(consumed.Length == given.Length, $"the components are gone ({consumed.Length} of {given.Length})");
        return new(record, components, made, consumed, message);
    }

    /// <summary>The hero's hit points now.</summary>
    internal static int TechSmokeHeroHitPoints(GameSessionOwner session) => session.QueryHeroLife()!.Status.HitPoints;

    /// <summary>The hero's fatigue points now.</summary>
    internal static int TechSmokeHeroFatigue(GameSessionOwner session) => session.QueryHeroLife()!.Status.Fatigue;

    /// <summary>The hero is hurt down to one hit point and to a fatigue of one; the hit points and the fatigue lost.</summary>
    internal static (int HitPoints, int Fatigue) TechSmokeHurtHero(GameSessionOwner session)
    {
        var hitPoints = TechSmokeHeroHitPoints(session); var fatigue = TechSmokeHeroFatigue(session);
        TechSmokeRequire(hitPoints > 1 && fatigue > 1, $"the hero has hit points and fatigue to lose ({hitPoints}, {fatigue})");
        var hit = session.ApplyDamage(session.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = hitPoints - 1, Fatigue = fatigue - 1 });
        TechSmokeRequire(hit.Disposition == GameSessionDisposition.Changed && TechSmokeHeroHitPoints(session) == 1 && TechSmokeHeroFatigue(session) == 1,
            $"the hero was hurt to 1 hit point and 1 fatigue ({hit.Disposition} {hit.Failure}, {TechSmokeHeroHitPoints(session)}, {TechSmokeHeroFatigue(session)})");
        return (hitPoints - 1, fatigue - 1);
    }
}
