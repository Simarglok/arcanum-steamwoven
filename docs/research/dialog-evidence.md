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
| DLG-002 | Numeric script lookup can derive a `dlg` path from an indexed `scr` path. | source fact | arcanum-ce | commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`, `src/game/script_name.c:157-207` | Sustainable Use License 1.0; one source lineage | read-only source inspection | static path rule, not runtime invocation |
| DLG-003 | CE describes seven brace-delimited fields and numeric sort/search behavior. | source fact | arcanum-ce | same commit, `src/game/dialog.c:150-161,1291-1357,2641-2802,2968-2991` | Sustainable Use License 1.0; no code copied | read-only source inspection | source-derived grammar/roles/reference checks |
| DLG-004 | The probe rejects malformed grammar, policy overflows, unsafe selectors, and invalid references without payload reads where selection fails. | local structural experiment | `Arcanum.DialogProbe` | synthetic self-test | independently authored | synthetic fixtures with tracked DAT reads | requires current self-test result |
| DLG-005 | Runtime dialogue operations should use explicit IDs, read/query and effect/command ports, and fail-closed tagged operations. | proposal | `dialog-minimal-contract.md` | repository document | independently authored | design boundary | not source/runtime fact |
| DLG-006 | `01324Virgil` identifies Virgil's initial encounter, archive precedence, and actual start node. | unknown | none | none | none | not inferred from filename/order | unknown |
| DLG-007 | AMTUT/DBMaker is a community DAT-tool reference. | community report, not DLG proof | https://terra-arcanum.com/amtut/amtut/undat_tutorial.htm | URL only; no immutable local snapshot acquired for this task | reuse license and DLG lineage unknown | not used for parser or semantic claims | provenance gap recorded; not corroboration |
| DLG-008 | Arkanum and ArcNET are possible community source routes. | provenance only | https://github.com/vleon1/Arkanum/tree/ad58733e9f3e7c95055ae34f8c6d997e3b2d8d29 / https://github.com/Bia10/ArcNET/tree/baa7cb5a7b8d1498344cdb5e3170bce8f191fd3c | local archive bundle SHA-256 `67c18d146120c49f1a2249214efcda593513ff5767478c5b473b5011e94ea19b` / `398b0daa72fe69eb526bf4ad3a4fb6c33091bc6b233ab065c9cd1f698098dba6` from DAT evidence; neither inspected for DLG in this task | Arkanum license not established; ArcNET MIT but README identifies CE as a primary route | no code/content/semantic claim reused | no independent DLG corroboration; ArcNET is CE-derived/dependent |

## Candidate and selection rationale

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
second decision frontier, exit, special/generated transition, callback,
uninterpreted operation, unresolved edge, or either cap. All included or
stopped rows are published below without text or raw operation strings.

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

## Privacy, provenance, and readiness

Before and after the opt-in run, SHA-256 of the selected archive must match.
The probe's synthetic matrix executes the same selector/archive-selection/entry
policy path used by the CLI for duplicate archive identifiers, oversized entry,
malformed DLG, and response overflow. It separately exercises valid
seven-field NPC/player records, comments, CRLF/LF, empty fields,
malformed/missing/nested braces, NUL, malformed/overflowed IDs, field/payload/
record limits, duplicate DLG IDs, dangling references, callback and unsupported
operations, cycle-shaped references, opaque non-ASCII text bytes, required
`.dlg` selector shape, rooted/drive/forward-slash/backslash/non-ASCII selector
rejection, and an incremental 256-node traversal cap. Pre-read selector,
duplicate archive, and oversized-entry rejections assert zero payload reads;
malformed/overflow response fixtures assert a payload read. Duplicate DLG IDs
are malformed rather than an admissible ambiguous graph; the graph retains an
explicit ambiguous-target outcome as a defensive classification. The matrix
does not claim a realizable >1,024-reference frontier because the stricter
256-node cap stops such a unique-record frontier first.

**DLG-reader readiness: Blocked.** Physical grammar and bounded rejection
checks pass, but operation meanings/services, text encoding/localization, and
independent DLG-source corroboration are gaps. **Game-dialog-runtime readiness:
Blocked.** Initial invocation, live archive precedence, runtime entry,
operation semantics, generated/special transitions, SCR bridge, and query or
command services remain unknown. No CE code, toolkit code, original content,
or runtime claim is included here.
