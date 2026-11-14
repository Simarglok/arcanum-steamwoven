# Spell eye candy (`Rules\SpellEyeCandy.mes`)

ARC-MAGIC-017. The spells' visual effects as the original's magic-tech init loads them (0x44F07C) through its animation-effect
module (0x4CDB70), and the eye-candy art id they name. Behaviour was read in the retail 1.0.7.4 executable by two readers
([ledger EC1–EC13](../research/ledger/spell-runs.md#spell-eye-candy-arc-magic-017)); no source reconstruction was used. Reader:
`Arcanum.Data.Magic.SpellEyeCandyTable` and `EyeCandyArt`; the timing rules that play it are in
[the spell cast host](../world/spell-cast-host.md#eye-candy-arc-magic-017).

## Layout

- **Groups.** Six entries per spell, ten message numbers per group: entry `6·s + k` is message `10·s + k`, k = 0 cast,
  1 projectile, 2 destination, 3 secondary destination, 4 secondary cast, 5 unnamed; messages ending in 6..9 are not read. The
  groups run from message 0 while message `10·s` exists (EC1). A missing message, or an empty text, is an entry without art.
- **The file.** An init file: read from the first base source holding it (ARC-DATA-027 load phases), as `rules\magictech.mes`.
- **Names.** `art\eye_candy\eye_candy.mes` maps an art number to a file name; it is read with the area owner's name-table reader
  (`AreaObjectArt.ReadNameTable`), the same one the viewer's draw uses.

## A line (EC2–EC5)

With `Art: n` the reader then takes, each only where it starts at the cursor (a key out of this order is never read, and a value
without text of its own reads the next key's text):

| Key | Value | Without it |
| --- | --- | --- |
| `Palette:` | 0..3 | 0 |
| `Scale:` | a percent of {50, 62, 75, 87, 100, 125, 150, 200}; another is logged and takes the first | 100 % |
| `Blend:` | none, add, sub, mult, alphC, alphA, alphS (any case) | — |
| `Sound:` | a sound number | the group's first entry's sound plus {0, 4, 5, 6, 3, 7} by place, or none |
| `Flags:` | overlay_f, overlay_b, overlay_fb, underlay, tinting, animates, anim_forever, can_autoscale | none |

The entry is translucent when the blend is not none — or when `Scale:` is given without `Blend:` (the two share one value; no
installed line does this). The art id is then built (EC6); a part out of range (error 12), or a part the flags name whose file does
not load (`_F`, `_B` or `_U`), leaves the entry without art and flags. Without `Art:` a line reads `Sound:` or else `ArtEffect:`
(Translucency, Tinting). Every line then reads `Light:` (a light art number; with `Light Color: (r@g@b)`, white without it; a triple
without its second `@` repeats r as b, without any `@` all three are r) and `Proj Speed:`.

## The art id (EC6)

Class 14: bits 1..3 the scale index, 4..5 the palette, 6..7 the variant (foreground, background, underlay), 8 translucency, 9..11
the rotation, 12..18 the frame, 19..27 the number. The file of a variant is `art\eye_candy\<name>_<F|B|U>.art`. The draw scales a
foreground or background part by {50, 63, 75, 87, 100, 130, 160, 200} % of its index times the object's scale (another table than
the parse's: a `Scale: 125%` line draws at 130 %) and an underlay at 100 % (EC12).

## Installed census

From the reader on the owner's install (opt-in `SpellEyeCandyRealDataTests`, counts only): 223 groups, 1,338 entries, 364 with art
(by place 103, 12, 176, 64, 4, 5); every one of the 80 college spells has a cast entry, none a secondary cast entry; 356 entries
are translucent, 55 scaled, 249 have a light, 64 are `anim_forever`. No entry has an underlay beside an overlay; two name an art
without a part (nothing is placed). One line, Disarm's destination, reads its `Flags:` as the sound's value (EC2): it places
nothing, as in the original.

## The script list (`Rules\ScriptEyeCandy.mes`, ARC-SCRIPT-1592)

The script module's init (0x4446E0) loads the same list type (0x4CDB70) from `Rules\ScriptEyeCandy.mes` as the list 0x5E2FB0, with the loader's own defaults (0x4CDB30; the init writes a preset count of 10 that the loader's count overwrites) instead of the spell list's set-up: one entry per group, message stride 1 and no sound offsets. So **entry `n` is message `n`**, read in a row from message 0 up to the first missing message, each line parsed as above (`EyeCandyListShape.Script`, `SpellEyeCandyTable.OpenScript`); an entry without `Sound:` has none. The number a script's play action names (action 120, 0x44C7A0) is the entry index itself, not scaled by 6. Installed: 75 entries (messages 0 to 74), all with art and animating, 33 looping (`anim_forever`), 13 lit, 22 with a sound. A goal's entry is a reference that names its list (`EyeCandyEntryRef`, spell list 0, script list 3: the list in the bits above 24, the index below), so a script's entry and a spell's entry of the same number are different entries (ARC-SCRIPT-1829, read in the add 0x4CCF70: a script's play sets the block's animate word, so a static entry writes no overlay and no light and only sounds once; the entry's art is turned to the object's art rotation, 0x502880 into 0x502930, whatever the entry's flags; an entry's sound plays once at the run's begin, or looping until the run's end node when it loops); the other original lists (animation 0x5DE670, weapon 0x5DE610, combat 0x5FC1F8, trap 0x5FC460) have their numbers reserved there and are not read yet.

## The animation list (`Rules\AnimEyeCandy.mes`, ARC-RENDER-1794)

The animation module's init (0x421B00) loads the same list type as the list 0x5DE670 from `Rules\AnimEyeCandy.mes` with the loader's own defaults too (the init's preset count of 11 is overwritten by the loader's count): **entry `n` is message `n`**, no sound offsets (`EyeCandyListShape.Anim`, `SpellEyeCandyTable.OpenAnim`; the viewer reads it with the spell data as `AnimEyeCandy`). It is the blood of a hit: 0 normal, 1 poison, 2 electrical, 3 fire, 4 fatigue, 5 critical splotch (never asked for by a weapon hit, [RH6](../research/ledger/magic.md)), 6 undead, 7 the clean effect (the violence filter, a stoned or mechanical critter), 8 to 10 the damage tiers (`HitGoreRules`). The installed file has 11 entries, each with art, a foreground part and animating, none looping. The damage effect (0x433020) plays an entry on the struck critter with the add's limit of 3 (`AreaEyeCandyHost.Play`'s `limit`), then calls the hit effect 0x459A30.

## The weapon list (`Rules\WeaponEyeCandy.mes`, ARC-RENDER-1990)

The same init (0x421B00) loads the list 0x5DE610 from `Rules\WeaponEyeCandy.mes` with its own settings: **first message 1, five entries per group, a stride of ten messages**, no sound offsets, a preset count of 750 that the count 0x4CDCD0 overwrites (`EyeCandyListShape.Weapon` with its `FirstMessage`, `SpellEyeCandyTable.OpenWeapon`; the viewer reads it as `WeaponEyeCandy`). Entry `5g + k` is message `10g + 1 + k`, the groups counted while message `10g + 1` exists; message `10g` is a label. A weapon's group is `basic prototype − 0x178D` (the prototype file's number less 20, then less 6029): `WeaponHitEffectRules.Index(prototypeNumber, kind)`. The places: 0 the begin of the attack animation (0x432700), 1 the swing (0x4B3170), 2 the projectile's speed (`Proj Speed:`, 0x435870), 3 the hit (0x4B6930, [ledger RH8](../research/ledger/magic.md)), 4 asked by no caller. Only the hit is played by the viewer (as an overlay run with the add limit 2 on the struck critter, replacing the blood of the hit when it plays); the installed hit entries with art are the Tesla Rod's (prototype 6060) and Bronwyck's Gun's (6069).

## Refusals and differences

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A blend name or flag the tables lack, and a light colour without `(`, end the process. | The whole file is refused with a typed status (`UnknownName`, `UnknownFlag`, `MissingParenthesis`), and so is a line with a byte above 0x7F (the original's white-space test misreads one); the casts then play without eye candy. | both |
| A sound taken from the group is dropped when its file does not exist (0x41A940). | Kept. | — (not ready: no spell sounds) |
| `Light:` is turned into a light art id (0x504690); a lit entry puts a live light on its target (an overlay light slot, 0x43EE10, [IW2–IW5](../research/ledger/spell-runs.md#small-executable-reads-illuminate-unseen-force-on-objects-disarm-arc-res-1109)). | The light number and colour are kept, and a lit entry keeps a live light on its object while its run is shown (ARC-MAGIC-1229, `AreaEyeCandyHost.Lights`): the first free of the object's four overlay light slots, light art class 9 of the entry's number, frame 0, flags 0x20, the entry's colour; with all four slots in use the add refuses (IW2). The viewer stands it as a carried light where its object stands. | both (not ready: the glow after a load, the object's own offsets) |

## Not ready

- Sounds and tinting are read but not played (the live light of a lit entry is built, ARC-MAGIC-1229, and its run is saved, ARC-MAGIC-1280); `can_autoscale` and the rotation are carried in the art id but not applied.
