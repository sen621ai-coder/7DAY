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
- The ordinary trader job list offers the player's current GS tier with up to 30 valid POI variants.
- Adds complete T17, T18 and T19 clear-contract definitions and localization.
- Adds Legendary Operations for T16-T19; Master Operations now contains T11-T15.
- Legendary category pages resolve quest IDs directly instead of obsolete list offsets.
- Each of five POI-size pages attempts six offers; availability depends on nearby matching POIs.
- T17/T18/T19 menu entries unlock at current GS 50000/70000/90000; T16 keeps its existing area unlocks.
- The assembly-qualified dialog requirement reads current player GS without waiting for watcher CVars.
- Quest rewards, enemy strength and the ordinary trader offer generator are unchanged by this menu fix.

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
