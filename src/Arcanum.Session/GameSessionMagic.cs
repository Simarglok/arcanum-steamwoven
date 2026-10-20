using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The session's seeded roll source for the magic rules (ARC-MAGIC-005): each roll is a labelled session roll
    /// (<see cref="Roll"/>, label <c>magic.</c> and the <see cref="SpellRollPurpose"/> name), so a resistance, a saving throw or a
    /// fail-chance prediction moves the saved random position as the original's random stream does. A refused roll throws.
    /// </summary>
    public ISpellRollSource SpellRolls { get { ThrowIfDisposed(); return new SessionSpellRolls(this); } }
}
