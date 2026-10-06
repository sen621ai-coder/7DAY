"""Rebuild Complete semantic bindings without accumulating repair geometry."""
from pathlib import Path
import json,runpy,subprocess,sys
res=Path(__file__).resolve().parents[2]/'ZZ-PZAEC_Mecha/Resources'
doc=json.loads((res/'samurai_style_gundam_mecha_rig.json').read_text())
if not ('panelRepair' in doc or 'articulationRepair' in doc):
 runpy.run_path(str(Path(__file__).with_name('Prepare-CompleteBindings.py')))
subprocess.run([sys.executable,str(Path(__file__).with_name('Rebuild-CompletePanels.py')),'--source',str(res),'--output',str(res)],check=True)
