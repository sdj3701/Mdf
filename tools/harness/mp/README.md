# MDF Multiplayer Harness Scripts

Codex should implement these scripts during Phase 10+.

Planned files:

- `common.py` — ports/tokens/artifact dirs/session helpers.
- `launch_player.py` — launch/kill built players and capture stdout/stderr.
- `automation_client.py` — call build-side automation server endpoints.
- `build_player.py` — build/reuse Development player.
- `compare_state_snapshots.py` — compare durable MDF state only.
- `parse_mptest_logs.py` — parse `[MPTEST]` timeline.
- `collect_artifacts.py` — collect logs, screenshots, snapshots, transcripts.
- `run_editor_host_build_client.py` — E2E MVP case A.
- `run_build_host_editor_client.py` — E2E MVP case B.
- `run_build_host_build_client.py` — build-build case.
- `run_ai_fill_smoke.py` — AI fill if supported.
- `run_disconnect_ai_takeover.py` — disconnect/takeover if supported.
- `run_same_token_reconnect.py` — reconnect if supported.
- `run_four_player_smoke.py` — 4-player smoke.
- `run_host_migration_probe.py` — feasibility probe.
- `run_host_migration_e2e.py` — E2E only after feasibility passes.
- `run_durable_command_soak.py` — repeated durable command consistency.
- `run_matrix.py` — orchestrate selected cases.

Artifacts should go under `artifacts/mp/<timestamp>-<case>/` and include command transcript, stdout/stderr, `[MPTEST]` timeline, snapshots, screenshots, and failure summary.
