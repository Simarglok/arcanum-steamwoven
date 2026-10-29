"""ARC-DEV-047/ARC-DEV-048: the package command's steps, checks and install, with a fake runner on a synthetic git repository."""
from __future__ import annotations

import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest import mock
import zipfile

from tools.local_dev import package


class PackageTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        environment = dict(os.environ, GIT_AUTHOR_NAME="t", GIT_AUTHOR_EMAIL="t@t", GIT_COMMITTER_NAME="t", GIT_COMMITTER_EMAIL="t@t",
                           GIT_COMMITTER_DATE="2026-10-28T12:00:00Z", GIT_AUTHOR_DATE="2026-10-28T12:00:00Z")

        def git(*arguments: str) -> None:
            subprocess.run(["git", *arguments], cwd=self.repo, check=True, capture_output=True, env=environment)
        git("init", "-q", "-b", "main")
        (self.repo / package.INSTRUCTION).parent.mkdir(parents=True)
        (self.repo / package.INSTRUCTION).write_text("instruction\n")
        (self.repo / "untracked-later.txt").write_text("x")
        git("add", str(package.INSTRUCTION)); git("commit", "-qm", "c")
        self.commit = subprocess.check_output(["git", "-C", str(self.repo), "rev-parse", "HEAD"], text=True).strip()
        self.base = {"DOTNET_ROOT": "/sdk", "PATH": "/opt/homebrew/bin:/usr/bin", "HOME": str(self.root), "DAT_ROOT": "/d",
                     "ARC_AREA_MAP_SMOKE_ONLY": "walk", "ARCANUM_HD_PACK": "x", "ARCANUM_QUEUE_TICKET": "7",
                     "LAUNCHER_SETTINGS_TEST_PATH": "/p", "WORLD_DIAGNOSTIC_ZOOM_TEST_PATH": "/z"}
        self.calls: list[tuple[list[str], dict[str, str]]] = []
        self.export_text = "dotnet /sdk/dotnet publish ... SDK 8.0.425\n"
        self.smoke_text = ""
        self.signature = "Signature=adhoc\n"
        self.windows = {"machine": 0x8664, "subsystem": 2, "name": package.PLAYER_NAME, "description": "Arcanum Steamwoven alpha",
                        "pack": b"GDPC", "feature": package.FEATURE, "assemblies": package.WINDOWS_ASSEMBLIES, "data": 1}
        quiet = mock.patch.object(package, "say"); quiet.start(); self.addCleanup(quiet.stop)

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def runner(self, command, cwd, environment, log, minutes) -> int:
        command = list(command)
        self.calls.append((command, environment))
        if command[1:2] == ["restore"]:
            self.source_files = sorted(path.name for path in cwd.rglob("*") if path.is_file())
        text = ""
        if "--export-release" in command and command[-2] == "Windows package":
            self.write_windows_export(Path(command[-1]))
            text = self.export_text
        elif "--export-release" in command:
            product = Path(command[-1])
            executable = product / package.PLATFORMS["mac"].executable
            executable.parent.mkdir(parents=True)
            executable.write_text("#!/bin/sh\n"); executable.chmod(0o755)
            text = self.export_text
        elif command[:2] == ["/usr/bin/codesign", "-d"]:
            text = self.signature
        elif "--native-smoke" in command:
            text = self.smoke_text
        log.write_text(text)
        return 0

    def write_windows_export(self, executable: Path) -> None:
        """What Godot's Windows export writes beside the given .exe: a PE header, the .pck and the .NET data folder."""
        header = bytearray(0x40 + 24 + 70)
        header[:2] = b"MZ"
        struct.pack_into("<I", header, 0x3C, 0x40)
        header[0x40:0x44] = b"PE\0\0"
        struct.pack_into("<H", header, 0x44, self.windows["machine"])
        struct.pack_into("<H", header, 0x40 + 24, 0x20B)
        struct.pack_into("<H", header, 0x40 + 24 + 68, self.windows["subsystem"])
        strings = "VS_VERSION_INFO\0FileDescription\0" + self.windows["description"] + "\0ProductName\0\0" + self.windows["name"] + "\0"
        executable.write_bytes(bytes(header) + strings.encode("utf-16-le"))
        executable.with_suffix(".pck").write_bytes(self.windows["pack"] + self.windows["feature"].encode())
        for number in range(self.windows["data"]):
            data = executable.parent / f"data_Arcanum.TerrainViewer{number or ''}_windows_x86_64"
            data.mkdir()
            for name in self.windows["assemblies"]:
                (data / name).write_bytes(b"x")

    def run_package(self, smoke_folder: Path | None = None, platform: str = "mac", **options) -> Path:
        with mock.patch.object(package, "game_folder", return_value=smoke_folder):
            return package.package(package.PLATFORMS[platform], root=self.repo, output=self.root / "out", ref="HEAD", fetch=False,
                                   smoke=True, keep_work=False, runner=self.runner, base=self.base, **options)

    def test_steps_build_from_the_archive_with_the_pinned_toolchain_and_stamp(self) -> None:
        installed = self.run_package()
        names = [Path(command[0]).name for command, _ in self.calls]
        self.assertEqual(names, ["dotnet", "dotnet", "Godot", "Godot", "codesign", "codesign"])
        export = self.calls[3][0]
        self.assertIn("macOS package", export)
        self.assertEqual(self.source_files, ["alpha-build.md"])  # the commit's tracked files only
        environment = self.calls[0][1]
        self.assertEqual(environment["ArcanumPackageCommit"], self.commit)
        self.assertEqual(environment["ArcanumPackageDate"], "2026-10-28")
        self.assertEqual(environment["PATH"].split(os.pathsep)[0], "/sdk")
        self.assertNotIn("/opt/homebrew/bin", environment["PATH"])
        self.assertEqual(environment["__GODOT_SHELL_ENV_SET"], "1")
        self.assertEqual(environment["ARCANUM_QUEUE_TICKET"], "7")
        for dropped in ("DAT_ROOT", "ARC_AREA_MAP_SMOKE_ONLY", "ARCANUM_HD_PACK", "LAUNCHER_SETTINGS_TEST_PATH", "WORLD_DIAGNOSTIC_ZOOM_TEST_PATH"):
            self.assertNotIn(dropped, environment)
        self.assertEqual(installed, self.root / "out" / "macos" / "Arcanum Steamwoven.app")
        self.assertTrue((installed / package.PLATFORMS["mac"].executable).is_file())
        build = (self.root / "out" / "macos" / "BUILD.txt").read_text()
        self.assertIn(f"commit {self.commit}", build)
        self.assertIn("smoke SKIPPED", build)
        self.assertIn(f"map-smoke {package.MAP_SMOKE_NOT_RUN}\n", build)
        self.assertEqual((self.root / "out" / "macos" / "README.md").read_text(), "instruction\n")
        self.assertFalse(Path(export[export.index("--path") + 1]).parents[3].exists())  # the work folder is removed

    def test_a_second_build_keeps_the_first_as_previous(self) -> None:
        self.run_package()
        first = self.root / "out" / "macos" / "Arcanum Steamwoven.app" / "marker"
        first.write_text("first")
        self.run_package()
        self.assertFalse(first.exists())
        self.assertEqual((self.root / "out" / "macos" / "previous" / "Arcanum Steamwoven.app" / "marker").read_text(), "first")
        self.assertTrue((self.root / "out" / "macos" / "previous" / "BUILD.txt").is_file())
        self.assertTrue((self.root / "out" / "macos" / "previous" / "README.md").is_file())
        self.assertEqual(sorted(path.name for path in (self.root / "out" / "macos").iterdir()),
                         ["Arcanum Steamwoven.app", "BUILD.txt", "README.md", "previous"])

    def test_the_daily_build_records_the_map_smoke_chain_in_build_txt(self) -> None:
        self.run_package(map_smoke="FAIL town; passed main (log /l/smoke.log)")
        self.assertIn("map-smoke FAIL town; passed main (log /l/smoke.log)\n", (self.root / "out" / "macos" / "BUILD.txt").read_text())
        with self.assertRaisesRegex(package.PackageError, "one line"):
            self.run_package(map_smoke="PASS\ncommit 0")

    def test_an_export_without_the_pinned_sdk_is_refused_and_nothing_installed(self) -> None:
        self.export_text = "dotnet /opt/homebrew/bin/dotnet publish 8.0.131\n"
        with self.assertRaisesRegex(package.PackageError, "pinned SDK"):
            self.run_package()
        self.assertFalse((self.root / "out" / "macos").exists())

    def test_a_bundle_that_is_not_ad_hoc_signed_is_refused(self) -> None:
        self.signature = "Signature=size=9000\n"
        with self.assertRaisesRegex(package.PackageError, "ad-hoc"):
            self.run_package()

    def test_the_smoke_needs_the_package_line_its_own_user_folder_and_both_passes(self) -> None:
        line = f"ARC-DEV-047 build Arcanum Steamwoven alpha · main {self.commit[:10]} · 2026-10-28 user=/Users/p/Library/Application Support/"
        passes = "ARC-UI-014 native-smoke launcher PASS\nARC-UI-011 native-smoke main menu PASS\n"
        self.smoke_text = line + "Arcanum Steamwoven Game\n" + passes
        self.run_package(smoke_folder=self.root)
        self.assertIn("smoke PASS", (self.root / "out" / "macos" / "BUILD.txt").read_text())
        smoke_command, smoke_environment = self.calls[-1]
        self.assertEqual(smoke_command[1:], ["--", "--native-smoke"])
        self.assertEqual(smoke_environment["ARC_AREA_MAP_SMOKE_ONLY"], "menu")
        self.assertTrue(smoke_environment["LAUNCHER_SETTINGS_TEST_PATH"].endswith("launcher_v1.json"))
        for text, error in ((line + "Godot/app_userdata/Arcanum Terrain Viewer\n" + passes, "user folder"),
                            (line.replace("alpha · main", "alpha · main x") + "Arcanum Steamwoven Game\n" + passes, "package of"),
                            (line + "Arcanum Steamwoven Game\nARC-UI-014 native-smoke launcher PASS\n", "main menu PASS")):
            self.smoke_text = text
            with self.assertRaisesRegex(package.PackageError, error):
                self.run_package(smoke_folder=self.root)

    def test_the_windows_build_is_checked_by_its_files_zipped_and_installed(self) -> None:
        installed = self.run_package(smoke_folder=self.root, platform="win")
        self.assertEqual([Path(command[0]).name for command, _ in self.calls], ["dotnet", "dotnet", "Godot", "Godot"])  # no signing, no smoke
        export = self.calls[3][0]
        self.assertEqual(export[-2:-1], ["Windows package"])
        self.assertEqual(Path(export[-1]).name, "Arcanum Steamwoven.exe")
        self.assertEqual(installed, self.root / "out" / "windows" / "Arcanum Steamwoven Windows x64.zip")
        with zipfile.ZipFile(installed) as archive:
            names = sorted(archive.namelist())
        self.assertTrue(all(name.startswith("Arcanum Steamwoven/") for name in names))
        self.assertIn("Arcanum Steamwoven/Arcanum Steamwoven.exe", names)
        self.assertIn("Arcanum Steamwoven/Arcanum Steamwoven.pck", names)
        self.assertIn("Arcanum Steamwoven/data_Arcanum.TerrainViewer_windows_x86_64/coreclr.dll", names)
        build = (self.root / "out" / "windows" / "BUILD.txt").read_text()
        self.assertIn(f"commit {self.commit}", build)
        self.assertIn("product Arcanum Steamwoven Windows x64.zip", build)
        self.assertIn("smoke NOT RUN", build)
        self.calls.clear()
        self.run_package(platform="win")
        self.assertEqual(sorted(path.name for path in (self.root / "out" / "windows").iterdir()),
                         ["Arcanum Steamwoven Windows x64.zip", "BUILD.txt", "README.md", "previous"])
        self.assertTrue((self.root / "out" / "windows" / "previous" / "Arcanum Steamwoven Windows x64.zip").is_file())

    def test_a_windows_export_with_a_wrong_file_is_refused_and_nothing_installed(self) -> None:
        good = dict(self.windows)
        for change, error in (({"machine": 0x14C}, "x86-64 GUI"), ({"subsystem": 3}, "x86-64 GUI"),
                              ({"name": "Godot Engine"}, "product name"), ({"pack": b"PK\3\4"}, "Godot pack"),
                              ({"feature": "standard"}, "feature"), ({"assemblies": ("GodotSharp.dll",)}, "lacks"),
                              ({"data": 0}, "0 .NET data"), ({"data": 2}, "2 .NET data")):
            self.windows = dict(good, **change)
            with self.assertRaisesRegex(package.PackageError, error):
                self.run_package(platform="win")
            self.assertFalse((self.root / "out" / "windows").exists())

    def test_a_failed_step_names_its_log(self) -> None:
        def failing(command, cwd, environment, log, minutes) -> int:
            log.write_text("error CS0001: broken\n")
            return 1
        with self.assertRaisesRegex(package.PackageError, r"step restore FAILED exit=1 log=.*restore\.log\nerror CS0001"):
            package.package(package.PLATFORMS["mac"], root=self.repo, output=self.root / "out", ref="HEAD", fetch=False, smoke=False,
                            keep_work=False, runner=failing, base=self.base)

    def test_a_command_that_cannot_start_is_a_failed_step(self) -> None:
        log = self.root / "missing.log"
        self.assertEqual(package.run_logged([str(self.root / "no-such-tool")], self.root, {}, log, None), 127)
        self.assertIn("could not start", log.read_text())

    def test_a_ref_that_is_not_a_commit_is_refused(self) -> None:
        with self.assertRaises(package.PackageError):
            package.resolve(self.repo, "no-such-branch")


if __name__ == "__main__":
    unittest.main()
