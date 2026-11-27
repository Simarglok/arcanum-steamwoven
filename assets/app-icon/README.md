# App icon

`steamwoven-icon-1024.png` (1024 x 1024, RGBA, transparent ground) is the icon of Arcanum Steamwoven, picked by the
owner (ARC-UI-3456, [#3456](https://github.com/Simarglok/arcanum-steamwoven/issues/3456)). The packages do not use it
yet ([#3457](https://github.com/Simarglok/arcanum-steamwoven/issues/3457)).

## Status

This picture is **derived from the original game's emblem** and is the one exception to the rule that nothing derived
from original art goes into git ([owner decision 2026-11-28](../../docs/architecture/decisions.md)). It may ship in the
packages. The exception covers this icon only. The project's MIT licence does not grant rights to the original game's
emblem; the game and its marks belong to their owners.

## Provenance

- Source: a high-quality picture of the emblem supplied by the owner, recoloured into a layout input.
- Drawing: Codex image generation (`gpt-6.1-sol`, reasoning effort `medium`), 2026-11-28: the wheel of an earlier
  candidate with the blue shape drawn as woven steam (candidate H2, 1254 x 1254).
- Ground removal: cut from that output without repainting (alpha from the distance to the dark ground, the wheel's
  outline fitted as straight edges and arcs), then reduced to 1024 x 1024.
- SHA-256: `9fafdf042929e2f0d2fd4c9094e6646f47e5ed54f7291ec6e35d58ebc012e8f4`

Briefs, raw outputs, the other candidates and the cutting script stay local in the task's image archive.
