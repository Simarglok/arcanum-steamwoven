# HD interface local repairs, 2026-11-09 to 2026-11-10

Index of the local HD art work that was done in the owner's working folder (`local/res095`, ignored by git) and had no
document in git. Each row has its own note with sources, checks and installation. No original or derived art, manifest or
capture is in git; only the evidence in prose.

## Numbering

The local reports carried labels that collided with real task codes. They were renumbered; the old label is the folder name
in `local/res095`.

| Work | Local label | Code | Note |
| --- | --- | --- | --- |
| Top strip and four left buttons | ARC-UI-116 | ARC-UI-116 (same work as PR #882) | [top strip art](arc-ui-116-top-bar-hd-art.md) |
| Phantasm bright top edge | ARC-UI-117 | ARC-UI-128 | [Phantasm](arc-ui-128-phantasm-hd-art.md) |
| Maintained-spell slots (wood, contour) | ARC-UI-118 | ARC-UI-129 | [slots](arc-ui-129-spell-slot-hd-art.md) |
| Combat button centring | ARC-UI-119 | ARC-UI-130 | [Combat](arc-ui-130-combat-centering-hd-art.md) |
| Fate Points ring | ARC-UI-120 | ARC-UI-131 | [Fate Points](arc-ui-131-fate-points-hd-art.md) |

ARC-UI-117 is [PR #888](https://github.com/Simarglok/arcanum-steamwoven/pull/888) (per-item `lock: false`), ARC-UI-120 is the
[Combat press feedback](arc-ui-120-combat-press-feedback.md) code, ARC-UI-121 the
[Fate and Sleep press feedback](arc-ui-121-top-bar-press-feedback.md).

## Installation chain of `interface-redraw-4x`

All on 2026-11-09, game closed, shared install lock, backup, atomic swap, re-verification. Manifest SHA-256 prefixes
(full hashes in each note and in the local receipts `installed*.json`).

| Time | Step | Manifest before | Manifest after |
| --- | --- | --- | --- |
| 11:16 | ARC-UI-116 strip and buttons | `c5f7a6a4` | `0fcb407d` |
| 11:22 | ARC-UI-128 Phantasm | `0fcb407d` | `eb62bff7` |
| 11:27 | ARC-UI-129 slots, first step | `eb62bff7` | `fe3a8dc7` |
| 11:30 | ARC-UI-130 Combat centring | `fe3a8dc7` | `a2a99ce4` |
| 11:32 | ARC-UI-130 Combat bevel | `a2a99ce4` | `ae812c77` |
| 11:36 | ARC-UI-129 slots, continuous wood | `ae812c77` | `6f2c2ffb` |
| 11:37 | ARC-UI-131 Fate Points v2 (rebuilt over the previous) | `6f2c2ffb` | `f3fa4748` |
| 16:39 | ARC-UI-129 slots, contour | `f3fa4748` | `1abf6a51` |

The manifest of the active pack on the owner's Mac matched `1abf6a51…` (the last row) when this note was written on 2026-11-10.
The widescreen main-menu PNG is outside the manifest ([ARC-UI-122](arc-ui-122-main-menu-widescreen.md)).

## Rules these repairs followed

- Compare the live manifest with the verified base immediately before a swap; if it changed, rebuild the candidate over the
  current pack instead of replacing it ([HD pack workflow](../workflow/hd-pack.md)).
- Keep the previous pack outside the discovery folder; compare every file after the swap, not only the manifest.
- Per repair, show before and after at one scale and require identical frame identity, size and alpha unless the change is
  about alpha; a change confined to one area is shown by a byte comparison of everything else.
- Local art repairs change no tracked file: root checks and a PR are not required, and the evidence is this prose.
