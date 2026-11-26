# ARC-RES-005 dialogue evidence record

## Scope and immutable inputs

This record is identifier-only. It contains no original dialogue, extracted
payload, toolkit archive, binary, installation path, or executable output.
The probe is independently authored, read-only, requires an explicit archive
path and raw DLG identifier, and writes any full local report only beneath
ignored `research-private/ARC-RES-005/`.

| Claim ID | Claim | Category | Source ID | Immutable locator | Lineage / license | Method | Status / unknown |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DLG-001 | `scr\01324Virgil.scr` is ordinal 8973 in `arcanum2.dat`; `dlg\01324Virgil.dlg` is ordinal 160 in `arcanum3.dat`. | local structural experiment | local archive inventory | SHA-256 `4078653c740b2b62473a6d6b60efe4c822390fc1b0958194ac0bffa1d493e24d` / `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`; raw identifiers | user-owned data; no content reused | bounded index inspection | observed pair only; no runtime winner |
| DLG-002 | Numeric script lookup can derive a `dlg` path from an indexed `scr` path. | source fact | earlier source reading (provenance in git history) | script-name functions 0x4CEDC0, 0x4CEE60 | behaviour recorded in prose only | earlier source reading cited by executable address; checked in the executable in ledger row SN1 | static path rule, not runtime invocation |
| DLG-003 | The dialog loader reads seven brace-delimited fields and sorts and searches records by number. | source fact | earlier source reading (provenance in git history) | dialog functions 0x414E60, 0x414F50, 0x417860, 0x417C20, 0x418030 and their static tables | behaviour recorded in prose only | earlier source reading cited by executable address; the field reading is checked in the executable in ledger row DL1, the rest is level F0 (not checked in the executable) | source-derived grammar/roles/reference checks |
| DLG-004 | The probe rejects malformed grammar, policy overflows, unsafe selectors, and invalid references without payload reads where selection fails. | local structural experiment | `Arcanum.DialogProbe` | synthetic self-test | independently authored | synthetic fixtures with tracked DAT reads | requires current self-test result |
| DLG-005 | Runtime dialogue operations should use explicit IDs, read/query and effect/command ports, and fail-closed tagged operations. | design boundary | [transition runtime page](https://github.com/Simarglok/arcanum-steamwoven/blob/b85ea8319b017c999a58b5258f139c859026b6de/docs/scripting/dialog-transition-runtime.md) (removed with the runtime) | repository document | independently authored | design boundary, kept by the conversation runtime | not source/runtime fact |
| DLG-006 | `01324Virgil` identifies Virgil's initial encounter, archive precedence, and actual start node. | unknown | none | none | none | not inferred from filename/order | unknown |
| DLG-007 | AMTUT/DBMaker is a community DAT-tool reference. | community report, not DLG proof | https://terra-arcanum.com/amtut/amtut/undat_tutorial.htm | URL only; no immutable local snapshot acquired for this task | reuse license and DLG lineage unknown | not used for parser or semantic claims | provenance gap recorded; not corroboration |
| DLG-008 | Arkanum and ArcNET are possible community source routes. | provenance only | https://github.com/vleon1/Arkanum/tree/ad58733e9f3e7c95055ae34f8c6d997e3b2d8d29 / https://github.com/Bia10/ArcNET/tree/baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c | local archive bundle SHA-256 `67c18d146120c49f1a2249214efcda593513ff5767478c5b473b5011e94ea19b` / `398b0daa72fe69eb526bf4ad3a4fb6c33091bc6b233ab065c9cd1f698098dba6` from DAT evidence; neither inspected for DLG in this task | Arkanum license not established; ArcNET MIT | no code/content/semantic claim reused | no independent DLG corroboration |
| DLG-009 | `editdocs.zip` is an acquired community editor-documentation archive whose `Eventscripts.rtf` and `GeneratedDialog.rtf` were later inspected for the limited DLG-018 vocabulary/structure disposition. | community supporting evidence | Terra-Arcanum resources | archive SHA-256 `a237d7f6b50e771230ca8e32593a87b6c3097abadfff74cb5e50fba81fb1137b`; acquired `2026-09-21T14:09:07.958758Z`; members `Edit.rtf`, `Eventscripts.rtf`, `GeneratedDialog.rtf`, `ARCANUM REFERENCE SHEET.rtf` | license and lineage not reviewed | read-only member-name inventory followed by read-only textual content inspection; supports DLG-018 as detailed in the source matrix | no content/code reused; supporting evidence only, not independent corroboration |
| DLG-010 | WorldEd manual artifacts are present as community documentation and `WorldEdManual.pdf` was inspected for the limited DLG-018 row/edge/range disposition. | community supporting evidence | Terra-Arcanum resources | PDF SHA-256 `049faa6956bab507f99d6174ce14b2b4feb49a49b0dcf7b69bec3203a3df72cb` acquired `2026-09-21T14:09:07.952071Z`; ZIP SHA-256 `3e9495488b0d7b17ff0b230ebfd7afa1b50dc1af0f6e89b124ae126fd69aa342` acquired `2026-09-21T14:09:07.947924Z`, member `WorldEdManual.doc` | license and lineage not reviewed | read-only archive/member inspection followed by read-only PDF content inspection; supports DLG-018 as detailed in the source matrix | no executable run or reuse; supporting evidence only, not independent corroboration |
| DLG-011 | Grant Davies DialogEditor 1.03 was acquired as a community editor archive whose `ReadMe.txt` was inspected for the limited DLG-018 identity/entry boundary. | community supporting evidence | Grant Davies catalog | `DialogEditor103.7z` SHA-256 `d5e17f319c349b0598c8b773da4a9d89a28df61fe9d02952f7d3acf5c518cc17`; acquired `2026-09-21T17:03:00.114260Z`; members `ReadMe.txt`, `DialogEditor.exe`, `00001HighPriestGridez.dlg`, `01074Raven.dlg` | manifest license status `not reviewed`; source lineage not established | read-only archive/member inventory followed by read-only `ReadMe.txt` content inspection; supports DLG-018 as detailed in the source matrix | archive not executed; no code, examples, or content reused; not independent corroboration |
| DLG-012 | DiAreA / `dlgedit2` is an acquired DLG editor archive. | provenance only | Terra-Arcanum tools | ZIP SHA-256 `4d92d1882d2cb66a36f69a1a6844c44641664fbba3305ada3642890efed7f638`; acquired `2026-09-21T14:09:08.003042Z`; inspected member names `DIAREA.EXE`, `ENGLISH.LNG`, `RUSSIAN.LNG`, `WHATSNEW.TXT`, `HINTS.TXT` | license and lineage not reviewed | member listing only; not used | executable not run; no code/content reuse or independent corroboration |
| DLG-013 | Dialog debugger is an acquired community tool archive whose condition/action tables were inspected for the limited DLG-018 operation-vocabulary and participant-slot disposition. | community supporting evidence | Terra-Arcanum tools | ZIP SHA-256 `1a3d9d872c22518fc6bea07337d155745496419033067e19cbfd177bf95463e6`; acquired `2026-09-21T14:09:08.001532Z`; members include `SEMES/condition.mes` and `SEMES/action.mes` | license and lineage not reviewed | read-only member-name inventory followed by read-only table content inspection; supports DLG-018 as detailed in the source matrix | executable not run; no table/code/content reuse; does not establish runtime dispatch or independent corroboration |
| DLG-014 | DLGCheck is an acquired community checker archive whose `src/Unit1.pas` was inspected for the limited DLG-018 grammar/range/positive-target disposition. | community supporting evidence | Arcanum Club tools catalogue | ZIP SHA-256 `f791eb7dd0643bbd71296c6bd4d2e6aa7dbd54bbbed949b83a639a022df5c170`; acquired `2026-09-21T14:11:28.994659Z`; members `ReadMe.txt` and `src/Unit1.pas` | license and lineage not reviewed | read-only archive/member inventory followed by source-text inspection; supports DLG-018 as detailed in the source matrix | no code or implementation reuse; not runtime-effect proof or independent corroboration |
| DLG-015 | ScriptEd is conditionally relevant only if a later bounded slice reaches an SCR bridge. | provenance disposition | `https://github.com/TimoP80/ScriptEd_v1_50` | local immutable revision `da7e6d2d5fa3566a99b297917c263d6828ace202`, archive acquired `2026-09-21T14:12:18.569733Z`, bundle SHA-256 `f39cb1ba050d8c415241c692f24da73e1d871fa427e72aa077d5f92cdb2a5494` | local documentation identifies GPL-2.0; lineage/independence not established for this task | not needed because the selected start stops before SCR; no source semantics used | no reuse; not independent corroboration |

## Candidate and selection rationale

Later [Crash Site NPC-dialog evidence](crash-site-npc-dialog-evidence.md)
establishes one base-world Virgil instance/event chain and conditional SAP_DIALOG
entry through the original fields. This does not retroactively prove original
execution, automatic first speech or universal precedence for DLG-006.

Inspection begins with `dlg\01324Virgil.dlg`: its paired numeric SCR/DLG names
and bounded archive metadata are known, while an initial-encounter link is not.
The real-data probe reports only archive fingerprint, identifier hash, ordinal,
entry sizes, payload hash, aggregate field lengths/counts, opcode classes, and
classified graph bounds. It must label any structural start as
`research-selected start`; it does not call it a runtime entry.

The deterministic selected slice begins with the lowest numeric structurally
NPC record. It can include immediate responses and one direct target only when
every encountered operation is supported; candidates sort by positive target
ID then response ID and require only the source-backed `transition` service.
Traversal stops before materializing beyond 256 nodes / 1,024 references at a
second decision frontier or at the **first** exit, special/generated transition,
callback, uninterpreted operation, dangling/ambiguous/unresolved edge, cycle,
or either cap. It retains the stopped row's classification but does not inspect
or select a later direct candidate. All included or stopped rows are published
below without text or raw operation strings.

## Identifier-only graph schema

| node_id | role | predecessor_id | serialized_target_or_special | condition_class | action_class | required_service | edge_status | source_claim_ids | confidence | notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| numeric only | NPC / player | numeric or empty | numeric / `exit` / `special` | opcode class or `none` | opcode class or `none` | query / command / transition / unsupported | direct / exit / special / dangling / ambiguous / unresolved / frontier | DLG-001..006 | bounded | never store text |

The schema preserves identifiers but deliberately excludes display text and raw
condition/action strings.

## Bounded local structural result

The opt-in read-only run selected ordinal 160 and completed with identical
before/after archive SHA-256 `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`.
The decoded DLG payload SHA-256 was
`34a49a2c160cf8a97856cc0a9e48baa04fe0611c5d6bf05b5f52a7310f45e53a`.
It reported 409 records (103 structural NPC, 306 structural player), 1,059
empty fields, 410 CRLFs, and no LF-only newlines. No original field content
was retained in this record.

The aggregate condition opcode classes were `AR:2`, `IA:24`, `PA:2`, `RA:38`,
`RE:12`, `SS:62`, and `WA:12`; action classes were `AL:2`, `CE:2`, `FL:7`,
`GF:4`, `IN:1`, `JO:21`, `LF:5`, `LV:2`, `MM:29`, `QU:1`, `RE:11`, `RU:4`,
`SC:2`, `SO:2`, `UW:4`, and `WA:2`. These are local structural aggregates,
not operation semantics. Under the corrected fail-closed policy, the run
classified 0 direct, 49 exit, 2 special, 0 dangling, 0 ambiguous, 255
unresolved, and 2 callback-shaped player records; this does not establish
runtime reachability or precedence.

The research-selected start is numeric node `1`, selected solely as the lowest
numeric structurally NPC record. It is **not** a claimed runtime entry. On the
corrected run it is one included row and stops before a response frontier:
its nonempty condition/action classes have no source-backed meaning or
admitted service in this bounded research. This replaces the earlier overclaim
of a 12-identifier/10-reference branch.

| node_id | role | predecessor_id | serialized_target_or_special | condition_class | action_class | required_service | edge_status | source_claim_ids | confidence | notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | NPC | empty | unknown (not examined after stop) | unsupported | unsupported | unsupported | included-start / stopped-unsupported-operation | DLG-001, DLG-003, DLG-005 | local structural fact for ID and stop; semantics unknown | selected only by lowest numeric structural NPC rule; no text or raw operation is retained; needs a source-backed operation mapping plus query/command service before a branch can continue |

## ARC-RES-005 historical privacy, provenance, and readiness snapshot

Before and after the opt-in run, SHA-256 of the selected archive must match.
The probe's synthetic matrix executes the CLI's same
selector/archive-selection/entry policy path through `InspectArchive`. It
asserts exact `InvalidDataException` reason prefixes for malformed structure,
numeric overflow, field and record limits, duplicate DLG IDs, duplicate archive
selection, and oversize selection. It separately asserts distinct graph
outcomes for dangling, wrong-role unresolved, callback, unsupported, cycle, and
traversal-cap fixtures. The callback-before-direct fixture proves that the first
callback stops selection and leaves a later direct response/target unselected.
Every post-selection fixture asserts a positive payload read; pre-read selector,
duplicate archive, and oversized-entry rejections assert zero payload reads.
The matrix also covers valid seven-field NPC/player records, comments, CRLF/LF,
empty fields, malformed/missing/nested braces, NUL, opaque non-ASCII text bytes,
required `.dlg` selector rejection, full signed `Int32` minimum/maximum values,
and stable just-outside-range rejections through `InspectArchive`. Its positive
archive-path traversal fixture proves smallest target ID then response ID
selection, identifies the `direct-included` response and target, reaches the
second-decision frontier, and performs a positive payload read. This is a
synthetic structural result, not runtime ordering evidence. Duplicate DLG IDs are malformed rather
than an admissible ambiguous graph; the graph retains an explicit ambiguous
target outcome as a defensive classification. It does not claim a realizable
>1,024-reference frontier because the stricter 256-node cap stops such a
unique-record frontier first.

**Historical ARC-RES-005 DLG-reader readiness: Blocked.** Physical grammar and bounded rejection
checks pass, but operation meanings/services, text encoding/localization, and
independent DLG-source corroboration are gaps. **Historical ARC-RES-005 game-dialog-runtime readiness:
Blocked.** Initial invocation, live archive precedence, runtime entry,
operation semantics, generated/special transitions, SCR bridge, and query or
command services remain unknown. No toolkit code, original content, or runtime
claim is included here.

## ARC-RES-006 selected-slice evidence

This section supplements the ARC-RES-005 physical-reader record above. It
records a separate, identifier-only semantic research boundary. It does not
replace the older `01324Virgil` observation with a runtime identity or entry
claim.

### Immutable candidate ledger and local observation

| Claim ID | Claim | Category | Method / disposition | Status and boundary |
| --- | --- | --- | --- | --- |
| DLG-016 | The selected local input is archive basename `arcanum3.dat`, SHA-256 `cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499`; member `dlg\01478Erland.dlg` is ordinal 220 with compressed flag `0x002`, stored/decoded sizes 1290/4346, and payload SHA-256 `0bdfb29d04cdc5702d8d6f0e0208077c3cec8e708d923162a1bb33dfc1d022cc`. | local structural experiment | explicit read-only member selection; archive fingerprint checked immediately before and after the opt-in probe | immutable locator only; not identity, invocation, winner, or precedence proof |
| DLG-017 | The sample has 30 records, 31 CRLF, no LF-only newlines, 3523 opaque text-field bytes, and zero high-bit bytes in those text fields. | local structural experiment | byte-oriented aggregate inspection | ASCII-only observation for this one sample, not a codepage/localization claim |
| DLG-018 | Structural NPC `N30` has selected QU action; immediate player `P31` has selected LF condition and LF action; its positive direct target is structural NPC `N50`, which has selected RU action. | local structural experiment plus source-backed grammar | explicit semantic-slice validation emits node IDs, opcode/arity classes, and `Direct(target NPC)` only | no display text, expression, or parameter value retained; structural slice is not runtime entry |
| DLG-019 | The selected read-only probe left the archive fingerprint unchanged. | local structural experiment | before/after SHA-256 equality in the same opt-in command | no input copy, write, or extracted asset |

### Claim-by-claim source and lineage matrix

| Claim IDs / support | Source inspected substantively | Evidence role | Lineage and limitation |
| --- | --- | --- | --- |
| DLG-003, DLG-018 | earlier source reading of the dialog functions at executable addresses 0x417860, 0x417C20, 0x417720, 0x414E60, 0x414F50, 0x4150D0 (LF condition), 0x415BA0 (QU, LF and RU actions), 0x4167C0, 0x416840, 0x413130, 0x417590 and 0x418030 | seven fields; response enumeration and phase order; LF/QU/RU dispatch; direct/exit/special and role checks | level F0 (not checked in the executable) except the field reading, checked in ledger row DL1; provenance in git history; behaviour in prose only, not treated as independent corroboration |
| DLG-018 | `editdocs.zip` / `Eventscripts.rtf` and `GeneratedDialog.rtf` | reports seven fields, ordered dialogue results, LF/QU/RU vocabulary; distinguishes generated-dialog forms excluded from this slice | community/editor source, archive SHA-256 retained in DLG-009; lineage/license unreviewed |
| DLG-018 | `WorldEdManual.pdf` | NPC/player row relationship, direct/zero/negative form, local-flag index range, dialogue-only ordered results, and quest-state labels/range | community manual, DLG-010 provenance; not independent proof |
| DLG-018 | DialogEditor 1.03 `ReadMe.txt` | supports separating numeric sequence from runtime identity/entry | community editor readme, DLG-011 provenance; no executable run |
| DLG-018 | `dlgdebugbeta.zip` `SEMES/condition.mes` and `SEMES/action.mes` | supports operation vocabulary, participant placeholders, and numeric-slot categories | community debugger tables, DLG-013 provenance; does not establish runtime dispatch |
| DLG-018 | `DLGCheck.zip` `src/Unit1.pas` | supports brace/numeric/role parsing, positive-target checks, and documented LF/QU ranges | community checker, DLG-014 provenance; not reused and not runtime-effect proof |
| DLG-002 | script-name functions 0x4CEDC0, 0x4CEE60 (ledger row SN1) | only numeric script-to-name derivation | explicitly not member identity, invocation, or entry proof |

The archived community/editor materials were inspected read-only for this
bounded research. Their licenses and lineage remain unreviewed where the prior
record says so; they are supporting evidence, not a license for source reuse.
Generated dialogue, SCR callbacks, and every nonselected operation retain an
unsupported/out-of-scope disposition.

### Selected grammar, proposal, and synthetic evidence

The selected parser accepts only ordered integer `LF(int,int)`,
`QU(int,int)`, and `RU(int)` forms at the selected structural positions. Its
admission domains are LF index `0..31`, LF expected/new value `0..1`, QU state
`0..6`, and RU ID `>=1000`. A positive player response must resolve to the
specified NPC target. Malformed, missing, out-of-domain, unknown, missing-target
or wrong-role data reject fail-closed.

The synthetic self-test executes an invented N100/P200/N300 walk-through:
NPC QU effects occur before player visibility; gender/IQ eligibility precedes
the LF local-state query; a true LF permits selection; selected LF writes before
direct N300 entry; N300 RU then runs. It covers false LF (no LF write or
transition), command failure, ordered multiple QU actions, participant routing,
and direct/exit/special classification. These are project-proposal and
synthetic-fixture results, never asserted original-runtime execution.

### Current readiness and remaining uncertainty

Physical parsing is ready for the bounded byte-preserving reader; selected
LF/QU/RU/direct semantics are synthetically verified proposal plus structural
validation of the hashed slice; actual in-game conversation remains not ready.
Unknown or excluded: runtime entry/invocation, member identity, archive
precedence, nonselected operations, generated dialogue, SCR bridge, game
services/UI/gameplay, and text encoding beyond the one ASCII-only observation.
