#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import (
    failure_summary,
    free_port,
    hash_for_log,
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
    wait_build_peer_started,
    write_json,
    write_standard_result,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report, write_orphan_pressure_report


CASE_NAME = "same-token-reconnect"


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def players(snapshot: dict[str, Any]) -> list[dict[str, Any]]:
    state = normalize_snapshot_response(snapshot)
    if not isinstance(state, dict):
        return []
    return [player for player in state.get("players") or [] if isinstance(player, dict)]


def local_player(snapshot: dict[str, Any]) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("isLocal") is True:
            return player
    return None


def player_by_id(snapshot: dict[str, Any], player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def has_unique_player_ids(snapshot: dict[str, Any]) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids))


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def compare_equal(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_known(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left in (None, "", "unknown") or right in (None, "", "unknown"):
        return
    compare_equal(errors, field, left, right)


def compare_reconnect_target(host_snapshot: dict[str, Any], client_snapshot: dict[str, Any], target_player_id: int) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    if not isinstance(host_state, dict) or not isinstance(client_state, dict):
        return {"success": False, "errors": ["snapshot_not_dict"], "warnings": warnings}

    compare_equal(errors, "session", host_state.get("session"), client_state.get("session"))
    if not scene_matches(host_state.get("scene"), client_state.get("scene")):
        errors.append(f"scene left={host_state.get('scene')} right={client_state.get('scene')}")

    host_target = player_by_id(host_snapshot, target_player_id)
    client_target = player_by_id(client_snapshot, target_player_id)
    if host_target is None:
        errors.append(f"target.{target_player_id}.missing_host")
    if client_target is None:
        errors.append(f"target.{target_player_id}.missing_client")
    if host_target is None or client_target is None:
        return {"success": False, "errors": errors, "warnings": warnings}

    compare_equal(errors, f"target.{target_player_id}.health", host_target.get("health"), client_target.get("health"))
    compare_equal(errors, f"target.{target_player_id}.gold", host_target.get("gold"), client_target.get("gold"))
    compare_equal(errors, f"target.{target_player_id}.wallCount", host_target.get("wallCount"), client_target.get("wallCount"))
    compare_known(errors, f"target.{target_player_id}.shop.itemsHash", nested(host_target, "shop", "itemsHash"), nested(client_target, "shop", "itemsHash"))
    compare_known(errors, f"target.{target_player_id}.field.gridHash", nested(host_target, "field", "gridHash"), nested(client_target, "field", "gridHash"))
    compare_equal(errors, f"target.{target_player_id}.field.permanentWallCount", nested(host_target, "field", "permanentWallCount"), nested(client_target, "field", "permanentWallCount"))
    compare_known(errors, f"target.{target_player_id}.field.wallHash", nested(host_target, "field", "wallHash"), nested(client_target, "field", "wallHash"))
    compare_equal(errors, f"target.{target_player_id}.field.ready", nested(host_target, "field", "ready"), nested(client_target, "field", "ready"))
    compare_equal(errors, f"target.{target_player_id}.isAI", host_target.get("isAI"), client_target.get("isAI"))
    compare_equal(errors, f"target.{target_player_id}.isConnected", host_target.get("isConnected"), client_target.get("isConnected"))

    for side, snapshot in (("host", host_state), ("client", client_state)):
        if snapshot.get("errors"):
            warnings.extend(f"{side}.snapshot_error {err}" for err in snapshot["errors"])

    return {"success": not errors, "errors": errors, "warnings": warnings}


def wait_session_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "lobby-latest")
        client_state = dump_state(client, artifact_dir, "build-client-a", "lobby-latest")
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


def wait_initial_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "initial-latest")
        client_state = dump_state(client, artifact_dir, "build-client-a", "initial-latest")
        host_ready = snapshot_ready(host_state, expected_players, scene)
        client_ready = snapshot_ready(client_state, expected_players, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        write_json(artifact_dir / "initial-state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, expected_players, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, expected_players, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "initial-comparison-latest.json", comparison)
        if host_ready and client_ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, False


def takeover_assertions(snapshot: dict[str, Any], target_player_id: int) -> dict[str, Any]:
    state = normalize_snapshot_response(snapshot)
    target = player_by_id(snapshot, target_player_id)
    field = target.get("field") if isinstance(target, dict) else {}
    ai = target.get("ai") if isinstance(target, dict) else {}
    runner = state.get("runner") if isinstance(state, dict) else {}
    return {
        "scene": state.get("scene") if isinstance(state, dict) else None,
        "activePlayerCount": runner.get("activePlayerCount") if isinstance(runner, dict) else None,
        "playerCount": len(players(snapshot)),
        "uniquePlayerIds": has_unique_player_ids(snapshot),
        "targetPlayerId": target_player_id,
        "targetExists": target is not None,
        "targetIsAI": target.get("isAI") is True if isinstance(target, dict) else False,
        "targetConnected": target.get("isConnected") is True if isinstance(target, dict) else None,
        "targetFieldReady": field.get("ready") is True if isinstance(field, dict) else False,
        "targetAiRegistered": ai.get("controllerRegistered") is True if isinstance(ai, dict) else False,
    }


def takeover_ready(snapshot: dict[str, Any], target_player_id: int, scene: str) -> bool:
    assertions = takeover_assertions(snapshot, target_player_id)
    return (
        scene_matches(assertions["scene"], scene)
        and assertions["activePlayerCount"] == 1
        and assertions["uniquePlayerIds"] is True
        and assertions["targetExists"] is True
        and assertions["targetIsAI"] is True
        and assertions["targetConnected"] is False
        and assertions["targetFieldReady"] is True
        and assertions["targetAiRegistered"] is True
    )


def wait_takeover(
    host: AutomationClient,
    artifact_dir: pathlib.Path,
    target_player_id: int,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], bool]:
    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        snapshot = dump_state(host, artifact_dir, "build-host", "takeover-latest")
        assertions = takeover_assertions(snapshot, target_player_id)
        ready = takeover_ready(snapshot, target_player_id, scene)
        write_json(artifact_dir / "takeover-wait-latest.json", {
            "ready": ready,
            "assertions": assertions,
            "stableMatches": stable_matches,
        })
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return snapshot, True
        else:
            stable_matches = 0
        time.sleep(2)
    return snapshot, False


def reconnect_assertions(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
) -> dict[str, Any]:
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    host_runner = host_state.get("runner") if isinstance(host_state, dict) else {}
    host_target = player_by_id(host_snapshot, target_player_id)
    client_local = local_player(client_snapshot)
    full_comparison = compare_snapshots(host_snapshot, client_snapshot)
    target_comparison = compare_reconnect_target(host_snapshot, client_snapshot, target_player_id)
    host_field = host_target.get("field") if isinstance(host_target, dict) else {}
    return {
        "hostActivePlayerCount": host_runner.get("activePlayerCount") if isinstance(host_runner, dict) else None,
        "hostPlayerCount": len(players(host_snapshot)),
        "clientPlayerCount": len(players(client_snapshot)),
        "hostUniquePlayerIds": has_unique_player_ids(host_snapshot),
        "clientUniquePlayerIds": has_unique_player_ids(client_snapshot),
        "targetPlayerId": target_player_id,
        "hostTargetExists": host_target is not None,
        "hostTargetIsAI": host_target.get("isAI") is True if isinstance(host_target, dict) else None,
        "hostTargetConnected": host_target.get("isConnected") is True if isinstance(host_target, dict) else None,
        "hostTargetFieldReady": host_field.get("ready") is True if isinstance(host_field, dict) else False,
        "clientLocalPlayerId": client_local.get("playerId") if isinstance(client_local, dict) else None,
        "clientLocalTokenHash": client_local.get("connectionTokenHash") if isinstance(client_local, dict) else None,
        "expectedTokenHash": expected_token_hash,
        "targetComparison": target_comparison,
        "fullComparison": full_comparison,
    }


def reconnect_ready(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
    scene: str,
    expected_players: int,
    require_full_world: bool,
) -> bool:
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    if not isinstance(host_state, dict) or not isinstance(client_state, dict):
        return False
    if not scene_matches(host_state.get("scene"), scene) or not scene_matches(client_state.get("scene"), scene):
        return False
    if not snapshot_ready(host_snapshot, expected_players, scene) or not snapshot_ready(client_snapshot, expected_players, scene):
        return False
    assertions = reconnect_assertions(host_snapshot, client_snapshot, target_player_id, expected_token_hash)
    target_ready = (
        assertions["hostActivePlayerCount"] == 2
        and assertions["hostPlayerCount"] == expected_players
        and assertions["clientPlayerCount"] == expected_players
        and assertions["hostUniquePlayerIds"] is True
        and assertions["clientUniquePlayerIds"] is True
        and assertions["hostTargetExists"] is True
        and assertions["hostTargetIsAI"] is False
        and assertions["hostTargetConnected"] is True
        and assertions["hostTargetFieldReady"] is True
        and assertions["clientLocalPlayerId"] == target_player_id
        and assertions["clientLocalTokenHash"] == expected_token_hash
        and assertions["targetComparison"]["success"] is True
    )
    if not target_ready:
        return False

    return not require_full_world or assertions["fullComparison"]["success"] is True


def wait_reconnect(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    target_player_id: int,
    expected_token_hash: str,
    timeout: int,
    scene: str,
    expected_players: int,
    require_full_world: bool,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "reconnect-latest")
        client_state = dump_state(client, artifact_dir, "build-client-b", "reconnect-latest")
        assertions = reconnect_assertions(host_state, client_state, target_player_id, expected_token_hash)
        ready = reconnect_ready(host_state, client_state, target_player_id, expected_token_hash, scene, expected_players, require_full_world)
        write_json(artifact_dir / "reconnect-wait-latest.json", {
            "ready": ready,
            "assertions": assertions,
            "requireFullWorld": require_full_world,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", assertions["targetComparison"])
        write_json(artifact_dir / "full-comparison-latest.json", assertions["fullComparison"])
        write_json(artifact_dir / "same-token-reconnect-target-assertions-latest.json", {
            "success": assertions["targetComparison"]["success"],
            "targetPlayerId": target_player_id,
            "assertions": assertions,
        })
        write_json(artifact_dir / "same-token-reconnect-fullworld-assertions-latest.json", {
            "success": assertions["fullComparison"]["success"],
            "comparison": assertions["fullComparison"],
        })
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, False


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
        "headlessPlayer": args.headless_player,
    }
    failures: list[str] = []

    if args.dry_run:
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "playerPath": str(player_path) if player_path else None,
            "dryRun": True,
            "headlessPlayer": args.headless_player,
        })
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    if player_path is None or not player_path.exists():
        failures.append("player_path_missing")
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "playerPath": str(player_path) if player_path else None,
            "dryRun": False,
            "headlessPlayer": args.headless_player,
        })
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {"cleanupStatus": "NEEDS_ENVIRONMENT", "cleanupSuccess": False, "orphanedPids": []},
            headless_player=args.headless_player,
        )
        print(json.dumps(result, indent=2))
        return 2

    session = args.session or new_session("str")
    host_token = new_token()
    client_a_token = new_token()
    client_b_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    client_connection_hash = hash_for_log(client_connection)
    host_port = free_port()
    client_a_port = free_port()
    client_b_port = free_port()
    host_proc: PlayerProcess | None = None
    client_a_proc: PlayerProcess | None = None
    client_b_proc: PlayerProcess | None = None
    target_player_id = -1

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientAPort": client_a_port,
        "clientBPort": client_b_port,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "maxPlayers": args.max_players,
        "clientConnectionTokenHash": client_connection_hash,
        "dryRun": args.dry_run,
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
                "orphanedPids": [proc.get("pid") for proc in orphan_gate.get("processes") or [] if isinstance(proc, dict)],
            },
            headless_player=args.headless_player,
            extra={"orphanPressure": orphan_gate},
        )
        print(json.dumps(result, indent=2))
        return 2

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
            max_players=args.max_players,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="same_token_reconnect",
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")

        client_a_proc = launch_player(
            player_path,
            "client",
            session,
            client_a_port,
            client_a_token,
            client_connection,
            artifact_dir,
            "build-client-a",
            max_players=args.max_players,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario="same_token_reconnect",
            headless_player=args.headless_player,
        )
        client_a = AutomationClient(client_a_port, client_a_token)
        client_a_ping = client_a.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-a-ping.json", client_a_ping)
        if not client_a_ping.get("success"):
            failures.append("client_a_automation_ping_timeout")

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, args.max_players, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client_a.join, artifact_dir, "build-client-a", session, args.lobby_scene, args.max_players, args.start_timeout):
            failures.append("client_a_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(
            host,
            client_a,
            artifact_dir,
            args.lobby_timeout,
            args.lobby_scene,
            args.max_players,
        )
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-a-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_initial, client_initial, ready = wait_initial_states(
            host,
            client_a,
            artifact_dir,
            args.state_timeout,
            args.scene,
            args.max_players,
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-disconnect.json", host_initial)
        write_json(artifact_dir / "snapshots" / "build-client-a-before-disconnect.json", client_initial)
        if not ready:
            failures.append("initial_state_ready_timeout")

        client_a_local = local_player(client_initial)
        if not isinstance(client_a_local, dict):
            failures.append("client_a_local_player_missing")
        else:
            target_player_id = int(client_a_local.get("playerId", -1))
            write_json(artifact_dir / "reconnect-target.json", {
                "playerId": target_player_id,
                "connectionTokenHash": client_a_local.get("connectionTokenHash"),
                "expectedConnectionTokenHash": client_connection_hash,
            })

        if client_a_proc is not None:
            client_a_proc.process.kill()
            client_a_proc.process.wait(timeout=10)
            client_a_proc.close_logs()

        if target_player_id < 0:
            failures.append("target_player_id_invalid")
        else:
            host_takeover, takeover_ok = wait_takeover(host, artifact_dir, target_player_id, args.takeover_timeout, args.scene)
            write_json(artifact_dir / "snapshots" / "build-host-post-takeover.json", host_takeover)
            write_json(artifact_dir / "takeover-assertions.json", takeover_assertions(host_takeover, target_player_id))
            if not takeover_ok:
                failures.append("disconnect_ai_takeover_timeout")

        client_b_proc = launch_player(
            player_path,
            "client",
            session,
            client_b_port,
            client_b_token,
            client_connection,
            artifact_dir,
            "build-client-b",
            max_players=args.max_players,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 2,
            scenario="same_token_reconnect",
            headless_player=args.headless_player,
        )
        client_b = AutomationClient(client_b_port, client_b_token)
        client_b_ping = client_b.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-b-ping.json", client_b_ping)
        if not client_b_ping.get("success"):
            failures.append("client_b_automation_ping_timeout")

        if not wait_build_peer_started(client_b.join, artifact_dir, "build-client-b", session, args.scene, args.max_players, args.start_timeout):
            failures.append("client_b_rejoin_timeout")

        if target_player_id >= 0:
            host_final, client_final, reconnect_ok = wait_reconnect(
                host,
                client_b,
                artifact_dir,
                target_player_id,
                client_connection_hash,
                args.reconnect_timeout,
                args.scene,
                args.max_players,
                not args.target_only,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-reconnect.json", host_final)
            write_json(artifact_dir / "snapshots" / "build-client-b-post-reconnect.json", client_final)
            assertions = reconnect_assertions(host_final, client_final, target_player_id, client_connection_hash)
            write_json(artifact_dir / "same-token-reconnect-assertions.json", assertions)
            write_json(artifact_dir / "same-token-reconnect-target-assertions.json", {
                "success": assertions["targetComparison"]["success"],
                "targetPlayerId": target_player_id,
                "assertions": assertions,
            })
            write_json(artifact_dir / "same-token-reconnect-fullworld-assertions.json", {
                "success": assertions["fullComparison"]["success"],
                "comparison": assertions["fullComparison"],
            })
            write_json(artifact_dir / "comparison.json", assertions["targetComparison"])
            write_json(artifact_dir / "full-comparison.json", assertions["fullComparison"])
            if not reconnect_ok:
                failures.append("same_token_reconnect_full_world_timeout" if not args.target_only else "same_token_reconnect_timeout")

        if args.headless_player:
            write_json(artifact_dir / "build-host-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
            write_json(artifact_dir / "build-client-b-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
        else:
            write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
            if client_b_proc is not None:
                write_json(artifact_dir / "build-client-b-screenshot.json", client_b.screenshot())
        write_json(artifact_dir / "build-host-logs-recent.json", host.logs_recent())
        if client_b_proc is not None:
            write_json(artifact_dir / "build-client-b-logs-recent.json", client_b.logs_recent())
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc for proc in (host_proc, client_a_proc, client_b_proc) if proc is not None],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(failures),
            strict_cleanup=args.strict_cleanup,
        )
        host_log = collect_player_log(artifact_dir, "build-host-or-last")
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client-a.stdout.log",
            artifact_dir / "build-client-a.stderr.log",
            artifact_dir / "build-client-b.stdout.log",
            artifact_dir / "build-client-b.stderr.log",
        ]
        if host_log:
            logs.append(host_log)
        write_timeline(artifact_dir, logs)

    result = write_standard_result(
        artifact_dir,
        CASE_NAME,
        failures,
        cleanup_report,
        headless_player=args.headless_player,
        extra={"targetPlayerId": target_player_id},
    )
    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if result["success"] else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=4201)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--takeover-timeout", type=int, default=90)
    parser.add_argument("--reconnect-timeout", type=int, default=120)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--max-players", type=int, default=3)
    parser.add_argument("--target-only", action="store_true", help="Only require target identity reclaim. Default requires full-world comparison.")
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--cleanup-report", action="store_true", help="Compatibility flag; cleanup-report.json is always written.")
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-check", action="store_true", help="Compatibility flag; orphan pressure check is always performed.")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
