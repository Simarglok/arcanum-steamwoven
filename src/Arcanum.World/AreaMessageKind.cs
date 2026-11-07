namespace Arcanum.World;

/// <summary>
/// The original's message-log entry types the viewer posts (the entry's first field, ledger ML2–ML5): 0 the level-up entry, 4 a game
/// message with sound 3004, 5 the same text layout without a sound, 6 a hover text (a bar button's name), which is shown but never
/// stored; ARC-UI-035: 7 a skill's hint and 8 a spell's hint (the hotkey bar's hover descriptions, HK20), shown like a hover text.
/// <see cref="Item"/> is not an entry type: it is the item window the original draws straight into the message window (0x554F10,
/// HK22), never stored and drawn whatever the time since the last entry. The other types (1–3 and 9–13) are listed in the ledger
/// and not posted by the viewer.
/// </summary>
public enum AreaMessageKind { LevelUp = 0, Message = 4, Plain = 5, Hover = 6, SkillHint = 7, SpellHint = 8, Item = 100, Object = 101 }
