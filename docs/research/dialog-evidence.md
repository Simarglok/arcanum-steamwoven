# ARC-RES-005 dialogue evidence record

## Scope and immutable inputs

This record is identifier-only. It contains no original dialogue, extracted
payload, toolkit archive, binary, installation path, or executable output.
The probe is independently authored, read-only, requires an explicit archive
path and raw DLG identifier, and writes any full local report only beneath
ignored `research-private/ARC-RES-005/`.

| Claim ID | Claim | Category | Source ID | Immutable locator | Lineage / license | Method | Status / unknown |
| --- | --- | --- | --- | --- | --- | --- | --- |
| DLG-001 | `scr\01324Virgil.scr` is ordinal 8973 in `arcanum2.dat`; `dlg\01324Virgil.dlg` is ordinal 160 in `arcanum3.dat`. | local structural experiment | local archive inventory | archive SHA-256 `4078653c...e24d` / `cbadd76...499`; raw identifiers | user-owned data; no content reused | bounded index inspection | observed pair only; no runtime winner |
| DLG-002 | Numeric script lookup can derive a `dlg` path from an indexed `scr` path. | source fact | arcanum-ce | commit `a7ff41b300ef712f0e7d088183a3d08957110cdd`, `src/game/script_name.c:157-207` | Sustainable Use License 1.0; one source lineage | read-only source inspection | static path rule, not runtime invocation |
| DLG-003 | CE describes seven brace-delimited fields and numeric sort/search behavior. | source fact | arcanum-ce | same commit, `src/game/dialog.c:150-161,1291-1357,2641-2802,2968-2991` | Sustainable Use License 1.0; no code copied | read-only source inspection | source-derived grammar/roles/reference checks |
| DLG-004 | The probe rejects malformed grammar, policy overflows, unsafe selectors, and invalid references without payload reads where selection fails. | local structural experiment | `Arcanum.DialogProbe` | synthetic self-test | independently authored | synthetic fixtures with tracked DAT reads | requires current self-test result |
| DLG-005 | Runtime dialogue operations should use explicit IDs, read/query and effect/command ports, and fail-closed tagged operations. | proposal | `dialog-minimal-contract.md` | repository document | independently authored | design boundary | not source/runtime fact |
| DLG-006 | `01324Virgil` identifies Virgil's initial encounter, archive precedence, and actual start node. | unknown | none | none | none | not inferred from filename/order | unknown |

## Candidate and selection rationale

Inspection begins with `dlg\01324Virgil.dlg`: its paired numeric SCR/DLG names
and bounded archive metadata are known, while an initial-encounter link is not.
The real-data probe reports only archive fingerprint, identifier hash, ordinal,
entry sizes, payload hash, aggregate field lengths/counts, opcode classes, and
classified graph bounds. It must label any structural start as
`research-selected start`; it does not call it a runtime entry.

The deterministic selected branch is the smallest coherent structural slice:
a structurally NPC node, immediate player-response frontier, one ordinary
direct response with the smallest traceable service set, its target NPC node,
and immediate frontier. Traversal stops at a second decision frontier, exit,
special/generated transition, SCR callback, unsupported operation, unresolved
edge, or a cap of 256 nodes / 1,024 references. All stops are classified,
never omitted.

## Identifier-only graph schema

| node_id | role | predecessor_id | serialized_target_or_special | condition_class | action_class | required_service | edge_status | source_claim_ids | confidence | notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| numeric only | NPC / player | numeric or empty | numeric / `exit` / `special` | opcode class or `none` | opcode class or `none` | query / command / transition / unsupported | direct / exit / special / dangling / ambiguous / unresolved / frontier | DLG-001..006 | bounded | never store text |

No real graph rows are published until the local real-data run is completed and
reviewed. The schema preserves identifiers but deliberately excludes display
text and raw condition/action strings.

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
not operation semantics. Across all structural player records, 222 positive
direct candidates, 82 exits, two negative/special candidates, and zero
dangling, ambiguous, or unsupported references were observed. This does not
establish runtime reachability or precedence.

The research-selected start is numeric node `1`, selected solely as the
lowest numeric structurally NPC record. It is **not** a claimed runtime entry.
Its bounded immediate-frontier calculation included 12 identifiers, examined
10 references, and stopped at the second decision frontier. No text or
unreviewed node/edge list is published.

## Privacy, provenance, and readiness

Before and after the opt-in run, SHA-256 of the selected archive must match.
The probe's synthetic matrix covers valid seven-field NPC/player records,
comments, CRLF/LF, empty fields, malformed/missing/nested braces, NUL,
malformed/overflowed IDs and response values, field/payload/record limits,
duplicate/dangling/ambiguous references, unsupported operations, cycles and
traversal caps. It also proves rooted, drive-prefixed, forward-slash, backslash
traversal, duplicate, and oversized archive-selector cases do not read a
payload when rejected.

Reader readiness is **not yet established** until the focused build/self-test
and the opt-in local candidate run are recorded. Game-dialog-runtime readiness
is **Blocked**: initial invocation, live archive precedence, runtime entry,
operation semantics, generated/special transitions, SCR bridge, and query or
command services remain unknown. No CE code, toolkit code, original content,
or runtime claim is included here.
