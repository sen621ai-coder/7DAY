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
