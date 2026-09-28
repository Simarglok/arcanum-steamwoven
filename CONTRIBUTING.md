# Contributing

The repository contains bounded C# readers/runtime components, diagnostic Godot viewers, research tools and synthetic labs. Use the pinned [toolchain and checks](docs/workflow/toolchain.md); the [component catalogue](docs/project/status.md) records their limits.

Implementation requires a bounded accepted task or explicit user instruction defining outcome, scope and relevant checks. Hermes-managed work uses the [Kanban workflow](docs/workflow/hermes.md). A roadmap is not task authorization.

For research changes, include source URL, version/commit, observed facts, uncertainty and a reproducible verification method. For code, include meaningful tests of format/behavior boundaries. Use synthetic fixtures in Git; real-data tests are optional and receive explicit local inputs. A synthetic pass does not establish original-game compatibility or physical controller behavior.

Run checks appropriate to the changed component. Documentation-only changes need source/configuration consistency and relative-link checks; do not describe them as runtime validation. Root solution checks do not cover every standalone viewer or lab.

Keep PRs scoped and independently reviewed where required. Preserve local archives and game data outside version control. Check staged filenames and diff before publishing. The human maintainer performs merges; publication requires task authorization.
