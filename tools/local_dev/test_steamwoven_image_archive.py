"""ARC-DEV-3277: the per-task image archive tests (tools/steamwoven_ui/test_image_archive.py, synthetic files, about 2 s) run in the batched `run_tests.py` set, which only
reads this folder. Standard library only."""
from tools.steamwoven_ui.test_image_archive import *  # noqa: F401,F403
