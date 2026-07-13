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
    normalize_snapshot_response,
    scene_matches,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    unity_cli_json,
    wait_unity_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report


CASE_NAME = "editor-host-build-client"
KING_SELECTION_SCENE = "JoinLobby"
EDITOR_PLAYER_ID = 0
BUILD_PLAYER_ID = 1


def dump_editor_state(artifact_dir: pathlib.Path, label: str) -> dict:
    data = unity_cli_json(["mp_dump_state", "--role", "editor-host", "--case_name", CASE_NAME], artifact_dir, timeout=60)
    write_json(artifact_dir / "snapshots" / f"editor-{label}.json", data)
    return data


def dump_build_state(client: AutomationClient, artifact_dir: pathlib.Path, label: str) -> dict:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"build-client-{label}.json", data)
    return data


def wait_session_states(client: AutomationClient, artifact_dir: pathlib.Path, expected_players: int, scene: str, timeout: int) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    editor_state: dict = {}
    build_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        editor_state = dump_editor_state(artifact_dir, "lobby-latest")
        build_state = dump_build_state(client, artifact_dir, "lobby-latest")
        editor_ready = session_ready(editor_state, expected_players, scene)
        build_ready = session_ready(build_state, expected_players, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "editorReady": editor_ready,
            "buildReady": build_ready,
            "editorReasons": session_not_ready_reasons(editor_state, expected_players, scene),
            "buildReasons": session_not_ready_reasons(build_state, expected_players, scene),
            "stableMatches": stable_matches,
        })
        if editor_ready and build_ready:
            stable_matches += 1
            if stable_matches >= 2:
                return editor_state, build_state, True
        else:
            stable_matches = 0
        time.sleep(1)
    return editor_state, build_state, False


def wait_states(client: AutomationClient, artifact_dir: pathlib.Path, expected_players: int, scene: str, timeout: int) -> tuple[dict, dict, bool]:
    deadline = time.time() + timeout
    editor_state: dict = {}
    build_state: dict = {}
    stable_matches = 0
    while time.time() < deadline:
        editor_state = dump_editor_state(artifact_dir, "latest")
        build_state = dump_build_state(client, artifact_dir, "latest")
        editor_ready = snapshot_ready(editor_state, expected_players, scene)
        build_ready = snapshot_ready(build_state, expected_players, scene)
        comparison = compare_snapshots(editor_state, build_state) if editor_ready and build_ready else {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
        write_json(artifact_dir / "state-wait-latest.json", {
            "editorReady": editor_ready,
            "buildReady": build_ready,
            "editorReasons": snapshot_not_ready_reasons(editor_state, expected_players, scene),
            "buildReasons": snapshot_not_ready_reasons(build_state, expected_players, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", comparison)
        if editor_ready and build_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return editor_state, build_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return editor_state, build_state, False


def king_command_payload(response: object) -> dict | None:
    if not isinstance(response, dict):
        return None
    wrapped = response.get("data")
    if response.get("success") is True and isinstance(wrapped, dict):
        return wrapped
    if response.get("command") == "select_king":
        return response
    return None


def king_command_response_ok(response: object, expected_player_id: int) -> bool:
    data = king_command_payload(response)
    return (
        isinstance(data, dict)
        and data.get("command") == "select_king"
        and data.get("playerId") == expected_player_id
        and isinstance(data.get("kingKeyHash"), int)
    )


def failed_king_command(reason: str) -> dict:
    return {
        "success": False,
        "message": reason,
        "error": {"code": "king_selection_not_executed", "details": reason},
    }


def execute_king_selection_commands(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    editor_king: str | None,
    build_king: str | None,
) -> dict[str, dict]:
    results: dict[str, dict] = {}
    if editor_king:
        try:
            response = unity_cli_json([
                "mp_command",
                "--command",
                "select_king",
                "--player_id",
                str(EDITOR_PLAYER_ID),
                "--king_key",
                editor_king,
            ], artifact_dir, timeout=60)
        except Exception as exc:
            response = {
                "success": False,
                "message": "Editor select_king request failed",
                "error": {"code": type(exc).__name__, "details": str(exc)},
            }
        results["editor"] = {
            "playerId": EDITOR_PLAYER_ID,
            "requestedKing": editor_king,
            "response": response,
        }

    if build_king:
        try:
            response = client.command(
                name="select_king",
                playerId=BUILD_PLAYER_ID,
                kingKey=build_king,
            )
        except Exception as exc:
            response = {
                "success": False,
                "message": "Build select_king request failed",
                "error": {"code": type(exc).__name__, "details": str(exc)},
            }
        results["build"] = {
            "playerId": BUILD_PLAYER_ID,
            "requestedKing": build_king,
            "response": response,
        }

    write_json(artifact_dir / "king-selection-commands.json", results)
    return results


def load_king_selection_lobby(artifact_dir: pathlib.Path, current_lobby_scene: str) -> dict:
    if scene_matches(current_lobby_scene, KING_SELECTION_SCENE):
        result = {
            "success": True,
            "skipped": True,
            "message": "JoinLobby is already the active lobby scene; network scene reload skipped.",
            "data": {
                "currentLobbyScene": current_lobby_scene,
                "targetScene": KING_SELECTION_SCENE,
                "sceneAliasMatched": True,
            },
        }
    else:
        result = unity_cli_json(
            ["mp_load_game", "--scene", KING_SELECTION_SCENE],
            artifact_dir,
            timeout=60,
        )
    write_json(artifact_dir / "editor-load-king-lobby.json", result)
    return result


def skipped_king_selection_commands(editor_king: str | None, build_king: str | None, reason: str) -> dict[str, dict]:
    results: dict[str, dict] = {}
    if editor_king:
        results["editor"] = {
            "playerId": EDITOR_PLAYER_ID,
            "requestedKing": editor_king,
            "response": failed_king_command(reason),
        }
    if build_king:
        results["build"] = {
            "playerId": BUILD_PLAYER_ID,
            "requestedKing": build_king,
            "response": failed_king_command(reason),
        }
    return results


def snapshot_player(snapshot: object, player_id: int) -> dict | None:
    normalized = normalize_snapshot_response(snapshot)
    if not isinstance(normalized, dict):
        return None
    for player in normalized.get("players") or []:
        if isinstance(player, dict) and player.get("playerId") == player_id:
            return player
    return None


def verify_king_selection_snapshots(
    editor_snapshot: object,
    build_snapshot: object,
    commands: dict[str, dict],
) -> dict:
    checks: dict[str, dict] = {}
    errors: list[str] = []
    for owner, command in commands.items():
        player_id = command.get("playerId")
        response = command.get("response")
        response_data = king_command_payload(response)
        expected_hash = response_data.get("kingKeyHash") if isinstance(response_data, dict) else None
        response_ok = (
            isinstance(player_id, int)
            and king_command_response_ok(response, player_id)
        )
        editor_player = snapshot_player(editor_snapshot, player_id) if isinstance(player_id, int) else None
        build_player = snapshot_player(build_snapshot, player_id) if isinstance(player_id, int) else None
        editor_hash = editor_player.get("selectedKingUnitKeyHash") if isinstance(editor_player, dict) else None
        build_hash = build_player.get("selectedKingUnitKeyHash") if isinstance(build_player, dict) else None
        editor_matches = response_ok and editor_hash == expected_hash
        build_matches = response_ok and build_hash == expected_hash
        same_player_hash = (
            response_ok
            and editor_player is not None
            and build_player is not None
            and editor_hash == build_hash == expected_hash
        )
        owner_errors: list[str] = []
        if not response_ok:
            owner_errors.append("select_king_command_response_invalid")
        if not editor_matches:
            owner_errors.append("editor_snapshot_king_hash_mismatch")
        if not build_matches:
            owner_errors.append("build_snapshot_king_hash_mismatch")
        if not same_player_hash:
            owner_errors.append("same_player_king_hash_not_replicated")
        errors.extend(f"{owner}:{error}" for error in owner_errors)
        checks[owner] = {
            "playerId": player_id,
            "requestedKing": command.get("requestedKing"),
            "responseAccepted": response_ok,
            "expectedHashFromResponse": expected_hash,
            "editorHash": editor_hash,
            "buildHash": build_hash,
            "editorMatchesResponse": editor_matches,
            "buildMatchesResponse": build_matches,
            "samePlayerHashOnBothPeers": same_player_hash,
            "errors": owner_errors,
        }

    return {
        "enabled": True,
        "success": bool(checks) and not errors,
        "joinLobbyScene": KING_SELECTION_SCENE,
        "checks": checks,
        "errors": errors,
        "artifacts": {
            "commands": "king-selection-commands.json",
            "editorJoinLobby": "snapshots/editor-king-lobby.json",
            "buildJoinLobby": "snapshots/build-client-king-lobby.json",
            "editorGame": "snapshots/editor-pre.json",
            "buildGame": "snapshots/build-client-pre.json",
        },
    }


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    editor_king = getattr(args, "editor_king", None)
    build_king = getattr(args, "build_king", None)
    verify_king_selection = bool(editor_king or build_king)
    effective_lobby_scene = KING_SELECTION_SCENE if verify_king_selection else args.lobby_scene
    if args.dry_run:
        dry_run = {
            "case": CASE_NAME,
            "dryRun": True,
            "playerPath": args.player_path,
            "headlessPlayer": args.headless_player,
        }
        if verify_king_selection:
            dry_run["kingSelection"] = {
                "editorKing": editor_king,
                "buildKing": build_king,
                "joinLobbyScene": KING_SELECTION_SCENE,
            }
        write_json(artifact_dir / "run.json", dry_run)
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    session = args.session or new_session("ehbc")
    token = new_token()
    connection_token = new_token()
    port = free_port()
    build_proc: PlayerProcess | None = None
    king_selection_commands: dict[str, dict] = {}
    king_selection_verification: dict | None = None
    failures: list[str] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
        "headlessPlayer": args.headless_player,
    }

    run_config = {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "buildAutomationPort": port,
        "expectedPlayers": 2,
        "scene": args.scene,
        "lobbyScene": effective_lobby_scene,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
    }
    if verify_king_selection:
        run_config["kingSelection"] = {
            "editorKing": editor_king,
            "buildKing": build_king,
            "joinLobbyScene": KING_SELECTION_SCENE,
        }
    write_json(artifact_dir / "run.json", run_config)

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    try:
        unity_cli_json(["console", "--clear"], artifact_dir, timeout=30)
        unity_cli_json(["editor", "play", "--wait"], artifact_dir, timeout=120)
        if not wait_unity_ready(artifact_dir, timeout_seconds=60):
            failures.append("editor_not_ready_after_play")
        build_proc = launch_player(
            player_path=player_path,
            role="client",
            session=session,
            port=port,
            token=token,
            connection_token=connection_token,
            artifact_dir=artifact_dir,
            peer_name="build-client",
            max_players=2,
            scene=effective_lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="game_smoke",
            headless_player=args.headless_player,
        )
        client = AutomationClient(port, token, timeout=5.0)
        ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", ping)
        if not ping.get("success"):
            failures.append("automation_ping_timeout")

        host_start = unity_cli_json([
            "mp_start_host",
            "--session",
            session,
            "--scene",
            effective_lobby_scene,
            "--max_players",
            "2",
            "--timeout_ms",
            str(args.editor_timeout_ms),
        ], artifact_dir, timeout=120)
        write_json(artifact_dir / "editor-host-start.json", host_start)
        if not host_start:
            failures.append("editor_host_start_failed")

        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, effective_lobby_scene, 2, args.start_timeout):
            failures.append("build_client_join_timeout")

        editor_lobby, build_lobby, lobby_ready = wait_session_states(client, artifact_dir, 2, effective_lobby_scene, args.lobby_timeout)
        write_json(artifact_dir / "snapshots" / "editor-lobby.json", editor_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-lobby.json", build_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        if verify_king_selection:
            king_lobby_load = load_king_selection_lobby(artifact_dir, effective_lobby_scene)
            if not isinstance(king_lobby_load, dict) or king_lobby_load.get("success") is not True:
                failures.append("king_selection_join_lobby_load_failed")

            editor_king_lobby, build_king_lobby, king_lobby_ready = wait_session_states(
                client,
                artifact_dir,
                2,
                KING_SELECTION_SCENE,
                args.lobby_timeout,
            )
            write_json(artifact_dir / "snapshots" / "editor-king-lobby.json", editor_king_lobby)
            write_json(artifact_dir / "snapshots" / "build-client-king-lobby.json", build_king_lobby)
            if not king_lobby_ready:
                failures.append("king_selection_join_lobby_timeout")
                king_selection_commands = skipped_king_selection_commands(
                    editor_king,
                    build_king,
                    "JoinLobby did not become ready on both peers.",
                )
                write_json(artifact_dir / "king-selection-commands.json", king_selection_commands)
            else:
                king_selection_commands = execute_king_selection_commands(
                    client,
                    artifact_dir,
                    editor_king,
                    build_king,
                )
                for owner, command in king_selection_commands.items():
                    if not king_command_response_ok(command.get("response"), command.get("playerId")):
                        failures.append(f"{owner}_king_selection_command_failed")
                # RequestKingSelection uses the production InputAuthority -> StateAuthority RPC.
                # Give Fusion time to commit the lobby value before unloading JoinLobby.
                time.sleep(1.0)

        load_result = unity_cli_json(["mp_load_game", "--scene", args.scene], artifact_dir, timeout=60)
        write_json(artifact_dir / "editor-load-game.json", load_result)
        if not load_result:
            failures.append("editor_load_game_failed")

        editor_pre, build_pre, ready = wait_states(client, artifact_dir, 2, args.scene, args.state_timeout)
        write_json(artifact_dir / "snapshots" / "editor-pre.json", editor_pre)
        write_json(artifact_dir / "snapshots" / "build-client-pre.json", build_pre)
        if not ready:
            failures.append("state_ready_timeout")

        if verify_king_selection:
            king_selection_verification = verify_king_selection_snapshots(
                editor_pre,
                build_pre,
                king_selection_commands,
            )
            write_json(artifact_dir / "king-selection-verification.json", king_selection_verification)
            if king_selection_verification.get("success") is not True:
                failures.append("king_selection_snapshot_mismatch")
            command_result = {
                "success": king_selection_verification.get("success") is True,
                "kind": "king_selection",
                "commands": "king-selection-commands.json",
                "verification": "king-selection-verification.json",
            }
        else:
            command_result = {"success": False, "skipped": True, "reason": "no safe durable command before Phase 17"}
        write_json(artifact_dir / "command-result.json", command_result)

        editor_post = dump_editor_state(artifact_dir, "post")
        build_post = dump_build_state(client, artifact_dir, "post")
        comparison = compare_snapshots(editor_post, build_post)
        write_json(artifact_dir / "comparison.json", comparison)
        if not comparison["success"]:
            failures.append("snapshot_mismatch")

        if args.headless_player:
            write_json(artifact_dir / "editor-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
            write_json(artifact_dir / "build-client-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
        else:
            unity_cli_json(["mp_screenshot", "--view", "game", "--output_path", str(artifact_dir / "screenshots" / "editor.png")], artifact_dir, timeout=60)
            write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        write_json(artifact_dir / "build-client-logs-recent.json", client.logs_recent())

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
        copied = collect_player_log(artifact_dir, "build-client-or-last")
        logs = [artifact_dir / "build-client.stdout.log", artifact_dir / "build-client.stderr.log"]
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    result = {
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
    }
    if verify_king_selection:
        result["kingSelectionVerification"] = king_selection_verification
    write_json(artifact_dir / "result.json", result)
    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=1001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--editor-timeout-ms", type=int, default=30000)
    parser.add_argument("--headless-player", action="store_true")
    parser.add_argument(
        "--editor-king",
        help="Select this canonical UnitData_King_* key for the Editor host in JoinLobby and verify it after Game load.",
    )
    parser.add_argument(
        "--build-king",
        help="Select this canonical UnitData_King_* key for the Build client in JoinLobby and verify it after Game load.",
    )
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
