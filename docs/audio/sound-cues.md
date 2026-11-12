# Sound cues of world events (ARC-WORLD-041)

Status: **Implemented bounded component**, task 6 of the
[audio plan](../research/audio-evidence.md#task-plan). Godot-free rules that choose the sound of a
world event (an item picked up or dropped, an attack, a critter's bark or step, a door or chest, a
busted or animated object) as the original does, for any module. Nothing here plays sound: the
viewer plays the cues positionally ([ARC-UI-042](positional-sound.md)).

## Fields (Arcanum.Data.Audio)

- `ObjectSoundReader` reads `ObjectSoundFields` for any object type 0–17, from a prototype file by
  number (in play `ContentPrototypes.Read`: base sources only) or from an Area object's effective
  fields (an instance's own field over its prototype's). Slots of the v119 layout
  ([combat fields](../formats/combat-fields.md)): 0 current art, 18 flags, 29 material, 32 sound
  base (`OBJ_F_SOUND_EFFECT`); for items 38 weight, 39 magic weight adjustment, 45 complexity and
  47 unknown description, and for ammunition 60 quantity. A field that is not a plain 32-bit number refuses the object with its slot.
- `TileSoundTable` reads the footstep class of every ground tile from `art/tile/tilename.mes` at
  game start (base sources only, ledger LP1), in the original's grammar
  ([audio study](../research/audio-evidence.md) SN34): four categories (records 0–99 outdoor
  flippable, 100–199 outdoor, 200–299 indoor flippable, 300–399 indoor), each its records in order;
  a tile names its record by bits 22–27 of its art id, the category by bits 8 (outdoor) and 7
  (flippable). Since ARC-DATA-043 a facade in a tile slot (art type 11, as on void) names
  its record by bits 11–16 and its category by bits 25 (outdoor) and 26 (flippable), as the
  original's art helpers read it; any other art type reads as position 0 of the indoor
  category (record 300's class). A record with `/` has flag letters up to the first space and the class after them
  (C `atoi`, 0 when there is none); a record without one is the number after its first three
  characters. A table without record 0, 100, 200 or 300 (the original fails only when such a
  category has records), a bare record shorter than three characters or a record holding NUL is
  refused; a tile or facade beyond its category gives no class.
  Classes: 0 dirt, 1 sand, 2 snow, 3 stone, 4 water, 5 wood.

## Rules (Arcanum.Simulation.Sound)

`SoundCueRules` returns a `SoundCue`: an id plus a variant count (1, 2 or 4) the presentation
draws from, and for a weapon hit an `Unresolved` cue that plays when the drawn id is in no sound
table (an id held with an empty record counts as held, as in the original, and plays nothing).
Checked in the 1.0.7.4 executable ([audio study](../research/audio-evidence.md) SN23–SN27):

| Event | Cue |
| --- | --- |
| Pickup / drop | Gold (unknown description 9056) 5958 / 5968; else 5950 / 5960 + material class (flesh, light metal, heavy metal, stone, wood, glass, cloth, paper); heavy metal at most 2,000 heavy counts as light. |
| Weapon use, out of ammo, miss | Base + 0, + 7, + 3; nothing for a base of 0 (use, out of ammo); a miss without an item or base is 4010. |
| Weapon hit, critical | Base + 4 or + 5 (2 variants), critical base + 6; unresolved, or without an item or base: `7000 + 20 × weapon class + 3 × target class` (2 variants), weapon class flesh unarmed, target the body armour (wear slot 1006) a critter wears. Nothing without a target. |
| Critter bark | Base + 0 critically hit, 1 dying, 2 gruesome dying, 3 fidget, 4 attacking, 5 alerted, 6 agitated. |
| Footsteps | 2900 in plate armour (critter art armour 4 or 6), else 2904 dirt, 2912 sand, 2916 snow, 2920 stone, 2928 water, 2932 wood, + 4 in chain armour (3) on dirt, stone and wood; 4 variants. On frames 3 and 8 of a walk or run of a critter that is not monstrous (SN26). |
| Critter rule | Only for a PC or NPC, and only while the local PC exists and is not switched off (object flag `0x2`, as during world-map travel). |
| Container, portal | Base + 0 open, + 1 close, + 2 locked, for that type only; a base of 0 still plays ids 0–2. |
| Misc (wall, scenery, projectile, weapon, trap) | Busting base + 0 (weapon + 1), destroying base + 1 (weapon + 2), animating base + 2; nothing for a base of 0. |
| Attack (SN27) | When it starts: out of ammo or use, on the attacker. Result: critical hit plays the target's "critically hit" bark on the target and the weapon's critical on the attacker; a hit the weapon's hit, a miss its miss, on the attacker. |

`ItemWeight` is the original's item weight: gold 0, the stored weight plus the holder's magic
weight adjustment, ammunition times its quantity / 4 (32-bit, truncating). A material outside 0–13
or a tile class outside 0–5, which the original reads past its tables, gives no material or
footstep cue (a weapon hit with a base keeps its own cue, without the fallback).

Randomness: the original draws the variants (and every other sound draw) from its game generator
(SN33). Here the presentation draws them with its own generator, as the scheme player does, so a
sound never moves a game roll; the rules return the variant count instead of drawing.

## Session bridge (Arcanum.Session)

The session holds no object fields, so `SessionSoundCues` takes the `ObjectSoundFields` the host
read: `Item` (with a holder's aptitude, the magic weight adjustment scaled by `EquipmentRules.AdjustMagic`,
as the original adds it only for a held item), `Critter` (with the ground tile's class), `HitTarget`
(a critter's body armour), `LocalPc` (absent or off) and `Attack`, which turns a resolved attack
(`AttackResolution`) into its ordered cues: use, then the outcome's. A critical miss plays the
miss sound.

## Installed data

Real-data test (`ObjectSoundRealDataTests`, `DAT_ROOT`): all 1,423 prototypes read. Sound bases:
15 of 15 portals, 38 of 38 containers, 3 of 45 scenery, 150 of 150 weapons, 13 of 13 PCs, 496 of
497 NPCs; no wall, projectile, other item or trap has one; one gold prototype. 22 weapons have a
base no table holds, nor any of its hit and critical ids (their hits use the material sounds); every other weapon's hit and critical ids
are empty records, so in the original their hits are silent (rule SN23: Steamwoven falls back to the material sound, [ARC-UI-042](positional-sound.md)). Every footstep id 2900–2939, pickup and drop id and material hit id names an
installed file. The tile table has 31, 6, 2 and 13 tiles by category; classes 21 dirt, 3 sand,
0 snow, 14 stone, 8 water, 6 wood.

## NOT READY

- Playing the cues: [ARC-UI-042](positional-sound.md) (position, attenuation, pan, the variant
  draw, the fallback with its rule switch).
- The self-hit of a critical miss; spell and eye-candy sounds (later slices, audio plan "Later"). Looping scenery and AI alert sounds have callers since ARC-RENDER-1126.
- Callers: the viewer plays footsteps, pickup and the hero's attacks (ARC-UI-042), and since ARC-RENDER-1126 the scenery loops, busting and destroying, NPC and
  companion footsteps, the item drop and the fidget, attacking and alerted barks of NPCs ([positional sound](positional-sound.md)); doors, chests and other
  events have no caller yet.
