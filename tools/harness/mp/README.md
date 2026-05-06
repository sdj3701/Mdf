# MDF Multiplayer Harness Scripts

Codex should implement and maintain these scripts during Phase 10+.

Planned files:

- `common.py` - ports, tokens, artifact dirs, and session helpers.
- `launch_player.py` - launch or kill built players and capture stdout/stderr.
- `automation_client.py` - call build-side automation server endpoints.
- `build_player.py` - build or reuse player builds. Development Build is the default.
- `compare_state_snapshots.py` - compare durable MDF state only.
- `parse_mptest_logs.py` - parse `[MPTEST]` timelines.
- `collect_artifacts.py` - collect logs, screenshots, snapshots, and transcripts.
- `run_editor_host_build_client.py` - E2E MVP case A.
- `run_build_host_editor_client.py` - E2E MVP case B.
- `run_build_host_build_client.py` - build-build case.
- `run_ai_fill_smoke.py` - AI fill if supported.
- `run_disconnect_ai_takeover.py` - disconnect/takeover if supported.
- `run_same_token_reconnect.py` - reconnect if supported.
- `run_four_player_smoke.py` - 4-player smoke.
- `run_host_migration_probe.py` - feasibility probe.
- `run_host_migration_e2e.py` - E2E only after feasibility passes.
- `run_durable_command_soak.py` - repeated durable command consistency.
- `run_battle_spawn_monster_command.py` - battle spawn command sync and monster snapshot coverage.
- `run_magic_scroll_command.py` - magic scroll command sync and scroll/effect snapshot coverage.
- `run_human_bot_battle_progression.py` - HumanBot battle progression through shared decision policies. The client peer observes by default; pass `--client-human-bot` only when the case specifically needs both peers driving bot decisions.
- `run_progressed_host_migration_after_battle.py` - post-battle Host Migration harness entrypoint with frozen battle checkpoint preservation checks.
- `run_progressed_reconnect_after_battle.py` - post-battle client drop, AI takeover, same-token reconnect, and full-world comparison.
- `run_progressed_disconnect_after_battle.py` - post-battle client drop and AI takeover preservation check.
- `run_battle_seed_sweep.py` - repeated HumanBot battle progression across diagnostic seeds with random-aware outcome summaries.

Battle command scripts default to `--bot-prepare-mode augment-only` so the HumanBot can take a first augment without entering expensive maze wall planning before the battle command assertions. Use `--bot-prepare-mode full` only when prepare behavior itself is under test.
Post-battle lifecycle scripts progress without `--mpFreezeGameFlow`, then call the `--mpTest` automation endpoint `/test/freezeGameFlow` immediately after the battle checkpoint so disconnect/reconnect/Host Migration assertions compare a stable durable state.
- `run_production_negative_automation.py` - normal non-development build must not expose automation `/ping`.
- `run_matrix.py` - orchestrate selected cases.

Artifacts should go under `artifacts/mp/<timestamp>-<case>/` and include command transcript, stdout/stderr, `[MPTEST]` timeline, snapshots, screenshots, and failure summary.
