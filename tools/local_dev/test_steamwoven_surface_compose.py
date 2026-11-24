"""ARC-RES-2457: the method A composition's pure-part tests (tools/steamwoven_ui/test_surface_compose.py) run in the batched `run_tests.py` set, which only
reads this folder. They run no ImageMagick and need no environment."""
from tools.steamwoven_ui.test_surface_compose import *  # noqa: F401,F403
