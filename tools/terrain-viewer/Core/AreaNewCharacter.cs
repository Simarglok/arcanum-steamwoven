using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The race, gender and background buttons of the new-character window (the original's buttons 8/9, 4/5 and 6/7, ledger NW1).</summary>
public enum AreaNewCharacterButton { GenderPrevious, GenderNext, RacePrevious, RaceNext, BackgroundPrevious, BackgroundNext }

/// <summary>
/// ARC-UI-030 (N10b): the original's new-character window (window 12) as far as Steamwoven has it, Godot-free: the PC it makes from
/// the generic prototype (human, male, background 0; ledger NW1) and its race, gender and background buttons. A race or gender press
/// that changes it clears the background first (BD10; the original's refused gender press clears it too, NW4, under the Original rule
/// setting only); a background press clears and then sets the block the table's creation button lands on (BD4, BD10), with its
/// description number and effect. The background the window starts with (0) has neither a description number nor its effect: only a
/// background press sets them, as in the original. <see cref="Apply"/> gives a session made from <see cref="Hero"/> the background the
/// window holds. The portrait, the name, the character editor and the shop that the original's Accept leads to are later slices.
/// Caller-serialized.
/// </summary>
public sealed class AreaNewCharacter
{
    /// <param name="generic">The new character's ready load (<see cref="AreaStartingHero.NewCharacter"/>); its definition's race and
    /// gender are the start.</param>
    public AreaNewCharacter(AreaHeroLoad generic)
    {
        Generic = generic ?? throw new ArgumentNullException(nameof(generic));
        if (generic is not { Status: AreaHeroStatus.Ready, Definition: { } start } || generic.Hero.BasicPrototype != AreaStartingHero.GenericPrototype)
            throw new ArgumentException("not the new character's ready load", nameof(generic));
        if (!CharacterCreationRules.Allows(start.Race, start.Gender)) throw new ArgumentException($"{start.Race} {start.Gender} is not a creation character", nameof(generic));
        Race = start.Race; Gender = start.Gender; Background = start.Background;
    }

    public AreaHeroLoad Generic { get; }
    public CharacterRace Race { get; private set; }
    public CharacterGender Gender { get; private set; }
    /// <summary>The background id (the original's field 0x101): what the background buttons step from.</summary>
    public int Background { get; private set; }
    /// <summary>What the last background press set (its block, description number and effect); null after a clear or before any press.</summary>
    public BackgroundChoice? Chosen { get; private set; }

    /// <summary>One press: null when it acted (or changed nothing, as the original's), else why it was refused (nothing changed).</summary>
    public string? Press(AreaNewCharacterButton button, CharacterCreationRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        switch (button)
        {
            case AreaNewCharacterButton.GenderPrevious or AreaNewCharacterButton.GenderNext:
                Take(CharacterCreationRules.ToggleGender(Race, Gender, rules));
                return null;
            case AreaNewCharacterButton.RacePrevious:
                Take(CharacterCreationRules.PreviousRace(Race, Gender));
                return null;
            case AreaNewCharacterButton.RaceNext:
                Take(CharacterCreationRules.NextRace(Race, Gender));
                return null;
            case AreaNewCharacterButton.BackgroundPrevious or AreaNewCharacterButton.BackgroundNext:
                if (Generic.Backgrounds is not { } table) return "the background table did not read: " + Generic.BackgroundsDetail;
                var race = (int)Race;
                var gender = Gender == CharacterGender.Female ? BackgroundTable.FemaleGender : BackgroundTable.MaleGender;
                var block = button == AreaNewCharacterButton.BackgroundNext ? table.ButtonNext(Background, race, gender) : table.ButtonPrevious(Background, race, gender);
                if (block is not { } landed) return null; // no block is offered: the button changes nothing (BD4)
                // The original reads the two slots with atoi; the reader refuses a slot it would read partially (latent, BD8).
                if (table.Choice(landed) is not { } choice) return $"background {landed} has no readable description or effect number";
                Background = landed; Chosen = choice;
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(button));
        }
    }

    /// <summary>The hero the window would start: the generic hero with its race and gender (<see cref="AreaStartingHero.NewCharacterHero"/>).</summary>
    public AreaHeroLoad? Hero(out string? detail) => AreaStartingHero.NewCharacterHero(Generic, Race, Gender, out detail);

    /// <summary>The name and text of the shown background: the description the window holds (none: the default text 1000, as the
    /// original draws field 0x102 = 0), decoded as the game's texts are; null when the table did not read or the text is missing.</summary>
    public (string Name, string Body)? BackgroundText()
    {
        if (Generic.Backgrounds is not { } table) return null;
        var text = table.Text(Chosen?.Description ?? 0);
        if (text.Status == BackgroundTextStatus.Missing) return null;
        static string Decode(ReadOnlyMemory<byte> bytes) =>
            MesText.Decode(bytes.Span, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } decoded } ? decoded : "";
        return (Decode(text.Name), Decode(text.Body));
    }

    /// <summary>
    /// The window's background on <paramref name="owner"/>, a session just made from <see cref="Hero"/> (background 0, no effect): a set
    /// with the chosen block's description number and effect (cause 1, ARC-WORLD-097), or nothing when no background press set one.
    /// <paramref name="effects"/> answers the effect rule of a number (<see cref="AreaHeroLoad.Effects"/>); a number without one adds an
    /// entry with no changes, as the original's effect list does. Null on success, else why the session refused it.
    /// </summary>
    public string? Apply(GameSessionOwner owner, Func<int, EffectRule?> effects)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(effects);
        if (Chosen is not { } choice) return null;
        var effect = effects(choice.Effect) is { } rule ? SessionEffectDefinition.FromRule(rule) : new SessionEffectDefinition(choice.Effect, []);
        var set = owner.SetHeroBackground(owner.Version, choice, effect);
        return set.Disposition == GameSessionDisposition.Rejected ? $"the background {choice.Background} was refused ({set.Failure})" : null;
    }

    private void Take(CreationPress press)
    {
        if (press.ClearsBackground) { Background = SessionBackgrounds.None; Chosen = null; }
        Race = press.Race; Gender = press.Gender;
    }
}
