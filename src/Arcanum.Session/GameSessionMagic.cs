using Arcanum.Simulation.Character;
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

    /// <summary>
    /// The hero's fate save against magick for <see cref="SpellResistanceRules.Resist"/> when the hero is the target (ARC-MAGIC-005,
    /// MG13): asking it uses the armed <see cref="FateKind.SaveAgainstMagick"/> through <see cref="UseHeroFate"/> (0x4F5270, one
    /// revision when it fires) and answers whether it was armed; without a hero, or with that fate not armed, it answers false. A
    /// use the session refuses otherwise (its revision exhausted) throws <see cref="InvalidOperationException"/>, as a refused roll does.
    /// </summary>
    public ISpellFateSave HeroMagickSave { get { ThrowIfDisposed(); return new SessionHeroMagickSave(this); } }
}

/// <summary>The hero's armed "save against magick", used when the resistance asks it.</summary>
internal sealed class SessionHeroMagickSave(GameSessionOwner owner) : ISpellFateSave
{
    public bool ConsumeSaveAgainstMagick()
    {
        var result = owner.UseHeroFate(owner.Version, FateKind.SaveAgainstMagick);
        return result.Disposition switch
        {
            GameSessionDisposition.Changed => true,
            GameSessionDisposition.NoChange => false,
            _ when result.Failure == GameSessionFailure.FateUnavailable => false,
            _ => throw new InvalidOperationException($"The hero's fate save was refused: {result.Failure}."),
        };
    }
}
