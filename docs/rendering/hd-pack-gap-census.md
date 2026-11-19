# HD pack gap census: what the installed pack still lacks (ARC-RES-2946)

Status: **census, 2026-11-19** (work item ARC-RES-2946, [#2946](https://github.com/Simarglok/arcanum-steamwoven/issues/2946)). Docs only; the
installed pack and the installation were read, nothing was written to them. Requirements touched (status unchanged):
[FR-UI-007](../requirements/ui-media-lifecycle.md#fr-ui-007), [FR-PRODUCT-001](../requirements/content-product.md#fr-product-001).

Scope is the one **HD** option of the [owner decision of 2026-11-15](../architecture/decisions.md): the HD interface (art and fonts) plus the relit
lighting. HD sprites of characters, objects and terrain are out of the plan, so world art is not counted here. The question is which of the
images the interface, HUD, menus, reading windows and portraits draw, and which fonts, have no frame in the installed `interface-redraw-4x` pack, so
that they draw Legacy in HD mode. No image, hash or pixel of the game is in this note; only art numbers, names, sizes and counts.

## Summary

| Measure | Count |
| --- | ---: |
| Interface ART files under `art/interface` (installed) | 815 (33 bitmap fonts, 782 other) |
| Frames of those files | 8,734 (7,425 font frames, 1,309 other) |
| Frames the pack holds for them | 6,949 (5,807 font, 1,142 other); every one matches the installed file (0 stale) |
| Non-font files complete in the pack | 668 of 782 |
| Non-font files with at least one missing frame | 114 (167 frames) |
| of which **drawn by the viewer through the HD path with a frame that is missing** | 76 files, 80 drawn frames (13 single arts, 61 schematic record pictures, art 787, art 329) |
| of which Legacy by design (cursors, the hover ring) | 8 files |
| of which not drawn by the viewer at all (dead art, frames never read) | 22 files |
| of which network play | 4 files |
| of which only frames the viewer never draws (vial frames 1 to 14, marker frame 1) | 4 files |
| Fonts the viewer draws, with HD glyph frames | 26 of 26 (221 to 223 of 225 frames each, 24 with 223; the missing frames carry no ink, except possibly one glyph of font 497, see Fonts) |
| Portrait bitmaps (`portrait\`), with an HD frame | 126 of 126 |
| Bitmaps outside `art/interface` that the pack cannot hold (no entry kind) | 113 (45 slides, 65 world-map tiles, 3 splash) |
| Lighting arts needed | 0 (the relit look is computed; it reads no pack art) |

The gap that matters for the owner is small: **13 single arts and 61 schematic pictures** (plus art 787, [#2917](https://github.com/Simarglok/arcanum-steamwoven/issues/2917)).
Fonts, portraits and 668 of the 782 interface arts are complete.

## Method

1. **Installed side.** A throwaway local tool (not committed) opened the player's installation through the project's content stack, listed every
   `art/interface/*.art` file the stack's winner holds (815), decoded every frame with the project's ART reader and computed the same identity that the pack
   key uses ([`HdInterfaceKey`](../../src/Arcanum.Data/Hd/HdInterfaceKey.cs): SHA-256 of the lowercase path `art/interface/<file>`, SHA-256 of the file,
   the frame number, SHA-256 of the Legacy RGBA). Art numbers and names come from `art/interface/interface.mes` (the 25 files without a message have no number).
   The bitmaps outside `art/interface` were listed by extension and header size only.
2. **Pack side.** `manifest.json` of the installed pack (`~/Library/Application Support/Arcanum Steamwoven Game/hd-packs/interface-redraw-4x`, read only): 7,075
   entries, 6,949 `sprite` and 126 `portrait`.
3. **Join.** A frame is **covered** when the pack has a `sprite` entry with the same name hash and frame number and the file hash and Legacy RGBA hash agree
   (exactly what the viewer checks); **missing** when there is no entry; **stale** when the entry exists but a hash differs. Result: 6,949 covered, 1,785 missing,
   0 stale. Fonts and the other arts were then separated by the 225-frame font layout.
4. **Where it is drawn.** The window ids are those of the [Steamwoven interface census](../ui/steamwoven-interface-census.md); the draw sites and whether a frame goes through
   the HD path (`InterfaceHd.Texture`, falls back to Legacy only when the pack lacks the frame) were read in the viewer code on 2026-11-19. This is a code reading, not a run:
   the pack and the viewer were not started.
5. **Priority.** P1: seen in every play session (bottom strip, follower panel). P2: a window or state that many sessions reach. P3: rare (only some items, a fallback,
   or a one-pixel strip). N: never drawn, decided Legacy, or out of scope.

Not covered by this census: the new Steamwoven kit (art authored for the 4:3 composition; batches [#2927](https://github.com/Simarglok/arcanum-steamwoven/issues/2927)
to [#2935](https://github.com/Simarglok/arcanum-steamwoven/issues/2935) and the B-batches of the [census](../ui/steamwoven-interface-census.md#production-batches)) is new art to
make, not a gap of the redraw pack.

## Drawn arts with no pack frame (the real gaps)

Every one goes through the HD frame path, so a delivered frame is used without a code change. Follow-up: [#2950](https://github.com/Simarglok/arcanum-steamwoven/issues/2950) (this table),
[#2951](https://github.com/Simarglok/arcanum-steamwoven/issues/2951) (schematic pictures), [#2917](https://github.com/Simarglok/arcanum-steamwoven/issues/2917) (art 787).

| Art | Name | Frames missing / total | Size (Legacy) | Where it is drawn | Priority |
| ---: | --- | ---: | --- | --- | :-: |
| 471 | `Schematics_Button` | 3 / 3 | 39x39 | Bottom strip: the Schematics button (idle, pressed, hover) | P1 |
| 772 | `XP_GaugeMag` | 1 / 1 | 372x3 | Bottom strip: experience strip, cropped to the remainder percent | P1 |
| 504 | `FollowerFrame_FT` | 1 / 1 | 34x3 | Follower panel: fatigue bar (while a follower is in the party) | P1 |
| 505 | `FollowerFrame_HP` | 1 / 1 | 34x3 | Follower panel: health bar | P1 |
| 616 | `FollowerFrame_PO` | 1 / 1 | 34x3 | Follower panel: poisoned health bar | P1 |
| 826 | `FollwerComandMenu` | 1 / 1 | 136x148 | Follower order menu (right click on a follower) | P2 |
| 36 | `Char_Spells_Skills` | 3 / 3 | 39x39 | Character sheet: the Spells tab button | P2 |
| 845 | `BlockedShot` | 1 / 1 | 37x20 | Message window: to-hit penalty icon of the combat panel, when its flag is set | P2 |
| 231 | `Lns_Bart` | 1 / 1 | 89x89 | Lens ring of the barter window, the schematic round view and the reading window | P2 |
| 247 | `CVR_Ring2` | 1 / 1 | 32x32 | Inventory, loot and barter windows: cover of the empty Ring2 slot | P2 |
| 204 | `MM_Cross` | 2 / 2 (only frame 0, 11x11, is drawn) | 11x11, 13x13 | World map and town map: the party marker | P2 |
| 787 | `ScrllSlideM1` | 1 / 1 | 11x1 | Character sheet, schemes page: middle of the scroll thumb ([#2917](https://github.com/Simarglok/arcanum-steamwoven/issues/2917)) | P3 |
| 627 | `Saltys_Map` | 1 / 1 | 668x400 | Reading window: picture of the written object that names it | P3 |
| 803 | `MazzerinsMystery` | 1 / 1 | 668x400 | Reading window: same | P3 |
| 329 | `MainMenuBack` | 1 / 1 | 800x600 | Main menus. Legacy in HD only when the pack's `main-menu-16x9.png` is not read or a Steamwoven full-screen plan is active (the plate is then the layout zone's backdrop) | P3 |
| 61 arts | schematic record pictures, [appendix](#appendix-schematic-record-pictures-without-a-pack-frame) | 61 / 61 | 295x225 | Schematic window: the picture of the selected record; 72 of 133 pictures are in the pack | P2 |

## Legacy by design

| Art | Name | Size | Why |
| ---: | --- | --- | --- |
| 0, 21, 352, 353, 818, 819, 820 | `cursor`, `spellcur` (7 frames), `skillcur`, `battlecur`, `Cursor-Called-Head`, `Cursor-Called-Leg`, `Cursor-Called-Arm` | 24x27 to 32x37 | Mouse cursors are Godot OS cursors set from the Legacy RGBA, never from an HD frame (`TerrainViewerCursor`). Owner recommendation of 2026-11-07 (U3 section of [HD interface art](hd-interface-art.md)): cursors stay Legacy nearest; the soft alpha fails the silhouette gate (0.904 against 0.937) |
| 468 | `hoverover` (15 frames) | 39x27 to 41x28 | The hover ring over a critter is a world-canvas sprite, and world frames stay 1x under HD ([decision of 2026-11-15](../architecture/decisions.md)) |

## Not drawn by the viewer

Frames that no code reads. They need no HD frame unless a window starts to draw them; most are already `no window row` in the [census](../ui/steamwoven-interface-census-arts.tsv).

| Art | Name | Size | Finding |
| ---: | --- | --- | --- |
| 140, 199 | `wp_main`, `WP_Cover` | 130x152 | Not in the world map's art list; [viewer status](../project/status/viewer.md) says drawn by no window |
| 257, 258 | `SP_Minus`, `SP_Plus` | 22x13 (2 frames) | The sheet draws 23/24, 624/625 and 647/648 instead |
| 463 | `MesHealthBar_Full` | 235x11 | The hovered critter's bar is a coloured rectangle and a code comment says arts 463-466, 665, 666 are not drawn (the [census](../ui/steamwoven-interface-census-arts.tsv) lists 463 as drawn by H21; the code reading of 2026-11-19 overrides it) |
| 678 | `scroll_not` | 25x26 | No reader |
| 679 to 686 | `scroll-0` to `scroll-7` | 21x20 to 26x27 | No reader |
| 1 | `cur_del` | 16x22 | Not in the cursor art list |
| 620 | `TechCur` | 24x28 | Same |
| 498, 499 | `Cursor_UP`, `Cursor_DWN` | 17x17 | Not built (ledger CU6) |
| (no number) | `cursor-identify-item` | 22x23 | No reader |
| (no number) | `followerframe_poison` | 34x3 | No reader; the poisoned bar in use is 616 |
| (no number) | `mineentrancepiece` | 166x206 | No reader |
| 642 | `PrivMes_Window` | 410x107 | Multiplayer only, not built ([#2307](https://github.com/Simarglok/arcanum-steamwoven/issues/2307)) |

Art 18 `redvial` and 19 `bluvial` (15 frames each) and 205 `mm_loc`, 206 `mm_wayp` (2 frames each) have their drawn frame 0 in the pack; the viewer reads frame 0 only
(the vial fill is a crop of frame 0), so the 28 other vial frames and the second marker frames cost nothing.

## Network play

Out of scope ([census](../ui/steamwoven-interface-census.md)): 330 `MultiPlayerBack` (800x600), 640 `MPly_PlaqueA` and 643 `MPly_PlaqueB` (234x40), 759 `MPMainMenu` (800x600).
The font 841 `MPIconsFont` is in the font table below.

## Fonts

The pack holds HD glyph frames for 28 of the 33 font ARTs; the viewer draws 26 fonts, and all 26 are in the pack with 221 to 223 frames of 225
([fonts note](../ui/steamwoven-interface-fonts.md) lists the source of each). The missing frames of a drawn font are frame 0 (character 31, a placeholder with no glyph) and frame 1 (the space, no ink), so they draw their Legacy frame by design ([format](../formats/hd-pack.md)). Two extra frames are absent: `morph15font` 126 and 129 (width 1, no ink) and
`pepper20font` 144 (a 13-wide glyph, character 175; **to check**: it has width and may be a vanished trace, priority P3, font 497 is the note body of reading windows).
Seven font ARTs are never drawn by the viewer (26, 226, 227, 228, 488, 744, 841) and need no frames; 488 and 744 hold 1 and 11 frames from earlier work.
Font 486 `LatinXCN30Font` is in the pack with 223 frames, so [#2843](https://github.com/Simarglok/arcanum-steamwoven/issues/2843) (trace and add font 486) looks done and should be closed or re-scoped by its owner; the origin of these frames (group C of the fonts note) is still not recorded.

Priority of the font gap overall: **none**.

## Bitmaps outside `art/interface`

The pack format holds interface ARTs (`sprite` entries) and portraits (`portrait` entries) only. Other bitmaps that the interface draws cannot be in the pack today.

| Group | Files | Size | Drawn by | Status |
| --- | ---: | --- | --- | --- |
| `portrait\` heroes and NPCs | 126 | 64x64 (78), 128x128 (48) | Sheet, hover panel, loot, barter, follower frames, creation, pre-made browser | **Complete**: all 126 have a `portrait` entry |
| `slide\` and `module template\slide\` | 42 + 3 | 800x600 | Ending slides, death cover, credits | No entry kind; priority N until [#2952](https://github.com/Simarglok/arcanum-steamwoven/issues/2952) is decided |
| `worldmap\` | 65 | 250x250 (64), 365x365 (1) | World map window (64 tiles) | Same |
| `art\splash\` | 3 | 800x400 | Not found drawn by a window ([census](../ui/steamwoven-interface-census.md): `splash2` has no window row) | Same, lower |
| `players\` | 24 | 64x64 (12), 128x128 (12) | Saved multiplayer characters (`.mpc`); the pre-made browser reads `portrait\`, not these | N (network play) |
| `modules\arcanum\save\` | 3 | 468x300 | Save slot screenshots written by the game at run time | N (not shipped art) |
| `townmap\` | not listed | small | Town map tiles under the town map window | Not counted: not interface chrome; same question as the slides ([#2952](https://github.com/Simarglok/arcanum-steamwoven/issues/2952)) |

## Lighting

The relit look is computed in the viewer from the map's light list and each sprite's own pixels (owner decision of 2026-11-15, [area lighting](area-lighting.md)). It reads no pack
art and no stored normal maps (deferred with HD world frames). The installation's lighting files (`art/light/*.art` 32 files, `shadowmap.bmp`) are inputs of the Legacy
lighting, not HD art. **Missing lighting images: 0.**

## Follow-up issues

| Issue | Group | Owner action |
| --- | --- | --- |
| [#2950](https://github.com/Simarglok/arcanum-steamwoven/issues/2950) | The 13 single arts of the table above | Decide A (redraw and add) or B (accept Legacy) per priority |
| [#2951](https://github.com/Simarglok/arcanum-steamwoven/issues/2951) | 61 schematic record pictures | Same |
| [#2917](https://github.com/Simarglok/arcanum-steamwoven/issues/2917) | Art 787 | Existing |
| [#2952](https://github.com/Simarglok/arcanum-steamwoven/issues/2952) | Slides, world-map tiles, splash | Decide whether HD covers them |

No art is made by a thread; frames reach the pack by the owner's pipeline and `hd_pack.py plan/apply --add-new` with the game closed ([HD pack workflow](../workflow/hd-pack.md)).
Re-run the census after a pack change by repeating the join of the method above; the numbers of this note are of the pack installed on 2026-11-19 (7,075 entries, the redraw build of 2026-11-08 with the traced and Codex fonts and the portraits added since).

## Appendix: font ARTs

| Art | Font | Frames in pack of 225 | Drawn by the viewer | Missing frames |
| ---: | --- | ---: | --- | --- |
| 25 | `rollerfont` | 223 | yes | 0, 1 |
| 26 | `arial10font` | 0 | no | all |
| 27 | `morph15font` | 221 | yes | 0, 1, 126, 129 |
| 28 | `pork12font` | 223 | yes | 0, 1 |
| 37 | `casablanca16font` | 223 | yes | 0, 1 |
| 171 | `Cloister18Font` | 223 | yes | 0, 1 |
| 226 | `ArialB12Font` | 0 | no | all |
| 227 | `Comic12Font` | 0 | no | all |
| 228 | `Elga12Font` | 0 | no | all |
| 229 | `Flare12Font` | 223 | yes | 0, 1 |
| 230 | `Flare14Font` | 223 | yes | 0, 1 |
| 300 | `Icons17Font` | 223 | yes | 0, 1 |
| 301 | `Nick16Font` | 223 | yes | 0, 1 |
| 327 | `Morph30Font` | 223 | yes | 0, 1 |
| 370 | `Icons32Font` | 223 | yes | 0, 1 |
| 371 | `CasablancaAntique30Font` | 223 | yes | 0, 1 |
| 478 | `BookmanOldBold18Font` | 223 | yes | 0, 1 |
| 479 | `ClarendonBLK18Font` | 223 | yes | 0, 1 |
| 480 | `Courier10Font` | 223 | yes | 0, 1 |
| 481 | `Garmond6Font` | 223 | yes | 0, 1 |
| 482 | `Garmond8Font` | 223 | yes | 0, 1 |
| 483 | `Garmond9Font` | 223 | yes | 0, 1 |
| 484 | `Georgia30Font` | 223 | yes | 0, 1 |
| 485 | `Zurich20Font` | 223 | yes | 0, 1 |
| 486 | `LatinXCN30Font` | 223 | yes | 0, 1 |
| 488 | `NewsIconsFont` | 1 | no | 224 of 225 |
| 490 | `Swiss921Font` | 223 | yes | 0, 1 |
| 492 | `Zurich16Font` | 223 | yes | 0, 1 |
| 497 | `Pepper20Font` | 222 | yes | 0, 1, 144 |
| 744 | `BookImagesFont` | 11 | no | 214 of 225 |
| 815 | `Euph30Font` | 223 | yes | 0, 1 |
| 840 | `NewTimes16Font` | 223 | yes | 0, 1 |
| 841 | `MPIconsFont` | 0 | no | all |

## Appendix: schematic record pictures without a pack frame

All 295x225, one frame each, drawn in the schematic window (priority P2). Art number and `interface.mes` name.

| Art | Name | Art | Name | Art | Name |
| ---: | --- | ---: | --- | ---: | --- |
| 523 | `explosive_grenade_scheme` | 524 | `fatigue_limiter_scheme` | 527 | `fire_obstruction_scheme` |
| 528 | `flash_grenadescheme` | 529 | `folding_rifle_scheme` | 530 | `hallicinite_scheme` |
| 531 | `heal_accelerator_scheme` | 535 | `intelligence_booster_scheme` | 543 | `pure_ore_scheme` |
| 544 | `repeater_rifle_scheme` | 546 | `smoke_grenade_scheme` | 547 | `strength_booster_scheme` |
| 548 | `stun_grenade_scheme` | 549 | `sword_of_balance_scheme` | 550 | `telsa_rod_scheme` |
| 551 | `ultralite_axe_scheme` | 552 | `ultralite_axe_scheme_2` | 553 | `ultralite_chain_mail_scheme` |
| 554 | `wonder_drug_scheme` | 591 | `fuel_scheme` | 593 | `S_Auto_Skeleton_Key` |
| 594 | `S_bear_trap` | 598 | `S_eye_gear` | 599 | `S_spike_trap` |
| 600 | `S_trap_springer` | 618 | `hushed_revolver_schem` | 622 | `Crude_Flintlock_schem` |
| 691 | `S_Reanimator` | 692 | `S_Clockwork_Physician` | 695 | `S_Necromizer` |
| 696 | `S_Fortifier` | 697 | `S_Sleep_Aid` | 698 | `S_Potion_of_Paralysis` |
| 699 | `S_Charged_Sword` | 700 | `S_Flow_Disruptor` | 701 | `S_Tesla_Gun` |
| 702 | `S_Bellringer` | 704 | `S_Pyrotechnic_Bow` | 706 | `S_Hallucination_Grenade` |
| 709 | `S_Time_Bomb` | 711 | `S_Paralysis_Grenade` | 712 | `S_Mustard_Gas_Grenade` |
| 713 | `S_Plastique` | 717 | `S_Pyrotechnic_Gun` | 718 | `S_Blade_Launcher` |
| 719 | `S_High_Velocity_Pistol` | 720 | `S_Schrecks_Pistol` | 723 | `S_Tranquilizer_Gun` |
| 725 | `S_Compound_Bow` | 727 | `S_Medical_Arachnid` | 728 | `S_Illuminated_Decoy` |
| 729 | `S_Explosive_Decoy` | 730 | `S_Automaton_Gunner` | 735 | `S_Pyrotechnic_Axe` |
| 736 | `S_Rifled_Cannon` | 739 | `S_Brain_Builder` | 740 | `S_Muscle_Maker` |
| 742 | `S_Charges` | 743 | `s_hand_cannon` | 769 | `s_Vendigrothian_War_Guantlets` |
| 790 | `S_noise_maker_decoy` |  |  |  |  |
