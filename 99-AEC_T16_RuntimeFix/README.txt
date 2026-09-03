AEC T16-T19 Runtime Fix
=======================

This compatibility layer completes the runtime side of the T16-T19 enemy tiers.

Fixes
-----
- Accepts signed custom entity-class IDs when spawning leader followers.
- Repositions spawned followers on terrain instead of reusing an invalid Y=0 leader hook position.
- Gives all 35 ordinary T16 enemies the matching T15 kill-reward rules.
- Gives 20 T16 bosses the matching T15 kill-reward rules.
- Extends those reward aliases to T17 Ascendant, T18 Eternal and T19 Apocalyptic enemies.
- Keeps AECExplosiveEagleBossT16 harvest-only, matching its T15 design.
- Adds display names for all 56 T16 combat entities and repairs the T16 boss bundle label.
- Raises the Blood Moon boss roll from 2% at GS 600 to 12% before T16.
- Uses an 18% Blood Moon boss roll at GS 30001+ and supplies all 21 T16 bosses to that pool.
- Forces integrated Blood Moon groups to use their nighttime biome weights.

Model tint initialization safety
--------------------------------
- Skips only the cosmetic MatColor work when the model has no valid tint material.
- Preserves native renderer materials and completes the remaining entity initialization.
- Prevents the observed Instantiate<Material>(null) failure and its cascading LateUpdate errors.
- Logs the affected entity class once instead of suppressing exceptions every frame.
- T16 Demolition also retains its native materials, matching T15/T17-T19.

Blood Moon current-game compatibility (1.8.0)
--------------------------------------------
- Hooks the actual selector call inside AIDirectorBloodMoonParty.SpawnZombie.
- Current builds call GetRandomEntityFromGroupMaxTier, bypassing the old GetRandomFromGroup hook.
- Selects by the chased player's current GS, never by party average or stale spawn context.
- T16/T17/T18/T19 start at GS 30001/50000/70000/90000 and select only that exact tier.
- Uses the existing nighttime biome weights, beast/ranged mix and 18% high-tier boss roll.
- Blood Moon selection does not enter the wilderness land-claim exclusion path.
- Four exact-tier XML backup pools replace old high-GS stage references; each has 64 classes and 18% boss weight.
- Preserves engine enemy/max-tier filters, native vehicle-vulture behavior, wave counts and player limits.
- Logs [AEC-BloodMoon-Fix] at installation, sampled selections, each dynamic boss roll success and fallback warnings.
- Existing material-tint, navigation, quest and reward fixes are preserved. Restart the game/server to load the DLL.

Player weapon headshot damage
-----------------------------
- Multiplies existing player weapon headshot damage by 5 after the original headshot calculation.
- Covers the shared gun, bow/crossbow and melee hit path without a weapon-name whitelist.
- Preserves all flat bonuses, including +2000 for improved/Gaus sniper rifles, perks and sandbox settings.
- Does not amplify body hits, block damage, damage-over-time or non-player attacks.
- Target armor, resistances and difficulty still apply afterward; extreme damage saturates instead of overflowing.

High-tier AI optimization (T14-T19)
-----------------------------------
- Preserves all original AITask and custom boss-skill logic.
- Improves jump and obstacle clearance progressively by tier.
- Refreshes valid combat targets to reduce premature target loss.
- Detects stalled pursuit at a low sampling rate and discards only the stale path.
- Uses per-entity recovery cooldowns to remain safe during large Blood Moon waves.

T16 ranged pressure
-------------------
- Balances the T16 Blood Moon pool to approximately one ranged enemy per three spawns.
- Compensates per biome while preserving the 18% overall boss roll.
- Distributes the added weight across five ordinary T16 ranged families.

T17-T19 progression
--------------------
- T17 Ascendant begins at GS 50000 and runs through GS 69999.
- T18 Eternal begins at GS 70000 and runs through GS 89999.
- T19 Apocalyptic begins at GS 90000.
- Each tier carries forward all 35 ordinary enemies, 21 bosses and eight beasts.
- The existing ranged weighting and 18% Blood Moon boss roll continue at each tier.

Trader quest repair
-------------------
- Replaces the upstream all-tier trader merge that generated 510 AEC offers at once.
- Before T16 the generator offers the player's current GS tier. At T16+ it supplies all unlocked legendary tiers (up to 120 offers) for the category browser.
- Adds complete T17, T18 and T19 clear-contract definitions and localization.
- Adds Legendary Operations for T16-T19; Master Operations now contains T11-T15.
- Legendary pages use six logical slots resolved against server-generated, per-player synchronized quest lists. They never create quests or request POIs locally.
- Each of five POI-size pages attempts six offers; availability depends on nearby matching POIs.
- T17/T18/T19 menu entries unlock at current GS 50000/70000/90000; T16 keeps its existing area unlocks.
- The assembly-qualified dialog requirement reads current player GS without waiting for watcher CVars.
- The networking fix does not alter rewards or enemy strength. Host and joining players must both use the updated runtime DLL and dialog config.
- Selection indices are absolute; the RemoveQuest packet uses the correctly computed difficulty-relative byte index, including preceding non-AEC/special quests.
- Invalid player requests are rejected without guessing an identity. The packet handler's unused POI-center calculation no longer dereferences a null prefab before its native null check.
- Close the game and update Mods/98-AECxProjectZ_Tweaks and Mods/99-AEC_T16_RuntimeFix on all peers, then restart/reconnect to rebuild offer caches.
- Launch without EAC on all peers; the runtime DLL is skipped when anti-cheat is enabled and is not automatically downloaded from the host.

T16-T17 quest reward uplift (Tweaks 3.9.5)
----------------------------------------
- Guaranteed turn-in rewards increase by POI size A1-A5; no items are awarded merely for accepting a quest.
- T16: XP 300000 / 600000 / 900000 / 1200000 / 1500000; Dukes 15000 / 17000 / 19000 / 21000 / 23000.
- T16: Universal Tokens 350 / 375 / 400 / 425 / 450; T5 samples 1200 / 2400 / 3600 / 4800 / 6000.
- T17: XP 600000 / 1200000 / 1800000 / 2400000 / 3000000; Dukes 25000 / 27000 / 29000 / 31000 / 33000.
- T17: Universal Tokens 450 / 490 / 530 / 570 / 610; T5 samples 2400 / 4800 / 7200 / 9600 / 12000.
- XP rises 50%, samples rise 60%; the same-size guaranteed rewards remain strictly ordered T16 < T17 < T18 < T19.
- This currency uplift preserves the existing one-bundle A5 reward, T18-T19 currency values, unlock thresholds, objectives and multiplayer acceptance logic. Advanced item choices are described below.
- Restart the host/game to load the XML changes; verify the new amounts on a freshly accepted quest. Existing saved quests may retain their previously generated rewards.

T16-T19 advanced quest items (Tweaks 3.9.6)
------------------------------------------
- All five POI sizes at each tier now offer three advanced choices: weapon, combat mod, or crafting materials. The game's existing reward-selection limit still applies; these three are not all guaranteed.
- T16 weapon choice: one Q5 unique weapon (14-item pool). Mod choice: one rare improved combat mod.
- T17 weapon choice: one Q5 legendary ranged/melee weapon (47-item pool). Mod choice: one unique combat mod.
- T18 weapon choice: one Q6 legendary weapon. Mod choice: two copies of the rolled unique combat mod.
- T19 weapon choice: one Q6 legendary weapon. Mod choice: three copies of the rolled unique combat mod; additionally one random unique combat mod is guaranteed regardless of choice.
- Material choice rolls one stack: experimental alloys / UniqueParts / legendary parts, respectively 20/2/5 at T16, 40/4/10 at T17, 60/6/15 at T18, 100/10/25 at T19.
- Guaranteed legendary parts scale by POI size A1-A5: T16 2/4/6/8/10; T17 4/8/12/16/20; T18 6/12/18/24/30; T19 10/20/30/40/50.
- Quest-only pools use existing item IDs and fixed quality/count rules. Vanilla quest pools, boss drops, XP/currency/sample rewards and existing A5 boss bundles are not changed by this item upgrade.
- New rewards appear on freshly generated quests after restarting the host/game with updated Tweaks. Already-saved reward items may retain old rolls.

T16-T19 ordinary-mob loot quality (Runtime 1.10.0 / Tweaks 3.9.7)
---------------------------------------------------------------
- Applies to the 35 ordinary AEC zombie families at each tier (140 classes), not bosses, beasts, quest completion rewards or world containers.
- Ordinary equipment rolls quality 5-6 at all four tiers. Advanced equipment rolls exactly quality 2 / 3 / 4 / 5 for T16 / T17 / T18 / T19.
- Advanced means Rare/Unique item variants, LegendWeapon-tagged items and the existing improved/unique/legendary weapon pools. Classification adds only a marker property; it does not change the item globally.
- Items without an actual quality stat, including ammunition, materials and ordinary modifier items, retain native behavior.
- 36 scoped bags preserve all nine native loot families, including plague/regular weighted selection, existing drop probabilities, contents, stack counts and item-selection chances. Legendary item drop chances are not increased.
- The bag identity carries its source tier across looting by another player, network synchronization and saves; the looter's GS does not determine the tier.
- Quality is selected before the native ItemValue constructor initializes mod slots and before loot stats/durability are populated. Nested unrelated containers and quest reward generation are explicitly isolated; exception finalizers restore scope.
- Both host and joining players must update Mods/98-AECxProjectZ_Tweaks and Mods/99-AEC_T16_RuntimeFix, then fully restart without EAC. Check newly dropped bags: existing generated loot and old unmarked bags are not rewritten.
- Offline regression covers real SpawnItem IL, the actual factory with the Unity constructor stubbed, 896 quality cases, scope isolation, all 140 merged entity mappings and unchanged native bag contents/rates.

T16-T19 boss escalation
-----------------------
- Final boss health: 40M / 80M / 150M / 260M.
- Final skill damage: x6 / x8 / x11 / x15.
- Final block damage: x10 / x14 / x20 / x28.
- Adds 35% / 45% / 55% / 65% physical resistance and progressive control resistance.

Blood Moon beast balance
------------------------
- Restores ordinary beasts to every Blood Moon game-stage band.
- Targets about 12% ordinary beasts in T5-T15 and about 10% total beasts in T16.
- Uses eight bear, boar, dire-wolf, wolf and zombie-bear families per band.
- Progresses through T03/T06/T09/T12/T15 variants and adds true T16 variants.
- Rebalances T16 ranged weights after adding melee beasts, preserving one ranged enemy per three spawns.

Duplicate-rule isolation
------------------------
The six spawn-rule JSON files under the sibling "Mods - 副本" backup were renamed
with a .disabled suffix. This prevents the spawner's recursive parent-directory scan
from merging stale backup rules while keeping every backup file recoverable.

Related data correction
-----------------------
The invalid zombieJanitorCharged reference in the AEC spawn-rule JSON and its
template was replaced with the defined zombieJanitorChargedElite class.

Scope
-----
T16-T19 remain enemy/challenge tiers. No new equipment or recipes are introduced
by this fix, so the existing T15 equipment balance is unchanged.

Verification
------------
The DLL builds without warnings or errors. All mod XML and JSON files parse, and
the localization CSV has consistent 20-column rows with no duplicate keys.
