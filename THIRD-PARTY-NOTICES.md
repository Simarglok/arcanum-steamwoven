# Third-party notices

## SharpZipLib 1.4.2

Arcanum.Data references SharpZipLib 1.4.2 solely for its low-level `Inflater` API, which exposes completion, remaining-input, dictionary, and output state needed by bounded zlib handling. SharpZipLib is MIT licensed. No third-party DAT parser code, archive, or game data is included.

## FFmpeg (external program, not shipped)

Arcanum Steamwoven plays the original's Bink movies by running a separate `ffmpeg` program that the player installs (for example with Homebrew). Nothing of FFmpeg is linked into, copied into or shipped with the project, its packages or its repository. FFmpeg's Bink demuxer and decoder are LGPL 2.1 or later (https://ffmpeg.org/legal.html); a build with GPL options is GPL as a whole, which does not matter for a program run as a separate process. The licence reading is an engineering reading, not legal advice ([Bink movies](docs/research/bink-movies.md), section 4).

## Godot Engine 4.7.2

Arcanum.Godot is built with Godot Engine 4.7.2 .NET. Godot is MIT licensed: https://github.com/godotengine/godot/blob/4.7.2-stable/LICENSE.txt. The Godot editor and matching export templates are local shared tools, not repository contents or runtime dependencies of Arcanum.Data. No Godot binaries, export templates, game assets, or third-party archives are committed.
