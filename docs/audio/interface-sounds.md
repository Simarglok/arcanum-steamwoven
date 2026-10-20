# Interface sounds and volume settings (ARC-UI-044)

Status: **Implemented bounded component**, task 10 of the
[audio plan](../research/audio-evidence.md#task-plan) (the last one). The AREA viewer's windows and
messages play the original's interface sounds, and the palette holds the original's three volume
settings, applied at once.

## Interface sounds

Checked in the 1.0.7.4 executable ([audio study](../research/audio-evidence.md) SN31, SN37). Every
interface sound is non-positional at the plain volume (127, centre) through the effects channel:

| Event in the viewer | Sound |
| --- | --- |
| Logbook opens (`L`) | 3011 (book switch), 3010 (page) and 3008 (book open), in that order: opening selects the tab |
| Logbook tab pressed (button or `Tab`) | 3010 (page) |
| The player closes the logbook | 3009 (book close); closing it on a map change or reload is silent |
| The hero gains a level | 3005 (level up) |
| A quest becomes completed for the PC (also by un-botching) | 3028 (quest completed) |
| An item is taken from a body in the inventory window | the item's pickup sound ([sound cues](sound-cues.md)) |

The viewer's logbook has no pages to turn, so the page sound plays only for the tab. The original
plays the pickup sound when an item is picked up in its inventory window; the viewer's take is one
click and plays it once, and taking every item plays the first item's sound once (a Steamwoven
mapping). Level up and quest completed are noticed once per session revision; the original plays
3028 on every set to completed, which the session does not report, so a quest already completed
that is set again stays silent here.

## Volume settings

The palette (Ctrl+Shift+F9 / Start) has **Effects volume**, **Voice volume** and **Music volume**,
0–10 each (the original's three bars, SN9), after the sound test. A change (SN7, SN8):

- is saved at once to `user://audio_volumes_v1.json` (outside game saves; an automated run saves only
  to `AUDIO_VOLUMES_TEST_PATH`);
- sets every live sound of that channel to the bare channel volume (the original's flattening,
  which loses per-song volume until the next scheme load);
- for effects, also moves the listener to the PC and recalculates every positional sound (an
  original quirk kept in both settings); the listener goes back to the view centre's tile when that
  tile next changes.

## Checks

Native smoke `ARC-UI-044 native-smoke interface PASS` (exterior AREA and map mode, after the audio
smokes): with a logbook, the production hooks open it (three sounds), press a tab (one) and close it
(one); the level and quest sounds start; the palette's own Effects volume control flattens a plain
effect to the new channel volume, moves the listener to the PC and recalculates a positional effect
from there; the setting is restored. The level and quest detection and the loot hook are not driven
by the smoke. The palette self-test counts 26 navigable controls and 8 number fields.

## NOT READY

- The inventory window's own sounds (container open, close and locked by the container's formula,
  dropping items, 3007), the character sheet's and the conversation's buttons: the viewer's windows
  are diagnostic panels without the original's buttons, so they play nothing yet.
- Interface sounds of windows the viewer does not have (schematics, written items, the main menu,
  barter with its refused-trade sound 8158), the bless, curse, poison and exclamation messages, the
  hot-key equip slot sound (switch candidate SN31).
- Nothing was compared by ear with the running original.
