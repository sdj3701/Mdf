#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import failure_summary, free_port, latest_player_path, make_artifact_dir, new_session, new_token, normalize_snapshot_response, read_json, scene_matches, write_json, ROOT
from launch_player import PlayerProcess, launch_player


CASE_NAME = "host-migration-feasibility"
CONFIG_PATH = ROOT / "Mdfproject" / "Assets" / "Photon" / "Fusion" / "Resources" / "NetworkProjectConfig.fusion"


def read_host_migration_config() -> dict[str, Any]:
    data = read_json(CONFIG_PATH)
    host_migration = data.get("HostMigration") or {}
    return {
        "path": str(CONFIG_PATH),
        "enableAutoUpdate": bool(host_migration.get("EnableAutoUpdate")),
        "updateDelay": host_migration.get("UpdateDelay"),
    }


def snapshot_ready(snapshot: dict[str, Any], expected_players: int, scene: str) -> bool:
    state = normalize_snapshot_response(snapshot)
    return (
        isinstance(state, dict)
        and scene_matches(state.get("scene"), scene)
        and len(state.get("players") or []) == expected_players
        and (state.get("game") or {}).get("hasGameManagers") is True
    )


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


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


def host_migration_state(snapshot: dict[str, Any]) -> dict[str, Any]:
    state = normalize_snapshot_response(snapshot)
    if not isinstance(state, dict):
        return {}
    host_migration = state.get("hostMigration")
    return host_migration if isinstance(host_migration, dict) else {}


def wait_migration(client: AutomationClient, artifact_dir: pathlib.Path, timeout: int) -> dict[str, Any]:
    deadline = time.time() + timeout
    latest: dict[str, Any] = {}
    while time.time() < deadline:
        latest = dump_state(client, artifact_dir, "survivor-client", "post-migration-latest")
        migration = host_migration_state(latest)
        write_json(artifact_dir / "host-migration-latest.json", migration)
        if migration.get("completeCount", 0) > 0 or migration.get("failureCount", 0) > 0:
            return latest
        time.sleep(2)
    return latest


def append_probe_failures(failures: list[str], snapshot: dict[str, Any]) -> None:
    migration = host_migration_state(snapshot)
    if migration.get("onHostMigrationCount", 0) <= 0:
        failures.append("HOST_MIGRATION_NOT_PROVEN:on_host_migration_not_observed")
    if migration.get("nonNullTokenCount", 0) <= 0:
        failures.append("HOST_MIGRATION_NOT_PROVEN:host_migration_token_not_observed")
    if migration.get("startGameSuccessCount", 0) <= 0:
        failures.append("HOST_MIGRATION_NOT_PROVEN:token_start_game_success_not_observed")
    if migration.get("resumeCount", 0) <= 0:
        failures.append("HOST_MIGRATION_NOT_PROVEN:host_migration_resume_not_observed")
    if migration.get("completeCount", 0) <= 0:
        failures.append("HOST_MIGRATION_NOT_PROVEN:migration_complete_not_observed")
    if migration.get("recoverySucceeded") is not True:
        failures.append("HOST_MIGRATION_NOT_PROVEN:recovery_success_not_observed")


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hm")
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
            scenario="host_migration_probe",
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
            scenario="host_migration_probe",
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

        post = wait_migration(client, artifact_dir, args.migration_timeout)
        write_json(artifact_dir / "snapshots" / "survivor-client-post-migration.json", post)
        append_probe_failures(failures, post)

        write_json(artifact_dir / "survivor-client-logs-recent.json", client.logs_recent())
        write_json(artifact_dir / "survivor-client-screenshot.json", client.screenshot())
        write_json(artifact_dir / "host-migration-result.json", {
            "hostWasKilled": host_was_killed,
            "config": config,
            "migration": host_migration_state(post),
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

        player_log = collect_player_log(artifact_dir, "host-migration-last")
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
    parser.add_argument("--seed", type=int, default=7001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--migration-timeout", type=int, default=90)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
