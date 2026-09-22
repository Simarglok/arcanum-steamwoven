# Arcanum.Data

`Arcanum.Data` is an independently written, evidence-bounded reader for the observed R1 `1TAD` DAT container subset. It parses metadata incrementally and reads one selected payload into memory; it does not extract files, write archives, normalize names, or implement resource precedence.

## Toolchain and commands

The repository pins SDK `8.0.425` (`net8.0`) in `global.json` and central package versions in `Directory.Packages.props`. The SDK is a temporary compatibility floor for future Godot 4.5 C# work and reaches .NET support end on 2026-11-10; future integration must revisit it.

```sh
export DOTNET_ROOT="$PWD/local/dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --info
dotnet restore
dotnet restore --locked-mode
dotnet build --no-restore
dotnet test --no-build --filter "TestCategory!=RealData"
python3 -B tools/research/dat_probe.py --self-test
```

## API and ownership

- `DatArchive.Open(string, DatReaderOptions?)` owns its file stream.
- `DatArchive.Open(Stream, DatReaderOptions?, bool leaveOpen = false)` requires a readable, seekable stream and owns it unless `leaveOpen` is true. Ownership also applies when opening fails. The caller position is not restored and must not use the stream concurrently with the archive.
- `Footer` and ordered `Entries` preserve raw bytes. `RawNameBytes` includes the terminal NUL. No decoding, lookup, case folding, or normalization is performed, so duplicates remain duplicates.
- `ReadEntry` accepts only exact flag `0x001` (plain) and `0x002` (zlib); all other values, including directory metadata `0x400`, are enumerated but rejected before payload access.
- `Dispose` is idempotent. Metadata remains readable after disposal; `ReadEntry` throws `ObjectDisposedException`.

`DatFormatException` represents malformed structure or payload integrity errors; `DatUnsupportedFormatException` is the unsupported marker variant; `DatLimitExceededException` reports reader-policy caps; `DatUnsupportedEntryException` reports unreadable flags/directory metadata. Native `IOException` and argument/disposal exceptions remain unwrapped.

## Reader limits

Defaults are policy safeguards, not format facts: archive 4 GiB; index 64 MiB; entry count 1,000,000; each name 64 KiB; total names 64 MiB; stored entry 256 MiB; decoded entry 512 MiB. All must be positive and are checked before the corresponding allocation or payload read.

The supported structure and evidence boundary are in [formats/dat.md](formats/dat.md). Local real data are opt-in only and are never committed. The R1 evidence covers eight user-owned candidates but does not establish canonical bytes, other variants, flag semantics beyond the admitted exact values, filename semantics, or archive precedence.
