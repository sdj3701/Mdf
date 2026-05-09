## human-bot-prepare-teardown-ai-takeover-v1: Keep prepare E2E snapshots from being polluted by teardown takeover

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: HumanBot prepare seed sweep fails late with host/client augment or active effect hash mismatch after the client process drops near the harness timeout
Applies to: `AIPlayerController`, `NetworkManager.TryEnableDisconnectedAiTakeover`, `run_human_bot_prepare_progression.py`, `run_human_bot_seed_sweep.py`
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`run_human_bot_prepare_progression.py` waits for stable host/client snapshots after the HumanBot issues a command. If the build client exits or disconnects near the wait timeout, `NetworkManager` can enable disconnected AI takeover for that durable `playerId`. The takeover AI may issue a new prepare command on the host before the final comparison, producing host-only deltas such as a second `SelectAugment` and `augment.activeEffectHash` mismatch. The failure can be mislabeled as `bot_no_meaningful_command` even though the assertions show meaningful deltas.

Recipe:
- Confirm the timeline contains `disconnect_cache` followed by `disconnect_ai_takeover` just before host-only `prepare_decision_policy` or `mdf_decision_emit` lines.
- Confirm `bot-journal-latest.json` shows the HumanBot remained a real client command path before the teardown window.
- In `--mpTest` prepare-only HumanBot scenarios, do not let an AI takeover controller with `InputAuthority == PlayerRef.None` continue prepare decisions that pollute the HumanBot host/client comparison.
- Keep `NotifyAugmentSelectedCommand` presentation-only on clients. Do not add to `chosenAugments`, owned-boss lists, active summon lists, owned scroll lists, or permanent stat bonuses from the notification command; those must come from State Authority replication/sync paths.
- Build augment active-effect snapshot hashes from replicated selected augment snapshots first, and avoid double-counting authority-side derived boss/summon lists as separate peer-equality requirements.
- If a seed sweep fails on `attackMonsterPoolHash` after a boss augment, inspect logs for `InvalidKeyException` on the boss `MonsterData.name`. Attack-pool sync must not partially apply a snapshot when one entry cannot resolve; abort/resync or resolve from loaded `AugmentData`/wave/asset references. Also verify the Addressables address matches the `MonsterData` asset name, for example `MonData_Boss_Elemental`.
- Rebuild the Development player after changing driver/controller code; stale player builds will still show the old bot journal schema and do not prove the current C# path.

Verification:
- Stale build detection: `bot-journal-latest.json` lacked `MdfDecision` fields until a new Development player was built.
- `python tools/harness/mp/build_player.py` produced `artifacts/builds/20260506-053915/MDF-MPTest.exe` with `developmentBuild=true`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-053915/MDF-MPTest.exe` passed and the journal included `score`, `kind`, `attackMonsterPoolHash`, and `ownedScrollsHash`.
- After adding the takeover guard and rebuilding `artifacts/builds/20260506-054901/MDF-MPTest.exe`, `run_human_bot_prepare_progression.py --seed 1001` still passed.
- After removing client-side persistent augment notification effects and duplicate active-effect snapshot counting, `python tools/harness/mp/build_player.py` produced `artifacts/builds/20260506-061424/MDF-MPTest.exe`.
- `python tools/harness/mp/run_human_bot_prepare_progression.py --seed 1001 --player-path artifacts/builds/20260506-061424/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-061500-human-bot-prepare`.
- `python tools/harness/mp/run_human_bot_seed_sweep.py --seeds 5101,5102 --player-path artifacts/builds/20260506-061424/MDF-MPTest.exe` passed with artifact `artifacts/mp/20260506-061538-human-bot-seed-sweep`.
- After removing the remaining presented-augment cache mutation, `artifacts/builds/20260506-062445/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-062528-human-bot-prepare`.
- The same build's `run_human_bot_seed_sweep.py --seeds 5101,5102` passed seed 5101 but failed seed 5102 on `player.1.attackMonsterPoolHash` mismatch after a boss augment. This is a monster-pool replication blocker to handle in the command/snapshot phases, not a Behavior Tree v2 HumanBot routing failure.
- Artifact inspection found `MonData_Boss_Elemental` failed Addressables resolution because the group address was `MonData_Elemental`; the client skipped that boss entry and applied a partial attack pool.
- After adding read-only fallback resolution plus all-or-nothing attack-pool snapshot apply, `artifacts/builds/20260506-064820/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-064924-human-bot-prepare` and `run_human_bot_seed_sweep.py --seeds 5101,5102` with artifact `artifacts/mp/20260506-065043-human-bot-seed-sweep`.
- After correcting the Addressables address for GUID `b9ad52bb50cfab741bb309010e961c5b` to `MonData_Boss_Elemental`, reserializing the group asset, and rebuilding, `artifacts/builds/20260506-065615/MDF-MPTest.exe` passed `run_human_bot_prepare_progression.py --seed 1001` with artifact `artifacts/mp/20260506-065647-human-bot-prepare` and `run_human_bot_seed_sweep.py --seeds 5101,5102` with artifact `artifacts/mp/20260506-065810-human-bot-seed-sweep`.
