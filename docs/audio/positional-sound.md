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
- **Scenery loops (ARC-RENDER-1126, 0x42DA50)**: the loop's begin node (`AreaSceneryHost.TakeSoundBegins`) decides the object's looping sound with
  `SceneryLoopSoundRules`: the misc sound of kind 2 (base + 2, nothing for a base of 0), the size class from the scenery flags, the range = the
  size's maximum radius / 40 tiles (truncating) against the Chebyshev tile distance to the local PC. Within the range and with no voice a looping
  positional voice starts on the scenery's tile; a voice that exists is stopped when the PC is farther at a begin and never doubled. The original
  tests this at the begin only, so a loop that began far away is silent until it begins again, and a voice that began near keeps looping (its volume
  falls to 0 as the listener moves away). The voices stop with their loops, as the loop's end node (0x42DED0) stops the run's sound handle: when the
  window of the map is restamped or the map is left. The begins wait in the host (each loop once) while there is no backend or catalogue.
- **Busting (0x43F1C0, ARC-RENDER-1126)**: a hero attack, a spell, burn or trap hit or an item hit that breaks an object plays the object's busting sound
  (`BustSound`) or destroying sound (`DestroySound`) of `SoundCueRules.Misc` on its tile after the break's trap and BUST script (OD15); none for a
  portal (a door or window), none without a sound base, and nothing when the id has no file.
- **NPC footsteps (0x4305D0, ARC-RENDER-1126)**: an NPC the AI host walks, or a companion, plays the footsteps cue (tile class, armour of its art) on frames 3
  and 8 of the walk or run cycle the viewer draws, once per frame change, at its tile; silent for a monstrous critter (critter flags undead, animal, monster
  or mechanical, mask 0x2000800C, 0x45F730) and while the local PC is absent or off.
- **Item drop (the release 0x575930, ARC-RENDER-1126)**: an item, an ammunition stack or the coins dropped from the inventory window onto the ground play the
  drop sound (5960 + material class, gold 5968) not positionally, as the interface sounds do (the original's release of a held item, 0x41B780).
- **AI barks (ARC-RENDER-1126)**: the fidget bark (`CritterSoundAction.Fidget`) when the AI host starts an NPC's fidget; the attacking bark when an NPC's
  attack starts (0x432700) on a roll of 1 in 1..4 (the same session roll as the hero's, `combat.AttackSound`, made by the AI host at the swing's start whatever plays it); the alerted bark: the danger update's setter
  call (0x4A92D0 through 0x4AABE0) asks for it, the setter gives it when the NPC turns hostile, the pass's perform stores it in the attack goal and the
  combat approach goal plays it once (0x4296D0, `AreaAiHost.PlaySound`, `AreaAiScene.AlertedSound`).
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

- Doors, chests and windows (the viewer cannot open them), eye-candy sounds; the looping sound of a scenery with a one-frame ART (the original starts
  its loop for the sound alone; the host runs no loop for it) and of a created scenery (a Summon's piece); the fidget bark plays with the start of the
  fidget (the original plays it on frame 1 of the fidget animation, which is not drawn for an NPC, and an NPC with no fidget ART would not fidget at all);
  the drop sound of a release into a grid cell or a worn slot (the original's release 0x575930 plays it for every release of the held item; only the
  drop onto the ground has a caller here); the pickup of an item by an NPC; the hero's own fidget bark (the hero's fidget is not drawn); the alerted bark of an
  NPC that is already in reach when its attack starts (the approach goal's path node plays it; whether the original carries the slot into the attack goal was not
  read); an object that moved or is held when it breaks sounds at its map data's or live tile (a held item, with no tile, none); footsteps need the moving-body
  pass to draw each frame (a frame skipped by a low frame rate misses its step).
- The hero's armour from what it wears (the viewer draws the prototype's art); a holder's magic
  weight adjustment in the pickup weight.
- Following a moving source (the original fixes a sound's position at its start). The effects
  volume change moving the listener to the PC (SN7) came with ARC-UI-044.
- Nothing was compared by ear with the running original.
