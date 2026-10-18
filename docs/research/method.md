# Research method and R1 boundary

Research starts from community knowledge, then verifies claims on the user's unchanged original data. Preserve available tools and source snapshots locally with URL, acquisition date, version/commit and SHA-256. The ignored toolkit archive has its own manifest; archived code is not automatically reusable under MIT.

Each technical claim records its source and lineage, conditions/version, evidence, confidence and unknowns. Suggested states: reported, corroborated, experimentally verified, disputed, unknown. Two projects sharing one decoder are not independent confirmation.

Code behaviour of the original game is checked with the [original executable check](original-executable-check.md): the retail binary is read at the function's address, so a slip of an earlier reading can be told apart from a bug of the original game. Single functions may also be run in an emulator as an oracle (owner decision 2026-10-17); no source reconstruction is used (owner decision 2026-10-18, [plan](original-oracle-research.md)).

## Historical R1 direction — not an executable task

The initial research sequence below is preserved as the R1 scope record. Bounded DAT/ART implementations and diagnostic viewers now exist; consult the [current component catalogue](../project/status.md). Future-tense statements below describe the initial plan, not current implementation status.

First establish the DAT container structure and validate selected local files. DAT parsing and runtime resource precedence are separate questions. A later bounded implementation can provide a C# reader and inspection CLI; ART decoding and a Godot viewer follow.

The initial DAT research task should specify allowed read-only probes, selected baseline files, offset/size/compression checks, independent comparison opportunities and deliverables. It should not silently authorize a production parser, writer, game patch or renderer.

Public fixtures must be synthetic. Local real-data evidence may include approved hashes and aggregate metadata; extracted game text, graphics and bytes remain local. Never upload original data to an external service as part of debugging.

Known research entry points (not endorsements or proof of format claims):

- [AMTUT / DBMaker](https://terra-arcanum.com/amtut/amtut/undat_tutorial.htm)
- [Arkanum and format research](https://github.com/vleon1/Arkanum)
- [ArcNET](https://github.com/Bia10/ArcNET) — its README names a source reconstruction as a research route; assess lineage.
- [High Resolution Patch](https://terra-arcanum.com/drog/highres.html)

Detailed discovery notes remain in the project's Obsidian vault. Carry necessary evidence into reviewed repository documents as work progresses; do not treat those notes as tasks or automatically accepted specifications.
