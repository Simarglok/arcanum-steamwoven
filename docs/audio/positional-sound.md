# Positional event sounds in the viewer (ARC-UI-042)

Status: **Implemented bounded component**, task 7 of the
[audio plan](../research/audio-evidence.md#task-plan). The AREA viewer (exterior, cave and map
mode) plays the event cues of [ARC-WORLD-041](sound-cues.md) on the tile where they happen, with the
original's attenuation and pan, for any map of any module.

## Positional rules (Godot-free)

`Arcanum.Presentation.Audio.PositionalSoundRules`, checked in the 1.0.7.4 executable
([audio study](../research/audio-evidence.md) SN4–SN7):

- **Points.** A tile (X, Y) is the point `(40 × (Y − X − 1), 20 × (X + Y))`, the original's isometric
  projection; its view origin cancels out of every distance.
- **Level.** Distance `sqrt(dx² + (2·dy)²)` computed in single precision and rounded to the nearest
  (ties to even). Volume 127 below the size's minimum radius, 0 above its maximum, else
  `127 × (min − d) / (max − min) + 127` truncated and clamped (127 when the range is empty), then
  times the size's volume percentage / 100. Balance 64 (centre) within the minimum pan distance,
  hard left (0) or right (127) beyond the maximum, else `64 ± 64 × (|dx| − min) / (max − min)`
  truncated and clamped.
  Radii, percentages and pan distances come from the catalogue's `soundparams.mes` profile.
- **Size.** Scenery with scenery flag 0x40 is small, 0x80 medium, 0x100 extra-large; every other
  object is large (`ObjectSoundFields.SceneryFlags`, slot 36 of scenery).
- **Volume at start** is the level × the effects channel / 100; a **recalculation** (the listener
  moved) sets level × channel / 127, the original's ~21 % drop at the first recalculation (SN8).
  There is no distance cut-off: a sound beyond the maximum radius starts at volume 0.
- **Pan.** The backend routes a voice through one of nine pan buses per channel; balance maps to
  `(balance − 64) / 64` (a Steamwoven choice: the original hands the balance to its mixer).

`SoundCueRules.Choose` (Simulation) picks the id a cue plays: the variant drawn with the audio
generator (never the game's), and for a weapon hit the material fallback when the drawn id is in no
table. An id held with an empty record counts as held in the **Original** setting, so 128 installed
weapons hit silently; **Steamwoven** lets an empty record fall back to the material sound
(`SoundRuleProfile.EmptyHitRecordSilent`, ledger SN23, [rule modes](../world/rule-modes.md#registry)).

## Viewer integration

- **Listener**: the tile at the centre of the view (`AreaSoundListener.ChartCell` of the camera
  centre), checked every frame; when it changes, every live positional sound takes its new volume
  and pan. A map change stops every sound.
- **Footsteps**: on frames 3 and 8 of the hero's walk or run, by the hero's armour (the art of its
  prototype) and the class of the ground tile under it (`TileSoundTable` over the area's terrain).
- **Pickup**: a pickup from the ground plays the item's pickup sound on the hero.
- **Attack**: the start plays the weapon's use sound and, when the attack sound roll (1–4) is 1, the
  hero's attacking bark; the hit plays the critical bark on the target, then the hit, critical or
  miss sound on the hero (the target's body armour from its stored inventory decides the material); a
  kill plays the target's dying sound (type 1, the fall-down death the viewer draws; the original plays
  type 2 for other death animations).
- The status line counts the event sounds played and names the last one.

## Checks

Unit tests: `PositionalSoundTests` (Presentation), `SoundCueRulesTests` (cue choice),
`AreaSoundListenerTests` (viewer core). Self-test (`ARC-UI-040 self-test audio PASS`): a listener
move sets a voice's volume and pan bus. Native smoke `ARC-UI-042 native-smoke cues PASS` (exterior
AREA and map mode): the hero's tile has a footstep class, a footstep id 2900–2939 plays, a source
beyond the maximum radius starts silent and hard right, moving the listener onto it gives the full
volume × channel / 127; the attack smoke requires its attacks to play their sounds.

## NOT READY

- The first event sound of a map opens the prototype index and indexes the map's terrain, a one-time
  pause on a large map. A target's sounds play at its stored position (an NPC the session moved, such
  as a follower, is heard where its map data put it).

- Doors, chests and windows (the viewer cannot open them), looping scenery and eye-candy sounds,
  NPC and companion footsteps (they are not animated), barks of AI states (fidget, alerted).
- The hero's armour from what it wears (the viewer draws the prototype's art); a holder's magic
  weight adjustment in the pickup weight.
- Moving the listener to the PC when the effects volume changes (SN7, with the volume sliders of
  ARC-UI-044); following a moving source (the original fixes a sound's position at its start).
- Nothing was compared by ear with the running original.
