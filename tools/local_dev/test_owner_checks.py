"""ARC-DEV-2186: the owner check list parser (tools/local_dev/owner_checks.py), on synthetic lists."""
from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from tools.local_dev import owner_checks as oc

HEAD = "| # | Step | Expected (from the original executable) | Status |\n| --- | --- | --- | --- |\n"


def section(code: str = "ARC-DEV-100", rows: str = "| 1 | Do a. | See a. | open |\n", title: str = "thing (2026-01-01)") -> str:
    return f"## {code} — {title}\n\nIntro.\n\n{HEAD}{rows}\n"


class ParseTests(unittest.TestCase):
    def test_a_clean_list_reads_every_field(self) -> None:
        text = "# List\n\nRules.\n\n" + section(rows=(
            "| 1 | Do a. | See a. | open |\n"
            "| 2 | Do b with `code`. | See b; **bold**. | same (5 pieces, owner 2026-11-12) |\n"
            "| 3 | Do c. | See c. | differs ([bug](../x.md)) |\n"
            "| 4 | Do d. | See d. | cannot-test |\n")) + section("ARC-UI-1364", "| 1 | Row. | Exp. | open |\n")
        parsed = oc.parse(text)
        self.assertEqual([], parsed.problems)
        self.assertEqual(["ARC-DEV-100", "ARC-UI-1364"], [s.code for s in parsed.sections])
        self.assertEqual(["ARC-DEV-100#1", "ARC-DEV-100#2", "ARC-DEV-100#3", "ARC-DEV-100#4", "ARC-UI-1364#1"],
                         [s.id for s in parsed.steps])
        second = parsed.steps[1]
        self.assertEqual(("DEV", 100, 2, "same", "(5 pieces, owner 2026-11-12)"),
                         (second.type, second.task, second.number, second.status, second.note))
        self.assertEqual("Do b with `code`.", second.step)
        self.assertEqual("See b; **bold**.", second.expected)
        self.assertEqual(["open", "same", "differs", "cannot-test", "open"], [s.status for s in parsed.steps])
        self.assertEqual("([bug](../x.md))", parsed.steps[2].note)
        self.assertEqual(parsed.steps[0].line, text.split("\n").index("| 1 | Do a. | See a. | open |") + 1)
        self.assertEqual(parsed.steps[0].section_title, "ARC-DEV-100 — thing (2026-01-01)")
        self.assertIn("ARC-UI-1364#1", parsed.by_id())

    def test_header_variants_and_escaped_pipes(self) -> None:
        text = ("## ARC-RES-7 — x\n\n| # | Step | Expected | Status |\n| --- | --- | --- | --- |\n"
                "| 1 | Press `a\\|b`. | Seen. | open |\n\n"
                "## ARC-RES-8 — y\n\n| # | Step | Expected in the original; ours differs on purpose | Status |\n| --- | --- | --- | --- |\n"
                "| 1 | Z. | Z. | same |\n")
        parsed = oc.parse(text)
        self.assertEqual([], parsed.problems)
        self.assertEqual("Press `a|b`.", parsed.steps[0].step)

    def test_a_full_id_in_the_number_cell_must_match_the_section(self) -> None:
        good = oc.parse(section(rows="| ARC-DEV-100#1 | A. | B. | open |\n"))
        self.assertEqual([], good.problems)
        self.assertEqual("ARC-DEV-100#1", good.steps[0].id)
        bad = oc.parse(section(rows="| ARC-DEV-101#1 | A. | B. | open |\n"))
        self.assertEqual(1, len(bad.problems))

    def test_every_bad_row_is_reported_with_its_line(self) -> None:
        rows = ("| 1 | Fine. | Fine. | open |\n"        # +0
                "| x | Bad id. | E. | open |\n"        # +1
                "| 0 | Zero id. | E. | open |\n"       # +2
                "| 3 | Missing expected. |  | open |\n"  # +3
                "| 4 | | E. | open |\n"                # +4
                "| 5 | Bad status. | E. | done |\n"     # +5
                "| 6 | Status in prose. | E. | open soon |\n"  # +6
                "| 7 | Three cells. | open |\n"          # +7
                "| 8 | Five. | E. | open | extra |\n"   # +8
                "| 1 | Repeat. | E. | open |\n")        # +9
        text = section(rows=rows)
        first = text.split("\n").index("| 1 | Fine. | Fine. | open |") + 1
        parsed = oc.parse(text)
        by_line = {p.line: p.message for p in parsed.problems}
        self.assertEqual(sorted(first + k for k in range(1, 10)), sorted(by_line))
        self.assertIn("bad step id", by_line[first + 1])
        self.assertIn("bad step id", by_line[first + 2])
        self.assertIn("missing Expected", by_line[first + 3])
        self.assertIn("missing Step", by_line[first + 4])
        self.assertIn("unknown status: 'done'", by_line[first + 5])
        self.assertIn("unknown status", by_line[first + 6])
        self.assertIn("3 columns", by_line[first + 7])
        self.assertIn("5 columns", by_line[first + 8])
        self.assertIn(f"repeats the row at line {first}", by_line[first + 9])
        self.assertEqual(["ARC-DEV-100#1"], [s.id for s in parsed.steps])
        self.assertTrue(parsed.problems[0].format("x.md").startswith(f"x.md:{first + 1}: "))

    def test_a_table_without_header_or_outside_a_coded_section_is_a_problem(self) -> None:
        no_header = oc.parse("## ARC-DEV-100 — x\n\n| 1 | A. | B. | open |\n")
        self.assertEqual([3], [p.line for p in no_header.problems])
        wrong_header = oc.parse("## ARC-DEV-100 — x\n\n| # | Step | Status |\n| --- | --- | --- |\n| 1 | A. | open |\n")
        self.assertTrue(wrong_header.problems)
        uncoded = oc.parse("## Notes\n\n" + HEAD + "| 1 | A. | B. | open |\n")
        self.assertTrue(uncoded.problems)
        self.assertEqual([], uncoded.steps)
        # a heading that has no table is fine, coded or not
        self.assertEqual([], oc.parse("## Notes\n\nText.\n\n## ARC-DEV-1 — x\n\nNo steps yet.\n").problems)

    def test_code_fences_and_other_levels_are_not_read(self) -> None:
        text = "## ARC-DEV-100 — x\n\n```\n## ARC-DEV-999 — fake\n| 1 | A. | B. | nope |\n```\n\n### Sub\n\n" + HEAD + "| 1 | A. | B. | open |\n"
        parsed = oc.parse(text)
        self.assertEqual([], parsed.problems)
        self.assertEqual(["ARC-DEV-100#1"], [s.id for s in parsed.steps])

    def test_a_row_after_a_break_continues_the_table_and_a_known_repeat_takes_the_next_number(self) -> None:
        rows = "| 1 | A. | B. | open |\n| 2 | C. | D. | open |\n\n- A note.\n\n| 2 | E. | F. | open |\n"
        # a repeat that is not known stays a problem
        parsed = oc.parse(section(rows=rows))
        self.assertEqual(1, len(parsed.problems))
        known = oc.parse(section("ARC-UI-1364", "| 1 | A. | B. | open |\n| 4 | C. | D. | open |\n\n- A note.\n\n| 4 | E. | F. | open |\n"))
        self.assertEqual([], known.problems)
        self.assertEqual(["ARC-UI-1364#1", "ARC-UI-1364#4", "ARC-UI-1364#5"], [s.id for s in known.steps])
        # a continuation with a new number is fine
        cont = oc.parse(section(rows="| 1 | A. | B. | open |\n\n- A note.\n\n| 2 | E. | F. | open |\n"))
        self.assertEqual([], cont.problems)
        self.assertEqual(2, len(cont.steps))

    def test_the_same_number_in_two_sections_is_two_steps(self) -> None:
        parsed = oc.parse(section("ARC-DEV-1") + section("ARC-DEV-2"))
        self.assertEqual(["ARC-DEV-1#1", "ARC-DEV-2#1"], [s.id for s in parsed.steps])
        self.assertEqual(len(parsed.steps), len(parsed.by_id()))


    def test_a_section_code_used_twice_cannot_repeat_an_id(self) -> None:
        parsed = oc.parse(section("ARC-DEV-1", title="a") + section("ARC-DEV-1", "| 1 | X. | Y. | open |\n| 2 | Z. | W. | open |\n", title="b"))
        self.assertEqual(1, len(parsed.problems))
        self.assertIn("ARC-DEV-1#1 repeats the row at line", parsed.problems[0].message)
        self.assertEqual(["ARC-DEV-1#1", "ARC-DEV-1#2"], [s.id for s in parsed.steps])


class IdTests(unittest.TestCase):
    def test_step_ids(self) -> None:
        self.assertEqual(("ARC-UI-1364", "UI", 1364, 4), oc.parse_step_id("ARC-UI-1364#4"))
        self.assertEqual(("ARC-DEV-352", "DEV", 352, 12), oc.parse_step_id(" ARC-DEV-352#12 "))
        for bad in ("", "ARC-UI-1364", "ARC-UI-1364#", "ARC-UI-1364#0", "ARC-UI-1364#a", "arc-ui-1364#1", "ARC-UI#1",
                    "ARC-UI-1364#1#2", "ARC-1364#1", "ARC-UI-1364 #1"):
            self.assertIsNone(oc.parse_step_id(bad), bad)

    def test_split_row(self) -> None:
        self.assertEqual(["1", "a|b", "c", "open"], oc.split_row("| 1 | a\\|b | c | open |"))
        self.assertEqual(["1", "a", "b", "open"], oc.split_row("| 1 | a | b | open"))
        self.assertIsNone(oc.split_row("text | with | pipes"))


class SourceTests(unittest.TestCase):
    def test_json_has_steps_and_problems(self) -> None:
        parsed = oc.parse(section(rows="| 1 | A. | B. | open |\n| 2 | C. | D. | nope |\n"))
        data = json.loads(oc.to_json(parsed))
        self.assertEqual(["ARC-DEV-100#1"], [s["id"] for s in data["steps"]])
        self.assertEqual({"id", "code", "type", "task", "number", "step", "expected", "status", "note", "line",
                          "section_title", "section_line"}, set(data["steps"][0]))
        self.assertEqual(1, len(data["problems"]))
        self.assertIn("1 steps", oc.summary(parsed))

    def test_file_and_git_ref_readers_do_not_write(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp)
            target = repo / "docs" / "project" / "owner-recheck-list.md"
            target.parent.mkdir(parents=True)
            target.write_text(section("ARC-DEV-5"), encoding="utf-8")
            git = ["git", "-C", tmp, "-c", "user.name=t", "-c", "user.email=t@example.com"]
            subprocess.run(git + ["init", "-q", "-b", "main"], check=True)
            subprocess.run(git + ["add", "-A"], check=True)
            subprocess.run(git + ["commit", "-q", "-m", "x"], check=True)
            target.write_text("changed in the checkout\n", encoding="utf-8")   # the ref must not see this
            before = sorted(p.name for p in repo.rglob("*") if ".git" not in p.parts)
            from_ref = oc.parse_ref("main", repo=repo)
            self.assertEqual(["ARC-DEV-5#1"], [s.id for s in from_ref.steps])
            self.assertEqual(before, sorted(p.name for p in repo.rglob("*") if ".git" not in p.parts))
            self.assertEqual("changed in the checkout\n", target.read_text(encoding="utf-8"))
            self.assertEqual([], oc.parse_file(target).steps)
            with self.assertRaises(RuntimeError):
                oc.read_ref("no-such-ref", repo=repo)

    def test_main_exit_codes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            good = Path(tmp) / "good.md"
            good.write_text(section(), encoding="utf-8")
            bad = Path(tmp) / "bad.md"
            bad.write_text(section(rows="| 1 | A. | B. | maybe |\n"), encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as out, contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(0, oc.main(["--file", str(good)]))
                self.assertEqual(1, oc.main(["--file", str(bad)]))
                self.assertEqual(2, oc.main(["--file", str(Path(tmp) / "missing.md")]))
            self.assertIn("bad.md:7: unknown status", out.getvalue())


if __name__ == "__main__":
    unittest.main()
