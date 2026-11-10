# ARC-UI-117 — HD interface import: per-item `lock: false` in every mode

Work-Item: ARC-UI-117
Result: The HD interface import documents and tests `"lock": false` for sheet items too.
Components: HD import, HD pack format docs
Human-Gates: none
Requirements: FR-PRODUCT-004

Owner decision E (2026-11-10): old HD arts and their derived button frames are converted without the shading lock.

Finding: the behaviour was already on `origin/main` since ARC-UI-114 (#878): `HdInterfaceImport.Build` reads the item's `lock` flag
(default `true`) for sheet, exact and ready items, and a derived frame without the lock is the registered unlocked base tile plus the
bicubic-enlarged Legacy difference (`HdRedrawLock.AddLegacyDifference`). This task closes the remaining gaps:

AC1. `docs/formats/hd-pack.md` states that `"lock": false` applies to every item mode, derived frames included (it said `ready` only).
AC2. A synthetic regression covers a `sheet` item with `lock: false` (the redraw's own tone survives) against the default (Legacy shading restored).
AC3. The plan parser and record docs name the `lock` field.

No original or derived art is added. Coverage status of FR-PRODUCT-004 is unchanged (partial).
