using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

public sealed partial class AreaAiHost
{
    /// <summary>
    /// ARC-WORLD-1157 (E2-a, ledger PO2 to PO6): the spawner (0x4BA790) of a monster generator, called by the AI tick for an NPC with the generator bit that passed the heartbeat. It answers null (a refusal) only for an object that
    /// is no generator NPC or whose fields are unread; every other path answers the delay of the generator's next tick by its rate, also when nothing was made. The session decides and makes the copies
    /// (<see cref="GameSessionOwner.RunGenerator"/>: the room, the slot's block, the hour, the budget and the placement draws); this host reads the generator's record (its prototype, cell, packed settings and rate), the view
    /// (the off-screen option's rectangle test, SZ6) and the tile classes of the spots (0x4D70B0 → 0x5038C0). The copies are created critters the viewer's sync hosts. The off-screen option's rectangle is the generator's drawn body when the viewer draws one, else its tile's (a limit). A copy is no generator (the host answers false for a critter that carries a generator mark). The copy is built from the generator's own row (ARC-WORLD-1526, ledger GS5: its instance fields over the prototype, unmarked, see <see cref="AreaCreatedCritters"/>); the generator's held items are not copied (a limit, #1652).
    /// </summary>
    public long? Spawn(string npc)
    {
        if (!IsGenerator(npc) || !_npcs.TryGetValue(npc, out var generator) || Fields(npc)?.Npc is not { } fields) return null;
        var rate = GeneratorRules.RateOf(fields.Flags);
        // The spawner answers success for every generator NPC; one whose tile is unknown (it left the host's map) makes nothing.
        if (Tile(npc) is not { } cell) return GeneratorRules.NextDelayMs(rate);
        var screen = _scene.Screen?.Invoke();
        var offScreen = (GeneratorSettings.Unpack(fields.GeneratorData).Options & GeneratorSettings.OffScreen) != 0;
        var home = AreaCellPicking.Center(cell);
        // 0x43DFD0 with mask 8 gives the object's rectangle whatever its draw flags are, but a real generator carries flags 0x400002 (off, invulnerable) and the viewer's draw plan then holds no body for it: the
        // rectangle is then the tile's own (80 by 40 pixels around its centre), a limit (the generator's own art is not read for it).
        var own = offScreen ? screen?.BodyRect(generator.Row.Key) ?? new AreaPixelRect(home.X - 40, home.Y - 20, 80, 40) : (AreaPixelRect?)null;
        var steps = Steps;
        var homeClass = TileClass(steps, cell);
        bool Off(AreaPixelRect? body) => body is not { } rect || screen?.GameWindow is not { } window || !rect.Meets(window);
        var run = new SessionGeneratorRun
        {
            Entity = npc, Map = new SessionMapId(_scene.RuntimeMap), Cell = cell, Prototype = generator.Row.PrototypeNumber, Packed = fields.GeneratorData, Rate = rate,
            DropMap = _scene.DropMap?.Invoke() ?? NoDropMap.Instance,
            OffView = !offScreen || Off(own),
            // 0x4BA910: a spot counts when its tile class is the generator's tile's and, with the off-screen option, the generator's rectangle moved to it is off the view.
            SpotAccepted = spot => TileClass(steps, spot) == homeClass &&
                (!offScreen || Off(own is { } body ? body with { X = body.X + AreaCellPicking.Center(spot).X - home.X, Y = body.Y + AreaCellPicking.Center(spot).Y - home.Y } : null)),
        };
        // The spawner answers success whatever was made (a refused creation is the loop's end with the count so far), so the next tick's delay is the rate's in every case.
        _ = _session.RunGenerator(_session.Version, run);
        return GeneratorRules.NextDelayMs(run.Rate);
    }

    // 0x5038C0 over 0x4D70B0: the tile art word's class bit, bit 8 for art type 0 (the plain tile) and bit 25 for type 11, else 0; a tile with no art reads as -1, whose type is 15.
    private static uint TileClass(AreaStepSnapshot steps, CellCoord cell)
    {
        var art = steps.Terrain(new(cell.X, cell.Y))?.RawArt ?? 0xFFFFFFFF;
        return (art >> 28) switch { 0 => art >> 8 & 1, 0xB => art >> 25 & 1, _ => 0u };
    }

    // The placement's map when the scene gives none (a host without a viewer): no step leaves the map and no path exists, so every try keeps the generator's own tile.
    private sealed class NoDropMap : Arcanum.Simulation.Items.IDropMap
    {
        public static readonly NoDropMap Instance = new();
        public CellCoord? Step(CellCoord from, int rotation) => rotation is >= 0 and <= 7 ? null : from;
        public Arcanum.Simulation.Items.DropPath Path(CellCoord from, CellCoord to, Arcanum.Simulation.Items.DropOwner owner) => Arcanum.Simulation.Items.DropPath.None;
        public bool HasCritter(CellCoord cell) => false;
    }
}
