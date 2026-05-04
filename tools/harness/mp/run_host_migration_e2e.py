#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import failure_summary, free_port, latest_player_path, make_artifact_dir, new_session, new_token, normalize_snapshot_response, write_json
from launch_player import PlayerProcess, launch_player
from run_host_migration_probe import read_host_migration_config


CASE_NAME = "host-migration-e2e"


def snapshot_body(snapshot: dict[str, Any]) -> dict[str, Any]:
    state = normalize_snapshot_response(snapshot)
    return state if isinstance(state, dict) else {}


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def snapshot_ready(snapshot: dict[str, Any], expected_players: int, scene: str) -> bool:
    state = snapshot_body(snapshot)
    return (
        state.get("scene") == scene
        and len(state.get("players") or []) == expected_players
        and (state.get("game") or {}).get("hasGameManagers") is True
    )


def wait_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "latest")
        client_state = dump_state(client, artifact_dir, "survivor-client", "latest")
        if snapshot_ready(host_state, 2, scene) and snapshot_ready(client_state, 2, scene):
            return host_state, client_state, True
        time.sleep(2)
    return host_state, client_state, False


def wait_post_migration(client: AutomationClient, artifact_dir: pathlib.Path, timeout: int) -> dict[str, Any]:
    deadline = time.time() + timeout
    latest: dict[str, Any] = {}
    while time.time() < deadline:
        latest = dump_state(client, artifact_dir, "survivor-client", "post-migration-latest")
        migration = (snapshot_body(latest).get("hostMigration") or {})
        if migration.get("completeCount", 0) > 0 or migration.get("failureCount", 0) > 0:
            return latest
        time.sleep(0.5)
    return latest


def players_by_id(snapshot: dict[str, Any]) -> dict[int, dict[str, Any]]:
    players: dict[int, dict[str, Any]] = {}
    for player in snapshot_body(snapshot).get("players") or []:
        player_id = player.get("playerId")
        if isinstance(player_id, int):
            players[player_id] = player
    return players


def field_value(player: dict[str, Any], key: str) -> Any:
    field = player.get("field") or {}
    return field.get(key)


def shop_value(player: dict[str, Any], key: str) -> Any:
    shop = player.get("shop") or {}
    return shop.get(key)


def assert_migration_proof(failures: list[str], post: dict[str, Any]) -> None:
    migration = snapshot_body(post).get("hostMigration") or {}
    if migration.get("onHostMigrationCount", 0) <= 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:on_host_migration_not_observed")
    if migration.get("nonNullTokenCount", 0) <= 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:host_migration_token_not_observed")
    if migration.get("startGameSuccessCount", 0) <= 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:token_start_game_success_not_observed")
    if migration.get("resumeCount", 0) <= 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:host_migration_resume_not_observed")
    if migration.get("completeCount", 0) <= 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:migration_complete_not_observed")
    if migration.get("failureCount", 0) > 0:
        failures.append("HOST_MIGRATION_E2E_FAIL:migration_failure_event_observed")
    if migration.get("recoverySucceeded") is not True:
        failures.append("HOST_MIGRATION_E2E_FAIL:recovery_success_not_observed")


def assert_durable_state(failures: list[str], pre: dict[str, Any], post: dict[str, Any]) -> dict[str, Any]:
    pre_state = snapshot_body(pre)
    post_state = snapshot_body(post)
    report: dict[str, Any] = {"checks": []}

    post_runner = post_state.get("runner") or {}
    if post_runner.get("gameMode") != "Host" or post_runner.get("isServer") is not True:
        failures.append("HOST_MIGRATION_E2E_FAIL:survivor_not_promoted_to_host")

    post_game = post_state.get("game") or {}
    pre_game = pre_state.get("game") or {}
    if post_game.get("hasGameManagers") is not True:
        failures.append("HOST_MIGRATION_E2E_FAIL:game_managers_missing_after_migration")
    for key in ("currentRound", "currentState"):
        if pre_game.get(key) != post_game.get(key):
            failures.append(f"HOST_MIGRATION_E2E_FAIL:game_{key}_changed")

    pre_players = players_by_id(pre)
    post_players = players_by_id(post)
    if len(post_players) != len(pre_players):
        failures.append("HOST_MIGRATION_E2E_FAIL:player_count_changed")
    if len(post_players) != len(set(post_players.keys())):
        failures.append("HOST_MIGRATION_E2E_FAIL:duplicate_player_id")
    if not any(player.get("isConnected") is True and player.get("isAI") is False for player in post_players.values()):
        failures.append("HOST_MIGRATION_E2E_FAIL:no_connected_human_survivor_after_migration")

    for player_id, before in sorted(pre_players.items()):
        after = post_players.get(player_id)
        if after is None:
            failures.append(f"HOST_MIGRATION_E2E_FAIL:player_{player_id}_missing_after_migration")
            continue

        per_player: dict[str, Any] = {"playerId": player_id, "mismatches": []}
        for key in ("health", "gold", "wallCount"):
            if before.get(key) != after.get(key):
                per_player["mismatches"].append({
                    "field": key,
                    "pre": before.get(key),
                    "post": after.get(key),
                })
                failures.append(f"HOST_MIGRATION_E2E_FAIL:player_{player_id}_{key}_changed")

        for key in ("gridHash", "wallHash", "placedUnitsHash"):
            if field_value(before, key) != field_value(after, key):
                per_player["mismatches"].append({
                    "field": f"field.{key}",
                    "pre": field_value(before, key),
                    "post": field_value(after, key),
                })
                failures.append(f"HOST_MIGRATION_E2E_FAIL:player_{player_id}_field_{key}_changed")

        for key in ("round", "count", "itemsHash"):
            if shop_value(before, key) != shop_value(after, key):
                per_player["mismatches"].append({
                    "field": f"shop.{key}",
                    "pre": shop_value(before, key),
                    "post": shop_value(after, key),
                })
                failures.append(f"HOST_MIGRATION_E2E_FAIL:player_{player_id}_shop_{key}_changed")

        report["checks"].append(per_player)

    return report


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hme2e")
    config = read_host_migration_config()
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    host_was_killed = False
    failures: list[str] = []

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientPort": client_port,
        "scene": args.scene,
        "config": config,
        "dryRun": args.dry_run,
    })
    if not config["enableAutoUpdate"]:
        failures.append("NEEDS_PROJECT_SUPPORT:host_migration_auto_update_disabled")

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True, "config": config}, indent=2))
        return 0

    try:
        host_proc = launch_player(
            player_path,
            "host",
            session,
            host_port,
            host_token,
            host_connection,
            artifact_dir,
            "build-host",
            max_players=2,
            scene=args.scene,
            case_name=CASE_NAME,
            auto_start=True,
            load_game=True,
            seed=args.seed,
            scenario="host_migration_e2e",
        )
        host = AutomationClient(host_port, host_token)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")

        client_proc = launch_player(
            player_path,
            "client",
            session,
            client_port,
            client_token,
            client_connection,
            artifact_dir,
            "survivor-client",
            max_players=2,
            scene=args.scene,
            case_name=CASE_NAME,
            auto_start=True,
            load_game=True,
            seed=args.seed + 1,
            scenario="host_migration_e2e",
        )
        client = AutomationClient(client_port, client_token)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "survivor-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

        host_pre, client_pre, ready = wait_ready(host, client, artifact_dir, args.state_timeout, args.scene)
        write_json(artifact_dir / "snapshots" / "build-host-pre.json", host_pre)
        write_json(artifact_dir / "snapshots" / "survivor-client-pre.json", client_pre)
        if not ready:
            failures.append("pre_migration_state_ready_timeout")

        if host_proc is not None and host_proc.process.poll() is None:
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
        else:
            failures.append("host_process_not_running_before_kill")

        post = wait_post_migration(client, artifact_dir, args.migration_timeout)
        write_json(artifact_dir / "snapshots" / "survivor-client-post-migration.json", post)
        assert_migration_proof(failures, post)
        durable_report = assert_durable_state(failures, client_pre, post)

        write_json(artifact_dir / "durable-state-report.json", durable_report)
        write_json(artifact_dir / "survivor-client-logs-recent.json", client.logs_recent())
        write_json(artifact_dir / "survivor-client-screenshot.json", client.screenshot())
        write_json(artifact_dir / "host-migration-e2e-result.json", {
            "hostWasKilled": host_was_killed,
            "config": config,
            "migration": (snapshot_body(post).get("hostMigration") or {}),
            "failures": failures,
        })

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        if client_proc is not None:
            try:
                automation = AutomationClient(client_port, client_token, timeout=2.0)
                write_json(artifact_dir / "survivor-client-quit.json", automation.quit())
                client_proc.process.wait(timeout=10)
                client_proc.close_logs()
            except Exception:
                client_proc.terminate()
        if host_proc is not None and not host_was_killed:
            host_proc.terminate()

        player_log = collect_player_log(artifact_dir, "host-migration-e2e-last")
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "survivor-client.stdout.log",
            artifact_dir / "survivor-client.stderr.log",
        ]
        if player_log:
            logs.append(player_log)
        write_timeline(artifact_dir, logs)

    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=7101)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--migration-timeout", type=int, default=90)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
