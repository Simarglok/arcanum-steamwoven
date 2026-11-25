#!/usr/bin/env python3
"""Documentation link check (ARC-DEV-406).

Checks the tracked Markdown and the paths that code, tests and tables name, so a moved or deleted page is found by a
check and not by a reader:

    python3 -B tools/docs_check.py                # the repository (tracked and new files, from git)
    python3 -B tools/docs_check.py --self-test    # synthetic tree, no git

Findings (each is an error, the exit code is 1 when there is one):

* ``link``    a relative Markdown link or reference definition whose target file does not exist.
* ``anchor``  a link to ``file#anchor`` (or ``#anchor``) whose heading does not exist in the target.
* ``path``    a ``docs/...`` path named in a code, test, script or table file, or in backticks in a page, that does not
              exist (a path with a placeholder such as ``<area>`` or ``*`` is skipped).
* ``orphan``  a page under ``docs/`` that no other page links to and no code file names (a path in backticks does not
              count as a link).
* ``owner``   a row of the owner re-check list (``docs/project/owner-recheck-list.md``) with a bad step id, a missing
              column or an unknown status (the parser is ``tools/local_dev/owner_checks.py``, ARC-DEV-2186).
* ``conflict`` a line of a page that starts with a merge-conflict marker (``<<<<<<<`` or ``>>>>>>>`` plus a space or the
              line end), outside fenced code (ARC-DEV-3220).
* ``stale``   an entry of ``KNOWN_ORPHANS`` that no longer exists or is no longer an orphan.

Links inside fenced code blocks and inline code are examples, not links.
"""
from __future__ import annotations

import argparse
import importlib.util
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
from urllib.parse import unquote

# ``](target)``, with an optional ``<...>`` target and an optional title; the text before it may wrap over lines
INLINE_LINK = re.compile(r"\]\(\s*(?:<([^>\n]+)>|([^)\s]+))(?:\s+(?:\"[^\"]*\"|'[^']*'))?\s*\)")
REFERENCE_DEFINITION = re.compile(r"^ {0,3}\[[^\]\n]+\]:\s*(?:<([^>\n]+)>|(\S+))", re.MULTILINE)
HEADING = re.compile(r"^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$")
HTML_ID = re.compile(r"""<a\s[^>]*?(?:id|name)=["']([^"']+)["']""", re.IGNORECASE)
INLINE_CODE = re.compile(r"(`+)(?:(?!\1).)+?\1", re.DOTALL)
DOC_PATH = re.compile(r"(?<![A-Za-z0-9_./-])((?:\.\./)*docs/[A-Za-z0-9_./-]*[A-Za-z0-9_-]\.(?:md|csv|json|tsv))")
CODE_SUFFIXES = (".cs", ".py", ".sh", ".csproj", ".props", ".json", ".csv", ".yml", ".yaml", ".md")
SKIP_PATH_PREFIXES = ("docs/verification/receipts/",)
# pages that may have no inbound link (none now); a stale entry is a finding
KNOWN_ORPHANS: tuple[str, ...] = ()
# files whose docs/ paths are synthetic test data, not references
SYNTHETIC_PATH_FILES = ("tools/local_dev/test_smoke_zones.py", "tools/docs_check.py")


OWNER_LIST = "docs/project/owner-recheck-list.md"
CONFLICT_MARKER = re.compile(r"^(?:<{7}|>{7})(?: |$)")
OWNER_PARSER = Path(__file__).resolve().parent / "local_dev" / "owner_checks.py"


def owner_list_problems(page: str, text: str) -> list[str]:
    """The rows of the owner re-check list that the shared parser cannot read, as ``page:line: owner: message``."""
    spec = importlib.util.spec_from_file_location("owner_checks_for_docs_check", OWNER_PARSER)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module      # the parser's dataclasses look their module up here
    try:
        spec.loader.exec_module(module)
    finally:
        sys.modules.pop(spec.name, None)
    return [f"{page}:{problem.line}: owner: {problem.message}" for problem in module.parse(text).problems]


def repository_files(root: Path) -> list[str]:
    out = subprocess.run(["git", "-C", str(root), "ls-files", "--cached", "--others", "--exclude-standard"],
                         capture_output=True, text=True, check=True).stdout
    return sorted({line for line in out.splitlines() if line and (root / line).is_file()})


def walk_files(root: Path) -> list[str]:
    found = []
    for base, _dirs, names in os.walk(root):
        for name in names:
            found.append(os.path.relpath(os.path.join(base, name), root).replace(os.sep, "/"))
    return sorted(found)


def strip_fences(text: str) -> str:
    """Blank the fenced code blocks, keeping every line break, so a link written as an example is not a link."""
    out, fence = [], ""
    for line in text.split("\n"):
        stripped = line.lstrip()
        opener = re.match(r"(`{3,}|~{3,})", stripped)
        if fence:
            if opener and opener.group(1)[0] == fence[0] and len(opener.group(1)) >= len(fence) and stripped.strip() == opener.group(1):
                fence = ""
            out.append("")
        elif opener:
            fence = opener.group(1)
            out.append("")
        else:
            out.append(line)
    return "\n".join(out)


def strip_code(text: str) -> str:
    """Blank the fenced blocks and the inline code too, keeping the line numbers."""
    return INLINE_CODE.sub(lambda match: re.sub(r"[^\n]", " ", match.group(0)), strip_fences(text))


def slug(heading: str) -> str:
    """GitHub's anchor of a heading: lower case, no punctuation but ``-`` and ``_``, spaces to ``-``."""
    text = re.sub(r"`", "", heading)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = text.lower().strip()
    text = re.sub(r"[^\w\- ]", "", text, flags=re.UNICODE)
    return text.replace(" ", "-")


def anchors_of(text: str) -> set[str]:
    anchors: set[str] = set()
    seen: dict[str, int] = {}
    for line in strip_fences(text).split("\n"):
        match = HEADING.match(line)
        if match:
            base = slug(match.group(2))
            count = seen.get(base, 0)
            seen[base] = count + 1
            anchors.add(base if count == 0 else f"{base}-{count}")
        for html_id in HTML_ID.findall(line):
            anchors.add(html_id)
    return anchors


def link_targets(body: str) -> list[tuple[int, str]]:
    """Every link target of the page text (inline links, images and reference definitions) with its line number."""
    found = []
    for pattern in (INLINE_LINK, REFERENCE_DEFINITION):
        for match in pattern.finditer(body):
            target = match.group(1) or match.group(2)
            if pattern is REFERENCE_DEFINITION and not re.search(r"[./#]", target):
                continue  # prose such as "[name]: running unless ...", not a definition of a path
            found.append((body.count("\n", 0, match.start()) + 1, target))
    return found


def check(root: Path, files: list[str], known_orphans: tuple[str, ...] | None = None) -> list[str]:
    known = KNOWN_ORPHANS if known_orphans is None else known_orphans
    present = set(files)
    dirs: set[str] = set()
    for f in files:
        for parent in Path(f).parents:
            dirs.add(parent.as_posix())
    pages = [f for f in files if f.endswith(".md")]
    texts: dict[str, str] = {}
    for f in files:
        if f.endswith(CODE_SUFFIXES) or f == "arcanum":
            try:
                texts[f] = (root / f).read_text(encoding="utf-8", errors="replace")
            except OSError:
                pass
    anchor_cache: dict[str, set[str]] = {}
    problems: list[str] = []
    inbound: dict[str, int] = {}
    named_by_code: set[str] = set()

    def anchors(path: str) -> set[str]:
        if path not in anchor_cache:
            anchor_cache[path] = anchors_of(texts.get(path, ""))
        return anchor_cache[path]

    if OWNER_LIST in present and OWNER_LIST in texts:
        problems.extend(owner_list_problems(OWNER_LIST, texts[OWNER_LIST]))

    for page in pages:
        for number, line in enumerate(strip_fences(texts.get(page, "")).split("\n"), 1):
            if CONFLICT_MARKER.match(line):
                problems.append(f"{page}:{number}: conflict: merge-conflict marker left in the page")
        body = strip_code(texts.get(page, ""))
        base = Path(page).parent
        for number, target in link_targets(body):
            if re.match(r"^(?:[a-z][a-z0-9+.-]*:|//)", target):
                continue
            path, _, anchor = target.partition("#")
            path = unquote(path.split("?", 1)[0])
            if path == "":
                resolved = page
            else:
                resolved = os.path.normpath((base / path).as_posix()).replace(os.sep, "/")
                if resolved not in present and resolved not in dirs:
                    problems.append(f"{page}:{number}: link: {target} -> {resolved} does not exist")
                    continue
            if resolved != page:
                inbound[resolved] = inbound.get(resolved, 0) + 1
            if anchor and resolved.endswith(".md") and resolved in present:
                wanted = unquote(anchor)
                if wanted not in anchors(resolved) and wanted.lower() not in anchors(resolved):
                    problems.append(f"{page}:{number}: anchor: {target} -> no heading '{anchor}' in {resolved}")
        # a path named in backticks or prose (not a link) must exist too
        prose = INLINE_LINK.sub("", texts.get(page, ""))
        for number, line in enumerate(prose.split("\n"), 1):
            for named in DOC_PATH.findall(line):
                _check_named(problems, present, dirs, f"{page}:{number}", named)

    for f, text in texts.items():
        if f.endswith(".md") or f in SYNTHETIC_PATH_FILES:
            continue
        for number, line in enumerate(text.split("\n"), 1):
            for named in DOC_PATH.findall(line):
                _check_named(problems, present, dirs, f"{f}:{number}", named)
                named_by_code.add(re.sub(r"^(?:\.\./)+", "", named))

    for page in pages:
        if not page.startswith("docs/") or page == "docs/README.md":
            continue
        orphan = inbound.get(page, 0) == 0 and page not in named_by_code
        if orphan and page not in known:
            problems.append(f"{page}: orphan: no page links to it")
        if not orphan and page in known:
            problems.append(f"{page}: stale: it is in KNOWN_ORPHANS but is linked")
    for page in known:
        if page not in present:
            problems.append(f"{page}: stale: it is in KNOWN_ORPHANS but does not exist")
    return problems


def _check_named(problems: list[str], present: set[str], dirs: set[str], where: str, named: str) -> None:
    path = re.sub(r"^(?:\.\./)+", "", named)
    if path.startswith(SKIP_PATH_PREFIXES) or "*" in path:
        return
    if path not in present and path not in dirs:
        problems.append(f"{where}: path: {named} does not exist")


def _write(root: Path, tree: dict[str, str]) -> None:
    for name, text in tree.items():
        (root / name).parent.mkdir(parents=True, exist_ok=True)
        (root / name).write_text(text, encoding="utf-8")


def self_test() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        _write(root, {
            "docs/README.md": ("# Index\n\n[a](a.md) and [b section](b.md#second-part) and [same](#index)\n"
                               "and a [wrapped\nlink text](c.md#a-title-with-code-and-link-1) and ![img](img.png) and\n"
                               "[![badge](img.png)](d.md) and [ref][r] and [angle](<e f.md>) and [enc](e%20f.md)\n\n"
                               "[r]: a.md\n"),
            "docs/a.md": "# A\n\nSee `docs/b.md` and [back](README.md).\n\n```\n[not a link](missing.md)\n```\n"
                         "~~~\n```\n[still not](missing.md)\n```\n~~~\n`[inline](missing.md)` and `docs/*.md` and docs/<area>.md\n",
            "docs/b.md": "# B\n\n## Second part\n\n## Second part\n\n[dup](#second-part-1)\n",
            "docs/c.md": "# C\n\n## A `title` with [code](d.md) and link, 1\n",
            "docs/d.md": "# D\n",
            "docs/e f.md": "# E\n",
            "docs/img.png": "",
            "tools/x.py": "# docs/a.md is cited by code\n",
            "docs/named.md": "# Named only by code\n",
            "tools/y.py": "NAMED = '../docs/named.md'\n",
        })
        found = check(root, walk_files(root), ())
        if found:
            print("self-test: clean tree reported problems:")
            print("\n".join(found))
            return 1
        # every finding kind, and the edge cases, must be found
        _write(root, {
            "docs/README.md": ("# Index\n\n[a](a.md) [b](b.md#nope) [gone](gone.md) [dup](b.md#second-part-2)\n"
                               "[wrapped\ntext](gone2.md) [![i](img.png)](gone3.md)\n\n[r]: gone4.md\n"),
            "docs/a.md": "# A\n\nSee `docs/missing.md` and `docs/<area>.md`.\n",
            "docs/b.md": "# B\n",
            "docs/orphan.md": "# Orphan\n",
            "tools/x.py": "# docs/also-missing.md\n",
        })
        found = check(root, walk_files(root), ())
        text = "\n".join(found)
        wanted = ["link: gone.md", "anchor: b.md#nope", "anchor: b.md#second-part-2", "link: gone2.md", "link: gone3.md",
                  "link: gone4.md", "path: docs/missing.md", "docs/orphan.md: orphan", "path: docs/also-missing.md"]
        missing = [k for k in wanted if k not in text]
        if missing or "<area>" in text:
            print("self-test: expected findings missing or wrong:", missing)
            print(text)
            return 1
        text = "\n".join(check(root, walk_files(root), ("docs/orphan.md", "docs/gone-entry.md", "docs/b.md")))
        if "docs/orphan.md: orphan" in text or "docs/gone-entry.md: stale" not in text or "docs/b.md: stale" not in text:
            print("self-test: KNOWN_ORPHANS handling is wrong:")
            print(text)
            return 1
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        _write(root, {"docs/README.md": "# Index\n\n[m](m.md)\n",
                      "docs/m.md": "# M\n\n<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> origin/main\n\n```\n<<<<<<< HEAD\n```\n"})
        found = [line for line in check(root, walk_files(root), ()) if ": conflict:" in line]
        if [line.split(": conflict:")[0] for line in found] != ["docs/m.md:3", "docs/m.md:7"]:
            print("self-test: conflict markers wrong:", found)
            return 1
    good_list = ("## ARC-DEV-1 — x\n\n| # | Step | Expected | Status |\n| --- | --- | --- | --- |\n"
                 "| 1 | A. | B. | open |\n| 2 | C. | D. | same (note) |\n")
    if owner_list_problems(OWNER_LIST, good_list):
        print("self-test: a clean owner list reported problems")
        return 1
    bad_list = good_list + "| x | A. | B. | open |\n| 3 | A. |  | open |\n| 4 | A. | B. | done |\n"
    found = owner_list_problems(OWNER_LIST, bad_list)
    if [line.split(": owner:")[0] for line in found] != [f"{OWNER_LIST}:{n}" for n in (7, 8, 9)]:
        print("self-test: owner list findings are wrong:", found)
        return 1
    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        _write(root, {OWNER_LIST: bad_list, "docs/README.md": "# Index\n\n[l](project/owner-recheck-list.md)\n"})
        if sum(": owner:" in line for line in check(root, walk_files(root), ())) != 3:
            print("self-test: check() does not report the owner list")
            return 1
    if slug("A `title` with [code](x.md) and link, 1") != "a-title-with-code-and-link-1":
        print("self-test: slug")
        return 1
    print("docs_check self-test passed")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true", help="run on a synthetic tree")
    parser.add_argument("--root", default=None, help="repository root (default: the parent of tools/)")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[1]
    problems = check(root, repository_files(root))
    for problem in problems:
        print(problem)
    print(f"docs_check: {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
