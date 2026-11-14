# Companion orders runtime

Status: **Implemented bounded component**, ARC-WORLD-046 (the orders slice of the
[companion orders study](../research/companion-orders-loyalty-evidence.md#implementation-slices), ARC-RES-064 rows OL1–OL16).
Out-of-combat orders to the PC's followers: pure rules in `Arcanum.Simulation.Party` (`CompanionOrderRules`,
`CompanionOrderRuleProfile`), one session entry (`GameSessionOwner.IssueCompanionOrder`, `GameSessionOrders.cs`) and the AREA
viewer's F1–F6 presets and follower portrait menu (exterior, cave and map mode). Universal: any follower on any map. It covers
the order part of [FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004) and the wait and leave orders of
[FR-SOCIAL-005](../requirements/story-social-ai.md#fr-social-005).

The combat orders (attack, back off/stop, curse) are the NPC AI's slice I9 (ARC-WORLD-091, [AI followers](ai-followers.md)) through
the same entry.

**NOT READY:** without the AI host (the AI off) the attack order is sent without a target (it does nothing) and no AI pass runs after an order; an attack order on a container or a ground item is ignored by the host's attack event, and on an object without a session entity (a door, scenery) it has no target (ARC-WORLD-117); there is no typed order text (Enter) and no keyword lookup of `mes\broadcast.mes`; the move order's "please move" goal
is not run by the viewer; lines are named by their message range, no line text or speech
is loaded; the spread-out step is a project approximation (below). Owner decisions D1–D4 (2026-10-21): the Steamwoven setting fixes each
bug, the Original setting keeps it. Parity of the viewer's goals NOT_VERIFIED.

## Rules (`Arcanum.Simulation.Party.CompanionOrderRules`)

| Rule | Behaviour | Rows |
| --- | --- | --- |
| `CompanionOrderType` | The fourteen types of `Rules\BroadCastRules.mes` in their numbering: none, leave, wait, follow, move, stay close, spread out, curse, join, disband, attack, walk here, back off, follow other. | OL3 |
| `Preset(index, profile)` (0x4C3BE0, partial) | F1–F5: walk here, attack, stay close, spread out and back off, each to every follower in reach. F6 names nobody in the Original setting; "follow" to all in Steamwoven (D2). The installed presets' meaning, not their text. | OL1, OL14 |
| `PortraitOrder(item, mindControlled)` (0x56A9D0, partial) | The portrait menu's items (`mes\follower_ui.mes` 0–7): walk (with a picked tile), attack, stay close, spread out, back off, wait (left out for a mind-controlled follower); inventory and character sheet open windows (null). | OL1 |
| `AllInReach` | Followers of the array within 10 tiles of the PC (the original's Chebyshev tile distance 0x4B96F0, farther than 10 skipped), in array order. | OL2 |
| `PortraitReceiver` | Original (D4): the menu sends "name label" (format two strings with a space); the first follower in reach, in array order, whose known name is a case-insensitive prefix of the text takes it and reads the rest after its own name, leading spaces skipped, as its order word (a shorter name sharing the prefix reads no order). Steamwoven: the clicked follower itself, in reach. | OL15 |
| `Farewell(reaction, profile)` (0x4133B0, partial) | Original: reaction level suspicious or worse (40 or less, 0x4C0FC0) takes 1900–1999, which the line files label friendly, above takes 2000–2099; Steamwoven the other way round (D3). | OL13 |
| `Lines(line)` | Acknowledgement 2100–2199, "cannot" 2200–2299, farewells 1900–1999 and 2000–2099, in the generated NPC line files by both genders (the host's choice and can-speak test). | OL12 |
| `IsCombat(type)` | Attack, back off and curse belong to stage A7 (slice I9). | OL10, OL11 |

`CompanionOrderRuleProfile` holds the four switches of decisions D1–D4 ([rule modes](rule-modes.md#registry)):
`FollowOrderNeverUnwaits` (OL6), `PresetSixAddressesNobody` (OL14), `FarewellRangesSwapped` (OL13) and
`PortraitAddressesByNamePrefix` (OL15); each is true in Original and false in Steamwoven, and each is a separate switch so another
owner answer changes one value. `GameRules.Orders` is the active profile, read at each order.

## Session (`GameSessionOwner.IssueCompanionOrder`, 0x4C31A0 partial)

`IssueCompanionOrder(expected, CompanionOrderRequest(Type, Addressing, Followers, Clicked, Label, Tile))`. The host gives
`CompanionOrderFacts` for the followers it knows: the name the PC knows, the Chebyshev distance to the PC, mind control, the follow
facts (whose reaction also chooses the farewell) and the wait-timer facts; a follower without facts is out of reach. Receivers are
the session's array members in array order (a waiting NPC is outside the array and gets nothing, OL16): all in reach, or the portrait's
receiver. Under the Steamwoven rules a follow order to all also reaches the waiting followers in reach, after the array in ordinal
order (D1). Each receiver is one step of its own through the party entries (one revision each when it changes something); one that
fails does not stop the others.

| Order | Effect (`CompanionOrderOutcome`) | Line | Rows |
| --- | --- | --- | --- |
| leave | mind controlled: `MindControlled`; otherwise a **forced** leave (`Left`, not jilted; an extraordinary leader does not stop it) | farewell by reaction | OL4, OL13 |
| wait | mind controlled: `MindControlled`; otherwise the ordinary wait with its timer ([ARC-WORLD-045](party-runtime.md#wait-timer-and-loyalty-arc-world-045)), `Waited`; the host interrupts its animations | acknowledgement | OL5 |
| follow (array member) | the follow check, the charisma limit ignored only for a forced follower (a follower counts toward the limit itself): `Acknowledged` with no state change (the unwait never acts) or `Refused` | acknowledgement / "cannot" | OL6 |
| follow (waiting, Steamwoven D1) | `UnwaitParty` unforced: `Unwaited` (back at the array's end) or `Refused` | acknowledgement / "cannot" | D1 |
| move | `MoveRequested`: the host's "please move" goal; it shows the acknowledgement when the goal starts, "cannot" otherwise | host | OL7 |
| stay close / spread out | the spread flag cleared / set (`SpreadCleared`, `SpreadSet`; follow distances 4/2 or 7/5); `StartSpreadGoal` when spread out set a clear flag | acknowledgement | OL8 |
| walk here | with a tile: `WalkRequested` (the host runs the follower there and it follows again after); without: `NoTile` | acknowledgement | OL9 |
| attack, back off, curse | slice I9 ([AI followers](ai-followers.md)): `NoTarget`, `PartyTarget`, `AttackIgnored`, `AttackLocked`, `Complained`; `Stopped`; `MindControlled`, `LeaveRefused`, `Cursed` | none; acknowledgement for back off; the upset line of a complaint, the insult of a curse | OL10, OL11, FO2–FO5 |
| none, join, disband, follow other | `NoOrder` (the AI pass only; join and disband act in the multiplayer phase) | none | OL3 |

Whole-order refusals (nothing applied): an undefined type or addressing, an invalid or repeated entity, a missing or empty name
(it would prefix every portrait text), a negative distance or a portrait order without a valid clicked follower
(`InvalidCompanion`), follow facts naming another leader or undefined trainings (`InvalidConsentFacts`), a portrait for an NPC that does not follow (`NotInParty`), a stale version. Mind control is the host's
answer in three places (`CompanionOrderFacts.MindControlled` for leave and wait, the follow facts' `MindControlledByPc`, the wait
facts' `MindControlled`); the host keeps them consistent. No new saved state: no snapshot change.

## AREA viewer

- **F1–F6** (the original's preset keys, until now reserved and not implemented) without modifiers send the presets on the key's
  release, as the original does on key up, with the chart cell under the pointer at the release as the walk tile, while the session
  party is wired and the world owns input (focused, no modal window or control focus, no map change pending; `RouteOrderKey`); the
  release counts only after a bare press while the world owned input with no other key pressed since (a modifier aside), so with
  overlapping F-keys only the last one pressed is sent. While the palette is open, the window has no focus, a control has focus or a
  map change is pending the key shows "ignored"; an open window that takes every key (inventory, character sheet, logbook,
  schematics, world map, a conversation) owns it, and it does nothing. An F-key pressed while the order menu is open closes the menu
  and is still sent on its release. F6 under the Original rules shows "no
  order (the original's preset names nobody)". Without a party they stay reserved keys. Requiring no modifier is the viewer's own:
  the original's F1–F6 handlers mostly ignore Ctrl and Shift (KM-09 in
  [original keyboard and mouse](../requirements/original-keyboard-mouse.md)), and the viewer keeps those combinations for its
  diagnostic layer.
- **Follower portraits** (`AreaFollowerPortraits`; since ARC-UI-024 the original's frames down the left edge, [panel art](../rendering/terrain-viewer.md#panel-art-arc-ui-024)): one frame per follower in the array (in its order, at most six; none for a waiting
  one), each opening, **on the right button's release** over it (ARC-DEV-280, B33, [OL34](../research/ledger/social.md)), the portrait menu: walk, attack, stay close, spread out, back off, wait, and inventory and character sheet
  shown disabled. The **left button's release** over a portrait is a click on that follower, as a press on its body is: out of combat mode a talk (goal 21; in combat mode no attack yet: the original attacks with left Alt held). Since ARC-UI-020 each frame has the follower's health and fatigue bars and turns red below 20 %
  health ([health and fatigue bars](../rendering/terrain-viewer.md#health-and-fatigue-bars-arc-ui-020)). The menu is an inline panel of buttons that take no focus, not a popup window (an embedded popup takes the world
  window's focus, which stops the PC's route and refuses the choice); a second right click or a left click on a portrait, a right release
  anywhere else, any key other than a bare modifier, or a press on the ground that the world accepts closes it (that press does nothing
  else; a press while the world does not own input is ignored as any ground press is). A window resize closes it too. Walk asks for a tile with the next left press on the ground (the original's target cursor; not while the hero is
  dead or unconscious, OL1); the HUD shows the pending walk (or attack) order, and any key other than a bare modifier, a modal window, focus
  loss, another menu choice or a rebuilt group cancels it. A ground press while the world does not own input is ignored as any
  ground press is (the walk stays pending); the press that picks the tile is refused while the hero is dead or unconscious. Attack
  asks for an object the same way (ARC-WORLD-117): the next left press's object is the target (none on an empty spot), and with the
  AI host the orders run each follower's AI pass ([AI host](ai-host.md#followers-in-combat-arc-world-115)); F2's target is the
  object under the pointer at the release. The strip is hidden and its menu closed
  while a modal window is open, and a menu choice is refused unless the world owns input (as for F1–F6). The menu's labels are
  the viewer's own words (the original reads `mes\follower_ui.mes`); the Original name addressing compares them as the label.
- **Facts** (`AreaCompanionOrders.Facts`): the following and waiting companions of the shown map, the hover name (else "script N"),
  the Chebyshev distance of its cell to the PC's, the loyalty step's follow facts and `AreaCompanionRoster.WaitFacts`. The PC side
  of both is the session hero with `--hero` (ARC-WORLD-067, [party runtime](party-runtime.md#the-session-hero-as-the-pc-side-arc-world-067)),
  else the labelled diagnostic profile.
- **Goals**: walk here and the spread-out step are ordered goals of the companion's `CompanionFollower` (`WalkTo`): following does not
  replace them, and once they end the companion decides again from where it stands (beyond the start distance it approaches, OL9).
  The spread-out step walks 3 cells straight away from the PC (`AreaCompanionOrders.SpreadStep`), a project approximation of the
  original's "attempt spread out" goal (69), whose move-away distance is not read. A goal whose cell is off the map's steps does not
  start; the log and HUD name those companions. A walk to a tile more than 30 cells from the PC ends with the project's jump onto the
  PC once the ordered goal ends (the follow distances' rule beyond 30, [follow rules](companion-follow-runtime.md)).
- **HUD and log**: the companion line names each reached follower's effect and line range ("F4: oid:… SpreadOut -> SpreadSet lines
  2100-2199"); the log prints `ARC-WORLD-046 order source=<F1..F6|portrait> type=… disposition=… reached=N | …`.

## Follower portraits' hover (ARC-UI-1281)

Ledger [HK26](../research/ledger/spell-runs.md) (callback 0x56A9D0, strip message type 1, code 2, the call 0x56AD44 into 0x57CD60; leave 0x550720).
A pointer entering a portrait button (`AreaFollowerPortraits`: `Button.MouseEntered`, raised as `PortraitHovered`) with no order menu open (0x67BC0C = −1)
and no dialog active (0x567400 = 0; the viewer reads the dialog window) describes the follower for the hero as the world's hover describes an object
(`TerrainViewerApp.FollowerPortraitHovered`): the follower's examine script (point 0, `ExamineAllows`) runs once, its printed lines go to the message
window, and unless it answered skip default the follower's window follows in the message window (the same `SyncHoverWindow` path as the world hover and the
dialog start, HK25), the highlight (0x43C270) and the rings (ARC-UI-1117) go on him too. Leaving the portrait (`MouseExited`, or the frames being rebuilt under
the pointer, `PortraitHoverLeft`; 0x550720, 0x43C270) clears the window and the highlight, and the world's hover takes over again. The pointer moving inside
the portrait runs nothing more, and a redraw of his window (the bars shown again) reuses the kept answer instead of running the script again;
a dialog started from the portrait describes him afresh (HK25). With the interface bars off the script still runs and no window is drawn (as the dialog start). The order menu's own rows
(OL34) are not part of it. The "hovered" key of the highlight, the window and the rings is the one under the pointer, else the portrait's follower
(`HoveredKeyNow`). A follower the shown map's hover index does not list (carried from another map) only runs his script.

## Follower panel and menu in the original's form (ARC-UI-1736)

Ledger [FP1–FP13](../research/ledger/ui-audio.md#follower-panel-and-menu-arc-res-1107) (module 0x56A4A0..0x56B970). `AreaFollowerPortraits` is the original's
follower module; its Godot-free rules are `AreaFollowerPanelRules` (tools/terrain-viewer/Core).

- **Show/hide and scroll (FP1, FP3, FP5)**: the show/hide button (art 501, 502 while hidden, 67 × 13 at (0, 428)) stands while there is a follower; hiding removes
  the six frames and the arrows. From seven followers the up arrow (art 506, 507 at offset 0, 33 × 13 at (0, 415)) and the down arrow (508, 509 at count − 6, 34 × 13 at
  (33, 415)) move the offset by one follower per release, and the six frames show `offset .. offset + 5`; the offset clamps to 0 up to six followers and to count − 6
  above. A new session (the panel's `ResetPanel`, the module's reset 0x56A880) shows the panel again from the first follower.
- **FP2 decision**: the shown flag and the offset are session-only. The original writes them into its save (two 32-bit values with no gameplay effect); FR-LIFECYCLE-004
  keeps saves for game state, so the viewer does not add a schema step for them.
- **Hidden-interface layout (SZ11, issue #1785)**: the original moves the three buttons to y 587, 574 and 574 while the interface bars are hidden (the portraits stay). The
  viewer takes the layout from the bars' state (`AreaGameBarPanel.BottomInset > 0` means drawn; `AreaFollowerPanelRules.ToggleRectFor/UpRectFor/DownRectFor`) and moves the
  existing buttons at once when the bars are shown or hidden (the follower companion smoke checks both).
- **Menu (FP7–FP9)**: art 826 at the slot's right edge (x 51, the slot's y), not clamped to the screen, with eight rows (Walk, Attack, Stay Close, Spread Out, Back Off,
  Wait, Inventory, Character Sheet) at (10, 3 + 18 × row), 118 × 15: white, mid grey when greyed, bright (0, 0xD2, 0xFF) under the pointer. Wait is greyed for a
  mind-controlled follower (spell flag 0x8000000), Inventory when the can-open test (0x575080, `BarterCycleSpeaker`) fails. With the menu open every release is the menu's:
  a row runs after the menu closes, any other left or right release closes it, and the portraits, the button and the arrows are not handled.
- **Rows 6 and 7 (FP11, FP12)**: Inventory opens the follower's barter window in its follower form (the pending open the dialog's `b:` reply uses); Character Sheet
  opens the NPC sheet (`OpenNpcSheet`, read-only).
- **Portrait aiming (FP6)**: with the spell cursor open (`FollowerAimSpell`: the cast host's pick with the follower as the object and no tile), the item cursor
  (`FollowerAimItem`: `ItemAimPickAt`) or the pick pocket cursor (`FollowerAimPickPocket`: `PickPocketOn`), a left release on a portrait aims at that follower, as a press on
  his body would, and the area test decides; the disarm cursor is cancelled (a follower is no trap). In any other mode the click is the ordinary one (a talk, B33). A visiting
  follower has no cast id on the shown map and refuses a spell or item target. The lockpick cursor and the follower order's own target mode (walk, attack) leave the click as it
  was (ignored).
- **Tests**: `AreaFollowerPanelRulesTests`; the `companion-click` stage's facts (the hide and show buttons, the menu's rows and place, the margin release, the sheet and
  the barter window, eight synthetic followers scrolled by their arrows). `ARC_AREA_FOLLOWER_PANEL_CAPTURE` saves readbacks of the stage to a caller's ignored path.

## Tests

- `tests/Arcanum.Simulation.Tests/Party/CompanionOrderRulesTests.cs`: presets with F6 under both settings (D2), the portrait items and
  the mind-controlled wait, the 10-tile reach in array order, name-prefix and clicked addressing (D4), farewell ranges (D3), the
  profiles. `CompanionFollowTests`: the ordered walk is not replaced by following and following resumes after it.
- `tests/Arcanum.Session.Tests/CompanionOrderStateTests.cs`: addressing (array order, reach, waiting and unknown followers excluded),
  leave (forced, farewells, mind control), wait with the ARC-WORLD-045 timer, follow as a no-op versus D1, spread out and stay close,
  walk, move, an attack without a target and no-order outcomes, portrait addressing under both settings, whole-order refusals.
- `tests/Arcanum.TerrainViewer.Tests/AreaCompanionOrderTests.cs`: the viewer's facts, the walk and spread goals, wait.
- Native smoke (`--native-smoke --mode area` with the original roles): `ARC-WORLD-046 native-smoke orders PASS` inside the
  follow stage: F4 through the keyboard path (press and release) spreads Virgil out and steps him away, F3 calls him close again; a
  mouse right click on his portrait button (ARC-DEV-280; a left click is a talk) opens the order menu without a focus change of the world window (no focus-exit signal; the
  check failed with a popup, also without OS focus) and a press on its wait button makes him wait (no timer, wait index 6) and
  removes his portrait; the GUI takes both presses (no world click, the PC does not move). The reserved-key smoke now uses F10.
- Native smoke, ARC-UI-1281 (HK26): the `hover` stage prints `ARC-UI-1281 native-smoke hover-portrait portrait=Nfacts` (a free companion of the shown map joins,
  comes up to the hero, and with motion events the pointer enters his portrait: his examine runs once, the pointer moving inside runs nothing more, his window
  shows with the bars drawn, leaving clears it, entering with the order menu open runs nothing; he leaves the party again; `none (…)` when the map has no free
  companion), and the `companion-click` stage's fact list has the same facts plus "a portrait hover with the dialog active runs nothing".

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~CompanionOrder|FullyQualifiedName~CompanionFollow"
"$DOTNET" test tests/Arcanum.Session.Tests --no-build --filter "FullyQualifiedName~CompanionOrder"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~AreaCompanionOrder"
```
