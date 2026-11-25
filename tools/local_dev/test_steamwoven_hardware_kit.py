"""ARC-UI-3261: the hardware kit tool's tests (tools/steamwoven_ui/test_hardware_kit.py) run in the batched `run_tests.py` set, which only
discovers `tools/local_dev/test_*.py`."""
from tools.steamwoven_ui.test_hardware_kit import *  # noqa: F401,F403
