"""Rebuild Complete semantic render roles and rigid mechanical bindings."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('Prepare-CompleteBindings.py')))
