# Old Sawmill (industrial building)

Author: **simple and extensive design** (@simpleandextensivedesign).

Source: https://sketchfab.com/3d-models/old-sawmill-industrial-building-39e4cd568ce74dd3a58118d3a5b94471

Licensed under **Creative Commons Attribution 4.0 International (CC BY 4.0)**:
https://creativecommons.org/licenses/by/4.0/

The user supplied the official original OBJ archive and converted GLB. The GLB
is used for its complete material assignments; the OBJ archive omits its referenced MTL.

Modifications for this mod: removed detached perimeter fences and yard ground;
rotated, uniformly scaled and recentered; converted meshes and PBR texture packing;
added backfaces, colliders, foundation, exterior control cabinet, timber and optional
upgrade props; rendered an inventory icon. The original author does not endorse
this adaptation. Retain this credit and the license link when redistributing.

Runtime-generated mesh resources are consumed by AEC.T16.RuntimeFix.dll. No Unity
editor or additional importer is required on a player's machine.

Detail pass (2026-09-18): color1.png is an AI-assisted albedo enhancement derived
from the original atlas, retaining its layout and the original CC BY attribution.
timber.png is a newly AI-generated bark/end-grain atlas. Added tapered timber meshes,
panel fasteners, vents, buttons, safety striping, packing bands and cooling fins.
Generated masters: tools/Forestry/Textures/color1-detail.png and this folder's timber.png.

Further adaptation (2026-09-18): separated roof/brick/wood/painted-metal surfaces
with world-scale UVs, retaining source-atlas assignments for miscellaneous and
boundary triangles. color2.png through color5.png are newly AI-generated material
tiles from tools/Forestry/Textures/surface-atlas.png. Their normal, smoothness and
occlusion maps are approximate albedo-derived details, not high-poly baked maps.
Added procedural sawmill machinery, read-only production/inventory presentation,
work light, wood chips and a spatially clustered distant mesh. Source geometry
and its derivatives remain credited to the original author under CC BY 4.0.

Woodworking corner: added a procedural beveled workbench, wooden vise with
guide rods and screw, hand plane, toothed hand saw, chisels, mallet, clamps,
marked try square, partly planed boards, offcut bin and curled shavings.
These use the existing surface materials and are grouped by material for LOD.

Review corrections: raised lower building walls with a piecewise vertical mapping
(overall height ratio 1.36), transformed normals and regenerated tiled UVs/LOD;
reworked the saw slot, roller clearance, shaft and belt tangency; added covered
infeed/outfeed and split-board animation, smooth motion and visual transitions.
