# Attribution

## Buster Drone (3D model)

- Title: Buster Drone
- Author: LaVADraGoN (https://sketchfab.com/lavadragon)
- Source: https://sketchfab.com/3d-models/buster-drone-294e79652f494130ad2ab00a13fdbafd
- License: CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)

The mod consumes the unmodified `Resources/buster_drone.glb` at runtime.
`Resources/MechaTextures/*.png` are downscaled copies of the textures embedded
in that GLB, produced by `tools/Mecha/Prepare-Textures.ps1`.

No Unity Editor or asset bundle pipeline is involved; the walker mesh is
assembled in-process from the GLB vertex buffers.
