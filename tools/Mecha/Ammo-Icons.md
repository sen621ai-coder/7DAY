# 机甲通用弹药图标（0.11.2）

使用内置 `image_gen`，分别生成两款透明背景图标。保留生成原图，从 alpha 内容边界统一留白并缩放为 256×256 RGBA PNG，分别安装到 `ZZ-PZAEC_Mecha/ItemIcons/` 与 `ZZ-PZAEC_Mecha/UIAtlases/ItemIconAtlas/`。物品的 `CustomIcon` 与文件名一致。

能量电池：`ammoPZAECMechaCell.png`，方形三联电芯、青绿发光。制导导弹：`ammoPZAECMechaMissile.png`，斜向长弹体、橙红弹头、明显尾翼。

## 能量电池提示词

Use case: game-asset. Asset type: one square inventory item icon for 7 Days to Die mech ammunition. Generate a single sci-fi ENERGY CELL on truly transparent background. It must read immediately at 48px as a compact broad rectangular battery cartridge, never a bullet, rocket, missile, or loose electronics. Three large luminous cyan-green vertical cells housed in a dark gunmetal rectangular frame, thick bright steel edges, one clearly visible chunky top electrical connector. Modest three-quarter perspective, upright, centered, fills 85% of square canvas, fully inside frame with clean padding. Polished realistic painted 3D game inventory icon, bold simple silhouette, restrained surface detail, crisp edges, soft upper-left studio light. Cyan/teal core and charcoal/steel shell. No text, lettering, numerals, branding, border, backdrop, scenery, floor, shadow outside object, loose accessories or watermark. Keep glow tightly within the object, actual transparent alpha background.

## 制导导弹提示词

Use case: game-asset. Asset type: one square inventory item icon for 7 Days to Die mech ammunition. Generate a single GUIDED MISSILE on truly transparent background. Instantly readable at 48px as one long pointed missile with four prominent stabilizing tail fins and a pointed saturated orange-red nose cone. Long bright light steel cylindrical body with a thick charcoal band and small orange accent stripe. Nose points towards upper-right, tail towards lower-left, diagonal across square, fills 85% of square with all fins visible and clean padding. Strongly different from a compact rectangular battery. Polished realistic painted 3D game inventory icon, bold clear silhouette, restrained surface details, crisp edges, soft upper-left studio light. Warm orange/red and steel white palette, no cyan or green. No text, letters, numbers, branding, border, floor, backdrop, scenery, launch smoke, fire, external shadow, accessories, extra missiles or watermark. Actual transparent alpha background.

## 验证

PNG 256×256、RGBA、透明背景，两处文件逐字节一致；48/64 像素深浅背景人工预览检查，现有 `tools/Mecha/Test-All.ps1` 检查通过。
