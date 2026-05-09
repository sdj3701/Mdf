# MDF Multiplayer Harness Scripts

Codex should maintain these scripts as the current multiplayer harness surface.

Current scripts:

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
- `run_human_bot_seed_sweep.py` - repeated HumanBot prepare progression across diagnostic seeds with random-aware outcome summaries.
- `run_human_bot_3round_progression.py` - bounded 3-round HumanBot progression.
- `run_human_bot_game_to_end.py` - explicit opt-in GameOver or timeout/stall endurance classification.
- `run_3round_reconnect.py`, `run_3round_disconnect_ai_takeover.py`, `run_3round_host_migration.py` - lifecycle checks after a 3-round checkpoint.

Battle command-specific scripts default to `--bot-prepare-mode augment-only` so the HumanBot can take a first augment without entering expensive maze wall planning before battle command assertions. `run_human_bot_battle_progression.py` defaults to `full` prepare for Prepare v2 rechecks; pass `--bot-prepare-mode augment-only` when the test should isolate battle command sync only.
Post-battle lifecycle scripts progress without `--mpFreezeGameFlow`, then call the `--mpTest` automation endpoint `/test/freezeGameFlow` immediately after the battle checkpoint so disconnect/reconnect/Host Migration assertions compare a stable durable state.
- `run_production_negative_automation.py` - normal non-development build must not expose automation `/ping`.
- `run_matrix.py` - orchestrate selected cases and profiles. Use `--list-cases`, `--list-profiles`, and `--dry-run` before adding a matrix invocation to automation.

Matrix profiles:

- `--profile smoke` - cheap Editor/Build smoke: editor-host-build-client, build-host-editor-client, build-host-build-client.
- `--profile regression` - smoke plus AI fill, disconnect takeover, same-token reconnect, four-player smoke, and HumanBot prepare.
- `--profile battle` - battle spawn, magic scroll, and HumanBot battle progression.
- `--profile lifecycle` - progressed reconnect, disconnect, and Host Migration after battle.
- `--profile long` - bounded 3-round HumanBot progression.
- `--profile long-lifecycle` - reconnect, disconnect/AI takeover, and Host Migration after a 3-round checkpoint.
- `--profile random-aware` - HumanBot prepare/4p, progressed lifecycle, and HumanBot seed sweep.
- `--profile full-regression` - regression, battle, lifecycle, and long progression; use for merge or large-change coverage.
- `--profile nightly` - regression, battle, lifecycle, battle seed sweep, and HumanBot seed sweep.
- `--profile endurance` - explicit opt-in GameOver or bounded timeout/stall classification.

`--case all` is backward-compatible and still means the existing default subset, not nightly.
`nightly` is not `full-regression`; it intentionally excludes `long` and `endurance`.

Artifacts should go under `artifacts/mp/<timestamp>-<case>/` and include command transcript, stdout/stderr, `[MPTEST]` timeline, snapshots, screenshots, and failure summary.

## Windows Firewall Prompt

Windows may show "Allow this app to communicate on public and private networks" for `MDF-MPTest.exe`. If every build uses a timestamped path, Windows treats each player as a different app and can prompt again.

For repeated local visual MP checks, build to a stable ignored path and reuse that player:

```powershell
python tools/harness/mp/build_player.py --output-dir artifacts/builds/mptest-current
python tools/harness/mp/run_human_bot_3round_progression.py --player-path artifacts/builds/mptest-current/MDF-MPTest.exe --no-headless-player
```

To suppress the prompt without opening inbound access, create a Windows Firewall block-inbound rule for that stable exe from an elevated Administrator shell:

```powershell
python tools/harness/mp/configure_windows_firewall.py --player-path artifacts/builds/mptest-current/MDF-MPTest.exe --apply
```

The helper defaults to `action=block` and `profile=any`. Use `--action allow --profile private` only for an explicit LAN/direct-connect test that needs inbound traffic.
