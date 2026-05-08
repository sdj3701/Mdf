from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log
from common import (
    failure_summary,
    free_port,
    hash_for_log,
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)
from long_progression_common import (
    checkpoint_key,
    completion_satisfied,
    dump_state,
    game,
    mptest_failures,
    progression_metrics,
    safe_request,
    unique_player_ids,
    wait_checkpoint_comparison,
    write_final_timeline,
)
from progressed_human_bot_common import local_player
from run_human_bot_3round_progression import (
    bot_args as long_bot_args,
    collect_bot_status,
    start_bot,
    wait_session_states,
    wait_stable_states,
)
from battle_progression_common import (
    poll_host_migration_after_battle,
    wait_battle_takeover,
    wait_reconnect_after_battle,
)


EXPECTED_PLAYERS = 2
EVENT_RECONNECT = "reconnect"
EVENT_DISCONNECT = "disconnect-ai-takeover"
EVENT_HOST_MIGRATION = "host-migration"


def add_long_lifecycle_args(parser: argparse.ArgumentParser, *, default_seed: int) -> None:
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--target-round", type=int, default=3)
    parser.add_argument("--completion-mode", choices=["round-complete", "battle2-reached"], default="round-complete")
    parser.add_argument("--max-duration-seconds", type=int, default=900)
    parser.add_argument("--checkpoint-every-state", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--checkpoint-every-round", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-max-commands", type=int, default=500)
    parser.add_argument(
        "--bot-prepare-mode",
        choices=["augment-only", "skip", "full"],
        default="augment-only",
    )
    parser.add_argument("--host-human-bot", action="store_true")
    parser.add_argument("--seed", type=int, default=default_seed)
    parser.add_argument("--headless-player", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=90)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--checkpoint-timeout", type=int, default=25)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    parser.add_argument("--poll-interval-seconds", type=float, default=1.0)
    parser.add_argument("--takeover-timeout", type=int, default=120)
    parser.add_argument("--reconnect-timeout", type=int, default=180)
    parser.add_argument("--host-migration-timeout", type=int, default=180)
    parser.add_argument("--stable-samples", type=int, default=2)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")


def validate_long_lifecycle_args(args: argparse.Namespace) -> None:
    if args.target_round < 1:
        raise SystemExit("--target-round must be >= 1")
    if args.max_duration_seconds <= 0:
        raise SystemExit("--max-duration-seconds must be > 0")
    if args.stable_samples < 1:
        raise SystemExit("--stable-samples must be >= 1")


def disable_ai_fill_args() -> list[str]:
    return ["--mpDisableAiFill"]


def bot_runtime_args(args: argparse.Namespace, persona: str, seed: int, journal_path: pathlib.Path) -> list[str]:
    return disable_ai_fill_args() + long_bot_args(args, persona, seed, journal_path)


def player_id_from_local(snapshot: Any) -> int:
    player = local_player(snapshot)
    player_id = player.get("playerId") if isinstance(player, dict) else None
    return player_id if isinstance(player_id, int) else -1


def freeze_long_checkpoint(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    args: argparse.Namespace,
    *,
    host_human_bot: bool,
    reason: str,
    client_peer: str = "build-client",
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    if host_human_bot:
        write_json(artifact_dir / "build-host-bot-stop-before-lifecycle.json", safe_request(lambda: host.bot_stop(reason=reason)))
    else:
        write_json(artifact_dir / "build-host-bot-stop-before-lifecycle.json", {
            "success": True,
            "message": "host HumanBot disabled; observer peer",
        })
    write_json(artifact_dir / f"{client_peer}-bot-stop-before-lifecycle.json", safe_request(lambda: client.bot_stop(reason=reason)))
    write_json(artifact_dir / "build-host-freeze-game-flow.json", safe_request(lambda: host.freeze_game_flow(True, reason=reason)))
    write_json(artifact_dir / f"{client_peer}-freeze-game-flow.json", safe_request(lambda: client.freeze_game_flow(True, reason=reason)))

    deadline = time.time() + args.checkpoint_timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    stable_matches = 0
    freeze_ok = False
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "pre-lifecycle-latest")
        client_state = dump_state(client, artifact_dir, client_peer, "pre-lifecycle-latest")
        host_ready = snapshot_ready(host_state, EXPECTED_PLAYERS, args.scene)
        client_ready = snapshot_ready(client_state, EXPECTED_PLAYERS, args.scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        completion_ok, completion_reason = completion_satisfied(
            host_state,
            [],
            target_round=args.target_round,
            completion_mode=args.completion_mode,
            allow_early_game_over=False,
        )
        ready = host_ready and client_ready and completion_ok and comparison.get("success") is True
        write_json(artifact_dir / "pre-lifecycle-freeze-wait-latest.json", {
            "ready": ready,
            "completionOk": completion_ok,
            "completionReason": completion_reason,
            "hostGame": game(host_state),
            "clientGame": game(client_state),
            "hostReasons": snapshot_not_ready_reasons(host_state, EXPECTED_PLAYERS, args.scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, EXPECTED_PLAYERS, args.scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        if ready:
            stable_matches += 1
            if stable_matches >= args.stable_samples:
                freeze_ok = True
                break
        else:
            stable_matches = 0
        time.sleep(1)

    write_json(artifact_dir / "snapshots" / "build-host-pre-lifecycle.json", host_state)
    write_json(artifact_dir / "snapshots" / f"{client_peer}-pre-lifecycle.json", client_state)
    write_json(artifact_dir / "pre-lifecycle-comparison.json", comparison)
    return host_state, client_state, comparison, freeze_ok


def migration_proof_from_result(result: dict[str, Any]) -> dict[str, Any]:
    migration = result.get("hostMigration") if isinstance(result, dict) else {}
    migration = migration if isinstance(migration, dict) else {}
    errors: list[str] = []
    if (migration.get("onHostMigrationCount") or 0) <= 0:
        errors.append("onHostMigrationCount_not_advanced")
    if (migration.get("nonNullTokenCount") or 0) <= 0:
        errors.append("nonNullTokenCount_not_advanced")
    if (migration.get("resumeCount") or 0) <= 0:
        errors.append("resumeCount_not_advanced")
    if (migration.get("startGameSuccessCount") or 0) <= 0:
        errors.append("startGameSuccessCount_not_advanced")
    if (migration.get("completeCount") or 0) <= 0:
        errors.append("completeCount_not_advanced")
    if migration.get("recoverySucceeded") is not True:
        errors.append(f"recoverySucceeded_not_true actual={migration.get('recoverySucceeded')}")
    if (migration.get("failureCount") or 0) > 0:
        errors.append(f"failureCount={migration.get('failureCount')}")
    return {
        "success": not errors,
        "errors": errors,
        "migration": migration,
    }


def host_migration_smoke_failures(artifact_dir: pathlib.Path) -> list[str]:
    failures: list[str] = []
    for path in sorted(artifact_dir.glob("*.Player.log")):
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        for line in lines:
            if "[HM-SMOKE] FAIL" in line or "handler_migration_complete_fail" in line:
                failures.append(f"{path.name}:{line.strip()}")
    return failures


def write_lifecycle_summary(
    artifact_dir: pathlib.Path,
    *,
    case_name: str,
    args: argparse.Namespace,
    event_type: str,
    checkpoints: list[dict[str, Any]],
    pre_comparison: dict[str, Any],
    metrics: dict[str, Any],
) -> None:
    write_json(artifact_dir / "checkpoint-summary.json", {
        "case": case_name,
        "eventType": event_type,
        "targetRound": args.target_round,
        "completionMode": args.completion_mode,
        "checkpoints": checkpoints,
        "preLifecycle": {
            "host": "snapshots/build-host-pre-lifecycle.json",
            "client": "snapshots/build-client-a-pre-lifecycle.json",
            "comparison": "pre-lifecycle-comparison.json",
            "success": pre_comparison.get("success") is True,
        },
        "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
        "statesReached": metrics.get("summary", {}).get("statesReached"),
    })


def run_long_lifecycle_case(
    args: argparse.Namespace,
    *,
    case_name: str,
    event_type: str,
) -> int:
    validate_long_lifecycle_args(args)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    max_players = 3 if event_type == EVENT_RECONNECT else EXPECTED_PLAYERS
    artifact_dir = make_artifact_dir(case_name, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hbot3life")
    host_token = new_token()
    client_a_token = new_token()
    client_b_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    client_connection_hash = hash_for_log(client_connection)
    host_port = free_port()
    client_a_port = free_port()
    client_b_port = free_port()
    host_bot_seed = args.seed + 100
    client_bot_seed = args.seed + 101
    host_journal_path = artifact_dir / "build-host-bot.jsonl"
    client_a_journal_path = artifact_dir / "build-client-a-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_a_proc: PlayerProcess | None = None
    client_b_proc: PlayerProcess | None = None
    host_was_killed = False
    client_a_was_killed = False
    failures: list[str] = []
    functional_failures: list[str] = []
    checkpoints: list[dict[str, Any]] = []
    checkpoint_keys_seen: set[tuple[int, str]] = set()
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        [host_token, client_a_token, client_b_token, host_connection, client_connection],
    )

    write_json(artifact_dir / "run.json", {
        "case": case_name,
        "eventType": event_type,
        "session": session,
        "playerPath": str(player_path),
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "maxPlayers": max_players,
        "expectedPlayers": EXPECTED_PLAYERS,
        "seed": args.seed,
        "hostBotSeed": host_bot_seed,
        "clientBotSeed": client_bot_seed,
        "targetRound": args.target_round,
        "completionMode": args.completion_mode,
        "maxDurationSeconds": args.max_duration_seconds,
        "botPersona": args.bot_persona,
        "botMaxCommands": args.bot_max_commands,
        "botPrepareMode": args.bot_prepare_mode,
        "hostHumanBot": args.host_human_bot,
        "headlessPlayer": args.headless_player,
        "clientConnectionTokenHash": client_connection_hash,
        "dryRun": args.dry_run,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
        "orphanPressure": orphan_gate,
    })

    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")
        cleanup_report = {
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "cleanupSuccess": False,
            "orphanedPids": [
                proc.get("pid")
                for proc in orphan_gate.get("processes", [])
                if isinstance(proc, dict) and isinstance(proc.get("pid"), int)
            ],
        }
        result = {
            "case": case_name,
            "artifactDir": str(artifact_dir),
            "success": False,
            "functionalSuccess": False,
            "cleanupSuccess": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "orphanPressure": orphan_gate,
            "headlessPlayer": args.headless_player,
            "failures": failures,
        }
        write_json(artifact_dir / "long-lifecycle-result.json", result)
        write_json(artifact_dir / "result.json", result)
        failure_summary(artifact_dir / "failure-summary.md", f"{case_name} blocked", failures)
        print(json.dumps({
            "artifactDir": str(artifact_dir),
            "success": False,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "failures": failures,
        }, indent=2))
        return 2

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": case_name, "dryRun": True}, indent=2))
        return 0

    final_host: dict[str, Any] = {}
    final_client: dict[str, Any] = {}
    pre_host: dict[str, Any] = {}
    pre_client: dict[str, Any] = {}
    pre_comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    event_result: dict[str, Any] = {"success": False, "errors": ["not_started"]}
    completion_ok = False
    completion_reason = "not_started"

    try:
        host_extra_args = disable_ai_fill_args()
        if args.host_human_bot:
            host_extra_args = bot_runtime_args(args, args.bot_persona, host_bot_seed, host_journal_path)
        host_proc = launch_player(
            player_path,
            "host",
            session,
            host_port,
            host_token,
            host_connection,
            artifact_dir,
            "build-host",
            max_players=max_players,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario=case_name,
            extra_args=host_extra_args,
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")
        if args.host_human_bot:
            write_json(artifact_dir / "build-host-bot-paused.json", safe_request(lambda: host.bot_stop(reason="long_lifecycle_pre_checkpoint_pause")))
        else:
            write_json(artifact_dir / "build-host-bot-paused.json", {
                "success": True,
                "message": "host HumanBot disabled; observer peer",
            })

        client_a_proc = launch_player(
            player_path,
            "client",
            session,
            client_a_port,
            client_a_token,
            client_connection,
            artifact_dir,
            "build-client-a",
            max_players=max_players,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario=case_name,
            extra_args=bot_runtime_args(args, args.bot_persona, client_bot_seed, client_a_journal_path),
            headless_player=args.headless_player,
        )
        client_a = AutomationClient(client_a_port, client_a_token, timeout=args.request_timeout)
        client_a_ping = client_a.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-a-ping.json", client_a_ping)
        if not client_a_ping.get("success"):
            failures.append("client_a_automation_ping_timeout")
        write_json(artifact_dir / "build-client-a-bot-paused.json", safe_request(lambda: client_a.bot_stop(reason="long_lifecycle_pre_checkpoint_pause")))

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, max_players, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client_a.join, artifact_dir, "build-client-a", session, args.lobby_scene, max_players, args.start_timeout):
            failures.append("client_a_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(host, client_a, artifact_dir, args.lobby_timeout, args.lobby_scene)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-a-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_before, client_before, before_ready = wait_stable_states(
            host,
            client_a,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-long-lifecycle-bot",
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-long-lifecycle-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-a-before-long-lifecycle-bot.json", client_before)
        if not before_ready:
            failures.append("before_long_lifecycle_bot_state_ready_timeout")

        before_key = checkpoint_key(host_before)
        if before_key is not None:
            checkpoint_keys_seen.add(before_key)
            checkpoints.append(wait_checkpoint_comparison(
                host,
                client_a,
                artifact_dir,
                index=len(checkpoints) + 1,
                round_number=before_key[0],
                current_state=before_key[1],
                expected_players=EXPECTED_PLAYERS,
                scene=args.scene,
                timeout=args.checkpoint_timeout,
                client_peer="build-client-a",
            ))

        bot_peers = [
            ("build-client-a", client_a, args.bot_persona, client_bot_seed, client_a_journal_path),
        ]
        if args.host_human_bot:
            bot_peers.insert(0, ("build-host", host, args.bot_persona, host_bot_seed, host_journal_path))

        for peer, automation, persona, bot_seed, journal in bot_peers:
            lifecycle_stop_round = args.target_round + 1 if args.completion_mode == "round-complete" else 0
            start_result = start_bot(
                automation,
                artifact_dir,
                peer,
                persona=persona,
                seed=bot_seed,
                duration_seconds=args.max_duration_seconds,
                stop_at_round=lifecycle_stop_round,
                max_commands=args.bot_max_commands,
                bot_prepare_mode=args.bot_prepare_mode,
                journal_path=journal,
            )
            if start_result.get("success") is not True:
                failures.append(f"{peer}_bot_start_failed")

        deadline = time.time() + args.max_duration_seconds
        poll_interval = max(0.5, args.poll_interval_seconds)
        while time.time() < deadline and not failures:
            final_host = dump_state(host, artifact_dir, "build-host", "long-lifecycle-latest")
            final_client = dump_state(client_a, artifact_dir, "build-client-a", "long-lifecycle-latest")
            host_key = checkpoint_key(final_host)
            client_key = checkpoint_key(final_client)
            if host_key is not None and host_key == client_key and host_key not in checkpoint_keys_seen:
                checkpoint_keys_seen.add(host_key)
                checkpoints.append(wait_checkpoint_comparison(
                    host,
                    client_a,
                    artifact_dir,
                    index=len(checkpoints) + 1,
                    round_number=host_key[0],
                    current_state=host_key[1],
                    expected_players=EXPECTED_PLAYERS,
                    scene=args.scene,
                    timeout=args.checkpoint_timeout,
                    client_peer="build-client-a",
                ))

            completion_ok, completion_reason = completion_satisfied(
                final_host,
                checkpoints,
                target_round=args.target_round,
                completion_mode=args.completion_mode,
                allow_early_game_over=False,
            )
            write_json(artifact_dir / "long-lifecycle-progress-wait-latest.json", {
                "completionOk": completion_ok,
                "completionReason": completion_reason,
                "hostGame": game(final_host),
                "clientGame": game(final_client),
                "checkpointCount": len(checkpoints),
                "deadlineSecondsRemaining": max(0, deadline - time.time()),
            })
            if completion_ok:
                break
            time.sleep(poll_interval)

        if not final_host:
            final_host = dump_state(host, artifact_dir, "build-host", "pre-lifecycle-final")
        if not final_client:
            final_client = dump_state(client_a, artifact_dir, "build-client-a", "pre-lifecycle-final")

        final_key = checkpoint_key(final_host)
        if final_key is not None and final_key == checkpoint_key(final_client) and final_key not in checkpoint_keys_seen:
            checkpoint_keys_seen.add(final_key)
            checkpoints.append(wait_checkpoint_comparison(
                host,
                client_a,
                artifact_dir,
                index=len(checkpoints) + 1,
                round_number=final_key[0],
                current_state=final_key[1],
                expected_players=EXPECTED_PLAYERS,
                scene=args.scene,
                timeout=args.checkpoint_timeout,
                client_peer="build-client-a",
            ))

        completion_ok, completion_reason = completion_satisfied(
            final_host,
            checkpoints,
            target_round=args.target_round,
            completion_mode=args.completion_mode,
            allow_early_game_over=False,
        )
        if not completion_ok:
            failures.append(completion_reason)
        failed_checkpoints = [checkpoint for checkpoint in checkpoints if checkpoint.get("success") is not True]
        if failed_checkpoints:
            failures.extend(f"checkpoint_failed:{checkpoint.get('checkpointId')}" for checkpoint in failed_checkpoints)

        pre_host, pre_client, pre_comparison, freeze_ok = freeze_long_checkpoint(
            host,
            client_a,
            artifact_dir,
            args,
            host_human_bot=args.host_human_bot,
            reason=f"{case_name}_pre_event_checkpoint",
            client_peer="build-client-a",
        )
        if not freeze_ok:
            failures.append("pre_lifecycle_checkpoint_freeze_timeout")
            failures.extend(f"pre_lifecycle_checkpoint:{err}" for err in pre_comparison.get("errors") or ["failed"])
        if not unique_player_ids(pre_host):
            failures.append("pre_lifecycle_host_player_ids_not_unique")
        if not unique_player_ids(pre_client):
            failures.append("pre_lifecycle_client_player_ids_not_unique")

        host_status = collect_bot_status(host, artifact_dir, "build-host", "pre-lifecycle") if args.host_human_bot else {
            "success": True,
            "data": {
                "enabled": False,
                "running": False,
                "commandsIssued": 0,
                "stopReason": "observer_peer",
            },
        }
        client_status = collect_bot_status(client_a, artifact_dir, "build-client-a", "pre-lifecycle")
        write_json(artifact_dir / "bot-status-summary.json", {
            "host": host_status,
            "clientA": client_status,
        })

        if not failures and event_type in {EVENT_DISCONNECT, EVENT_RECONNECT}:
            target_player_id = player_id_from_local(pre_client)
            client_local = local_player(pre_client)
            write_json(artifact_dir / "client-lifecycle-target.json", {
                "playerId": target_player_id,
                "connectionTokenHash": client_local.get("connectionTokenHash") if isinstance(client_local, dict) else "unknown",
                "expectedConnectionTokenHash": client_connection_hash,
            })
            if target_player_id < 0:
                failures.append("target_player_id_invalid")

            if client_a_proc is not None and client_a_proc.process.poll() is None:
                client_a_proc.process.kill()
                client_a_proc.process.wait(timeout=10)
                client_a_proc.close_logs()
                client_a_was_killed = True

            if target_player_id >= 0:
                host_takeover, takeover_assertions, takeover_ok = wait_battle_takeover(
                    host,
                    artifact_dir,
                    pre_host,
                    target_player_id,
                    args.takeover_timeout,
                    args.scene,
                    EXPECTED_PLAYERS,
                )
                write_json(artifact_dir / "snapshots" / "build-host-post-disconnect-ai-takeover.json", host_takeover)
                write_json(artifact_dir / "post-3round-disconnect-ai-takeover-assertions.json", takeover_assertions)
                write_json(artifact_dir / "long-preservation-assertions.json", takeover_assertions.get("battlePreservation"))
                event_result = takeover_assertions
                if not takeover_ok:
                    failures.append("post_3round_disconnect_ai_takeover_timeout")
                    failures.extend(takeover_assertions.get("errors") or [])

            if event_type == EVENT_RECONNECT and not failures and target_player_id >= 0:
                client_b_proc = launch_player(
                    player_path,
                    "client",
                    session,
                    client_b_port,
                    client_b_token,
                    client_connection,
                    artifact_dir,
                    "build-client-b",
                    max_players=max_players,
                    scene=args.lobby_scene,
                    case_name=case_name,
                    auto_start=False,
                    load_game=False,
                    seed=args.seed + 2,
                    scenario=case_name,
                    extra_args=disable_ai_fill_args() + ["--mpFreezeGameFlow"],
                    headless_player=args.headless_player,
                )
                client_b = AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout)
                client_b_ping = client_b.wait_ping(timeout_seconds=args.ping_timeout)
                write_json(artifact_dir / "build-client-b-ping.json", client_b_ping)
                if not client_b_ping.get("success"):
                    failures.append("client_b_automation_ping_timeout")

                if not wait_build_peer_started(client_b.join, artifact_dir, "build-client-b", session, args.scene, max_players, args.start_timeout):
                    failures.append("client_b_rejoin_timeout")

                host_final, client_final, reconnect_assertions, reconnect_ok = wait_reconnect_after_battle(
                    host,
                    client_b,
                    artifact_dir,
                    pre_host,
                    target_player_id,
                    client_connection_hash,
                    args.reconnect_timeout,
                    args.scene,
                    EXPECTED_PLAYERS,
                )
                write_json(artifact_dir / "snapshots" / "build-host-post-reconnect.json", host_final)
                write_json(artifact_dir / "snapshots" / "build-client-b-post-reconnect.json", client_final)
                write_json(artifact_dir / "post-3round-reconnect-assertions.json", reconnect_assertions)
                write_json(artifact_dir / "full-comparison.json", reconnect_assertions.get("fullComparison"))
                write_json(artifact_dir / "long-preservation-assertions.json", reconnect_assertions.get("battlePreservation"))
                event_result = reconnect_assertions
                if not reconnect_ok:
                    failures.append("post_3round_same_token_reconnect_timeout")
                    failures.extend(reconnect_assertions.get("errors") or [])

        if not failures and event_type == EVENT_HOST_MIGRATION:
            killed_host_player_id = player_id_from_local(pre_host)
            survivor_player_id = player_id_from_local(pre_client)
            write_json(artifact_dir / "host-migration-role-targets.json", {
                "killedHostPlayerId": killed_host_player_id,
                "survivorPlayerId": survivor_player_id,
                "hostLocal": local_player(pre_host),
                "survivorLocal": local_player(pre_client),
            })
            if killed_host_player_id < 0:
                failures.append("killed_host_player_id_invalid")
            if survivor_player_id < 0:
                failures.append("survivor_player_id_invalid")

            if not failures and host_proc is not None and host_proc.process.poll() is None:
                host_pid = host_proc.process.pid
                host_proc.process.kill()
                host_proc.process.wait(timeout=15)
                host_proc.close_logs()
                host_was_killed = True
                write_json(artifact_dir / "host-killed.json", {
                    "pid": host_pid,
                    "exitCode": host_proc.process.returncode,
                    "method": "process.kill",
                })
            elif not failures:
                failures.append("host_process_not_running_before_kill")

            if not failures:
                post_migration_snapshot, migration_result, migration_ok = poll_host_migration_after_battle(
                    client_a,
                    artifact_dir,
                    pre_client,
                    killed_host_player_id,
                    survivor_player_id,
                    args.host_migration_timeout,
                    args.scene,
                )
                proof = migration_proof_from_result(migration_result)
                write_json(artifact_dir / "snapshots" / "build-client-post-host-migration.json", post_migration_snapshot)
                write_json(artifact_dir / "host-migration-proof.json", proof)
                write_json(artifact_dir / "post-3round-host-migration-result.json", migration_result)
                write_json(artifact_dir / "long-preservation-assertions.json", migration_result.get("battlePreservation"))
                event_result = migration_result
                if not migration_ok:
                    failures.append("post_3round_host_migration_failed")
                    failures.extend(migration_result.get("errors") or [])
                failures.extend(f"host_migration_proof:{err}" for err in proof.get("errors") or [])

        if args.headless_player:
            skipped_screenshot = {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            }
            write_json(artifact_dir / "build-host-screenshot.json", skipped_screenshot if not host_was_killed else {
                "success": False,
                "error": {"code": "host_process_killed_for_migration"},
            })
            write_json(artifact_dir / "build-client-a-screenshot.json", skipped_screenshot if not client_a_was_killed else {
                "success": False,
                "error": {"code": "client_a_process_killed_for_lifecycle"},
            })
            if client_b_proc is not None:
                write_json(artifact_dir / "build-client-b-screenshot.json", skipped_screenshot)
        else:
            if not host_was_killed:
                write_json(artifact_dir / "build-host-screenshot.json", safe_request(host.screenshot))
            else:
                write_json(artifact_dir / "build-host-screenshot.json", {
                    "success": False,
                    "error": {"code": "host_process_killed_for_migration"},
                })
            if not client_a_was_killed:
                write_json(artifact_dir / "build-client-a-screenshot.json", safe_request(client_a.screenshot))
            else:
                write_json(artifact_dir / "build-client-a-screenshot.json", {
                    "success": False,
                    "error": {"code": "client_a_process_killed_for_lifecycle"},
                })
            if client_b_proc is not None:
                write_json(artifact_dir / "build-client-b-screenshot.json", safe_request(AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout).screenshot))

        host_logs = safe_request(host.logs_recent) if not host_was_killed else {"success": False, "data": {"lines": []}}
        client_a_logs = safe_request(client_a.logs_recent) if not client_a_was_killed else {"success": False, "data": {"lines": []}}
        client_b_logs = (
            safe_request(AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout).logs_recent)
            if client_b_proc is not None
            else {"success": True, "data": {"lines": []}}
        )
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        write_json(artifact_dir / "build-client-a-logs-recent.json", client_a_logs)
        write_json(artifact_dir / "build-client-b-logs-recent.json", client_b_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_a_logs, client_b_logs))
    finally:
        functional_failures = list(failures)
        cleanup_processes = [proc for proc in (client_b_proc, client_a_proc, host_proc) if proc is not None]
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            cleanup_processes,
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(functional_failures),
            strict_cleanup=args.strict_cleanup,
        )
        if cleanup_report.get("cleanupStatus") != "PASS":
            failures.append(f"cleanup_failed:{cleanup_report.get('cleanupStatus')}")

        collect_player_log(artifact_dir, "build-host-or-last")
        write_final_timeline(artifact_dir, ["build-host", "build-client-a", "build-client-b"])
        failures.extend(
            f"host_migration_smoke_failure:{line}"
            for line in host_migration_smoke_failures(artifact_dir)
        )
        functional_failures = list(failures)

        metrics = progression_metrics(artifact_dir, checkpoints, cleanup_report)
        write_json(artifact_dir / "bot-metrics-summary.json", metrics)
        write_lifecycle_summary(
            artifact_dir,
            case_name=case_name,
            args=args,
            event_type=event_type,
            checkpoints=checkpoints,
            pre_comparison=pre_comparison,
            metrics=metrics,
        )

        functional_success = not functional_failures and event_result.get("success") is True
        cleanup_success = cleanup_report.get("cleanupSuccess") is True
        result = {
            "case": case_name,
            "artifactDir": str(artifact_dir),
            "success": functional_success and cleanup_report.get("cleanupStatus") == "PASS",
            "functionalSuccess": functional_success,
            "cleanupSuccess": cleanup_success,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupReportPath": "cleanup-report.json",
            "eventType": event_type,
            "targetRound": args.target_round,
            "completionMode": args.completion_mode,
            "completion": {
                "success": completion_ok,
                "reason": completion_reason,
                "hostGame": game(pre_host or final_host),
                "clientGame": game(pre_client or final_client),
            },
            "preLifecycleComparison": pre_comparison,
            "eventResult": event_result,
            "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
            "statesReached": metrics.get("summary", {}).get("statesReached"),
            "checkpointSummaryPath": "checkpoint-summary.json",
            "botMetricsSummaryPath": "bot-metrics-summary.json",
            "headlessPlayer": args.headless_player,
            "failures": failures,
        }
        write_json(artifact_dir / "long-lifecycle-result.json", result)
        write_json(artifact_dir / "result.json", result)
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{case_name} failed", failures)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "success": not failures and cleanup_report.get("cleanupStatus") == "PASS",
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "eventType": event_type,
        "maxRoundReached": (progression_metrics(artifact_dir, checkpoints, cleanup_report).get("summary") or {}).get("maxRoundReached"),
        "completionReason": completion_reason,
        "failures": failures,
    }, indent=2))
    return 0 if not failures and cleanup_report.get("cleanupStatus") == "PASS" else 1
