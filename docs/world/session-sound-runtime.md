# Session music and ambient state (ARC-WORLD-040)

Status: **Implemented bounded component**, task 4 of the
[audio plan](../research/audio-evidence.md#task-plan). The session owns which music and ambient
schemes play, as the original's sound subsystem keeps them: two scheme slots, the pair a finished
transition song restores, combat music with the world pair it restores, and the movie lock. It
holds identities only; what plays from them, when within their hours, with which fades, is the
player's (ARC-UI-041). Behaviour rows SN10–SN12, SN15, SN17–SN20 of the
[audio study](../research/audio-evidence.md), checked in the 1.0.7.4 executable.

## State

`SessionSoundState` (saved): `Slot0`, `Slot1` (scheme ids, 0 = empty), `RestorePending`,
`Restore0`/`Restore1` (the pair a finished transition song requests again), `CombatMusic`,
`PreCombat0`/`PreCombat1` (the world pair that plays when combat music stops). `QuerySound()`
returns it with the movie lock depth and a counter of combat-music starts; neither is saved (the
original saves neither), so a load never replays a stinger.

## Calls (all versioned; a change takes one revision)

| Call | Original behaviour |
| --- | --- |
| `PlaySoundScheme(music, ambient)` | The scheme request (SN11): dropped while locked; an ambient equal to the music becomes 0; while combat music plays the pair only becomes the restore target; otherwise ids already in a slot keep playing, a slot no longer requested stops (its id becomes its restore id unless a restore is pending) and the others load into free slots. 0 leaves a slot empty. Scripts call it unchanged (`play_sound_scheme`, ARC-SCRIPT-040). |
| `EnterMapSound(music?, ambient?)` | Every map open (SN19): every sound stops, a pending restore is dropped, combat music and the lock are reset, then the map info's `SoundScheme` pair plays; without that line nothing plays until a sector or script asks. |
| `EnterSectorSound(music, ambient)` | The local PC placed in another sector (SN20): the pair plays unless both ids are 0. |
| `FinishSoundTransition()` | The player reports that a transition (`/over`) song ended: the restore pair is requested and the flag cleared. No installed scheme has a transition song. |
| `LockSound()` / `UnlockSound()` | A movie (SN10, nests): the slots stop and every request is dropped; the last unlock requests the pair saved by the first lock. No movie runtime calls it yet. |
| `AttachSoundSchemes(schemes)` | The host attaches the catalogue's facts: whether a scheme id fills its slot (an index entry without a list base, such as "No Sound", leaves it empty) and whether it starts a transition. Without them every non-zero id fills its slot. Not saved. |

Combat music follows combat mode (SN17): when an NPC enters combat mode (`SetCombatMode`,
real-time or turn-based) and combat music is not already on, the slots become the pre-combat pair
and stop, and combat music starts; when the PC leaves combat mode (the toggle, the exit before a
dialog, or its death notice), combat music stops and the latest world pair requested during
combat plays. There is one flag, no nesting.

## Snapshot schema 18

`sound`: `{"slots":[s0,s1],"restorePending":b,"restore":[r0,r1],"combatMusic":b,"preCombat":[p0,p1]}`,
ids 0 or more. Restore refuses (`InvalidSound`) two equal non-zero slots and filled slots while
combat music is flagged (no transition produces either). Loading follows the original (SN18): the
restore and combat state first, then a request of the saved slots. A save made during combat music
stores empty slots; in the **Original** rule setting that request replaces the saved pre-combat
pair (the original's bug: combat stays silent and leaving it restores silence), in the
**Steamwoven** setting the pre-combat pair is kept (`SoundRuleProfile.CombatSaveLosesSchemes`,
[rule modes](rule-modes.md#registry)).

## Registry and checks

Nine session functions join the [function registry](../verification/README.md) (F2): the scheme
request (full), load, stop, stop all, reset, combat music start and stop, lock and unlock
(partial: identities only). `tests/Arcanum.Session.Tests/SoundStateTests.cs` covers the request
cases, unloadable ids, the transition restore, combat music from an NPC and back from the PC, map
and sector entries, the lock, the save in both rule settings and refused saves.
