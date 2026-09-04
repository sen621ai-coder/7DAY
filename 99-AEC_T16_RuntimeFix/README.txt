AEC T16-T19 Runtime Fix
=======================

Endgame expansion design
- ENDGAME_EXPANSION_BLUEPRINT.md specifies the next T16-T19 development wave:
  advanced weapon families, behavior-changing components, field supplies,
  powered defense devices, fortress blocks, siege counters, recipes and loot.
- This blueprint is design documentation. Runtime 1.17.1 does not yet add those
  planned weapons, devices or blocks.

Runtime 1.17.1 Warden aura targeting fix
- Warden Field now always buffs its owner and only allied/party players in its
  area. Hostile players can no longer receive the defensive aura in PvP.
- Adds validation for the owner and allied-player target paths so regeneration
  cannot silently reintroduce the broad player target filter.

Runtime 1.17.0 endgame resonance armor
- Adds four true armor sets at T16, T17, T18 and T19. Every set has a helmet,
  outfit, gloves and boots, for 64 new wearable pieces in total.
- Exact-tier 2-piece bonuses add a role passive. Three pieces unlock a visible
  0-100 resonance meter. Four pieces allow the matching reusable toolbelt device.
- Harrier charges through ranged headshots, Storm through ranged hits, Tremor
  through melee power attacks, and Warden by taking hostile hits.
- Harrier designation creates a precision damage window; Storm vent grants rapid
  penetrating fire; Tremor release empowers power attacks without block damage;
  Warden field refreshes a defensive stamina aura on its owner and nearby allies.
- Adds 16 reusable active devices, three craftable chassis, tiered siege capacitors
  and mutant hearts, direct recipes, and T17-T19 upgrade recipes.
- Blood Moon siege engineers now drop their tier's capacitors. Same-tier boss
  reward caches always contain a mutant heart and can drop one armor piece.
- Removes a stray patch marker from items.xml which could invalidate that config.
- Generated arsenal data can be rebuilt with Tools/generate_endgame_arsenal.py.

This compatibility layer completes the runtime side of the T16-T19 enemy tiers.

Runtime 1.16.7 movement-effect safety
-------------------------
- Jump, walk, run, crouch and movement-factor results now reject negative,
  NaN and infinite gameplay values while preserving intentional zero-speed effects.
- Spring Heel's final loaded configuration is verified at +0.5% to +200%
  across 100 ranks, and its obsolete XML-patch warning is removed in Tweaks 3.9.8.

Runtime 1.16.6 current-save tier pacing
-------------------------
- T15 now extends through GS 179999. T16/T17/T18/T19 begin at GS
  180000/240000/270000/300000 across Blood Moon pools and quest selection.
- A player around GS 190000 now receives T16 ordinary enemies and T16 siege
  squads; later tiers rise in 30000-GS steps after T16.

Runtime 1.16.5 endgame tier pacing
-------------------------
- T15 now extends through GS 79999. T16/T17/T18/T19 begin at GS
  80000/140000/170000/200000 across Blood Moon pools and quest selection.
- Blood Moon engineering squads use the same thresholds, so their rank never
  runs ahead of the ordinary horde tier.

Runtime 1.16.4 automatic-fire overflow hotfix
-------------------------
- Invalid negative fire rates now fall back to each weapon's native rate
  instead of being forced to one round per minute.
- Valid zero-valued counts stay zero, and WeaponHandling keeps its native
  signed semantics while recoil angles remain protected from reversal.

Runtime 1.16.3 Blood Moon siege random-source hotfix
-------------------------
- Blood Moon engineering-squad replacement now mirrors the native selector's
  world-random fallback when its optional random argument is null.
- Missing world entity collections safely preserve the ordinary spawn instead
  of interrupting the Blood Moon director.

Runtime 1.16.2 contract-relay pickup fix
-------------------------
- All six Contract Relay tiers once again expose the standard take command.
  Pickup uses the game's normal land-claim, repair, inventory and server checks.

Runtime 1.16.1 contract-relay and respawn compatibility fixes
-------------------------
- Contract Relay blocks now call their quest-giver spawn events directly with
  the block position. V2.5 drops that position through the old nested event,
  which made all six relay tiers appear to do nothing when activated.
- The local-death recovery prefix now uses the current Respawn parameter name,
  allowing Harmony to install the recovery patch instead of rejecting it.

Runtime 1.16.0 stronghold construction challenge
-------------------------
- Base defense now requires a craftable Stronghold Core, functional Power
  Station and lockable Supply Depot. The beacon is used within 10m of the core.
- Starting construction is scored inside 20m from structural HP, block count,
  electrical systems and defenses. The core anchors the existing three waves.
- Core loss fails the run. Power/supply survival and retained structural HP
  combine with the starting grade to select a rank 1-3 conditional bonus.
- Rank 3 adds an exact-tier boss cache. See BASE_DEFENSE.md for formulas, costs
  and the T16-T19 bonus table.

Runtime 1.15.4 trader-label and loot-loader hotfix
-------------------------
- Loot groups are now defined before any container is rewired to them. This
  prevents the loot.xml loader failure and the follow-on EntityLootContainer
  NullReferenceException spam seen when a T16-T19 enemy drops a bag.
- 1.15.3 physically inserts the 40 utility/wrapper groups before the existing
  T16-T19 mob containers, matching LootFromXml's final-document parse order.
- 1.15.4 makes the trader show each legendary offer's exact task name. The
  hunter, bulwark and ranged-suppression contracts no longer look like three
  duplicate generic clear jobs merely because they inherit clear objectives.
- Every Legendary Operations page now mixes one clear, one infestation and one
  fetch/fetch-and-clear contract, followed by three affixed clears. All six use
  unique IDs and can coexist in one player's quest journal.
- Fully restart the host and every peer so XML, DLL and trader offer caches are
  rebuilt. Previously spawned broken loot bags should be allowed to despawn.

Endgame Reward Balance (Runtime 1.15.4)
---------------------------------------
- Chinese tables and exact rules: ENDGAME_REWARD_BALANCE.md.
- T18/T19 clear-contract Dukes now rise with A1-A5 size. Affix contracts keep
  their exact +25% premium; other existing quest guarantees stay unchanged.
- Every A5 contract awards its own T16/T17/T18/T19 boss cache.
- Ordinary high-tier bags preserve native equipment odds, then independently
  roll one utility supply at 12% / 16% / 20% / 25%.
- All 21 boss families use exact-tier bags. Mutation samples scale x1 / x1.25 /
  x1.6 / x2, with a second-cache roll of 25% / 40% / 60% / 80%.
- Equipment stays Q5-Q6. Advanced gear is rank 2 / 3 / 4 / 5 by tier.
- Portable boss caches ignore loot abundance and biome/POI LootStage inflation.
- Restart every peer without EAC and accept fresh quests. Offline regressions
  passed; actual opening frequency and two-player behavior still need live tests.

Blood Moon Ranged Siege Squad (Runtime 1.14.0; retuned in 1.16.6)
----------------------------------------------
- Chinese behavior, damage values and live test checklist: BLOOD_MOON_SIEGE.md.
- Siege ranks T16-T19 use the ordinary Blood Moon GS
  180000/240000/270000/300000 ladder.
- Blood Moons can replace 25% of eligible ordinary zombie selections with heavy
  bombardiers / wall corroders in an approximate 2:1 ratio. Native spawn counts,
  boss rolls and animal branches remain in force.
- At selection time allow at most eight living siege units within 96m of the
  target, and at most four currently targeting that player. Caps lower frequency.
- Native AI selects a visible structural face within 24m of the player and
  8-52m of the gunner. It retains that breach until invalidated; it does not
  need to see the player behind the wall. Terrain is not a siege target.
- Both roles use native physical projectiles and native block damage/explosions,
  with fixed target coordinates in native action packets. No direct block deletion.
- Broadcast warning and native windup precede a single shot (heavy 3s, acid 2s).
  Reload stays on station. Stun, electrocution, pain interruption, lost line of
  sight or target death cancels the attack. Already-fired rounds remain physical.
- Siege fire requires a server-side, living Blood Moon entity during an active
  Blood Moon. Afterward the surviving unit retains ordinary melee pursuit.
- All peers need the whole folder and an EAC-disabled restart. Offline tests
  verify native parsers/packet payloads; actual siege layouts and live multiplayer
  still need in-game testing.

Adventure and Defense Corrections (Runtime 1.13.1)
-------------------------------------------------
- Trial confirmation rechecks death, trader protection, enemy spawning and an
  already-active same-tier trial before consuming the voucher.
- Trial enemies carry their quest identity in the initial native spawn packet;
  nearby and abandoned encounters cannot supply another trial's kill credit.
- Explicit server enqueue denials release only the matching unapproved adventure
  or voucher request. Timely denials retry within the existing bounded dispatch
  loop; later denials leave it retryable on reload. Silence never triggers replay.
- Defense kill callbacks ignore an unscheduled next phase before checking the
  previous wave's deadline, avoiding a false failure just after clearing a wave.
- Update all peers and accept fresh trials: old spawned trial enemies do not
  carry the new identity. Abandon an unfinished pre-update trial and use a new
  voucher; existing enemies are not removed and vouchers are not refunded.
- Offline regressions passed. Live Unity hooks, gameplay and two-player smoke
  tests still require in-game validation.

Equipment Builds (Runtime 1.13.0)
---------------------------------
- Chinese guide, exact numbers and smoke checks: EQUIPMENT_BUILDS.md.
- Sixteen independent cores: Eagle Eye, Breaker, Barrage and Skirmisher,
  each with fixed ranks 2-5 corresponding to T16-T19 encounter components.
- Trial completion adds 3 matching components; base defense adds 5. All old
  rewards remain. Craft any chosen core from 6 components plus parts/metals.
- Direct workbench crafting takes 60 seconds, with no material discounts or
  lower-rank prerequisites. No global loot pool or original item-stat changes.
- Every build has a trade-off. Native modifier tags allow only one core per
  item; mobility is chest-only, and can pair with the active weapon's core.
- Native stat effects only: no new runtime combat hooks or persistent buffs.
  Rank is encoded by the core item ID, not the host weapon's quality.
- Update the whole folder on every peer, restart, and accept fresh encounters
  for component rewards. Offline tests are not live multiplayer validation.

Voluntary Base Defense (Runtime 1.12.0)
--------------------------------------
- Chinese instructions, exact rewards and smoke-test checklist: BASE_DEFENSE.md.
- Craft one tiered beacon at a workbench from one same-tier trial voucher,
  50 forged steel and 20 electrical parts. Costs cannot receive crafting discounts.
- Use at the chosen center and accept the native confirmation to consume one.
  Building damage is explicitly warned before activation. Nothing starts automatically.
- T16-T19 each have three waves: 9 / 12 / 9 enemies, including two wolves in
  wave 2 and two commanders in wave 3. Initial ranged count is exactly one third;
  bosses retain their existing summons. No global Blood Moon or spawn-pool edits.
- Prepare for 20s; clear a wave before the next starts, with 15s intermissions.
  Defend within a 45m horizontal radius and 60m vertical difference. Outside has
  15s return grace; death, disabled spawning, trader protection or 15min wave
  timeout fails the run. Reloading ends it without replaying or refunding waves.
- Server actions enforce the native-networked lease and full signed quest ID.
  NearPosition spawns stay anchored to the original center, not the owner's new
  position or the previous spawned enemy. Retreat pauses pending spawns.
- Encounter identity travels in native game-event ExtraData / initial entity
  spawnByName metadata (spawnById remains -1), so nearby runs cannot borrow kills.
- Explicit server enqueue denials release only the matching unapproved wave;
  an accepted request is not blindly replayed. Native limits and terrain still apply.
- Completion requires all eleven kill objectives across all three waves. Seven
  original guarantees remain; 1.13.0 adds one tactical-component reward entry.
- Existing spawned enemies are NOT deleted when an encounter ends. Structures
  are NOT repaired automatically. There is no beacon refund for a failed run.
- 44 isolated enemy classes inherit existing tier stats/loot; high-tier navigation
  recognizes their wave IDs. Eight defense commanders inherit the existing weaknesses.
- Both host/server and clients must update the whole 99-AEC_T16_RuntimeFix folder
  and restart without EAC, retaining matching Tweaks 3.9.8.
- Offline regressions passed; no live gameplay or multiplayer testing performed.

Legendary Adventures (Runtime 1.11.0)
------------------------------------
- Chinese gameplay and installation guide: LEGENDARY_ADVENTURES.md in this folder.
- First-batch features: affix contracts, optional post-clear trials, scoped boss weaknesses.
- Each T16-T19 / A1-A5 page attempts a clear, an infestation and a fetch contract
  followed by three affixed clears (hunter, bulwark, storm). A1 uses fetch-only;
  A2-A5 use fetch-and-clear. Affix order is server-randomized; all use native POIs.
- 60 new variant definitions preserve the original POI objectives and advanced
  reward pools. Only their XP / Dukes / samples increase by 25%.
- Rallying an affixed contract requests two corresponding exact-tier champions;
  the original T17-T19 two-boss dispatcher handles all three ordinary task types.
- Clearing a legendary POI awards its owner one tiered optional trial voucher.
  Use outside trader protection and accept the native quest offer to call three
  champions. Declining leaves the item intact; the original turn-in is independent.
- Trials require one kill of each trial champion, then automatically give weapon,
  combat mod(s), legendary parts, tokens and XP. Affix bosses do not count as trial kills.
- The 24 first-batch encounter classes get weaknesses: hunter head x2; bulwark
  18s normal / 6s overheating with player direct damage x3; storm melee x3.
- Native buff synchronization and native damage-response networking are preserved.
  Damage scaling runs on the calculating peer, including joining players, not again
  on packet receipt. No permanent invulnerability or global damage changes added.
- Quest data persists dispatch/clear/voucher markers. Shared copies do not summon
  or award vouchers. Native spawn limits and protected-area checks are not bypassed.
- An accepted game event is not proof that all entities actually spawned. Do not
  start trials in cramped terrain, and finish before disconnecting if possible.
  Accepted requests are never blindly replayed; abandoning does not refund vouchers.
- Update the whole 99-AEC_T16_RuntimeFix folder on host, server and all clients;
  retain matching 98-AECxProjectZ_Tweaks 3.9.8. Fully restart without EAC.
- Offline build and regression tests passed; in-game/two-player smoke testing is
  still required. No live game or save was launched or edited for this update.

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
- Uses an 18% Blood Moon boss roll at GS 180000+ and supplies all 21 T16 bosses to that pool.
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
- T16/T17/T18/T19 start at GS 180000/240000/270000/300000 and select only that exact tier.
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
- T16 begins at GS 180000 and runs through GS 239999.
- T17 Ascendant begins at GS 240000 and runs through GS 269999.
- T18 Eternal begins at GS 270000 and runs through GS 299999.
- T19 Apocalyptic begins at GS 300000.
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
- T17/T18/T19 menu entries unlock at current GS 240000/270000/300000; T16 keeps its existing area unlocks.
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
- All five POI sizes at each tier offer three advanced reward entries: weapon, combat mod and crafting materials. These use native chosen rewards; the inherited AEC templates currently set reward_choices_count to 3.
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
- 36 scoped bags preserve all nine native loot families and their equipment odds. Runtime 1.15.3 adds only the separately rolled tiered utility supply described above; it does not add equipment to ordinary-mob bonus rolls.
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
T16-T19 remain enemy/challenge tiers. Adventures and defense add eight quest-note
items and four beacon recipes. Builds add four components and sixteen cores /
recipes; core effects apply only when installed, without global equipment edits.

Verification
------------
The DLL builds without warnings or errors. All mod XML and JSON files parse, and
the localization CSV has consistent 20-column rows with no duplicate keys.
