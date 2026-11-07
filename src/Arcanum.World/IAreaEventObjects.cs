using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;

namespace Arcanum.World;

/// <summary>ARC-WORLD-039: the area objects an event world asks about (<see cref="AreaCombatHost"/> in the viewer): an NPC's
/// life facts and an object's presence in the stored objects. ARC-DEV-076 (MIG3): both by identity, wherever the object stands now
/// (the resolver finds its record); there is no home-map question any more.</summary>
public interface IAreaEventObjects
{
    LifeFacts? Facts(string entity);
    bool HasObject(string entity);
    /// <summary>ARC-WORLD-103: the NPC's combat values from its stored fields (its wearer and best-items chooser), or null.</summary>
    SessionCritterCombat? Critter(string entity) => null;
}
