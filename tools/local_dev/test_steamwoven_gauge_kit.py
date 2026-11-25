"""ARC-UI-2483: the gauge kit tool's tests (tools/steamwoven_ui/test_gauge_kit.py) run in the batched `run_tests.py` set, which only
collects test modules in this folder."""
from tools.steamwoven_ui.test_gauge_kit import *  # noqa: F401,F403
