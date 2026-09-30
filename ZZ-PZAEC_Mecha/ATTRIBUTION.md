# Attribution

## Combat Robot (3D model)

- Title: Combat Robot
- Author: ILUSHANDRO (https://sketchfab.com/ILUSHANDRO)
- Source: https://sketchfab.com/3d-models/combat-robot-55fe65bbb4074979ac0cb44850df4ddc
- License: CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)

The mod consumes the unmodified `Resources/combat_robot.glb` at runtime.
`Resources/MechaTextures/*.png` are downscaled copies of the textures embedded
in that GLB, produced by `tools/Mecha/Prepare-Textures.ps1`.

No Unity Editor or asset bundle pipeline is involved. The biped joint rig
(Hip/Knee/Shoulder/Hand) is rebuilt procedurally at load time from the flat
Sketchfab mesh list; walking is analytic two-bone IK authored in the mod.
