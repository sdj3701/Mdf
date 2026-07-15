#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import (
    failure_summary,
    free_port,
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report


CASE_NAME = "build-host-build-client"


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def wait_session_states(host: AutomationClient, client: AutomationClient, artifact_dir: pathlib.Path, timeout: int, scene: str) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    host_state: dict = {}
    client_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "lobby-latest")
        client_state = dump_state(client, artifact_dir, "build-client", "lobby-latest")
        host_ready = session_ready(host_state, 2, scene)
        client_ready = session_ready(client_state, 2, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": session_not_ready_reasons(host_state, 2, scene),
            "clientReasons": session_not_ready_reasons(client_state, 2, scene),
            "stableMatches": stable_matches,
        })
        if host_ready and client_ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(1)
    return host_state, client_state, False


def wait_states(host: AutomationClient, client: AutomationClient, artifact_dir: pathlib.Path, timeout: int, scene: str) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    host_state: dict = {}
    client_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "latest")
        client_state = dump_state(client, artifact_dir, "build-client", "latest")
        host_ready = snapshot_ready(host_state, 2, scene)
        client_ready = snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
        write_json(artifact_dir / "state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, 2, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, 2, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", comparison)
        if host_ready and client_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, False


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    if args.dry_run:
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "dryRun": True,
            "playerPath": args.player_path,
            "headlessPlayer": args.headless_player,
        })
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    session = args.session or new_session("bhbc")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    failures: list[str] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
        "headlessPlayer": args.headless_player,
    }

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientPort": client_port,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
    })
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
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
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="game_smoke",
            headless_player=args.headless_player,
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
            "build-client",
            max_players=2,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario="game_smoke",
            headless_player=args.headless_player,
        )
        client = AutomationClient(client_port, client_token)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(host, client, artifact_dir, args.lobby_timeout, args.lobby_scene)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_pre, client_pre, ready = wait_states(host, client, artifact_dir, args.state_timeout, args.scene)
        write_json(artifact_dir / "snapshots" / "build-host-pre.json", host_pre)
        write_json(artifact_dir / "snapshots" / "build-client-pre.json", client_pre)
        if not ready:
            failures.append("state_ready_timeout")

        # Do not compare two one-shot dumps that may land on opposite sides of a
        # sequence transition. Require the replicated state to converge twice.
        host_post, client_post, post_ready = wait_states(
            host,
            client,
            artifact_dir,
            args.state_timeout,
            args.scene,
        )
        write_json(artifact_dir / "snapshots" / "build-host-post.json", host_post)
        write_json(artifact_dir / "snapshots" / "build-client-post.json", client_post)
        comparison = compare_snapshots(host_post, client_post)
        write_json(artifact_dir / "comparison.json", comparison)
        if not post_ready:
            failures.append("post_state_ready_timeout")
        if not comparison["success"]:
            failures.append("snapshot_mismatch")

        if args.headless_player:
            skipped_screenshot = {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            }
            write_json(artifact_dir / "build-host-screenshot.json", skipped_screenshot)
            write_json(artifact_dir / "build-client-screenshot.json", skipped_screenshot)
        else:
            write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
            write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        write_json(artifact_dir / "build-host-logs-recent.json", host.logs_recent())
        write_json(artifact_dir / "build-client-logs-recent.json", client.logs_recent())

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc for proc in (client_proc, host_proc) if proc is not None],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=15.0,
        )
        host_log = collect_player_log(artifact_dir, "build-host-or-last")
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client.stdout.log",
            artifact_dir / "build-client.stderr.log",
        ]
        if host_log:
            logs.append(host_log)
        write_timeline(artifact_dir, logs)

    write_json(artifact_dir / "result.json", {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "success": (not failures) and cleanup_report.get("cleanupSuccess") is True,
        "functionalSuccess": not failures,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
        "cleanupReportPath": "cleanup-report.json",
        "orphanedPids": cleanup_report.get("orphanedPids") or [],
        "headlessPlayer": args.headless_player,
        "failures": failures,
    })
    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=3001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
