# Justice rendering regression — 2026-10-08

The native probe renders the production SkinnedMeshRenderers directly, without
baking static mesh copies. Two cameras render each of 180 frames. The primary
camera visits distances 6, 30 and 100; the secondary camera stays at 100. Both
versions visit LOD 0, 1 and 2. Camera callbacks compare all part meshes before
and after each render; the log handler counts native skinning errors.

| Installed-game native run | Camera renders | Vertex data/stride errors | Mesh changes during render |
|---|---:|---:|---:|
| 0.21.2 baseline | 360 | 828 | 100 |
| 0.21.3 fix | 360 | 0 | 0 |

The baseline reproduces the reported `Justice Backpack` error as well as errors
on other body parts. Only the old JusticeFinish source and version manifest
were restored in the isolated baseline copy; the live mod remained fixed.

Logs relative to the Mods folder:

- Fixed: `.local-tests/MechaNativeQA/run-e7ed2783aa9e49a98ea241f06c37f5f2/game.log`
- Baseline: `.local-tests/MechaNativeQA/run-74bcaa8f2071426a815546c452191361/game.log`
- Initial fixture attempt: `.local-tests/MechaNativeQA/run-cd3bca90ceef4fe99173f2447a9b481c/game.log`

The initial fixture correctly failed because dedicated mode did not render
cameras automatically (zero camera callbacks). The final fixture calls
Camera.Render explicitly between real Unity frame advances. This tests native
rendering and LOD transitions, not player-controlled multiplayer gameplay or
visual appearance in every lighting condition. Both QA game processes stopped.

Reproduce with no other game/QA process running:

```powershell
pwsh -File tools/Mecha/Start-NativeQA.ps1 -MotionProbe -ProbeSource tools/Mecha/JusticeRenderNativeQA.cs
```
