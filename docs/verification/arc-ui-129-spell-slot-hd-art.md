# ARC-UI-129 — Maintained-spell slots: closed-slot wood and contour (local art)

```text
Work-Item: ARC-UI-129
Result: Record the three local iterations of the closed maintained-spell slot art and their installation.
Components: local HD pack (not in git), native bar and cast smoke
Human-Gates: owner approval of the final contour (done)
Requirements: FR-PRODUCT-004
```

Local label before renumbering: ARC-UI-118. Local sources: `local/res095/character_repair/arc-ui-118`, its `continuous` and
`contour` subfolders. No original or derived art is in git.

## Layout

`IntTop` (185) is drawn under `AreaMaintainBar`. Five `SplSlt` (188–192, 32×32) stand at (281 + 50 i, 3). A closed slot draws
`SP_Plugged` (628–632, 35×35) over it, one pixel up and left. The INT/4 rule and the active-spell icon are unchanged.
In HD the gold frame of `IntTop` stuck out beyond a plug and the plug added its own bright edge.

## Iterations

| Step | Change | Smoke | Manifest before → after |
| --- | --- | --- | --- |
| First | Wood grain restored from neighbouring `IntTop` sections; the five plugs and the adjoining edge share one pattern; recesses of open slots and active spells kept; `SplSlt` unchanged in this step | bar 10099 (base) and 10100 (change); cast 10101 | `eb62bff7…` → `fe3a8dc7…` |
| Continuous | One continuous piece of the existing local wood reference across all five slots instead of a patch per cell (patches read as repeated squares); light and colour matched to `IntTop`; plugs take pixels from the same board | bar 10111/10112; cast 10113 | `ae812c77…` → `6f2c2ffb…` |
| Contour | Closed plugs are slightly darker than the surrounding wood in the original and have no bright open-slot frame: a soft darkening of about 9.5 % by area, top and left edges darker and bottom and right lighter within about one logical pixel; wood coordinates and alpha kept | bar 10126 | `f3fa4748…` → `1abf6a51…` |

The first two steps changed six frames (the strip and five plugs) and the manifest; the other entries and the recipe stayed
byte-identical. Against its previous candidate the contour step changed only the five plugs (the background and the five open
frames were identical), but the installed contour step differs from the live base `f3fa4748…` in 11 entries: `IntTop`, the five
`SP_Plugged` and the five `SplSlt` (its receipt lists all 11; the backup confirms it). With 2/5 slots open, all 12 native comparisons against
the previous candidate matched outside the closed slots, and the geometry matched. Every run logged the known texture leak
message at exit. For the contour step the sources were the 1× `IntTop`, five `SplSlt` and five `SP_Plugged`: all 11 RGBA hashes
matched the Legacy keys of the installed manifest, and the original composition was built with the `AreaMaintainBar`
coordinates. The contour was installed after the owner's approval. Full manifest hashes are in [the chain](hd-interface-local-repairs.md).

## Installation (2026-11-09)

First step 11:27, continuous 11:36, contour 16:39 after the owner's approval. Each ran with the game closed, under the shared
install lock (`fcntl.flock`) that also checks processes, the manifest and the files, with a verified backup before the atomic
swap; the receipts `installed.json` of each folder name the backup paths. No tracked code changed.
