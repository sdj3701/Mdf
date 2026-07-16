#!/usr/bin/env python3
"""Verify authority-owned permanent-wall stock, placement, sync, and refund."""
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any, Callable

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
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
    write_standard_result,
)
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)


CASE_NAME = "permanent-wall-placement"
EXPECTED_PLAYERS = 2


def state(snapshot: dict[str, Any]) -> dict[str, Any]:
    value = normalize_snapshot_response(snapshot)
    return value if isinstance(value, dict) else {}


def players_by_id(snapshot: dict[str, Any]) -> dict[int, dict[str, Any]]:
    result: dict[int, dict[str, Any]] = {}
    for player in state(snapshot).get("players") or []:
        if isinstance(player, dict) and isinstance(player.get("playerId"), int):
            result[int(player["playerId"])] = player
    return result


def nested(value: Any, *keys: str) -> Any:
    current = value
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def measurement(snapshot: dict[str, Any], player_id: int) -> dict[str, Any]:
    snapshot_state = state(snapshot)
    player = players_by_id(snapshot).get(player_id) or {}
    field = player.get("field") if isinstance(player.get("field"), dict) else {}
    game = snapshot_state.get("game") if isinstance(snapshot_state.get("game"), dict) else {}
    return {
        "playerId": player_id,
        "currentState": game.get("currentState"),
        "normalWallCount": player.get("wallCount"),
        "permanentWallPlacementCount": player.get("permanentWallPlacementCount"),
        # This excludes any structural/border permanent walls by design.
        "playerPlacedPermanentWallCount": field.get("playerPlacedPermanentWallCount"),
        "playerPlacedPermanentWallHash": field.get("playerPlacedPermanentWallHash"),
        "totalPermanentWallCount": field.get("permanentWallCount"),
        "wallHash": field.get("wallHash"),
        "fieldReady": field.get("ready"),
    }


def dump_state(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    peer: str,
    label: str,
) -> dict[str, Any]:
    snapshot = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", snapshot)
    return snapshot


def wait_for_lobby(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    scene: str,
    timeout_seconds: float,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    stable_samples = 0
    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", "lobby-latest")
        latest_client = dump_state(client, artifact_dir, "build-client", "lobby-latest")
        host_ready = session_ready(latest_host, EXPECTED_PLAYERS, scene)
        client_ready = session_ready(latest_client, EXPECTED_PLAYERS, scene)
        evidence = {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": session_not_ready_reasons(latest_host, EXPECTED_PLAYERS, scene),
            "clientReasons": session_not_ready_reasons(latest_client, EXPECTED_PLAYERS, scene),
            "stableSamples": stable_samples,
        }
        write_json(artifact_dir / "lobby-wait-latest.json", evidence)
        if host_ready and client_ready:
            stable_samples += 1
            if stable_samples >= 2:
                return latest_host, latest_client, True
        else:
            stable_samples = 0
        time.sleep(1)
    return latest_host, latest_client, False


def is_prepare_ready(snapshot: dict[str, Any], scene: str) -> bool:
    if not snapshot_ready(snapshot, EXPECTED_PLAYERS, scene):
        return False
    return nested(state(snapshot), "game", "currentState") == "Prepare"


def wait_for_prepare(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    scene: str,
    timeout_seconds: float,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    stable_samples = 0
    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", "prepare-latest")
        latest_client = dump_state(client, artifact_dir, "build-client", "prepare-latest")
        host_ready = is_prepare_ready(latest_host, scene)
        client_ready = is_prepare_ready(latest_client, scene)
        evidence = {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(latest_host, EXPECTED_PLAYERS, scene),
            "clientReasons": snapshot_not_ready_reasons(latest_client, EXPECTED_PLAYERS, scene),
            "hostState": nested(state(latest_host), "game", "currentState"),
            "clientState": nested(state(latest_client), "game", "currentState"),
            "stableSamples": stable_samples,
        }
        write_json(artifact_dir / "prepare-wait-latest.json", evidence)
        if host_ready and client_ready:
            stable_samples += 1
            if stable_samples >= 2:
                return latest_host, latest_client, True
        else:
            stable_samples = 0
        time.sleep(1)
    return latest_host, latest_client, False


def choose_target_player(snapshot: dict[str, Any]) -> int | None:
    players = players_by_id(snapshot)
    local_ids = sorted(
        player_id
        for player_id, player in players.items()
        if player.get("isLocal") is True
    )
    if local_ids:
        return local_ids[0]
    return min(players) if players else None


def evaluate_stage(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    player_id: int,
    expected_stock: int,
    expected_owned_count: int,
    expected_normal_count: int,
    expected_wall_hash: str | None = None,
    expected_owned_hash: str | None = None,
) -> dict[str, Any]:
    host_value = measurement(host_snapshot, player_id)
    client_value = measurement(client_snapshot, player_id)
    errors: list[str] = []

    exact_fields = (
        "normalWallCount",
        "permanentWallPlacementCount",
        "playerPlacedPermanentWallCount",
        "playerPlacedPermanentWallHash",
        "totalPermanentWallCount",
        "wallHash",
        "fieldReady",
        "currentState",
    )
    for field_name in exact_fields:
        if host_value.get(field_name) != client_value.get(field_name):
            errors.append(
                f"peer_mismatch.{field_name} "
                f"host={host_value.get(field_name)} client={client_value.get(field_name)}"
            )

    expected = {
        "permanentWallPlacementCount": expected_stock,
        "playerPlacedPermanentWallCount": expected_owned_count,
        "normalWallCount": expected_normal_count,
        "currentState": "Prepare",
        "fieldReady": True,
    }
    for side, value in (("host", host_value), ("client", client_value)):
        for field_name, expected_value in expected.items():
            if value.get(field_name) != expected_value:
                errors.append(
                    f"{side}.{field_name} expected={expected_value} actual={value.get(field_name)}"
                )
        if expected_wall_hash is not None and value.get("wallHash") != expected_wall_hash:
            errors.append(
                f"{side}.wallHash_not_restored expected={expected_wall_hash} "
                f"actual={value.get('wallHash')}"
            )
        if expected_owned_hash is not None and value.get("playerPlacedPermanentWallHash") != expected_owned_hash:
            errors.append(
                f"{side}.playerPlacedPermanentWallHash_not_restored "
                f"expected={expected_owned_hash} actual={value.get('playerPlacedPermanentWallHash')}"
            )

    return {
        "success": not errors,
        "errors": errors,
        "host": host_value,
        "client": client_value,
        "expected": expected,
        "expectedWallHash": expected_wall_hash,
        "expectedPlayerPlacedPermanentWallHash": expected_owned_hash,
    }


def wait_for_stage(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    label: str,
    timeout_seconds: float,
    evaluator: Callable[[dict[str, Any], dict[str, Any]], dict[str, Any]],
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    latest_evidence: dict[str, Any] = {"success": False, "errors": ["not_sampled"]}
    stable_samples = 0
    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        latest_client = dump_state(client, artifact_dir, "build-client", f"{label}-latest")
        latest_evidence = evaluator(latest_host, latest_client)
        write_json(
            artifact_dir / f"{label}-wait-latest.json",
            {"stableSamples": stable_samples, "evidence": latest_evidence},
        )
        if latest_evidence.get("success") is True:
            stable_samples += 1
            if stable_samples >= 2:
                return latest_host, latest_client, latest_evidence, True
        else:
            stable_samples = 0
        time.sleep(0.5)
    return latest_host, latest_client, latest_evidence, False


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(
        CASE_NAME,
        pathlib.Path(args.artifact_root) if args.artifact_root else None,
    )
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    failures: list[str] = []
    reports: dict[str, Any] = {}
    target_player_id: int | None = None
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None

    if args.dry_run:
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "playerPath": str(player_path) if player_path else None,
            "dryRun": True,
            "expectedPlayers": EXPECTED_PLAYERS,
            "headlessPlayer": args.headless_player,
        })
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    if player_path is None or not player_path.exists():
        failures.append("player_path_missing")
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {"cleanupStatus": "NEEDS_ENVIRONMENT", "cleanupSuccess": False, "orphanedPids": []},
            headless_player=args.headless_player,
        )
        print(json.dumps(result, indent=2))
        return 2

    session = args.session or new_session("permwall")
    host_token = new_token()
    client_token = new_token()
    host_connection_token = new_token()
    client_connection_token = new_token()
    host_port = free_port()
    client_port = free_port()

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientPort": client_port,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "expectedPlayers": EXPECTED_PLAYERS,
        "freezeGameFlow": True,
        "headlessPlayer": args.headless_player,
    })

    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
    )
    if orphan_gate.get("blocked"):
        failures.append("orphan_pressure_gate_blocked")
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {
                "cleanupStatus": "NEEDS_ENVIRONMENT",
                "cleanupSuccess": False,
                "orphanedPids": [
                    proc.get("pid")
                    for proc in orphan_gate.get("processes") or []
                    if isinstance(proc, dict)
                ],
            },
            headless_player=args.headless_player,
            extra={"orphanPressure": orphan_gate},
        )
        print(json.dumps(result, indent=2))
        return 2

    host: AutomationClient | None = None
    client: AutomationClient | None = None
    try:
        freeze_args = ["--mpFreezeGameFlow"]
        host_proc = launch_player(
            player_path,
            "host",
            session,
            host_port,
            host_token,
            host_connection_token,
            artifact_dir,
            "build-host",
            max_players=EXPECTED_PLAYERS,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="permanent_wall_placement",
            extra_args=freeze_args,
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if host_ping.get("success") is not True:
            failures.append("host_automation_ping_timeout")

        client_proc = launch_player(
            player_path,
            "client",
            session,
            client_port,
            client_token,
            client_connection_token,
            artifact_dir,
            "build-client",
            max_players=EXPECTED_PLAYERS,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario="permanent_wall_placement",
            extra_args=freeze_args,
            headless_player=args.headless_player,
        )
        client = AutomationClient(client_port, client_token)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if client_ping.get("success") is not True:
            failures.append("client_automation_ping_timeout")

        if not wait_build_peer_started(
            host.start_host,
            artifact_dir,
            "build-host",
            session,
            args.lobby_scene,
            EXPECTED_PLAYERS,
            args.start_timeout,
        ):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(
            client.join,
            artifact_dir,
            "build-client",
            session,
            args.lobby_scene,
            EXPECTED_PLAYERS,
            args.start_timeout,
        ):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_for_lobby(
            host,
            client,
            artifact_dir,
            args.lobby_scene,
            args.lobby_timeout,
        )
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_response = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_response)
        if load_response.get("success") is not True:
            failures.append("host_load_game_failed")

        host_prepare, client_prepare, prepare_ready = wait_for_prepare(
            host,
            client,
            artifact_dir,
            args.scene,
            args.state_timeout,
        )
        write_json(artifact_dir / "snapshots" / "build-host-prepare.json", host_prepare)
        write_json(artifact_dir / "snapshots" / "build-client-prepare.json", client_prepare)
        if not prepare_ready:
            failures.append("prepare_state_ready_timeout")

        host_freeze = host.freeze_game_flow(True, reason=CASE_NAME)
        client_freeze = client.freeze_game_flow(True, reason=CASE_NAME)
        write_json(artifact_dir / "build-host-freeze-game-flow.json", host_freeze)
        write_json(artifact_dir / "build-client-freeze-game-flow.json", client_freeze)
        if host_freeze.get("success") is not True:
            failures.append("host_freeze_game_flow_failed")
        if client_freeze.get("success") is not True:
            failures.append("client_freeze_game_flow_failed")

        target_player_id = choose_target_player(host_prepare)
        if target_player_id is None:
            failures.append("target_player_missing")
        else:
            initial_host = measurement(host_prepare, target_player_id)
            initial_client = measurement(client_prepare, target_player_id)
            normal_wall_count = initial_host.get("normalWallCount")
            if not isinstance(normal_wall_count, int):
                failures.append("initial_normal_wall_count_missing")
                normal_wall_count = -1

            baseline_evidence = evaluate_stage(
                host_prepare,
                client_prepare,
                target_player_id,
                expected_stock=0,
                expected_owned_count=0,
                expected_normal_count=normal_wall_count,
            )
            reports["baseline"] = baseline_evidence
            write_json(artifact_dir / "baseline-assertions.json", baseline_evidence)
            if baseline_evidence.get("success") is not True:
                failures.append("initial_permanent_wall_baseline_failed")

            baseline_wall_hash = initial_host.get("wallHash")
            baseline_owned_hash = initial_host.get("playerPlacedPermanentWallHash")

            grant_response = host.command(
                name="grant_permanent_walls",
                playerId=target_player_id,
                amount=3,
            )
            write_json(artifact_dir / "grant-permanent-walls-command.json", grant_response)
            if grant_response.get("success") is not True:
                failures.append("grant_permanent_walls_request_failed")
            else:
                grant_host, grant_client, grant_evidence, grant_ready = wait_for_stage(
                    host,
                    client,
                    artifact_dir,
                    "after-grant",
                    args.command_timeout,
                    lambda host_snapshot, client_snapshot: evaluate_stage(
                        host_snapshot,
                        client_snapshot,
                        target_player_id,
                        expected_stock=3,
                        expected_owned_count=0,
                        expected_normal_count=normal_wall_count,
                    ),
                )
                reports["grant"] = grant_evidence
                write_json(artifact_dir / "snapshots" / "build-host-after-grant.json", grant_host)
                write_json(artifact_dir / "snapshots" / "build-client-after-grant.json", grant_client)
                if not grant_ready:
                    failures.append("grant_permanent_walls_peer_agreement_timeout")

                place_response = host.command(
                    name="place_wall",
                    playerId=target_player_id,
                    wallKind="permanent",
                )
                write_json(artifact_dir / "place-permanent-wall-command.json", place_response)
                position = nested(place_response, "data", "position")
                if place_response.get("success") is not True or not isinstance(position, dict):
                    failures.append("place_permanent_wall_request_failed")
                else:
                    place_host, place_client, place_evidence, place_ready = wait_for_stage(
                        host,
                        client,
                        artifact_dir,
                        "after-place",
                        args.command_timeout,
                        lambda host_snapshot, client_snapshot: evaluate_stage(
                            host_snapshot,
                            client_snapshot,
                            target_player_id,
                            expected_stock=2,
                            expected_owned_count=1,
                            expected_normal_count=normal_wall_count,
                        ),
                    )
                    reports["place"] = place_evidence
                    write_json(artifact_dir / "snapshots" / "build-host-after-place.json", place_host)
                    write_json(artifact_dir / "snapshots" / "build-client-after-place.json", place_client)
                    if not place_ready:
                        failures.append("place_permanent_wall_peer_agreement_timeout")

                    remove_response = host.command(
                        name="remove_wall",
                        playerId=target_player_id,
                        position=position,
                    )
                    write_json(artifact_dir / "remove-permanent-wall-command.json", remove_response)
                    if remove_response.get("success") is not True:
                        failures.append("remove_permanent_wall_request_failed")
                    else:
                        remove_host, remove_client, remove_evidence, remove_ready = wait_for_stage(
                            host,
                            client,
                            artifact_dir,
                            "after-remove",
                            args.command_timeout,
                            lambda host_snapshot, client_snapshot: evaluate_stage(
                                host_snapshot,
                                client_snapshot,
                                target_player_id,
                                expected_stock=3,
                                expected_owned_count=0,
                                expected_normal_count=normal_wall_count,
                                expected_wall_hash=baseline_wall_hash if isinstance(baseline_wall_hash, str) else None,
                                expected_owned_hash=baseline_owned_hash if isinstance(baseline_owned_hash, str) else None,
                            ),
                        )
                        reports["remove"] = remove_evidence
                        write_json(artifact_dir / "snapshots" / "build-host-after-remove.json", remove_host)
                        write_json(artifact_dir / "snapshots" / "build-client-after-remove.json", remove_client)
                        if not remove_ready:
                            failures.append("remove_permanent_wall_peer_agreement_timeout")

        write_json(artifact_dir / "permanent-wall-report.json", {
            "success": not failures,
            "targetPlayerId": target_player_id,
            "reports": reports,
            "failures": failures,
        })
        write_json(artifact_dir / "build-host-logs-recent.json", host.logs_recent())
        write_json(artifact_dir / "build-client-logs-recent.json", client.logs_recent())
        if args.headless_player:
            screenshot_result = {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            }
            write_json(artifact_dir / "build-host-screenshot.json", screenshot_result)
            write_json(artifact_dir / "build-client-screenshot.json", screenshot_result)
        else:
            write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
            write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
    except Exception as exc:
        failures.append(f"unhandled_exception:{type(exc).__name__}")
        write_json(artifact_dir / "exception.json", {
            "type": type(exc).__name__,
            "message": str(exc),
        })
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc for proc in (client_proc, host_proc) if proc is not None],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(failures),
            strict_cleanup=args.strict_cleanup,
        )
        copied = collect_player_log(artifact_dir, CASE_NAME)
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client.stdout.log",
            artifact_dir / "build-client.stderr.log",
        ]
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    if failures:
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    write_json(artifact_dir / "permanent-wall-report.json", {
        "success": not failures,
        "targetPlayerId": target_player_id,
        "reports": reports,
        "failures": failures,
    })
    result = write_standard_result(
        artifact_dir,
        CASE_NAME,
        failures,
        cleanup_report,
        headless_player=args.headless_player,
        extra={
            "targetPlayerId": target_player_id,
            "reportPath": "permanent-wall-report.json",
            "reports": reports,
        },
    )
    print(json.dumps(result, indent=2))
    return 0 if result.get("success") is True else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=7313)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--command-timeout", type=int, default=20)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=20.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
