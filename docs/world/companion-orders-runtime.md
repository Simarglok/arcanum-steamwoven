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

**NOT READY:** the viewer gives the attack order no target (it does nothing) and runs no AI pass after an order; there is no typed order text (Enter) and no keyword lookup of `mes\broadcast.mes`; the move order's "please move" goal
and the follower's AI pass after an order are not run by the viewer; lines are named by their message range, no line text or speech
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
- **Follower portraits** (`AreaFollowerPortraits`, bottom left): one button per follower in the array (in its order; none for a waiting
  one), each opening the portrait menu: walk, attack, stay close, spread out, back off, wait, and inventory and character sheet
  shown disabled. The menu is an inline panel of buttons that take no focus, not a popup window (an embedded popup takes the world
  window's focus, which stops the PC's route and refuses the choice); a second press of the portrait, any key other than a bare
  modifier, or a press on the ground that the world accepts closes it (that press does nothing else; a press while the world does
  not own input is ignored as any ground press is). A window resize closes it too. Walk asks for a tile with the next left press on the ground (the original's target cursor; not while the hero is
  dead or unconscious, OL1); the HUD shows the pending walk order, and any key other than a bare modifier, a modal window, focus
  loss, another menu choice or a rebuilt group cancels it. A ground press while the world does not own input is ignored as any
  ground press is (the walk stays pending); the press that picks the tile is refused while the hero is dead or unconscious. Attack
  is sent at once, with no target cursor and no target (slice I9 answers `NoTarget`: nothing). The strip is hidden and its menu closed
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

## Tests

- `tests/Arcanum.Simulation.Tests/Party/CompanionOrderRulesTests.cs`: presets with F6 under both settings (D2), the portrait items and
  the mind-controlled wait, the 10-tile reach in array order, name-prefix and clicked addressing (D4), farewell ranges (D3), the
  profiles. `CompanionFollowTests`: the ordered walk is not replaced by following and following resumes after it.
- `tests/Arcanum.Session.Tests/CompanionOrderStateTests.cs`: addressing (array order, reach, waiting and unknown followers excluded),
  leave (forced, farewells, mind control), wait with the ARC-WORLD-045 timer, follow as a no-op versus D1, spread out and stay close,
  walk, move, an attack without a target and no-order outcomes, portrait addressing under both settings, whole-order refusals.
- `tests/Arcanum.TerrainViewer.Tests/AreaCompanionOrderTests.cs`: the viewer's facts, the walk and spread goals, wait.
- Native smoke (`--native-smoke --mode crash-site-area` with the original roles): `ARC-WORLD-046 native-smoke orders PASS` inside the
  follow stage: F4 through the keyboard path (press and release) spreads Virgil out and steps him away, F3 calls him close again; a
  mouse press on his portrait button opens the order menu without a focus change of the world window (no focus-exit signal; the
  check failed with a popup, also without OS focus) and a press on its wait button makes him wait (no timer, wait index 6) and
  removes his portrait; the GUI takes both presses (no world click, the PC does not move). The reserved-key smoke now uses F10.

```sh
"$DOTNET" test tests/Arcanum.Simulation.Tests --no-build --filter "FullyQualifiedName~CompanionOrder|FullyQualifiedName~CompanionFollow"
"$DOTNET" test tests/Arcanum.Session.Tests --no-build --filter "FullyQualifiedName~CompanionOrder"
"$DOTNET" test tests/Arcanum.TerrainViewer.Tests --no-build --filter "FullyQualifiedName~AreaCompanionOrder"
```
