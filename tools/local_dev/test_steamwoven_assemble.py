"""ARC-DEV-2972: the kit assembly tool's tests (tools/steamwoven_ui/test_assemble.py) run in the batched `run_tests.py` set, which only
reads this folder. The tool is standard library only, so they need no environment."""
from tools.steamwoven_ui.test_assemble import *  # noqa: F401,F403
