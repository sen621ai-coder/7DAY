# T17-T19 quest boss encounter

- Only `aec_quest_T17_A1_clear` through `aec_quest_T19_A5_clear` (15 contracts).
- Two seconds after rally activation enters phase 3, the quest owner requests two exact-tier bosses. The existing `single_choice=true` randomly selects one of the 21 families for both bosses. Native game events find valid positions roughly 10-18 metres from the owner and handle server spawning.
- `spawn_count=2`, `party_addition=0`, `ignore_multiplier=true`; shared quest copies do not request another encounter. Independent quests remain independent.
- The dispatch marker is stored in `Quest.DataVariables`, which the native quest save/load code serializes. Reloading/re-activating the same marked quest does not grant another encounter. Failed local event requests retry for up to one minute while the quest is active in phase 3; native spawn limits are not raised.
- These are two **additional** bosses, not a cap on existing random bosses or bosses' own summoned minions. No extra mandatory-kill objective, loot change, or change to T16/lower/vanilla quests.
- No retroactive spawn for an already-active phase-3 quest. Newly activated quests after restarting use the feature. Multiplayer quest owners and the host/server need the matching mod DLL and event config.
- Log prefixes: `[AEC-QuestBoss]`. A `Queued` line confirms event acceptance, not a world-spawn confirmation. Native event spawning can still be prevented/interrupted by world limits, death or leaving/reloading during the pending spawn; the marker deliberately prevents repeated event farming.

Verification: Release build and `pwsh -File tools/Test-QuestBossSpawn.ps1`. Live solo and multiplayer play remain the final smoke test: activate one T17/T18/T19 quest, verify exactly two extra same-tier bosses; share with another player; save/reload; confirm T16 and vanilla quests do not trigger it.
