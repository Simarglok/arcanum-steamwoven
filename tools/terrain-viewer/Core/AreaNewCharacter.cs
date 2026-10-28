using System.Collections.Immutable;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The race, gender, background and portrait buttons of the new-character window (the original's buttons 8/9, 4/5, 6/7 and
/// 2/3, ledger NW1).</summary>
public enum AreaNewCharacterButton { GenderPrevious, GenderNext, RacePrevious, RaceNext, BackgroundPrevious, BackgroundNext, PortraitPrevious, PortraitNext }

/// <summary>
/// ARC-UI-030 (N10b): the original's new-character window (window 12) as far as Steamwoven has it, Godot-free: the PC it makes from
/// the generic prototype (human, male, background 0; ledger NW1) and its race, gender and background buttons. A race or gender press
/// that changes it clears the background first (BD10; the original's refused gender press clears it too, NW4, under the Original rule
/// setting only); a background press clears and then sets the block the table's creation button lands on (BD4, BD10), with its
/// description number and effect. The background the window starts with (0) has neither a description number nor its effect: only a
/// background press sets them, as in the original. <see cref="Apply"/> gives a session made from <see cref="Hero"/> the background the
/// window holds. ARC-UI-033: the portrait buttons step through the portraits the race and gender may take (<see cref="PortraitCatalogue.Press"/>,
/// PO9) and a race or gender change takes the first one it finds; the name field types at its end (<see cref="CharacterNameRules"/>,
/// NW6–NW8), and <see cref="AcceptName"/> checks it as the original's Accept does. The character editor and the shop that the
/// original's Accept leads to are later slices. Caller-serialized.
/// </summary>
public sealed class AreaNewCharacter
{
    /// <param name="generic">The new character's ready load (<see cref="AreaStartingHero.NewCharacter"/>); its definition's race and
    /// gender are the start.</param>
    /// <param name="defaultName">The default name, main-menu message 500 as the game stores it (<see cref="CharacterNameRules.DefaultNameMessage"/>):
    /// the field starts with it and Accept refuses it; null takes <see cref="ProjectDefaultName"/>.</param>
    public AreaNewCharacter(AreaHeroLoad generic, IReadOnlyList<byte>? defaultName = null)
    {
        Generic = generic ?? throw new ArgumentNullException(nameof(generic));
        if (generic is not { Status: AreaHeroStatus.Ready, Definition: { } start } || generic.Hero.BasicPrototype != AreaStartingHero.GenericPrototype)
            throw new ArgumentException("not the new character's ready load", nameof(generic));
        if (!CharacterCreationRules.Allows(start.Race, start.Gender)) throw new ArgumentException($"{start.Race} {start.Gender} is not a creation character", nameof(generic));
        Race = start.Race; Gender = start.Gender; Background = start.Background;
        // The generic prototype's portrait (1005, NW1); the window only changes it through its buttons and race or gender changes.
        Portrait = generic.Read?.Hero?.Portrait ?? 0;
        DefaultName = [.. (defaultName ?? ProjectDefaultName).TakeWhile(b => b != 0)];
        Name = CharacterNameRules.Default(DefaultName);
    }

    /// <summary>The project's default name when the game's message 500 is not read (a project text, not the game's).</summary>
    public static IReadOnlyList<byte> ProjectDefaultName { get; } = "Enter Name"u8.ToArray();

    /// <summary>The default name (message 500) the field starts with and Accept refuses.</summary>
    public ImmutableArray<byte> DefaultName { get; }
    /// <summary>The portrait id (the original's field 0xE7).</summary>
    public int Portrait { get; private set; }
    /// <summary>The name field's bytes.</summary>
    public ImmutableArray<byte> Name { get; private set; }
    /// <summary>The name field is being typed into (a click on it began the typing).</summary>
    public bool TypingName { get; private set; }
    private ImmutableArray<byte> _nameBefore = [];

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
        // A click elsewhere ends the name's typing (0x549450): an empty field takes back its old text.
        EndName(enter: false);
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
            case AreaNewCharacterButton.PortraitPrevious or AreaNewCharacterButton.PortraitNext:
                if (Generic.Portraits is not { } portraits) return "the portraits did not read: " + Generic.PortraitsDetail;
                Portrait = portraits.Press(button == AreaNewCharacterButton.PortraitNext ? PortraitButton.Next : PortraitButton.Previous, Portrait, Subject());
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(button));
        }
    }

    /// <summary>The hero the window would start: the generic hero with its race and gender (<see cref="AreaStartingHero.NewCharacterHero"/>).</summary>
    public AreaHeroLoad? Hero(out string? detail) => AreaStartingHero.NewCharacterHero(Generic, Race, Gender, out detail, Portrait);

    /// <summary>The shown portrait's text (its image's file stem), decoded as the game's texts are; null when the catalogue did not read
    /// or the portrait has no readable text.</summary>
    public string? PortraitText()
    {
        if (Generic.Portraits?.Stem(Portrait) is not { Status: PortraitStemStatus.Found } stem) return null;
        return MesText.Decode(stem.Text.Span, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { } text } ? text : null;
    }

    /// <summary>The name field decoded as the game's texts are (a byte the profile lacks shows as '?').</summary>
    public string NameText() => MesText.Decode(Name.AsSpan(), AreaHoverIndex.TextProfile).Text
        ?? new string([.. Name.Select(b => MesText.Decode([b], AreaHoverIndex.TextProfile).Text is [var c] ? c : '?')]);

    /// <summary>A click on the name field (0x544FF0): it keeps its text to restore, clears and starts typing; nothing while typing.</summary>
    public void BeginName()
    {
        if (TypingName) return;
        _nameBefore = Name; Name = []; TypingName = true;
    }

    /// <summary>A typed character while the field is being typed into: its byte in the game's encoding at the field's end. Null when
    /// it acted, else why it was refused (a character the encoding lacks, a control character or a full field; nothing changed).</summary>
    public string? TypeName(char typed)
    {
        if (!TypingName) return "the name field is not being typed into";
        if (MesText.Encode(typed.ToString(), AreaHoverIndex.TextProfile) is not [var b]) return $"'{typed}' has no byte in the game's text encoding";
        if (CharacterNameRules.Type(Name, b) is not { } longer) return Name.Length >= CharacterNameRules.TypedLength ? "the name field is full" : "the name field refuses control characters";
        Name = longer;
        return null;
    }

    /// <summary>Backspace while typing: the field without its last byte.</summary>
    public void BackspaceName()
    {
        if (TypingName) Name = CharacterNameRules.Backspace(Name);
    }

    /// <summary>The end of typing: Enter (<paramref name="enter"/>) gives an empty field the default name, a click elsewhere its old
    /// text (<see cref="CharacterNameRules.EndTyping"/>); nothing when not typing.</summary>
    public void EndName(bool enter)
    {
        if (!TypingName) return;
        Name = CharacterNameRules.EndTyping(Name, _nameBefore, enter, DefaultName);
        TypingName = false;
    }

    /// <summary>The Accept check (NW6): the field as Accept leaves it (an empty one takes the default name) and whether it passes;
    /// a refused name is the window's message 506.</summary>
    public CreationNameAccept AcceptName()
    {
        var check = CharacterNameRules.Accept(Name, DefaultName);
        Name = check.Name;
        return check;
    }

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
    /// The window's name and background on <paramref name="owner"/>, a session just made from <see cref="Hero"/> (ARC-UI-033: the name
    /// field's bytes as the player name when they pass Accept's check, <see cref="AcceptName"/>; background 0, no effect): a set
    /// with the chosen block's description number and effect (cause 1, ARC-WORLD-097), or nothing when no background press set one.
    /// <paramref name="effects"/> answers the effect rule of a number (<see cref="AreaHeroLoad.Effects"/>); a number without one adds an
    /// entry with no changes, as the original's effect list does. Null on success, else why the session refused it.
    /// </summary>
    public string? Apply(GameSessionOwner owner, Func<int, EffectRule?> effects)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(effects);
        // ARC-UI-033: a name that passes Accept's check goes into the player-name field (0x10E) first, as the original's Accept stores
        // it; the default name, which the check refuses, is never stored.
        if (CharacterNameRules.Accept(Name, DefaultName) is { Accepted: true, Name: var accepted }
            && owner.SetHeroPlayerName(owner.Version, accepted) is { Disposition: GameSessionDisposition.Rejected } named)
            return $"the name was refused ({named.Failure})";
        if (Chosen is not { } choice) return null;
        var effect = effects(choice.Effect) is { } rule ? SessionEffectDefinition.FromRule(rule) : new SessionEffectDefinition(choice.Effect, []);
        var set = owner.SetHeroBackground(owner.Version, choice, effect);
        return set.Disposition == GameSessionDisposition.Rejected ? $"the background {choice.Background} was refused ({set.Failure})" : null;
    }

    private void Take(CreationPress press)
    {
        if (press.ClearsBackground) { Background = SessionBackgrounds.None; Chosen = null; }
        Race = press.Race; Gender = press.Gender;
        // A race or gender change stores the first portrait the new pair may take, when there is one (0x5453A0, 0x545550; PO9).
        if (press.Changed && Generic.Portraits is { } portraits) Portrait = portraits.AfterRaceOrGender(Portrait, Subject());
    }

    private PortraitSubject Subject() =>
        new((int)Race, Gender == CharacterGender.Female ? PortraitCatalogue.FemaleGender : PortraitCatalogue.MaleGender);
}
