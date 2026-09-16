# T16–T19 weapon durability display

All 28 weapons (six gun families and Faultline Hammer, four tiers each) now
enable the native ShowQuality bar. The existing durability stat row displays
remaining / maximum (percentage), using ItemValue.MaxUseTimes and UseTimes.
This includes native permanent durability loss and the normal mod/fusion effects.
No damage, wear, maximum-durability, repair, or saved-item values are changed.
Armor, attachments and legacy weapons are outside the custom text scope.

Restart the game after installing the updated XML and DLL. Existing weapons
do not need to be crafted again. No save migration is performed.

Offline check: `pwsh -File tools/Test-WeaponDurabilityDisplay.ps1`.
In-game read-only check: `aecequipmentdisplaycheck`, extended with full/half/broken
weapons, fusion ranks 0/1/10 and permanent-durability multipliers 1/0.75.
The offline check does not replace a live visual check of inventory/toolbelt UI.
