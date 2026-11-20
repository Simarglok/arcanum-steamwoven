"""ARC-DEV-2459: the kit fit tool's tests (tools/steamwoven_ui/test_fit.py, synthetic images, about 5 s) run in the batched `run_tests.py` set, which only
reads this folder. The tool is standard library only, so they need no environment."""
from tools.steamwoven_ui.test_fit import *  # noqa: F401,F403
