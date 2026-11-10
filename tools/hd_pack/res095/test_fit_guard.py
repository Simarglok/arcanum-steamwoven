"""ARC-DEV-340: the fit's edge guard (which mixes the Legacy colour back in) is skipped for lock:false sheets and with --no-guard.

Synthetic frames only, no art. Needs numpy, scipy and Pillow (the res094 venv); skipped where they are missing."""
from __future__ import annotations

from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))   # the scripts import hdlib, metrics and hd_paths as top-level modules

try:
    import numpy as np
    import fit_res095 as fit
except ImportError as e:   # no numpy/scipy/Pillow outside the res094 venv
    np = None
    fit = None
    SKIP = f'needs the res094 venv: {e}'
else:
    SKIP = ''


@unittest.skipIf(fit is None, SKIP)
class GuardSwitchTests(unittest.TestCase):
    def test_enabled_by_default_and_for_lock_true(self):
        self.assertTrue(fit.guard_enabled({}))
        self.assertTrue(fit.guard_enabled({'lock': True}))
        self.assertTrue(fit.guard_enabled({}, True))

    def test_disabled_by_flag_or_manifest_lock_false(self):
        self.assertFalse(fit.guard_enabled({}, False))
        self.assertFalse(fit.guard_enabled({'lock': False}))
        self.assertFalse(fit.guard_enabled({'lock': False}, True))
        self.assertFalse(fit.guard_enabled({'lock': True}, False))

    def test_guard_pulls_border_towards_legacy(self):
        h = w = 24
        a = np.zeros((h, w, 4), np.uint8)
        a[..., :3] = 60; a[..., 3] = 255                      # flat opaque Legacy frame
        hd = np.full((h * 4, w * 4, 3), 60.0, np.float32)
        hd[:8, :] = 250.0                                      # a bright edge line along the top border
        out, share = fit.guard(hd.copy(), a)
        self.assertGreater(share, 0.0)
        self.assertLess(out[:8].mean(), hd[:8].mean())         # pulled back towards the Legacy colour
        self.assertTrue(np.array_equal(hd[h * 2:h * 3, w * 2:w * 3], out[h * 2:h * 3, w * 2:w * 3]))   # the inside stays the redraw

    def test_cli_and_job_pass_the_switch(self):
        src = (HERE / 'fit_res095.py').read_text()
        self.assertIn("'--no-guard'", src)
        self.assertIn('fit_sheet(m, gdir, use_guard)', src)
        self.assertIn('guard(hd, a) if use_guard else (hd, 0.0)', src)


if __name__ == '__main__':
    unittest.main()
