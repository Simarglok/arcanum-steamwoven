# Lights on all maps: object, carried and animated lights (ARC-RES-069)

Status: **Research** (whole-world plan M4, step R6; the viewer slice V7 builds on it). The behaviour below is
read in the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…8d52`). Two readers checked it, and the rows are in
[ledger LT1–LT15](original-verification-ledger.md#lights-on-all-maps-arc-res-069). Data facts are from the owner's
installed module (GOG 1.0.7.4), counted by the probe `--light-census` (below).

The viewer already draws the stationary lights of sector files
([area lighting](../rendering/area-lighting.md), ARC-RENDER-012), as the earlier source reading
([time and lighting contract](../world/time-lighting-contract.md), F0) described them. This study adds what that
reading left open:
- where object lights come from and how long they live;
- the nocturnal scenery switch;
- the carried light of a critter;
- animated light ART;
- which light flags the original reads.

## One light, many owners

The original keeps one kind of light, a 48-byte record:
- an owner (an object, or none);
- a location and a signed screen offset;
- flags, a light ART id and a colour (stored r, b, g);
- a tint word and an animation word.

Sector light lists hold records of both kinds, and the drawing reads only those lists.

- **Stationary lights** are the records of a sector file. A record is read as stored (48 bytes) and a terrain
  template's records move to the real sector (LT3, [Wilderness A1](original-verification-ledger.md#world-movement-and-data-arc-res-047-arc-res-029-arc-res-034-arc-data-021-clock)).
  - In the game a light with no owner is created only this way: the general create refuses a record without
    an owner outside the editor (LT2).
  - The installed module has **2,797** such records in its 1,680 own sector files. None has an owner word.
  - Their flags: 654 outdoor-tinted (0x10), 20 indoor-tinted (0x8), 15 dark (0x2), and 5 with bit 0x40 (on one
    interior).
  - One of them uses an animated ART.
- **Object lights** are built from an object's fields (LT1):
  - one primary light: field 13 flags, 14 ART, 15 colour;
  - up to four overlay lights: arrays 16, 17, 18.
  - Each takes the object's location and offsets.
  - The handles live in two runtime fields (329, 330).
  - Before an object is saved or freed, its live lights' flags, ART and colour are written back to its fields
    (LT11). This is why saved fields can carry the runtime bits 0x80 (changed) and 0x8000000 (primary); a rebuild
    sets them again anyway.
- **When object lights are built** (LT4):
  - At sector load, for every object of the sector that is not in an inventory, the lights are built and
    inserted into the sector's list.
  - An object that changes its light (a script, the carried light) is rebuilt and inserted into the list of its
    location's sector, if that sector is loaded.
- **Moving and switching.** Lights move with their object (location and offsets, LT10). Switching the object off
  (`OF_OFF`) switches its lights off, and back on with it (LT13).

The installed module's object lights:
- **Sector objects:** 13,809 primary lights, all on scenery (type 3).
  - 11,477 of them are dark (subtractive), and most of those are also outdoor-tinted: the shade under trees and
    buildings.
  - 84 have the light's OFF flag, and 365 belong to objects that are switched off.
  - 483 are on nocturnal scenery.
- **Map mobiles:** 60, mostly NPCs (57).
- **Main map:** its generated sectors use 208 terrain templates. Weighted by how many sectors use each template,
  they hold 52.8 million scenery lights (dark, outdoor-tinted) and 2.76 million stationary records. That is about
  13 shade lights per generated sector.
- **Overlay lights:** none in the installed data.

## Nocturnal scenery

Scenery with the nocturnal flag (scenery flag 0x4), standing on a tile whose ART type flag is set (an outdoor tile),
is lit only at night: hours 18 to 5.

- **At sector load** by day, such scenery is switched off and its lights are not built at all (LT4). By night it
  is switched on and its lights are built.
- **At every refresh** (whenever the ambient colours are set, which the hour change does, or lighting is toggled),
  the loaded sectors' nocturnal scenery is switched only when its state differs (LT5). Scenery with the scenery flag
  0x400 is left alone. After a switch the scenery's lights are rebuilt and inserted.
- **On an indoor tile** (the tile ART's flag clear) nocturnal scenery is always on.
- **Two slips of the refresh** (anomalies A1, A2 below):
  - After a switch, the loop over the sector's tiles goes on from a wrong tile (its counter is reused), so tiles are
    skipped or scanned twice.
  - The lights of scenery that already has them are inserted into the sector list again.


## Carried light

A critter's light comes from the worn items' light sizes ([EQ12](original-verification-ledger.md#equipment-arc-world-028), the largest
size wins). It becomes the critter's primary light (LT12):
- light ART 1 (frame 0);
- one colour per size (r, g, b): extra large 244, 255, 255; large 255, 214, 172; medium 236, 138, 85; small 167, 88, 58;
- no light (ART −1) for none.
- The size is the largest of the worn items' sizes, each item's size shrinking with its condition (EQ12).

It is set again whenever the worn items' light is recomputed (EQ12).

In the installed module, 10 items in the map mobile files have a light size (9 large, 1 extra large), and 48 critters already carry the small size in their saved fields; 4 item prototypes and 9 critter prototypes have one. Equipment already holds
the size (ARC-WORLD-028); nothing draws it yet.

## Animated light

A light whose ART has more than one frame animates (LT6):
- When the light enters a sector list, it gets an animation event every ⌊1000 / frames per second⌋ ms of the
  animation clock (the clock object animations use, with the same convention), unless its flags carry 0x20 or 0x4.
- Each event steps the ART to the next frame, wrapping, and moves the light by the new frame's offsets (LT7).
- The frame is written back to the owner's ART field.
- Flag 0x4 marks a running animation. Destroying the light or clearing its ART stops it.
- The event is of the animation domain and is not saved ([event queue](game-event-queue.md#3-event-kinds-and-consumers-s)).

`art/light/light.mes` names 28 light ARTs; 7 have more than one frame (5, 6, 8 or 15 frames, at 5 or 8 frames per
second). The viewer refused an animated light ART, which left one interior unlit; since ARC-DATA-039 it is read with all
its frames and its frame 0 is drawn.

## Flags the original reads

The light functions that draw and sample lights gate on only two flags:
- **OFF (0x1):** the light gives nothing.
- **DARK (0x2):** the light subtracts instead of adding.

The other bits:
- **Tint (LT8, LT15):** INDOOR (0x8) and OUTDOOR (0x10) make the light follow the hour's indoor or outdoor colour. Setting
  one clears the other. Setting an explicit colour clears both (LT9).
- **Animation:** 0x4 and 0x20 only gate it (LT6).
- **Bookkeeping:** 0x80 marks a changed light, 0x8000000 a primary light, and 0x10000000 << slot an overlay slot.
- **0x40: casts no shadow.** It is read only by the object shadow builder (0x4D9B20), which skips lights that are
  off, dark or carry 0x40 (LT14). The light itself draws normally. The viewer refused the bit, which left the
  one interior that has it unlit; V7a (ARC-DATA-039) admits it.

## Steamwoven: slice V7 (proposed)

Four slices. They need only the readers that already exist plus object light fields, and the rules above.

| Slice | What | Notes |
| --- | --- | --- |
| V7a | Object light descriptions in Data (done: ARC-DATA-039, [object lights](../rendering/area-lighting.md#object-lights-arc-data-039)) | Effective primary and overlay lights of sector objects and mobiles (instance field, else prototype), animated light ART (frames, rate, per-frame offsets), stationary flag 0x40 admitted and ignored. |
| V7b | Object lights drawn in map mode (done: ARC-RENDER-030, [object lights in map mode](../rendering/area-lighting.md#object-lights-in-map-mode-arc-render-030)) | Built at load for objects not in an inventory, with the nocturnal rule at load and on each hour change; OFF and DARK as for stationary lights; the main map's template shade lights (its generated sectors' objects are part of the window). A1 and A2 (below) are fixed in both modes, proposed. |
| V7c | Animated lights (done: ARC-RENDER-031, [animated lights](../rendering/area-lighting.md#animated-lights-arc-render-031)) | The ⌊1000 / fps⌋ frame loop (animation time), the frame's offset step; stationary and object lights alike. |
| V7d | Moving and carried lights | Lights move with NPCs and the hero; the hero's carried light from session equipment (EQ12, LT12). |

Each slice keeps original parity NOT_VERIFIED: the lights' pixels and blending stay the composition of ARC-RENDER-012.

## Anomalies (switch candidates)

Each one is the original's behaviour; whether the Steamwoven setting changes it is an owner decision for the slice that
implements it (rule modes, [D8](npc-combat-ai-plan.md#owner-decisions-2026-10-16)).

| ID | What the original does | Effect |
| --- | --- | --- |
| A1 | The nocturnal refresh reuses its tile counter for the light-slot loop (LT5). | After a switch, the rest of that tile's objects are tested against another tile's ART, and the scan goes on from a wrong tile: some nocturnal scenery is not switched at that refresh, or is tested twice. |
| A2 | The refresh inserts the handles of scenery whose lights already exist again (LT5). | The same light can stand twice in a sector list; whether drawing then counts it twice is not read (F0). |
| A3 | Stepping a light's ART back (0x4DD830) stops at frame 0, while stepping forward wraps (LT7). | No effect on the animation loop, which only steps forward. |

## Open (F0)

- The lighter and darker buffers and their arithmetic (0x4DE900). These stay as the time and lighting contract reads
  them.
- What `OF_DONTLIGHT` (object flag 0x100000) does in the object renderer; 3,152 sector objects carry it.
- Shadows: the shadow builder (0x4D9B20) and `art\light\shadowmap.bmp` (0x4DE5D0); the slice that draws shadows reads them.
- Whether a light listed twice (A2) is drawn twice.
- Light ART loading time.

## Probe

`dotnet run --project tools/research/Arcanum.CrashSiteProbe -- --light-census --game-root GAME` prints the counts
above as JSON. It walks:
- every map's own sector files and mobiles;
- every prototype;
- the main map's terrain templates, weighted by use;
- `art/light/light.mes`.

`--light-census-self-test` checks its record and ART decoding on invented words. Only numbers leave the probe.
