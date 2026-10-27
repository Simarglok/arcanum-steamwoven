# Multiplayer of the original game: sessions, authority, modules and characters

Status: **Research**, work item ARC-RES-072 (2026-10-27). It describes how the
retail 1.0.7.4 game runs a network game: the session kinds, the transport, the
host's authority and what it replicates, why only a multiplayer module such as
`Vormantown` is offered, the server options, player parties, the multiplayer
character file and the clock, combat and travel limits of a network game. It
ends with a requirement group proposal and a recommendation on whether and
when multiplayer fits the project. It changes no code, viewer, session schema
or launcher. Multiplayer is not in alpha scope (owner's alpha plan, outside
the repository) and stays an early non-goal of
[UI, media and lifecycle](../requirements/ui-media-lifecycle.md#явные-ранние-non-goals-и-область-unknown).

Requirements: [FR-SOCIAL-006](../requirements/story-social-ai.md#fr-social-006)
(the player party of a network session) and the early non-goal "Multiplayer,
network, lobby, chat" of [UI, media and lifecycle](../requirements/ui-media-lifecycle.md#явные-ранние-non-goals-и-область-unknown);
the proposed new group is in [Requirement group proposal](#requirement-group-proposal).
The verified rows are in the ledger section
[Multiplayer (ARC-RES-072)](ledger/multiplayer.md#multiplayer-arc-res-072).

## Sources and evidence

- **The executable (F2).** The owner's unchanged GOG install, `Version.txt`
  1.0.7.4, `Arcanum.exe` SHA-256
  `9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52` (checked
  before and during the reading). Read on the owner's Mac with Xcode's
  `llvm-objdump` and the import table, following the
  [original executable check](original-executable-check.md): the multiplayer
  range 0x49C670–0x4A6560, the network layer around 0x527280–0x52AF20, the
  multiplayer screens of the main menu (0x540930–0x549A80, 0x583200–0x586ED0,
  0x569DD0) and the callers named below. A second reader re-read every function
  the answers rest on without seeing the first verdicts (method step 6); its
  refinements (listed in the ledger section) were checked and taken, and no disagreement remains. Every
  direct branch stays inside `.text`; none reaches `.IIDKing`. No bytes,
  listings or decompiled code were kept; scratch stayed on the Mac.
- **Original data (D).** Member names and sizes of `modules/Arcanum.dat`,
  `modules/Vormantown.dat` and the base archives compared with
  [`dat_probe.py`](../../tools/research/dat_probe.py) (read-only); the
  matchmaker settings (product, default server, port, server list) were read
  from the contents of `Rules\Matchmaker.mes` together with its reader
  0x4A4D60. No game text is copied here.
- **The manual (reported).** Chapter 5 of the install's `Manual.pdf`,
  paraphrased; the readme has no multiplayer section. A manual states intent,
  not code.
- **Public sources (reported).** The Sierra FAQ of March 2000 and the
  multiplayer page of the official site (pre-release plans, partly not
  shipped), forum threads on Steam and GOG (players' reports), and the
  Wikipedia list of WON games. They are context only; where they differ from
  the executable, the executable wins.
- **No source reconstruction.** The arcanum-ce project never implemented
  multiplayer; nothing from it is used (owner decision 2026-10-18).

Function names below are labels of this reading; the address is the reference.

## Answer in short

The original ships a complete **host-authoritative client-server** network
game for up to **8 players** over its own TCP layer on WinSock. Discovery is
a UDP broadcast on the LAN, a bookmark list or a typed IP address; the
"Internet" route uses Sierra's WON matchmaker (`mm_won.dll`) only as a lobby,
chat and server list, and game traffic still goes host-to-client over TCP. WON
was shut down on 2008-11-01 (reported), so in a retail install only the LAN and direct-IP
routes can still work (forum players use LAN tunnelling tools; not checked).

A module is multiplayer-capable only when its archive or folder contains a
root file `mp.txt` (its content is never read). `Vormantown` has it, the main
`Arcanum` module does not, so the server list never offers the main campaign;
nothing else in the read code forbids it. The host creates a fresh instance of the
module, runs all rules and AI, and replicates the results through 130 packet
types (124 handled); clients send requests and apply what the host sends. A network game has
no turn-based combat, no sleep and no world-map travel (town maps only), and
its clock is driven by the host. Characters travel between games as `.mpc`
files: an exported PC keeps its stats, skills, spells and tech, but loses its
items, followers and world state; items come from an auto-equip table at join.

## Session kinds and players

| Kind | What the original does | Evidence |
| --- | --- | --- |
| LAN ("Network") | A client finds hosts by a UDP broadcast; the host answers with a server info block. A bookmark file in `data` lists IP addresses one per line (with comments), and an address can be typed. | discovery (0x527BD0), bookmarks (0x52ADA0, 0x52AF20), direct join (0x5283C0) |
| Internet (WON) | WON account login and creation, message of the day, game list, server registration as the host's IP and the game port, chat servers and rooms, an update check through Sierra's updater; the matchmaker configuration (product name, default server, port 15101, three servers) comes from `Rules\Matchmaker.mes`. | WON loader (0x4F5820) and wrappers (0x4F5820–0x4F6170, thunks 0x4A4EB0–0x4A50C0), configuration (0x4A4D60), screens (0x549040, 0x5845E0, 0x5847D0, 0x584AE0) |
| Dedicated or playing host | The host is slot 0 and plays as well; no separate dedicated mode was found. | slot array (0x6188B8, slot 0 = server) |
| Command line | Two switches set auto-join and "no broadcast". | main (0x401000, around 0x4011C1–0x4011EA) |

The game has 8 player slots; the server's "max players" option is 1..8
(0x529310). The multiplayer player table holds 8 entries with the PC's object
id (0x5E8AD0; slot clear 0x49CB80; object ↔ slot 0x4A2B10, 0x4A2B60).

## Transport

- The network layer loads `ws2_32.dll`, else `wsock32.dll`, at run time and
  binds about 90 WinSock functions by name (0x527290, 0x5273B0, 0x527720);
  `Arcanum.exe` imports no network, WON or DirectPlay library, and DirectPlay
  is not used at all. Sockets are asynchronous, signalled to the game window.
- **Host** (0x527E80): a TCP listening socket on port **31435** with
  `TCP_NODELAY`, bound to the local address, backlog 5. **Client** (0x5282A0):
  a TCP connection to the host's port 31435. **Discovery** (0x527BD0): a UDP
  broadcast socket on port **31434** to the first interface's broadcast
  address, else the limited broadcast address.
- The topology is a **star**: a host sends to every admitted client slot 1..7
  (0x5295F0), optionally skipping the sender (0x529690), or to one slot
  (0x529730); a client sends only to the host. Each slot has an 8 KiB buffer;
  a packet header carries a protocol version and size checks. A file transfer
  layer moves maps, the save and characters.
- Session admission checks the password, a ban list and the protocol version
  in the network layer, and the level range in the game (join event 5, below).

## Host and client authority

- The network state word 0x62AC6C: bit 0x1 network active (0x527280), 0x2 this
  machine is the host (0x5280E0), 0x4 a game is running (set from the module
  start 0x49CC70), 0x8 the client was admitted to a slot, 0x10 auto-join,
  0x20 no broadcast, 0x40 a network reset was requested. A **client** is
  "bit 0x1 without bit 0x2", the test many rule functions already carry in the
  ledger (for example skills SC12, combat CR1, skills FT3, magic PC4).
- The network test 0x4A2BA0 answers 1 in single player; in a network game it
  answers 1 only while a counter is above 0. The dispatcher raises that
  counter around the application of a received packet (0x4A2BC0, 0x4A2BD0).
  The usual caller pattern (for example the rumor quell 0x4C5690, LB11) is:
  when the test answers 1, apply locally; otherwise the host broadcasts the
  change and applies it; otherwise (a client) do nothing and wait for the host.
- About 350 functions outside the multiplayer range test these flags or send
  packets. Damage, for example, is applied only on the host (0x4B43A0); a
  client runs no NPC AI, which confirms CL3 of the
  [AI core loop](npc-ai-core-loop.md).
- The dispatcher 0x49D690 is one switch on the packet type, 1..130; types 14,
  15, 45, 47, 55 and 78 are empty, so 124 types are handled. The validator
  0x4A2020 admits types 1..130. A third callback handles network events 0..13
  (0x4A2070): 5 a join request, 8 a client left, 11 the game started.
- **Requests a client sends, applied on the host only:** player list (1), move
  to a tile (4), a spoken line or broadcast (18), drop, put, give and place an
  item (26–29), start a spell (57), and types 81 and 127 (role not read).
- **Replication the host sends, applied on clients only:** blessings (42),
  spell runs started, maintained and processed (60–62), object creation and
  item transfer with subtypes (70), the party table (71), item decay (72),
  summoning (73), payment (82), effects (84–86) and types 32, 38, 58, 75–77,
  90–92.
- **Applied wherever received** (inside the counter bracket): time (3),
  animation goals (5–17), damage, healing, fatigue, rest and death (20, 21,
  34, 35, 104), inventory (22–25), skills, the skill interface and stealth
  (30, 31, 33, 56), fate points (36), rumors (37), quests (39–41), curses
  (43), dialog with its own subtable (44), spell and stat purchases and tech
  degrees (48–53), reaction (87), item damage and locks (88, 89), reputation
  (105), identify and repair services (125, 126), object flags (129), global
  flags and variables (through the flag write 0x444D20, IS4), notices (67).
  Types 46, 59, 66, 80, 123 and 128 have separate host and client branches; a
  type 128 that reaches the host ends the process.
- **Object locks** (0x4A2EC0–0x4A3230): an object flag marks an item taken by
  one player so that two players cannot take the same item; pickup, drop,
  insert and remove call the lock (0x4A3230).
- **What the host replicates**, in sum: time, objects and their fields, stats,
  skills, spells and tech, effects, quests, rumors, global and per-player PC
  flags and variables, parties and animations. The world exists once, on the
  host; clients mirror it.

## Modules: why only Vormantown

- The module list (0x4027C0) walks `Modules\*.*`, takes `.dat` files and
  folders without a dot, skips duplicates, mounts each candidate, looks for a
  root `mp.txt` and unmounts it. List type 0 (the options window, 0x589308)
  keeps every module; list type 1 (the server list, 0x584B85) keeps only
  modules with `mp.txt`.
- In the shipped install only `Vormantown.dat` has `mp.txt` (a 4-byte
  placeholder); `Arcanum.dat`, its patch and its folder have none. So the main
  campaign is never offered for a network game, and `Vormantown` appears in
  the single-player module list too. Nothing else in the read code refuses the
  main module; its world map, story and world-map travel (refused in a network
  game, below) make it unfit, and forum reports of a community co-op attempt
  that broke mid-campaign agree (reported, not checked).
- `Vormantown` differs from `Arcanum` in data: no `worldmap` folder, three maps
  (the multiplayer map, its original copy and a shop map; the shop map has
  editor mobiles but no aggregate, [data census](data-census.md)), its own
  scripts and dialogs, a PC-flag message file besides the PC variables, and
  no AI, reputation, effect or anti-teleport rule files of its own. The shared
  multiplayer data sits in the base archives: the multiplayer and main-menu
  message and rule files and the matchmaker rules in `arcanum3.dat` together
  with 12 pre-made multiplayer characters (`.mpc` with two portraits each), and
  the multiplayer interface art in `arcanum1.dat`.
- **Starting a game** (0x49CC70 with its tail): the host checks the module's
  `.dat`, creates a new world instance in `Modules` named after the module and
  a fresh GUID (`CoCreateGuid`, the only ole32 import), loads the module, then
  the save from the temporary folder, and sets "game running". A joining
  client receives the current map's files and a transfer file holding the
  animation, magic-tech and trap module states (0x4A33F0; table 0x5B3FF0,
  post-handlers 0x4A54A0, 0x4A54E0), the `Players` folder (0x4A3660) and the
  other players' characters and portraits (0x4A3780). While a client loads,
  the game pauses (0x45B300).
- The multiplayer module of the game library (table entry 0x59A9B8) has its
  own init (0x49C670: players folder, player name, max players, the table, the
  multiplayer messages, the per-player PC flag and variable stores), reset
  (0x49C820), exit (0x49C780), ping (0x4A5490), save (0x49C930) and load
  (0x49CA20). Its save holds markers, the character ids of players who left
  (the stored PCs) and the slot table.

## Server options

The option word 0x61241C (get 0x5294D0, set 0x5294E0, clear 0x529500) is set
in the "Host Game" window (0x585270, 0x585630):

| Option | Effect in the read code |
| --- | --- |
| Game type (free for all, cooperative, role-play; 0..2) | Only advertised in the server list and used by its filter; no game rule reads it. |
| Max players 1..8 | Slot limit. |
| Min and max level (0x6123F4, 0x6123F8) | A join outside the range is refused (event 5). |
| Password (bit 0x8) | Checked in the network layer; there is no other "locked" game. |
| Player killing (bit 0x1) | Without it, damage from a PC to a PC is vetoed. |
| Friendly fire (bit 0x2) | Without it, damage between PCs and NPCs of one party or one leader is vetoed (0x45E2E0). |
| Private chat (bit 0x4) | Read by one interface function (0x571489); its exact effect is not read. |
| Auto-join (network bit 0x10) | The host admits a joining player itself (0x52A3BB); otherwise a client waits until the host admits it in the multiplayer control window (0x569DD0: admit, kick, ban). |
| Auto-equip (bit 0x20, inverted) | On when the bit is clear (0x4A5687): a joining PC gets starting items by background, skills and schemes from `Rules\AutoEquip.mes` and wields the best (0x4A5670, 0x4A5710–0x4A5EE0, 0x4654F0; background money scales the gold, BG7). |
| Key sharing (bits 0x40, 0x80) | Off, any party member, or a party member on screen (0x463370). |
| Stored PCs (default 10) | How many characters of players who left the server keeps (0x4A55F0, 0x4A5600). |

The damage veto 0x4A6190 is the only hook of its table (0x5B5794), called from
the damage step (0x4B5520 from 0x4B43B1); it acts only in a network game
outside packet application, so only on the host. Per-player settings (always
run, auto-attack, auto-switch, follower skills) are applied by 0x4A5610 (PI5).

**Death.** The read code has no respawn rule: a kill (0x45D900) is applied
and replicated. The manual says the game goes on, a dead player may be raised
by others, and a player who leaves needs a new or imported character.

## Player parties and experience

- The party module keeps one party number per slot (0x5FC32C, −1 none): get
  (0x4BA020), join B to A (0x4BA080: refuses a non-player and a player already
  in a party, allocates a number through 0x4BA260, notifies, broadcasts the
  table and calls 0x4C5400 and 0x4C5B10 for the members), leave (0x4BA1E0),
  host broadcast (0x4BA290, type 71) and client receive (0x4BA2E0).
- Parties are made by the spoken commands "join" and "disband" aimed at a
  player (the broadcast order handler 0x4C3770; the multiplayer phase that the
  [companion orders research](companion-orders-loyalty-evidence.md) left out).
  Keys (0x463370) and quests (0x4C4D20) are shared in a party; the manual adds
  that a quest one player finishes closes for the others and that NPC
  followers are not shared.
- **Experience** (0x45F110): only the host awards it. After the difficulty
  adjustment (+50 % or −25 %) a PC in a party splits the award evenly (integer,
  at least 1) among the living PCs of the same party inside a screen-sized
  rectangle around the receiver (0x440FC0 with the PC mask); a PC without a
  party keeps it all. The second reader saw a possible division by zero when
  the receiver is dead and no living member is near (around 0x45F1F2; medium
  confidence, an oracle run would settle it).

## Multiplayer characters and saves

- A multiplayer character is `Players\G_<object GUID>.mpc` with two portrait
  bitmaps. The character window and the multiplayer menu list, load and save
  them (0x4A3D00, 0x4A3D70, 0x546EE0, 0x545C70); the auto-level scheme is
  written into the file and kept current (0x4A45B0, 0x4A47D0, 0x4A49E0, called
  by the character windows and the auto-level run 0x4A7030).
- **Export from single player** is a hidden key X in the character window
  (0x55ADDF–0x55AE5A), allowed only outside a network game and not during
  another critter's turn. The game saves to a temporary slot (0x403030), asks
  "export?" and "overwrite?" (0x4A6470; prompts 0x57D080, 0x57D150), writes
  the file (0x4A39F0), reloads the temporary save (0x403410) and deletes it
  (0x403790). The write is destructive, which is why the save is restored: it
  drops the whole inventory (0x463730), stops animations and resets the PC
  (0x4A6010: hit-point and fatigue damage, combat state, quests, rumors,
  blessings and curses, reputations, followers, flags and variables, spell
  flags). The file holds the serialised PC object and its auto-level scheme:
  stats, skills, spells and tech, no items, followers or world state.
- **Import** (0x4A3F40) refuses a file whose PC carries an inventory or
  followers; the joining PC gets its items from auto-equip. 0x4A6010 also
  runs at join.
- **The world**: the host can save and load a multiplayer game from the menu
  (0x5425C0, 0x543990, 0x543E40); the multiplayer module adds its stored-PC
  ids and slot table to that save. A leaving player is offered to save their
  character (0x49D320, the multiplayer timer event, which also runs the
  periodic broadcasts).

## Clock, combat and travel in a network game

- **Clock** (0x45B370): the step's real time is clamped to 5..250 ms, and game
  and animation time run at 8 × real time. The host sends the time (type 3)
  at most about once a second (more than 950 ms apart). A client advances its
  own time only within a budget of about 2 s of real time since the last
  synchronisation (a constant offset that nothing writes) and, on the host's
  packet, resets the budget and takes the time only when it is later than its
  own (0x45C230): time never runs backwards on a client.
- **No turn-based combat**: turning it on is refused in a network game
  (0x4B6C90), joining resets the setting (0x545F60), and the options window
  disables both turn-based modes (0x589610, 0x5896A0). Multiplayer combat is
  real time only.
- **No sleep**: the rest window refuses in a network game (0x57B180); the
  multiplayer control button takes its place.
- **No world-map travel**: the world map opens a sector's town map when it has
  one (0x560990); otherwise in a network game the window closes at once
  (0x560F40). This matches the manual and the missing `worldmap` folder of
  `Vormantown`.
- The manual adds that Charm and Dominate Will do not work on players and
  that the alignment shift of a kill goes to the striker (reported, not read).

## Function roles of the multiplayer range

A scan of call targets and aligned starts after a return finds about 140
entry points in 0x49C670–0x4A6560, where the
[source coverage](../requirements/source-coverage.md) counts 102 functions;
some scanned starts are tails of a longer function (0x49CEE0 continues
0x49CC70), and the difference was not settled. The roles read:

| Addresses | Role |
| --- | --- |
| 0x49C670, 0x49C780, 0x49C820, 0x49C930, 0x49CA20, 0x49CB50, 0x49CB60, 0x4A5490 | Module init, exit, reset, save, load, module load (returns 1) and unload, ping |
| 0x49CB80, 0x4A2AE0, 0x4A2B10, 0x4A2B60, 0x4A2BE0, 0x4A2C60, 0x4A5510–0x4A55D0 | Slot table: clear, object ↔ slot, player walks, edits with broadcast |
| 0x49CBD0, 0x49CC20, 0x49CC50 | Network start (registers dispatcher, validator, event handler), stop, become host |
| 0x49CC70 (+0x49CEE0), 0x49D100, 0x49D1E0, 0x49D590 | Start a module instance, create and start a player's PC, open a multiplayer map by name |
| 0x49D320 | Multiplayer timer event: periodic broadcasts, players who left |
| 0x49D690, 0x4A2020, 0x4A2070, 0x4A27C0 | Dispatcher (types 1..130), validator, network events (join, leave, start), leave or kick |
| 0x4A1F30, 0x4A2D00, 0x4A2D90 | Animation goal application and sync |
| 0x4A1F60, 0x4A1FC0, 0x4A2040, 0x4A2A40, 0x4A2A90 | Slot → player object, player list broadcast (type 2), notice broadcast (type 67), "player lagging" and "recovered" |
| 0x4A2BA0, 0x4A2BC0, 0x4A2BD0 | Network test and the remote-change counter |
| 0x4A2EC0–0x4A3230 | Object locks |
| 0x4A33F0, 0x4A3660, 0x4A3780 | Transfer of map files and module states, the players folder, a player's character |
| 0x4A38B0–0x4A4C40, 0x4A6010, 0x4A6470 | Character files: save with prompt, write, list, load and check, the selected character, extra data, scheme updates, portraits, reset, export |
| 0x4A4D60, 0x4A4EB0–0x4A50C0 | Matchmaker configuration and WON thunks |
| 0x4A50D0–0x4A5460 | Per-player PC flag and variable stores with broadcast (link to the module's PC-flag file not read) |
| 0x4A55F0, 0x4A5600, 0x4A5610, 0x4A5670–0x4A5EE0 | Stored-PC limit, per-player settings, auto-equip |
| 0x4A6190 | Damage veto (player killing, friendly fire) |

## Anomalies

| Ledger row | Function | Original 1.0.7.4 | Proposal |
| --- | --- | --- | --- |
| NT9 | direct join (0x5283C0) | A typed address that is a host name and not a dotted IP is never resolved; the join packet is still sent without a connection and the call reports success. | An original bug; a Steamwoven network mode would resolve names or report the failure. No rule-mode switch (no rule depends on it). |
| NT12 | experience split (0x45F110) | Possible division by zero when the receiver is dead and no living party member is near (medium confidence). | Confirm with the oracle before any network work; Steamwoven never divides by zero. |
| — | the requirement census | 102 functions recorded for the range, about 140 entry points scanned. | Settle when a network task reads the range; no effect now. |

## Open questions

- The roles of packet types 81, 123 and 127 and of the subtypes of 70 and 44.
- The link between the per-player PC flag and variable stores (0x4A50D0–
  0x4A5460) and the module's PC-flag message file.
- What the private-chat bit changes in 0x571489 (interface only?).
- What 0x4C5400 and 0x4C5B10 do when a player joins a party: this reading saw
  a mutual reaction setup with the members, while
  [FR-SOCIAL-006](../requirements/story-social-ai.md#fr-social-006) (F0) names
  quest transfer at 0x4C51C0 and 0x4C5250 and rumor transfer at 0x4C5B10 and
  does not name 0x4C5400.
- Raising a dead player: no respawn code; raising probably goes through the
  ordinary spells (not read).
- Whether the shipped LAN path works on current Windows or a compatibility
  layer: a runtime question, not answered by a static reading.

## Requirement group proposal

No present group covers a network session: FR-SOCIAL-006 covers only the
player party, and the UI section names multiplayer as an early non-goal. The
proposal is a new section **"Сетевая игра" (network game)** with eight groups,
added to the registry only if the owner accepts multiplayer as a product goal.
On 2026-10-27 the owner had the groups added without setting a date for
multiplayer, which stays outside alpha; ARC-DEV-044 added them as the section
[Сетевая игра](../requirements/network.md) (191 groups in all). FR-SOCIAL-006
keeps its own ID for the exchange of story state (an ARC-DEV-044 choice, not
part of the proposal); FR-NET-007 holds the network party rules. By the same
decision, loading a second module in single player (FR-CONTENT-003) comes
after alpha:

| Proposed ID | Group | Original basis |
| --- | --- | --- |
| FR-NET-001 | Host and join a session: LAN discovery, bookmarks, a typed address, an internet lobby in place of WON; up to 8 players; password, ban, protocol version, level range | Sessions, transport, options |
| FR-NET-002 | Host authority and replication: clients send requests, the host applies the rules and AI and replicates the results; item locks | Authority |
| FR-NET-003 | Network clock and limits: host time with a bounded client budget, pause while a client loads; real-time combat only, no sleep, no world-map travel | Clock, combat and travel |
| FR-NET-004 | Multiplayer modules: a module marker, a fresh world instance per game, world transfer to a joining client, host save and load | Modules |
| FR-NET-005 | Multiplayer characters: export from single player, import with checks, the reset, auto-equip at join, stored PCs | Characters and saves |
| FR-NET-006 | Server options: player killing, friendly fire, private chat, key sharing, auto-join, auto-equip, advertised game type | Server options |
| FR-NET-007 | Player parties and experience split, shared keys and quests (proposed to absorb FR-SOCIAL-006; ARC-DEV-044 keeps FR-SOCIAL-006 for the story-state exchange) | Parties |
| FR-NET-008 | Lobby, chat and administration: player list, broadcast and private messages, admit, kick, ban | Sessions, options |

## Recommendation

**Not before a single-player release; at the earliest as its own lane after
alpha, once the single-player game is complete.** Reasons:

- Most rule domains have network branches in the original (about 350 functions
  outside the range test the network flags or send packets), so a network mode
  touches nearly the whole session at once (inference); it
  pays off only after those domains are finished in single player.
- The original network game is small in content: one shipped module with
  three maps and no world-map travel. The main campaign was never playable
  online in the original, so a network mode adds a product feature, not
  parity, and needs the owner's product decision first.
- Wire compatibility with the original is of no value (WON is gone, and an
  original client would also need the original's state layout); a Steamwoven
  network mode would keep the original's rules (authority, options, party
  split, character reset, limits) on its own protocol.
- **Cheap now (inference):** keep the session's single owner
  (`Arcanum.Session`) changing state only through typed, serialisable
  operations, so a later host can apply client requests through the same
  path. This needs no network code and costs nothing extra if it is the
  existing direction.
- **Cheap after alpha, separate from networking:** the original lists
  `Vormantown` in its single-player module list too, so loading a second
  module in single player (FR-CONTENT-003) is a small step that later
  multiplayer would reuse.
