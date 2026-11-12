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

## Refusals and differences

| Original 1.0.7.4 | Steamwoven | Mode |
| --- | --- | --- |
| A blend name or flag the tables lack, and a light colour without `(`, end the process. | The whole file is refused with a typed status (`UnknownName`, `UnknownFlag`, `MissingParenthesis`), and so is a line with a byte above 0x7F (the original's white-space test misreads one); the casts then play without eye candy. | both |
| A sound taken from the group is dropped when its file does not exist (0x41A940). | Kept. | — (not ready: no spell sounds) |
| `Light:` is turned into a light art id (0x504690); a lit entry puts a live light on its target (an overlay light slot, 0x43EE10, [IW2–IW5](../research/ledger/spell-runs.md#small-executable-reads-illuminate-unseen-force-on-objects-disarm-arc-res-1109)). | The light number is kept. | — (not ready: no spell lights) |

## Not ready

- Sounds, lights and tinting are read but not played; `can_autoscale` and the rotation are carried in the art id but not applied.
