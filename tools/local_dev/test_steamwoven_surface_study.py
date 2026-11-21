"""ARC-RES-2457: the surface study planners' tests (tools/steamwoven_ui/test_surface_study.py) run in the batched `run_tests.py` set, which only
reads this folder. They run no ImageMagick and need no environment."""
from tools.steamwoven_ui.test_surface_study import *  # noqa: F401,F403
