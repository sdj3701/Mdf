#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import failure_summary, free_port, latest_player_path, make_artifact_dir, new_session, new_token, session_not_ready_reasons, session_ready, snapshot_not_ready_reasons, snapshot_ready, unity_cli_json, wait_build_peer_started, wait_unity_ready, write_json
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report


CASE_NAME = "build-host-editor-client"


def dump_editor_state(artifact_dir: pathlib.Path, label: str) -> dict:
    data = unity_cli_json(["mp_dump_state", "--role", "editor-client", "--case_name", CASE_NAME], artifact_dir, timeout=60)
    write_json(artifact_dir / "snapshots" / f"editor-client-{label}.json", data)
    return data


def dump_build_state(client: AutomationClient, artifact_dir: pathlib.Path, label: str) -> dict:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"build-host-{label}.json", data)
    return data


def wait_session_states(client: AutomationClient, artifact_dir: pathlib.Path, expected_players: int, scene: str, timeout: int) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    editor_state: dict = {}
    build_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        build_state = dump_build_state(client, artifact_dir, "lobby-latest")
        editor_state = dump_editor_state(artifact_dir, "lobby-latest")
        build_ready = session_ready(build_state, expected_players, scene)
        editor_ready = session_ready(editor_state, expected_players, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "buildReady": build_ready,
            "editorReady": editor_ready,
            "buildReasons": session_not_ready_reasons(build_state, expected_players, scene),
            "editorReasons": session_not_ready_reasons(editor_state, expected_players, scene),
            "stableMatches": stable_matches,
        })
        if build_ready and editor_ready:
            stable_matches += 1
            if stable_matches >= 2:
                return build_state, editor_state, True
        else:
            stable_matches = 0
        time.sleep(1)
    return build_state, editor_state, False


def wait_states(client: AutomationClient, artifact_dir: pathlib.Path, expected_players: int, scene: str, timeout: int) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    editor_state: dict = {}
    build_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        build_state = dump_build_state(client, artifact_dir, "latest")
        editor_state = dump_editor_state(artifact_dir, "latest")
        build_ready = snapshot_ready(build_state, expected_players, scene)
        editor_ready = snapshot_ready(editor_state, expected_players, scene)
        comparison = compare_snapshots(build_state, editor_state) if build_ready and editor_ready else {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
        write_json(artifact_dir / "state-wait-latest.json", {
            "buildReady": build_ready,
            "editorReady": editor_ready,
            "buildReasons": snapshot_not_ready_reasons(build_state, expected_players, scene),
            "editorReasons": snapshot_not_ready_reasons(editor_state, expected_players, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", comparison)
        if build_ready and editor_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return build_state, editor_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return build_state, editor_state, False


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

    session = args.session or new_session("bhec")
    token = new_token()
    connection_token = new_token()
    port = free_port()
    build_proc: PlayerProcess | None = None
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
        "buildAutomationPort": port,
        "expectedPlayers": 2,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
    })

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    try:
        build_proc = launch_player(
            player_path=player_path,
            role="host",
            session=session,
            port=port,
            token=token,
            connection_token=connection_token,
            artifact_dir=artifact_dir,
            peer_name="build-host",
            max_players=2,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="game_smoke",
            headless_player=args.headless_player,
        )
        client = AutomationClient(port, token, timeout=5.0)
        ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", ping)
        if not ping.get("success"):
            failures.append("automation_ping_timeout")

        if not wait_build_peer_started(client.start_host, artifact_dir, "build-host", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("build_host_start_timeout")

        unity_cli_json(["console", "--clear"], artifact_dir, timeout=30)
        unity_cli_json(["editor", "play", "--wait"], artifact_dir, timeout=120)
        if not wait_unity_ready(artifact_dir, timeout_seconds=60):
            failures.append("editor_not_ready_after_play")
        join = unity_cli_json([
            "mp_join_client",
            "--session",
            session,
            "--scene",
            args.lobby_scene,
            "--max_players",
            "2",
            "--timeout_ms",
            str(args.editor_timeout_ms),
        ], artifact_dir, timeout=120)
        write_json(artifact_dir / "editor-client-join.json", join)
        if not join:
            failures.append("editor_client_join_failed")

        build_lobby, editor_lobby, lobby_ready = wait_session_states(client, artifact_dir, 2, args.lobby_scene, args.lobby_timeout)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", build_lobby)
        write_json(artifact_dir / "snapshots" / "editor-client-lobby.json", editor_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = client.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("build_host_load_game_failed")

        build_pre, editor_pre, ready = wait_states(client, artifact_dir, 2, args.scene, args.state_timeout)
        write_json(artifact_dir / "snapshots" / "build-host-pre.json", build_pre)
        write_json(artifact_dir / "snapshots" / "editor-client-pre.json", editor_pre)
        if not ready:
            failures.append("state_ready_timeout")

        command_result = {"success": False, "skipped": True, "reason": "no safe durable command before Phase 17"}
        write_json(artifact_dir / "command-result.json", command_result)

        build_post = dump_build_state(client, artifact_dir, "post")
        editor_post = dump_editor_state(artifact_dir, "post")
        comparison = compare_snapshots(build_post, editor_post)
        write_json(artifact_dir / "comparison.json", comparison)
        if not comparison["success"]:
            failures.append("snapshot_mismatch")

        if args.headless_player:
            write_json(artifact_dir / "build-host-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
            write_json(artifact_dir / "editor-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
        else:
            write_json(artifact_dir / "build-host-screenshot.json", client.screenshot())
            unity_cli_json(["mp_screenshot", "--view", "game", "--output_path", str(artifact_dir / "screenshots" / "editor.png")], artifact_dir, timeout=60)
        write_json(artifact_dir / "build-host-logs-recent.json", client.logs_recent())

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc for proc in (build_proc,) if proc is not None],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=15.0,
        )
        unity_cli_json(["mp_stop"], artifact_dir, timeout=60)
        copied = collect_player_log(artifact_dir, "build-host-or-last")
        logs = [artifact_dir / "build-host.stdout.log", artifact_dir / "build-host.stderr.log"]
        if copied:
            logs.append(copied)
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
    parser.add_argument("--seed", type=int, default=2001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--editor-timeout-ms", type=int, default=30000)
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
