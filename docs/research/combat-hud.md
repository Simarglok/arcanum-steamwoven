# Combat HUD study (ARC-UI-017)

Status: **Research**, work item ARC-UI-017 (alpha-gap N4), 2026-10-28. Requirements: [FR-COMBAT-011](../requirements/combat-magic-tech.md#fr-combat-011),
[FR-UI-002](../requirements/ui-media-lifecycle.md#fr-ui-002), [FR-UI-005](../requirements/ui-media-lifecycle.md#fr-ui-005).
Evidence: the ledger section [Combat HUD](ledger/ui-audio.md#combat-hud-arc-ui-017), rows CH1–CH12 (F2, read in the retail
1.0.7.4 `Arcanum.exe` per the [original executable check](original-executable-check.md)).

This study records what the original shows of a fight and turns it into a plan for the AREA viewer. It changes no code; the
viewer work is split into the slices at the end, each its own task and pull request.

## What the original shows

- **Combat mode.** The combat interface adds no mode indicator: its turn-based panel (CH1) exists only while a round runs,
  so its presence is the turn-based cue (the main interface bar's own controls were not read here). The viewer's status
  line already names the mode (ARC-COMBAT-004).
- **Action points** (CH2–CH4). A row of 20 lamps in four groups of five just above the interface bar, lit from the left. The lamps
  show the points left after the hovered action, then that action's cost, then the part of the cost the points do not cover,
  each in its own art; more than 20 points show 20. Every action-point check (the cost preview of the cursor's action) and
  every charge redraws them, so the player sees the price before acting and the remainder after (with the last checked cost still
  previewed, CH4). With the interface bar
  hidden the same three shares are a 120 px bar on a 25-point scale: green, orange and red.
- **Whose turn** (CH5, CH6). On another participant's subturn the lamps show a running light (three lamps, seven apart,
  one step every 100 ms, a round of the 20 lamps every 2 s); on the PC's own subturn the points. The combat interface shows
  no turn order, no list of participants and no name of whoever acts. The end-turn button sits on the lamp panel (CT5).
- **Health and fatigue.** The PC: two vertical gauges in the interface bar with the current values printed, health in its
  poisoned art and the poison level when poisoned (CH7). A hovered critter: health and fatigue bars on the interface bar's
  description line, with "current/maximum" for the PC and its followers and a percentage for everyone else (CH8). The
  followers: health and fatigue bars under each portrait on the left edge, the face tinted red below 20 % health (CH9).
- **Floating text** (CH10–CH12). Lines rise over the object they belong to: 4 line heights in steps of speed + 1 px at most
  every 50 ms (up to 60 px a second at the default speed 2), fading in and out over 1.5 line heights, a new line stacking one line
  below a line that has just started. The "text floaters" setting chooses none (0), messages only (1, the default) or
  messages and numbers (2). Numbers are the damage (health and fatigue together in one line) and poison increases; messages
  are dodges, criticals, the damage multipliers and the critical effects. Colours mostly mark the side: red for the PC, yellow
  for the PC's followers, white for anyone else, green for poison (a dodge is always white). The healing function creates
  no floater (CH12).

## Steamwoven design for the viewer

The viewer draws no original interface art (its windows are plain layouts, as in ARC-UI-004 and ARC-UI-007), so each item
keeps the original's information and behaviour and draws it with Godot controls:

- **Action points**: 20 cells in four groups of five, filled in the three shares of CH2 (left, cost, shortfall) from the
  hero's turn-based points and the cost of the action under the cursor (the viewer's attack, step and cast already compute
  their costs through the round's checks); the running light of CH5 on another participant's subturn. Shown only while a
  round runs, next to the existing E (end turn) handling.
- **Turn order**: the alpha plan's row N4 asks for a queue, which the original does not show (CH6). A small list of the
  round's participants with the current one marked is a Steamwoven addition, approved by the owner on 2026-10-28: shown in
  the Steamwoven rule mode and hidden in the Original mode, which keeps the original's bare cue.
- **Health and fatigue**: the hero's two values as bars with numbers (CH7), the followers' bars on the existing follower
  portraits (ARC-WORLD-046) with the red tint below 20 % (CH9), and the hovered critter's bars with the CH8 text rule.
- **Floating text**: one layer over the AREA view with the CH11 motion, stacking and fade, the CH10 colours and the CH12
  texts (message lines read from the player's `mes\combat.mes` at run time), driven by the session's damage, poison and
  critical-effect results; the two settings join the options window's Video page (OP2 lists them as hidden today).

## Slices

Each slice is its own task code, thread and pull request (owner rule 2026-10-28), branched from `origin/main`:

1. **This study** (ARC-UI-017): the ledger rows and this plan. Docs only.
2. **Action points and turn cue**: the 20-cell panel with the three shares and the cost preview, the running light on other
   subturns, the turn-order list (Steamwoven mode only); FR-COMBAT-011.
3. **Health and fatigue bars**: the hero's gauges with numbers and poison, the follower portraits' bars and red tint, the
   hovered critter's bars; FR-UI-002. Done in ARC-UI-020 ([viewer](../rendering/terrain-viewer.md#health-and-fatigue-bars-arc-ui-020)).
4. **Floating text**: the layer, its motion and colours, the damage, poison and message lines, the two settings in the
   options window; FR-UI-005. Done in ARC-UI-021 ([viewer](../rendering/terrain-viewer.md#floating-text-arc-ui-021)).

Slice 2 depends on the viewer's turn-based host (ARC-COMBAT-020) and on the NPC fighting host (ARC-WORLD-092) for other
participants' subturns; slices 3 and 4 can start at once. Slices 2–4 all touch the viewer's screen layout, so they must keep
clear of the inventory window (ARC-UI-015, merged) and the item spell pane (ARC-UI-016).
