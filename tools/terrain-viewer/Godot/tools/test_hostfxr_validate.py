"""Independent hostfxr gate failure boundaries; no real-resource fixtures."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from hostfxr_validate import verify_hostfxr


class HostfxrValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.app = Path(self.temporary.name) / "Viewer.app"
        self.libraries = [
            self.app / "Contents/Resources/data_Arcanum.TerrainViewer_macos_arm64/libhostfxr.dylib",
            self.app / "Contents/Resources/data_Arcanum.TerrainViewer_macos_x86_64/libhostfxr.dylib",
        ]
        for library in self.libraries:
            library.parent.mkdir(parents=True)
            library.write_bytes(b"synthetic-not-a-binary")

    @patch("hostfxr_validate.subprocess.check_output", side_effect=["arm64\n", "x86_64\n"])
    def test_both_actual_architectures_are_queried(self, probe):
        verify_hostfxr(self.app)
        self.assertEqual(probe.call_args_list[0].args[0], ["/usr/bin/lipo", "-archs", str(self.libraries[0])])
        self.assertEqual(probe.call_args_list[1].args[0], ["/usr/bin/lipo", "-archs", str(self.libraries[1])])

    @patch("hostfxr_validate.subprocess.check_output", return_value="x86_64\n")
    def test_directory_name_does_not_prove_architecture(self, probe):
        with self.assertRaises(ValueError):
            verify_hostfxr(self.app)

    @patch("hostfxr_validate.subprocess.check_output", return_value="arm64\n")
    def test_second_library_must_have_its_own_architecture(self, probe):
        with self.assertRaises(ValueError):
            verify_hostfxr(self.app)
        self.assertEqual(probe.call_count, 2)

    @patch("hostfxr_validate.subprocess.check_output", return_value="arm64\n")
    def test_missing_second_library_is_rejected(self, probe):
        self.libraries[1].unlink()
        with self.assertRaises(ValueError):
            verify_hostfxr(self.app)

    @patch("hostfxr_validate.subprocess.check_output")
    def test_symlink_is_not_bundled_payload(self, probe):
        self.libraries[0].unlink()
        self.libraries[0].symlink_to(self.libraries[1])
        with self.assertRaises(ValueError):
            verify_hostfxr(self.app)
        probe.assert_not_called()


if __name__ == "__main__":
    unittest.main()
