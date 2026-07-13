#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import shutil
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
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report, write_orphan_pressure_report


CASE_NAME = "two-humanbot-two-ai-smoke"
LOBBY_HUMAN_PEERS = 2
EXPECTED_PLAYERS = 4
EXPECTED_HUMANS = 2
EXPECTED_AI = 2


def safe_request(call: Callable[[], dict[str, Any]]) -> dict[str, Any]:
    try:
        return call()
    except Exception as exc:
        return {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}


def state(snapshot: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(snapshot)
    return normalized if isinstance(normalized, dict) else {}


def players(snapshot: Any) -> list[dict[str, Any]]:
    return [player for player in state(snapshot).get("players") or [] if isinstance(player, dict)]


def nested(data: dict[str, Any], *keys: str) -> Any:
    current: Any = data
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def game(snapshot: Any) -> dict[str, Any]:
    value = state(snapshot).get("game")
    return value if isinstance(value, dict) else {}


def player_by_id(snapshot: Any, player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def human_player_ids(snapshot: Any) -> list[int]:
    result: list[int] = []
    for player in players(snapshot):
        if player.get("isAI") is False and isinstance(player.get("playerId"), int):
            result.append(int(player["playerId"]))
    return sorted(result)


def bot_args(args: argparse.Namespace, peer: dict[str, Any], artifact_dir: pathlib.Path) -> list[str]:
    result = [
        "--mpHumanBot",
        "--mpBotPersona",
        str(peer["persona"]),
        "--mpBotSeed",
        str(peer["botSeed"]),
        "--mpBotDurationSeconds",
        str(args.bot_duration_seconds),
        "--mpBotStopAtRound",
        str(args.bot_stop_at_round),
        "--mpBotMaxCommands",
        str(args.bot_max_commands),
        "--mpBotRecordJournal",
        str(artifact_dir / f"{peer['name']}-bot.jsonl"),
    ]
    if args.bot_prepare_mode == "augment-only":
        result.append("--mpBotPrepareAugmentOnly")
    elif args.bot_prepare_mode == "skip":
        result.append("--mpBotSkipPrepare")
    return result


def wait_lobby_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    scene: str,
) -> bool:
    deadline = time.time() + timeout_seconds
    stable = 0
    while time.time() < deadline:
        snapshots = {
            "host": safe_request(host.dump_state),
            "client": safe_request(client.dump_state),
        }
        for name, snapshot in snapshots.items():
            write_json(artifact_dir / "snapshots" / f"{name}-lobby-latest.json", snapshot)
        ready = {
            name: session_ready(snapshot, LOBBY_HUMAN_PEERS, scene)
            for name, snapshot in snapshots.items()
        }
        write_json(artifact_dir / "lobby-wait-latest.json", {
            "ready": ready,
            "reasons": {
                name: session_not_ready_reasons(snapshot, LOBBY_HUMAN_PEERS, scene)
                for name, snapshot in snapshots.items()
            },
            "stableMatches": stable,
        })
        if all(ready.values()):
            stable += 1
            if stable >= 2:
                return True
        else:
            stable = 0
        time.sleep(1)
    return False


def wait_game_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    scene: str,
    label: str,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    stable = 0
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    while time.time() < deadline:
        latest_host = safe_request(host.dump_state)
        latest_client = safe_request(client.dump_state)
        write_json(artifact_dir / "snapshots" / f"host-{label}-latest.json", latest_host)
        write_json(artifact_dir / "snapshots" / f"client-{label}-latest.json", latest_client)
        ready = {
            "host": snapshot_ready(latest_host, EXPECTED_PLAYERS, scene),
            "client": snapshot_ready(latest_client, EXPECTED_PLAYERS, scene),
        }
        if all(ready.values()):
            comparison = compare_snapshots(latest_host, latest_client)
            write_json(artifact_dir / f"comparison-{label}-latest.json", comparison)
        write_json(artifact_dir / f"{label}-wait-latest.json", {
            "ready": ready,
            "reasons": {
                "host": snapshot_not_ready_reasons(latest_host, EXPECTED_PLAYERS, scene),
                "client": snapshot_not_ready_reasons(latest_client, EXPECTED_PLAYERS, scene),
            },
            "comparison": comparison,
            "stableMatches": stable,
        })
        if all(ready.values()) and comparison.get("success") is True:
            stable += 1
            if stable >= 2:
                return latest_host, latest_client, comparison, True
        else:
            stable = 0
        time.sleep(2)
    return latest_host, latest_client, comparison, False


def wait_target_prepare_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    scene: str,
    label: str,
    target_round: int,
    require_comparison: bool = True,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    stable = 0
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    while time.time() < deadline:
        latest_host = safe_request(host.dump_state)
        latest_client = safe_request(client.dump_state)
        write_json(artifact_dir / "snapshots" / f"host-{label}-latest.json", latest_host)
        write_json(artifact_dir / "snapshots" / f"client-{label}-latest.json", latest_client)

        host_game = game(latest_host)
        client_game = game(latest_client)
        host_at_target = host_game.get("currentRound") == target_round and host_game.get("currentState") == "Prepare"
        client_at_target = client_game.get("currentRound") == target_round and client_game.get("currentState") == "Prepare"
        ready = {
            "host": snapshot_ready(latest_host, EXPECTED_PLAYERS, scene) and host_at_target,
            "client": snapshot_ready(latest_client, EXPECTED_PLAYERS, scene) and client_at_target,
        }
        if all(ready.values()):
            comparison = compare_snapshots(latest_host, latest_client)
            write_json(artifact_dir / f"comparison-{label}-latest.json", comparison)
        write_json(artifact_dir / f"{label}-wait-latest.json", {
            "targetRound": target_round,
            "requireComparison": require_comparison,
            "ready": ready,
            "hostGame": host_game,
            "clientGame": client_game,
            "reasons": {
                "host": snapshot_not_ready_reasons(latest_host, EXPECTED_PLAYERS, scene),
                "client": snapshot_not_ready_reasons(latest_client, EXPECTED_PLAYERS, scene),
            },
            "comparison": comparison,
            "stableMatches": stable,
        })
        comparison_ready = comparison.get("success") is True
        if all(ready.values()) and (comparison_ready or not require_comparison):
            stable += 1
            if stable >= 2:
                return latest_host, latest_client, comparison, True
        else:
            stable = 0
        time.sleep(2)
    return latest_host, latest_client, comparison, False


def wait_target_prepare_wall_stock_restored(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    scene: str,
    label: str,
    target_round: int,
    before_snapshot: Any,
    remove_results: list[dict[str, Any]],
    require_comparison: bool = True,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    stable = 0
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    while time.time() < deadline:
        latest_host = safe_request(host.dump_state)
        latest_client = safe_request(client.dump_state)
        write_json(artifact_dir / "snapshots" / f"host-{label}-latest.json", latest_host)
        write_json(artifact_dir / "snapshots" / f"client-{label}-latest.json", latest_client)

        host_game = game(latest_host)
        client_game = game(latest_client)
        host_at_target = host_game.get("currentRound") == target_round and host_game.get("currentState") == "Prepare"
        client_at_target = client_game.get("currentRound") == target_round and client_game.get("currentState") == "Prepare"
        ready = {
            "host": snapshot_ready(latest_host, EXPECTED_PLAYERS, scene) and host_at_target,
            "client": snapshot_ready(latest_client, EXPECTED_PLAYERS, scene) and client_at_target,
        }
        host_stock_restored = wall_stock_restored(before_snapshot, latest_host, remove_results)
        client_stock_restored = wall_stock_restored(before_snapshot, latest_client, remove_results)
        if all(ready.values()):
            comparison = compare_snapshots(latest_host, latest_client)
            write_json(artifact_dir / f"comparison-{label}-latest.json", comparison)
        write_json(artifact_dir / f"{label}-wait-latest.json", {
            "targetRound": target_round,
            "requireComparison": require_comparison,
            "ready": ready,
            "hostGame": host_game,
            "clientGame": client_game,
            "reasons": {
                "host": snapshot_not_ready_reasons(latest_host, EXPECTED_PLAYERS, scene),
                "client": snapshot_not_ready_reasons(latest_client, EXPECTED_PLAYERS, scene),
            },
            "comparison": comparison,
            "wallStockRestored": {
                "host": host_stock_restored,
                "client": client_stock_restored,
            },
            "wallCounts": {
                "before": wall_counts_by_player(before_snapshot),
                "host": wall_counts_by_player(latest_host),
                "client": wall_counts_by_player(latest_client),
            },
            "stableMatches": stable,
        })
        comparison_ready = comparison.get("success") is True
        stock_restored = host_stock_restored and client_stock_restored
        if all(ready.values()) and (comparison_ready or not require_comparison) and stock_restored:
            stable += 1
            if stable >= 2:
                return latest_host, latest_client, comparison, True
        else:
            stable = 0
        time.sleep(2)
    return latest_host, latest_client, comparison, False


def wait_game_over_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    scene: str,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout_seconds
    stable = 0
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    while time.time() < deadline:
        latest_host = safe_request(host.dump_state)
        latest_client = safe_request(client.dump_state)
        write_json(artifact_dir / "snapshots" / "host-game-over-latest.json", latest_host)
        write_json(artifact_dir / "snapshots" / "client-game-over-latest.json", latest_client)
        host_game = game(latest_host)
        client_game = game(latest_client)
        ready = {
            "host": snapshot_ready(latest_host, EXPECTED_PLAYERS, scene) and host_game.get("currentState") == "GameOver",
            "client": snapshot_ready(latest_client, EXPECTED_PLAYERS, scene) and client_game.get("currentState") == "GameOver",
        }
        if all(ready.values()):
            comparison = compare_snapshots(latest_host, latest_client)
            write_json(artifact_dir / "comparison-game-over-latest.json", comparison)
        write_json(artifact_dir / "game-over-wait-latest.json", {
            "ready": ready,
            "hostGame": host_game,
            "clientGame": client_game,
            "comparison": comparison,
            "stableMatches": stable,
        })
        if all(ready.values()) and comparison.get("success") is True:
            stable += 1
            if stable >= 2:
                return latest_host, latest_client, comparison, True
        else:
            stable = 0
        time.sleep(2)
    return latest_host, latest_client, comparison, False


def wait_bot_commands(
    clients: dict[str, AutomationClient],
    artifact_dir: pathlib.Path,
    timeout_seconds: int,
    min_commands_per_bot: int,
) -> dict[str, dict[str, Any]]:
    deadline = time.time() + timeout_seconds
    latest: dict[str, dict[str, Any]] = {}
    while time.time() < deadline:
        latest = {
            name: safe_request(client.bot_status)
            for name, client in clients.items()
        }
        for name, status in latest.items():
            write_json(artifact_dir / f"{name}-bot-status-latest.json", status)
            write_json(artifact_dir / f"{name}-bot-journal-latest.json", safe_request(clients[name].bot_journal))
        if all(int(((status.get("data") or status).get("commandsIssued") or 0)) >= min_commands_per_bot for status in latest.values()):
            return latest
        time.sleep(2)
    return latest


def stop_bots_before_move(
    clients: dict[str, AutomationClient],
    artifact_dir: pathlib.Path,
    timeout_seconds: int = 10,
    *,
    reason: str = "manual_move_verification",
    label: str = "before-move",
) -> tuple[dict[str, dict[str, Any]], bool]:
    results: dict[str, dict[str, Any]] = {}
    for name, client in clients.items():
        result = safe_request(lambda client=client: client.bot_stop(reason=reason))
        results[name] = result
        write_json(artifact_dir / f"{name}-bot-stop-{label}.json", result)

    deadline = time.time() + timeout_seconds
    latest: dict[str, dict[str, Any]] = {}
    stopped = False
    while time.time() < deadline:
        latest = {
            name: safe_request(client.bot_status)
            for name, client in clients.items()
        }
        write_json(artifact_dir / f"bot-stop-{label}-status-latest.json", latest)
        stopped = all(((status.get("data") or status).get("running") is False) for status in latest.values())
        if stopped:
            break
        time.sleep(0.5)

    return results, stopped


def issue_move_commands(
    host: AutomationClient,
    snapshot: Any,
    artifact_dir: pathlib.Path,
    label: str = "move-unit",
    player_ids: list[int] | None = None,
) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    for player_id in (player_ids if player_ids is not None else human_player_ids(snapshot)):
        result = safe_request(lambda player_id=player_id: host.command(name="move_unit", playerId=player_id))
        results.append({"playerId": player_id, "response": result})
        write_json(artifact_dir / f"{label}-player-{player_id}.json", result)
    write_json(artifact_dir / f"{label}-results.json", results)
    return results


def command_position(response: dict[str, Any]) -> dict[str, int] | None:
    data = response.get("data") if isinstance(response.get("data"), dict) else {}
    position = data.get("position") if isinstance(data.get("position"), dict) else None
    if not isinstance(position, dict):
        return None
    try:
        return {
            "x": int(position.get("x")),
            "y": int(position.get("y")),
            "z": int(position.get("z") or 0),
        }
    except (TypeError, ValueError):
        return None


def wall_command_player_ids(snapshot: Any) -> list[int]:
    result: list[int] = []
    for player in players(snapshot):
        if player.get("isAI") is not False or not isinstance(player.get("playerId"), int):
            continue
        if to_int(player.get("health")) <= 0 or to_int(player.get("wallCount")) <= 0:
            continue
        result.append(int(player["playerId"]))
    return sorted(result)


def issue_place_wall_commands(
    host: AutomationClient,
    snapshot: Any,
    artifact_dir: pathlib.Path,
    label: str,
    player_ids: list[int] | None = None,
) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    for player_id in (player_ids if player_ids is not None else wall_command_player_ids(snapshot)):
        result = safe_request(lambda player_id=player_id: host.command(name="place_wall", playerId=player_id))
        results.append({"playerId": player_id, "response": result})
        write_json(artifact_dir / f"{label}-player-{player_id}.json", result)
    write_json(artifact_dir / f"{label}-results.json", results)
    return results


def issue_remove_wall_commands(
    host: AutomationClient,
    place_results: list[dict[str, Any]],
    artifact_dir: pathlib.Path,
    label: str,
) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    for item in place_results:
        response = item.get("response") if isinstance(item.get("response"), dict) else {}
        if response.get("success") is not True:
            continue
        position = command_position(response)
        if position is None:
            continue
        player_id = int(item.get("playerId"))
        result = safe_request(lambda player_id=player_id, position=position: host.command(
            name="remove_wall",
            playerId=player_id,
            position=position,
        ))
        results.append({"playerId": player_id, "position": position, "response": result})
        write_json(artifact_dir / f"{label}-player-{player_id}.json", result)
    write_json(artifact_dir / f"{label}-results.json", results)
    return results


def movement_hash_changed(before: Any, after: Any, move_results: list[dict[str, Any]]) -> bool:
    for item in move_results:
        if (item.get("response") or {}).get("success") is not True:
            continue
        player_id = int(item.get("playerId"))
        before_player = player_by_id(before, player_id)
        after_player = player_by_id(after, player_id)
        if before_player is None or after_player is None:
            continue
        if nested(before_player, "field", "placedUnitsHash") != nested(after_player, "field", "placedUnitsHash"):
            return True
    return False


def wall_hash_changed(before: Any, after: Any, command_results: list[dict[str, Any]]) -> bool:
    for item in command_results:
        if (item.get("response") or {}).get("success") is not True:
            continue
        player_id = int(item.get("playerId"))
        before_player = player_by_id(before, player_id)
        after_player = player_by_id(after, player_id)
        if before_player is None or after_player is None:
            continue
        if nested(before_player, "field", "wallHash") != nested(after_player, "field", "wallHash"):
            return True
    return False


def wall_counts_by_player(snapshot: Any) -> dict[int, int]:
    return {
        int(player["playerId"]): to_int(player.get("wallCount"))
        for player in players(snapshot)
        if isinstance(player.get("playerId"), int)
    }


def wall_stock_restored(before: Any, after_remove: Any, remove_results: list[dict[str, Any]]) -> bool:
    before_counts = wall_counts_by_player(before)
    after_counts = wall_counts_by_player(after_remove)
    checked = False
    for item in remove_results:
        if (item.get("response") or {}).get("success") is not True:
            continue
        player_id = int(item.get("playerId"))
        if player_id not in before_counts or player_id not in after_counts:
            return False
        checked = True
        if after_counts[player_id] < before_counts[player_id]:
            return False
    return checked


def king_goal_snapshot_errors(snapshot: Any, peer_name: str) -> list[str]:
    errors: list[str] = []
    snapshot_players = players(snapshot)
    if len(snapshot_players) != EXPECTED_PLAYERS:
        return [f"{peer_name}.kingGoal.playerCount expected={EXPECTED_PLAYERS} actual={len(snapshot_players)}"]

    for player in snapshot_players:
        player_id = player.get("playerId")
        prefix = f"{peer_name}.player.{player_id}"
        field = player.get("field") if isinstance(player.get("field"), dict) else {}

        if player.get("kingPresentationReady") is not True:
            errors.append(f"{prefix}.kingPresentationReady")
        numeric_limits = (
            ("kingPresentationGoalDistance", 0.01),
            ("kingPresentationWorldScaleDrift", 0.002),
            ("kingPresentationTransformDrift", 0.002),
            ("kingRigTransformDrift", 0.002),
            ("kingCameraFacingAngle", 1.0),
        )
        for key, maximum in numeric_limits:
            try:
                actual = float(player.get(key))
            except (TypeError, ValueError):
                errors.append(f"{prefix}.{key}=missing")
                continue
            if actual < 0.0 or actual > maximum:
                errors.append(f"{prefix}.{key} expected=0..{maximum} actual={actual}")

        try:
            scale_multiplier = float(player.get("kingPresentationScaleMultiplier"))
        except (TypeError, ValueError):
            errors.append(f"{prefix}.kingPresentationScaleMultiplier=missing")
        else:
            if abs(scale_multiplier - 1.3) > 0.001:
                errors.append(f"{prefix}.kingPresentationScaleMultiplier expected=1.3 actual={scale_multiplier}")

        if player.get("kingUsesNeutralGoalAnchor") is not True:
            errors.append(f"{prefix}.kingUsesNeutralGoalAnchor")
        rig_pin_required = player.get("kingRigPinRequired")
        rig_pin_active = player.get("kingRigPinActive")
        if not isinstance(rig_pin_required, bool):
            errors.append(f"{prefix}.kingRigPinRequired=missing")
        if not isinstance(rig_pin_active, bool):
            errors.append(f"{prefix}.kingRigPinActive=missing")
        elif rig_pin_required is True and rig_pin_active is not True:
            errors.append(f"{prefix}.kingRigPinActive expected=true when required")
        if player.get("kingHeadLookActive") is not True:
            errors.append(f"{prefix}.kingHeadLookActive")
        if player.get("kingHeadLookApplied") is not True:
            errors.append(f"{prefix}.kingHeadLookApplied")
        if field.get("regularUnitGoalViolationCount") != 0:
            errors.append(
                f"{prefix}.field.regularUnitGoalViolationCount expected=0 "
                f"actual={field.get('regularUnitGoalViolationCount')}"
            )
    return errors


def verify_king_goal_stability(
    clients: dict[str, AutomationClient],
    artifact_dir: pathlib.Path,
    timeout_seconds: float = 30.0,
    required_stable_samples: int = 3,
) -> dict[str, Any]:
    deadline = time.time() + timeout_seconds
    stable_samples = 0
    sample_index = 0
    sample_summaries: list[dict[str, Any]] = []
    latest_errors: list[str] = ["not_started"]

    while time.time() < deadline:
        sample_index += 1
        snapshots = {
            name: safe_request(client.dump_state)
            for name, client in clients.items()
        }
        latest_errors = []
        for name, snapshot in snapshots.items():
            latest_errors.extend(king_goal_snapshot_errors(snapshot, name))
            write_json(
                artifact_dir / "snapshots" / f"{name}-king-goal-sample-{sample_index}.json",
                snapshot,
            )

        if latest_errors:
            stable_samples = 0
        else:
            stable_samples += 1
        sample_summaries.append({
            "sample": sample_index,
            "success": not latest_errors,
            "errors": latest_errors,
            "stableSamples": stable_samples,
        })
        if stable_samples >= required_stable_samples:
            break
        time.sleep(0.75)

    result = {
        "success": stable_samples >= required_stable_samples,
        "requiredStableSamples": required_stable_samples,
        "stableSamples": stable_samples,
        "samples": sample_summaries,
        "errors": [] if stable_samples >= required_stable_samples else latest_errors,
    }
    write_json(artifact_dir / "king-goal-placement-verification.json", result)
    return result


def capture_ai_field_screenshots(
    client: AutomationClient,
    snapshot: Any,
    artifact_dir: pathlib.Path,
    timeout_seconds: float = 8.0,
) -> dict[str, Any]:
    snapshot_players = players(snapshot)
    ai_player_ids = sorted(
        int(player["playerId"])
        for player in snapshot_players
        if player.get("isAI") is True and isinstance(player.get("playerId"), int)
    )
    local_player_id = next(
        (
            int(player["playerId"])
            for player in snapshot_players
            if player.get("isLocal") is True and isinstance(player.get("playerId"), int)
        ),
        None,
    )
    errors: list[str] = []
    captures: list[dict[str, Any]] = []
    visual_preparation = safe_request(
        lambda: client.command(name="prepare_visual_capture")
    )
    write_json(artifact_dir / "host-prepare-visual-capture.json", visual_preparation)
    if visual_preparation.get("success") is not True:
        errors.append("prepare_visual_capture_failed")
    time.sleep(0.25)

    if len(ai_player_ids) != EXPECTED_AI:
        errors.append(f"ai_player_count expected={EXPECTED_AI} actual={len(ai_player_ids)}")

    for player_id in ai_player_ids:
        request = safe_request(
            lambda player_id=player_id: client.command(
                name="view_player_field",
                playerId=player_id,
                requestNavigation=True,
            )
        )
        write_json(artifact_dir / f"host-ai-{player_id}-field-view-command.json", request)
        inspection = request
        deadline = time.time() + timeout_seconds
        while request.get("success") is True and time.time() < deadline:
            inspection = safe_request(
                lambda player_id=player_id: client.command(
                    name="view_player_field",
                    playerId=player_id,
                    requestNavigation=False,
                )
            )
            payload = inspection.get("data") if isinstance(inspection, dict) else None
            if (
                inspection.get("success") is True
                and isinstance(payload, dict)
                and payload.get("viewingPlayerId") == player_id
                and payload.get("currentViewingMatchesRegistry") is True
                and payload.get("transitioning") is False
                and payload.get("switched") is True
            ):
                break
            time.sleep(0.25)

        payload = inspection.get("data") if isinstance(inspection, dict) else None
        settled = (
            inspection.get("success") is True
            and isinstance(payload, dict)
            and payload.get("viewingPlayerId") == player_id
            and payload.get("currentViewingMatchesRegistry") is True
            and payload.get("transitioning") is False
            and payload.get("switched") is True
        )
        if settled:
            time.sleep(0.5)
        field_snapshot = safe_request(client.dump_state) if settled else {}
        write_json(
            artifact_dir / "snapshots" / f"host-ai-{player_id}-field-state.json",
            field_snapshot,
        )
        viewed_player = next(
            (
                player
                for player in players(field_snapshot)
                if player.get("playerId") == player_id
            ),
            None,
        )
        orientation_ready = (
            isinstance(viewed_player, dict)
            and viewed_player.get("kingHeadLookApplied") is True
        )
        if not orientation_ready:
            errors.append(f"ai_field_king_head_look_not_applied:{player_id}")
        screenshot = safe_request(client.screenshot) if settled else {
            "success": False,
            "error": {"code": "ai_field_view_not_settled", "details": str(inspection)},
        }
        screenshot_data = screenshot.get("data") if isinstance(screenshot, dict) else None
        screenshot_path = pathlib.Path(str(screenshot_data.get("path"))) if isinstance(screenshot_data, dict) and screenshot_data.get("path") else None
        if screenshot.get("success") is True and screenshot_path is not None:
            screenshot_deadline = time.time() + 3.0
            previous_size = -1
            stable_size_samples = 0
            while time.time() < screenshot_deadline:
                current_size = screenshot_path.stat().st_size if screenshot_path.exists() else -1
                if current_size > 0 and current_size == previous_size:
                    stable_size_samples += 1
                else:
                    stable_size_samples = 0
                previous_size = current_size
                if stable_size_samples >= 2:
                    break
                time.sleep(0.1)
            if screenshot_path.exists() and screenshot_path.stat().st_size > 0 and stable_size_samples >= 2:
                archived_path = artifact_dir / "screenshots" / f"host-ai-{player_id}-field.png"
                archived_path.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(screenshot_path, archived_path)
                screenshot_data["archivedPath"] = str(archived_path.resolve())
            else:
                screenshot = {
                    "success": False,
                    "error": {
                        "code": "ai_field_screenshot_file_missing",
                        "details": str(screenshot_path),
                    },
                }
        write_json(artifact_dir / f"host-ai-{player_id}-field-screenshot.json", screenshot)
        capture = {
            "playerId": player_id,
            "settled": settled,
            "inspection": inspection,
            "orientationReady": orientation_ready,
            "kingCameraFacingAngle": (
                viewed_player.get("kingCameraFacingAngle")
                if isinstance(viewed_player, dict)
                else None
            ),
            "kingHeadLookApplied": (
                viewed_player.get("kingHeadLookApplied")
                if isinstance(viewed_player, dict)
                else None
            ),
            "screenshot": screenshot,
        }
        captures.append(capture)
        if not settled or screenshot.get("success") is not True:
            errors.append(f"ai_field_screenshot_failed:{player_id}")
        time.sleep(1.1)

    if local_player_id is not None:
        safe_request(
            lambda: client.command(
                name="view_player_field",
                playerId=local_player_id,
                requestNavigation=True,
            )
        )

    result = {
        "success": not errors,
        "errors": errors,
        "aiPlayerIds": ai_player_ids,
        "localPlayerId": local_player_id,
        "captures": captures,
    }
    write_json(artifact_dir / "king-goal-ai-field-screenshots.json", result)
    return result


def parse_grid_cell(value: Any) -> dict[str, int] | None:
    if not isinstance(value, str):
        return None
    parts = value.split(",")
    if len(parts) != 3:
        return None
    try:
        return {"x": int(parts[0]), "y": int(parts[1]), "z": int(parts[2])}
    except (TypeError, ValueError):
        return None


def first_placed_unit_cell(player: dict[str, Any]) -> dict[str, int] | None:
    field = player.get("field") if isinstance(player.get("field"), dict) else {}
    for part in field.get("placedUnitParts") or []:
        if not isinstance(part, str):
            continue
        cell = parse_grid_cell(part.split(":", 1)[0])
        if cell is not None:
            return cell
    return None


def verify_goal_cell_move_rejection(
    host: AutomationClient,
    client: AutomationClient,
    before_snapshot: Any,
    artifact_dir: pathlib.Path,
) -> dict[str, Any]:
    errors: list[str] = []
    target_player: dict[str, Any] | None = None
    source: dict[str, int] | None = None
    goal: dict[str, int] | None = None
    for player in players(before_snapshot):
        if player.get("isAI") is not False or not isinstance(player.get("playerId"), int):
            continue
        source = first_placed_unit_cell(player)
        field = player.get("field") if isinstance(player.get("field"), dict) else {}
        goal = parse_grid_cell(field.get("goalCell"))
        if source is not None and goal is not None and source != goal:
            target_player = player
            break

    response: dict[str, Any] = {}
    after_host: dict[str, Any] = {}
    after_client: dict[str, Any] = {}
    if target_player is None or source is None or goal is None:
        errors.append("goal_rejection.no_human_unit_or_goal_cell")
    else:
        player_id = int(target_player["playerId"])
        before_hash = nested(target_player, "field", "placedUnitsHash")
        response = safe_request(lambda: host.command(
            name="move_unit",
            playerId=player_id,
            **{"from": source, "to": goal},
        ))
        if response.get("success") is not False:
            errors.append("goal_rejection.command_was_not_rejected")
        if nested(response, "error", "code") != "unit_goal_cell_blocked":
            errors.append(
                "goal_rejection.error_code "
                f"expected=unit_goal_cell_blocked actual={nested(response, 'error', 'code')}"
            )

        time.sleep(0.75)
        after_host = safe_request(host.dump_state)
        after_client = safe_request(client.dump_state)
        host_player = player_by_id(after_host, player_id)
        client_player = player_by_id(after_client, player_id)
        if host_player is None or nested(host_player, "field", "placedUnitsHash") != before_hash:
            errors.append("goal_rejection.host_placedUnitsHash_changed")
        if client_player is None or nested(client_player, "field", "placedUnitsHash") != before_hash:
            errors.append("goal_rejection.client_placedUnitsHash_changed")
        errors.extend(king_goal_snapshot_errors(after_host, "host-after-goal-rejection"))
        errors.extend(king_goal_snapshot_errors(after_client, "client-after-goal-rejection"))

    result = {
        "success": not errors,
        "errors": errors,
        "playerId": target_player.get("playerId") if target_player is not None else None,
        "from": source,
        "to": goal,
        "response": response,
        "afterHost": after_host,
        "afterClient": after_client,
    }
    write_json(artifact_dir / "goal-cell-rejection.json", result)
    return result


def build_assertions(
    before_move: Any,
    after_move_host: Any,
    after_move_client: Any,
    comparison: dict[str, Any],
    bot_statuses: dict[str, dict[str, Any]],
    move_results: list[dict[str, Any]],
    king_goal_verification: dict[str, Any] | None = None,
    goal_rejection: dict[str, Any] | None = None,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    host_players = players(after_move_host)
    client_players = players(after_move_client)
    human_count = sum(1 for player in host_players if player.get("isAI") is False)
    ai_count = sum(1 for player in host_players if player.get("isAI") is True)

    if len(host_players) != EXPECTED_PLAYERS:
        errors.append(f"host.playerCount expected={EXPECTED_PLAYERS} actual={len(host_players)}")
    if len(client_players) != EXPECTED_PLAYERS:
        errors.append(f"client.playerCount expected={EXPECTED_PLAYERS} actual={len(client_players)}")
    if human_count != EXPECTED_HUMANS:
        errors.append(f"host.humanCount expected={EXPECTED_HUMANS} actual={human_count}")
    if ai_count != EXPECTED_AI:
        errors.append(f"host.aiCount expected={EXPECTED_AI} actual={ai_count}")
    if comparison.get("success") is not True:
        errors.extend(f"comparison.{err}" for err in comparison.get("errors") or ["failed"])
    warnings.extend(f"comparison.{warn}" for warn in comparison.get("warnings") or [])

    for name, status_response in bot_statuses.items():
        status = status_response.get("data") if isinstance(status_response.get("data"), dict) else status_response
        commands = int(status.get("commandsIssued") or 0)
        if commands < 1:
            errors.append(f"{name}.commandsIssued expected>=1 actual={commands}")
        if status.get("isAI") is True or nested(status, "ai", "controllerRegistered") is True:
            errors.append(f"{name}.misclassified_as_ai")

    successful_moves = [item for item in move_results if (item.get("response") or {}).get("success") is True]
    if not successful_moves:
        errors.append("move_unit.no_successful_command")
    if successful_moves and not movement_hash_changed(before_move, after_move_host, move_results):
        errors.append("move_unit.placedUnitsHash_not_changed")
    if king_goal_verification is not None and king_goal_verification.get("success") is not True:
        errors.extend(
            f"king_goal.{error}"
            for error in king_goal_verification.get("errors") or ["verification_failed"]
        )
    if goal_rejection is not None and goal_rejection.get("success") is not True:
        errors.extend(
            f"goal_rejection.{error}"
            for error in goal_rejection.get("errors") or ["verification_failed"]
        )

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "playerCount": len(host_players),
        "humanCount": human_count,
        "aiCount": ai_count,
        "botCommands": {
            name: int(((status.get("data") if isinstance(status.get("data"), dict) else status) or {}).get("commandsIssued") or 0)
            for name, status in bot_statuses.items()
        },
        "successfulMoveCommands": len(successful_moves),
        "kingGoalPlacementVerified": king_goal_verification.get("success") if king_goal_verification else None,
        "goalCellMoveRejected": goal_rejection.get("success") if goal_rejection else None,
    }


def to_int(value: Any, default: int = 0) -> int:
    try:
        if value is None:
            return default
        return int(value)
    except (TypeError, ValueError):
        return default


def active_human_player_ids(snapshot: Any) -> list[int]:
    result: list[int] = []
    for player in players(snapshot):
        if player.get("isAI") is not False or not isinstance(player.get("playerId"), int):
            continue
        field = player.get("field") if isinstance(player.get("field"), dict) else {}
        if to_int(player.get("health")) > 0 and to_int(field.get("aliveUnitCount")) > 0:
            result.append(int(player["playerId"]))
    return sorted(result)


def progress_row(snapshot: Any, elapsed_seconds: float) -> dict[str, Any]:
    current_game = game(snapshot)
    return {
        "elapsedSeconds": round(elapsed_seconds, 3),
        "currentRound": current_game.get("currentRound"),
        "currentState": current_game.get("currentState"),
        "battlePhase": current_game.get("battlePhase"),
        "players": [
            {
                "playerId": player.get("playerId"),
                "isAI": player.get("isAI"),
                "health": player.get("health"),
                "aliveUnitCount": nested(player, "field", "aliveUnitCount"),
                "placedUnitsHash": nested(player, "field", "placedUnitsHash"),
            }
            for player in players(snapshot)
        ],
    }


def start_human_bots(
    clients: dict[str, AutomationClient],
    peers: list[dict[str, Any]],
    artifact_dir: pathlib.Path,
    args: argparse.Namespace,
    label: str,
) -> dict[str, dict[str, Any]]:
    results: dict[str, dict[str, Any]] = {}
    for peer in peers:
        name = str(peer["name"])
        result = safe_request(lambda peer=peer, name=name: clients[name].bot_start(
            persona=str(peer["persona"]),
            seed=int(peer["botSeed"]),
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=args.bot_stop_at_round,
            maxCommands=args.bot_max_commands,
            skipPrepare=args.bot_prepare_mode == "skip",
            prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
            journalPath=str(artifact_dir / f"{name}-bot.jsonl"),
        ))
        results[name] = result
        write_json(artifact_dir / f"{name}-bot-start-{label}.json", result)
    return results


def run_game_to_end_prepare_move_loop(
    clients: dict[str, AutomationClient],
    peers: list[dict[str, Any]],
    artifact_dir: pathlib.Path,
    args: argparse.Namespace,
) -> dict[str, Any]:
    deadline = time.time() + args.max_duration_seconds
    start_time = time.time()
    moved_rounds: set[int] = set()
    move_records: list[dict[str, Any]] = []
    wall_records: list[dict[str, Any]] = []
    progress_timeline: list[dict[str, Any]] = []
    errors: list[str] = []
    warnings: list[str] = []
    final_host: dict[str, Any] = {}
    final_client: dict[str, Any] = {}
    final_comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    battle_hud_capture: dict[str, Any] = {}
    battle_hud_captured = False
    limit_reason = "none"

    while time.time() < deadline:
        host_snapshot = safe_request(clients["host"].dump_state)
        client_snapshot = safe_request(clients["client"].dump_state)
        final_host = host_snapshot
        final_client = client_snapshot
        write_json(artifact_dir / "snapshots" / "host-game-end-latest.json", host_snapshot)
        write_json(artifact_dir / "snapshots" / "client-game-end-latest.json", client_snapshot)

        host_game = game(host_snapshot)
        client_game = game(client_snapshot)
        progress_timeline.append(progress_row(host_snapshot, time.time() - start_time))
        write_json(artifact_dir / "game-to-end-progress-timeline.json", progress_timeline)

        ready = {
            "host": snapshot_ready(host_snapshot, EXPECTED_PLAYERS, args.scene),
            "client": snapshot_ready(client_snapshot, EXPECTED_PLAYERS, args.scene),
        }
        if all(ready.values()):
            final_comparison = compare_snapshots(host_snapshot, client_snapshot)
            write_json(artifact_dir / "comparison-game-end-latest.json", final_comparison)

        current_round = to_int(host_game.get("currentRound"))
        host_state = host_game.get("currentState")
        client_state = client_game.get("currentState")
        write_json(artifact_dir / "game-to-end-wait-latest.json", {
            "ready": ready,
            "hostGame": host_game,
            "clientGame": client_game,
            "movedRounds": sorted(moved_rounds),
            "moveRecords": len(move_records),
            "deadlineSecondsRemaining": max(0, deadline - time.time()),
            "maxRounds": args.max_rounds,
        })

        same_battle_state = (
            all(ready.values())
            and host_state in ("Battle1", "Battle2")
            and host_state == client_state
        )
        if same_battle_state and not battle_hud_captured:
            label = f"battle-hud-round-{current_round}-{str(host_state).lower()}"
            screenshot_results: dict[str, Any] = {}
            for name, peer_client in clients.items():
                screenshot_results[name] = safe_request(peer_client.screenshot)
                write_json(artifact_dir / f"{name}-{label}-screenshot.json", screenshot_results[name])
            time.sleep(1.0)
            captured = all(result.get("success") is True for result in screenshot_results.values())
            battle_hud_capture = {
                "captured": captured,
                "round": current_round,
                "state": host_state,
                "screenshots": screenshot_results,
            }
            write_json(artifact_dir / "battle-hud-capture.json", battle_hud_capture)
            battle_hud_captured = captured

        if host_state == "GameOver" and client_state == "GameOver":
            limit_reason = "game_over_reached"
            final_host, final_client, final_comparison, game_over_ready = wait_game_over_ready(
                clients["host"],
                clients["client"],
                artifact_dir,
                args.game_over_timeout,
                args.scene,
            )
            if not game_over_ready:
                warnings.append("game_over_comparison_not_stable_before_timeout")
            break

        same_prepare_round = (
            all(ready.values())
            and current_round > 0
            and host_state == "Prepare"
            and client_state == "Prepare"
            and client_game.get("currentRound") == current_round
        )
        if same_prepare_round and current_round not in moved_rounds:
            label = f"round-{current_round}-move"
            write_json(
                artifact_dir / f"freeze-game-flow-{label}.json",
                safe_request(lambda label=label: clients["host"].freeze_game_flow(True, f"two_humanbot_two_ai_{label}")),
            )
            before_move_host, _, before_comparison, before_ready = wait_target_prepare_ready(
                clients["host"],
                clients["client"],
                artifact_dir,
                args.state_timeout,
                args.scene,
                f"{label}-before",
                current_round,
                require_comparison=False,
            )
            write_json(artifact_dir / f"comparison-{label}-before.json", before_comparison)
            if not before_ready:
                errors.append(f"{label}.before_move_ready_timeout")

            bot_stop_results, bots_stopped = stop_bots_before_move(
                clients,
                artifact_dir,
                reason=f"game_end_{label}",
                label=label,
            )
            if not all((result.get("success") is True) for result in bot_stop_results.values()) or not bots_stopped:
                errors.append(f"{label}.bot_stop_failed")

            command_base_snapshot = before_move_host if before_ready else host_snapshot
            if args.wall_command_every_prepare:
                place_label = f"{label}-place-wall"
                wall_player_ids = wall_command_player_ids(command_base_snapshot)
                place_results = issue_place_wall_commands(
                    clients["host"],
                    command_base_snapshot,
                    artifact_dir,
                    label=place_label,
                    player_ids=wall_player_ids,
                )
                after_place_host, after_place_client, after_place_comparison, after_place_ready = wait_target_prepare_ready(
                    clients["host"],
                    clients["client"],
                    artifact_dir,
                    args.state_timeout,
                    args.scene,
                    f"{place_label}-after",
                    current_round,
                    require_comparison=False,
                )
                remove_label = f"{label}-remove-wall"
                remove_results = issue_remove_wall_commands(
                    clients["host"],
                    place_results,
                    artifact_dir,
                    label=remove_label,
                )
                successful_places = [item for item in place_results if (item.get("response") or {}).get("success") is True]
                successful_removes = [item for item in remove_results if (item.get("response") or {}).get("success") is True]
                if successful_removes:
                    after_remove_host, after_remove_client, after_remove_comparison, after_remove_ready = wait_target_prepare_wall_stock_restored(
                        clients["host"],
                        clients["client"],
                        artifact_dir,
                        args.state_timeout,
                        args.scene,
                        f"{remove_label}-after",
                        current_round,
                        command_base_snapshot,
                        remove_results,
                        require_comparison=False,
                    )
                else:
                    after_remove_host, after_remove_client, after_remove_comparison, after_remove_ready = wait_target_prepare_ready(
                        clients["host"],
                        clients["client"],
                        artifact_dir,
                        args.state_timeout,
                        args.scene,
                        f"{remove_label}-after",
                        current_round,
                    )
                wall_record = {
                    "round": current_round,
                    "wallPlayerIds": wall_player_ids,
                    "successfulPlaceWallCommands": len(successful_places),
                    "successfulRemoveWallCommands": len(successful_removes),
                    "placeWallHashChanged": wall_hash_changed(command_base_snapshot, after_place_host, place_results),
                    "removeWallHashChanged": wall_hash_changed(after_place_host, after_remove_host, remove_results),
                    "wallStockRestored": wall_stock_restored(command_base_snapshot, after_remove_host, remove_results),
                    "afterPlaceReady": after_place_ready,
                    "afterRemoveReady": after_remove_ready,
                    "afterPlaceComparisonSuccess": after_place_comparison.get("success") is True,
                    "afterRemoveComparisonSuccess": after_remove_comparison.get("success") is True,
                    "placeResults": place_results,
                    "removeResults": remove_results,
                    "errors": [],
                }
                if wall_player_ids and len(successful_places) != len(wall_player_ids):
                    wall_record["errors"].append("not_all_wall_places_succeeded")
                if successful_places and not wall_record["placeWallHashChanged"]:
                    wall_record["errors"].append("place_wall_hash_not_changed")
                if successful_places and len(successful_removes) != len(successful_places):
                    wall_record["errors"].append("not_all_wall_removes_succeeded")
                if successful_removes and not wall_record["removeWallHashChanged"]:
                    wall_record["errors"].append("remove_wall_hash_not_changed")
                if successful_removes and not wall_record["wallStockRestored"]:
                    wall_record["errors"].append("wall_stock_not_restored")
                if not after_place_ready:
                    wall_record["errors"].append("after_place_wall_ready_timeout")
                if not after_remove_ready:
                    wall_record["errors"].append("after_remove_wall_ready_timeout")
                if after_place_comparison.get("success") is not True:
                    wall_record.setdefault("warnings", []).extend(
                        f"place_comparison.{item}" for item in after_place_comparison.get("errors") or ["failed"]
                    )
                if after_remove_comparison.get("success") is not True:
                    wall_record.setdefault("warnings", []).extend(
                        f"remove_comparison.{item}" for item in after_remove_comparison.get("errors") or ["failed"]
                    )
                errors.extend(f"{label}.{error}" for error in wall_record["errors"])
                wall_records.append(wall_record)
                write_json(artifact_dir / f"{label}-wall-record.json", wall_record)
                write_json(artifact_dir / "game-to-end-wall-records.json", wall_records)
                command_base_snapshot = after_remove_host if after_remove_ready else command_base_snapshot

            active_humans = active_human_player_ids(command_base_snapshot)
            if not active_humans:
                warnings.append(f"{label}.no_active_human_units")
            move_results = issue_move_commands(
                clients["host"],
                command_base_snapshot,
                artifact_dir,
                label=label,
                player_ids=active_humans,
            )
            after_move_host, after_move_client, after_comparison, after_ready = wait_target_prepare_ready(
                clients["host"],
                clients["client"],
                artifact_dir,
                args.state_timeout,
                args.scene,
                f"{label}-after",
                current_round,
                require_comparison=False,
            )
            successful_moves = [item for item in move_results if (item.get("response") or {}).get("success") is True]
            hash_changed = movement_hash_changed(command_base_snapshot, after_move_host, move_results)
            record = {
                "round": current_round,
                "activeHumanPlayerIds": active_humans,
                "successfulMoveCommands": len(successful_moves),
                "movementHashChanged": hash_changed,
                "afterMoveReady": after_ready,
                "comparisonSuccess": after_comparison.get("success") is True,
                "moveResults": move_results,
                "errors": [],
            }
            if active_humans and len(successful_moves) != len(active_humans):
                record["errors"].append("not_all_active_humans_moved")
            if successful_moves and not hash_changed:
                record["errors"].append("placedUnitsHash_not_changed")
            if not after_ready:
                record["errors"].append("after_move_ready_timeout")
            if after_comparison.get("success") is not True:
                record["warnings"] = [f"comparison.{warning}" for warning in after_comparison.get("warnings") or []]
                record["warnings"].extend(f"comparison.{error}" for error in after_comparison.get("errors") or ["failed"])
            errors.extend(f"{label}.{error}" for error in record["errors"])
            move_records.append(record)
            write_json(artifact_dir / f"{label}-record.json", record)
            write_json(artifact_dir / "game-to-end-move-records.json", move_records)

            start_results = start_human_bots(clients, peers, artifact_dir, args, f"after-{label}")
            if not all((result.get("success") is True) for result in start_results.values()):
                errors.append(f"{label}.bot_restart_failed")
            write_json(
                artifact_dir / f"unfreeze-game-flow-{label}.json",
                safe_request(lambda label=label: clients["host"].freeze_game_flow(False, f"two_humanbot_two_ai_{label}_resume")),
            )
            moved_rounds.add(current_round)

        if errors and not args.continue_game_end_on_move_error:
            limit_reason = "move_verification_failed"
            break
        if args.max_rounds > 0 and current_round >= args.max_rounds and (not same_prepare_round or current_round in moved_rounds):
            limit_reason = "max_rounds_reached"
            break
        time.sleep(max(0.5, args.poll_interval_seconds))

    if limit_reason == "none":
        limit_reason = "max_duration_reached" if time.time() >= deadline else "stopped"

    if not final_host:
        final_host = safe_request(clients["host"].dump_state)
    if not final_client:
        final_client = safe_request(clients["client"].dump_state)
    final_comparison = compare_snapshots(final_host, final_client) if final_host and final_client else final_comparison
    write_json(artifact_dir / "snapshots" / "host-game-end-final.json", final_host)
    write_json(artifact_dir / "snapshots" / "client-game-end-final.json", final_client)
    write_json(artifact_dir / "comparison-game-end-final.json", final_comparison)
    if not battle_hud_captured:
        battle_hud_capture = {"captured": False}
        write_json(artifact_dir / "battle-hud-capture.json", battle_hud_capture)
        errors.append("battle_hud_screenshot_not_captured")

    final_host_game = game(final_host)
    final_client_game = game(final_client)
    max_round_pass = (
        args.allow_max_round_result
        and limit_reason == "max_rounds_reached"
        and args.max_rounds > 0
        and to_int(final_host_game.get("currentRound")) >= args.max_rounds
        and to_int(final_client_game.get("currentRound")) >= args.max_rounds
    )
    game_over_pass = final_host_game.get("currentState") == "GameOver" and final_client_game.get("currentState") == "GameOver"
    if not game_over_pass and not max_round_pass:
        errors.append("game_over_not_reached")
    if final_comparison.get("success") is not True:
        errors.extend(f"final_comparison.{error}" for error in final_comparison.get("errors") or ["failed"])
    if not move_records:
        errors.append("no_prepare_move_records")
    if args.wall_command_every_prepare and not wall_records:
        errors.append("no_prepare_wall_records")

    result = {
        "success": not errors,
        "gameToEndPass": game_over_pass and not errors,
        "boundedProgressionPass": max_round_pass and not errors,
        "finalStatus": "PASS" if (game_over_pass or max_round_pass) and not errors else "FAIL",
        "limitReason": limit_reason,
        "errors": errors,
        "warnings": warnings,
        "moveRounds": sorted(moved_rounds),
        "prepareMoveRounds": len(move_records),
        "successfulPrepareMoveCommands": sum(to_int(record.get("successfulMoveCommands")) for record in move_records),
        "moveRecordsPath": "game-to-end-move-records.json",
        "prepareWallRounds": len(wall_records),
        "successfulPlaceWallCommands": sum(to_int(record.get("successfulPlaceWallCommands")) for record in wall_records),
        "successfulRemoveWallCommands": sum(to_int(record.get("successfulRemoveWallCommands")) for record in wall_records),
        "wallRecordsPath": "game-to-end-wall-records.json" if wall_records else None,
        "progressTimelinePath": "game-to-end-progress-timeline.json",
        "battleHudCapture": battle_hud_capture,
        "finalHost": final_host,
        "finalClient": final_client,
        "finalComparison": final_comparison,
        "hostGame": final_host_game,
        "clientGame": final_client_game,
    }
    write_json(artifact_dir / "game-to-end-move-result.json", result)
    return result


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    baseline_pids = mdf_player_pids()
    failures: list[str] = []
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "UNKNOWN",
        "cleanupSuccess": False,
        "orphanedPids": [],
    }

    if player_path is None or not player_path.exists():
        failures.append("player_path_missing")
        result = write_standard_result(artifact_dir, CASE_NAME, failures, cleanup_report, headless_player=args.headless_player)
        print(json.dumps(result, indent=2))
        return 2

    session = args.session or new_session("2h2ai")
    peers = [
        {"name": "host", "role": "host", "persona": "balanced", "seed": args.seed, "botSeed": args.seed + 100},
        {"name": "client", "role": "client", "persona": "unit", "seed": args.seed + 1, "botSeed": args.seed + 101},
    ]
    for peer in peers:
        peer["token"] = new_token()
        peer["connection"] = new_token()
        peer["port"] = free_port()

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "expectedPlayers": EXPECTED_PLAYERS,
        "expectedHumans": EXPECTED_HUMANS,
        "expectedAi": EXPECTED_AI,
        "moveRound": args.move_round,
        "moveEveryPrepareUntilGameOver": args.move_every_prepare_until_game_over,
        "wallCommandEveryPrepare": args.wall_command_every_prepare,
        "maxDurationSeconds": args.max_duration_seconds,
        "maxRounds": args.max_rounds,
        "allowMaxRoundResult": args.allow_max_round_result,
        "botPrepareMode": args.bot_prepare_mode,
        "verifyKingGoalPlacement": args.verify_king_goal_placement,
        "headlessPlayer": args.headless_player,
        "dryRun": args.dry_run,
    })
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    orphan_gate = write_orphan_pressure_report(artifact_dir, args.orphan_threshold, args.force_run_with_orphans)
    if orphan_gate.get("blocked"):
        failures.append("orphan_pressure_gate_blocked")
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {"cleanupStatus": "NEEDS_ENVIRONMENT", "cleanupSuccess": False, "orphanedPids": []},
            headless_player=args.headless_player,
            extra={"orphanPressure": orphan_gate},
        )
        print(json.dumps(result, indent=2))
        return 2

    processes: list[PlayerProcess] = []
    clients: dict[str, AutomationClient] = {}
    before_move_host: dict[str, Any] = {}
    after_move_host: dict[str, Any] = {}
    after_move_client: dict[str, Any] = {}
    final_comparison: dict[str, Any] = {"success": False, "errors": ["not_started"], "warnings": []}
    game_end_move_result: dict[str, Any] = {}
    king_goal_verification: dict[str, Any] | None = None
    king_goal_ai_screenshots: dict[str, Any] | None = None
    goal_rejection: dict[str, Any] | None = None

    try:
        for peer in peers:
            proc = launch_player(
                player_path,
                str(peer["role"]),
                session,
                int(peer["port"]),
                str(peer["token"]),
                str(peer["connection"]),
                artifact_dir,
                str(peer["name"]),
                max_players=EXPECTED_PLAYERS,
                scene=args.lobby_scene,
                case_name=CASE_NAME,
                auto_start=False,
                load_game=False,
                seed=int(peer["seed"]),
                scenario=CASE_NAME,
                extra_args=bot_args(args, peer, artifact_dir),
                headless_player=args.headless_player,
            )
            processes.append(proc)
            clients[str(peer["name"])] = AutomationClient(int(peer["port"]), str(peer["token"]), timeout=args.request_timeout)
            ping = clients[str(peer["name"])].wait_ping(timeout_seconds=args.ping_timeout)
            write_json(artifact_dir / f"{peer['name']}-ping.json", ping)
            if ping.get("success") is not True:
                failures.append(f"{peer['name']}_automation_ping_timeout")
            write_json(
                artifact_dir / f"{peer['name']}-bot-paused.json",
                safe_request(lambda peer=peer: clients[str(peer["name"])].bot_stop(reason="pre_lobby_pause")),
            )

        if not wait_build_peer_started(clients["host"].start_host, artifact_dir, "host", session, args.lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(clients["client"].join, artifact_dir, "client", session, args.lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
            failures.append("client_join_timeout")
        if not wait_lobby_ready(clients["host"], clients["client"], artifact_dir, args.lobby_timeout, args.lobby_scene):
            failures.append("lobby_ready_timeout")

        load_result = safe_request(lambda: clients["host"].load_game(args.scene))
        write_json(artifact_dir / "host-load-game.json", load_result)
        if load_result.get("success") is not True:
            failures.append("host_load_game_failed")

        before_bot_host, before_bot_client, before_comparison, before_ready = wait_game_ready(
            clients["host"], clients["client"], artifact_dir, args.state_timeout, args.scene, "before-bot"
        )
        write_json(artifact_dir / "comparison-before-bot.json", before_comparison)
        if not before_ready:
            failures.append("before_bot_ready_timeout")

        if args.move_every_prepare_until_game_over:
            write_json(artifact_dir / "freeze-game-flow.json", {
                "success": True,
                "skipped": True,
                "reason": "game_end_prepare_move_loop",
            })
        elif args.move_round <= 1:
            write_json(artifact_dir / "freeze-game-flow.json", safe_request(lambda: clients["host"].freeze_game_flow(True, "two_humanbot_two_ai_smoke")))
        else:
            write_json(artifact_dir / "freeze-game-flow.json", {
                "success": True,
                "skipped": True,
                "reason": "waiting_for_target_prepare_round",
                "targetRound": args.move_round,
            })
        for peer in peers:
            name = str(peer["name"])
            result = safe_request(lambda peer=peer, name=name: clients[name].bot_start(
                persona=str(peer["persona"]),
                seed=int(peer["botSeed"]),
                durationSeconds=args.bot_duration_seconds,
                stopAtRound=args.bot_stop_at_round,
                maxCommands=args.bot_max_commands,
                skipPrepare=args.bot_prepare_mode == "skip",
                prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
                journalPath=str(artifact_dir / f"{name}-bot.jsonl"),
            ))
            write_json(artifact_dir / f"{name}-bot-start.json", result)
            if result.get("success") is not True:
                failures.append(f"{name}_bot_start_failed")

        bot_statuses = wait_bot_commands(
            clients,
            artifact_dir,
            args.bot_timeout,
            0 if args.move_every_prepare_until_game_over else 1,
        )
        if args.move_every_prepare_until_game_over:
            game_end_move_result = run_game_to_end_prepare_move_loop(clients, peers, artifact_dir, args)
            final_comparison = game_end_move_result.get("finalComparison") or final_comparison
            after_move_host = game_end_move_result.get("finalHost") or {}
            after_move_client = game_end_move_result.get("finalClient") or {}
            if game_end_move_result.get("success") is not True:
                failures.extend(game_end_move_result.get("errors") or ["game_end_prepare_move_failed"])
        else:
            if args.move_round <= 1:
                before_move_host, _, _, before_move_ready = wait_game_ready(
                    clients["host"], clients["client"], artifact_dir, args.state_timeout, args.scene, "before-move"
                )
            else:
                before_move_host, _, target_comparison, target_prepare_seen = wait_target_prepare_ready(
                    clients["host"],
                    clients["client"],
                    artifact_dir,
                    args.target_prepare_timeout,
                    args.scene,
                    f"round-{args.move_round}-target-prepare",
                    args.move_round,
                    require_comparison=False,
                )
                write_json(artifact_dir / f"comparison-round-{args.move_round}-target-prepare.json", target_comparison)
                if target_prepare_seen:
                    write_json(artifact_dir / f"freeze-game-flow-round-{args.move_round}.json", safe_request(lambda: clients["host"].freeze_game_flow(True, f"two_humanbot_two_ai_smoke_round_{args.move_round}_move")))
                    before_move_host, _, target_comparison, before_move_ready = wait_target_prepare_ready(
                        clients["host"],
                        clients["client"],
                        artifact_dir,
                        args.state_timeout,
                        args.scene,
                        f"round-{args.move_round}-before-move",
                        args.move_round,
                        require_comparison=False,
                    )
                    write_json(artifact_dir / f"comparison-round-{args.move_round}-before-move.json", target_comparison)
                else:
                    before_move_ready = False
            if not before_move_ready:
                failures.append("before_move_ready_timeout")

            bot_stop_results, bots_stopped = stop_bots_before_move(clients, artifact_dir)
            if not all((result.get("success") is True) for result in bot_stop_results.values()) or not bots_stopped:
                failures.append("bot_stop_before_move_failed")

            if args.move_round <= 1:
                before_move_host, _, _, before_move_ready = wait_game_ready(
                    clients["host"], clients["client"], artifact_dir, args.state_timeout, args.scene, "before-manual-move"
                )
            else:
                before_move_host, _, target_comparison, before_move_ready = wait_target_prepare_ready(
                    clients["host"],
                    clients["client"],
                    artifact_dir,
                    args.state_timeout,
                    args.scene,
                    f"round-{args.move_round}-before-manual-move",
                    args.move_round,
                    require_comparison=False,
                )
                write_json(artifact_dir / f"comparison-round-{args.move_round}-before-manual-move.json", target_comparison)
            if not before_move_ready:
                failures.append("before_manual_move_ready_timeout")

            if args.verify_king_goal_placement and before_move_ready:
                goal_rejection = verify_goal_cell_move_rejection(
                    clients["host"],
                    clients["client"],
                    before_move_host,
                    artifact_dir,
                )
            else:
                write_json(artifact_dir / "goal-cell-rejection.json", {
                    "success": True,
                    "skipped": True,
                    "reason": "verification_disabled_or_prepare_not_ready",
                })

            move_results = issue_move_commands(clients["host"], before_move_host, artifact_dir)
            if args.move_round <= 1:
                after_move_host, after_move_client, final_comparison, after_move_ready = wait_game_ready(
                    clients["host"], clients["client"], artifact_dir, args.state_timeout, args.scene, "after-move"
                )
            else:
                after_move_host, after_move_client, final_comparison, after_move_ready = wait_target_prepare_ready(
                    clients["host"],
                    clients["client"],
                    artifact_dir,
                    args.state_timeout,
                    args.scene,
                    f"round-{args.move_round}-after-move",
                    args.move_round,
                )
            if not after_move_ready:
                failures.append("after_move_ready_timeout")

            write_json(artifact_dir / "snapshots" / "host-before-move.json", before_move_host)
            write_json(artifact_dir / "snapshots" / "host-after-move.json", after_move_host)
            write_json(artifact_dir / "snapshots" / "client-after-move.json", after_move_client)
            write_json(artifact_dir / "comparison-after-move.json", final_comparison)
            if args.verify_king_goal_placement and after_move_ready:
                king_goal_verification = verify_king_goal_stability(clients, artifact_dir)
                if args.headless_player:
                    king_goal_ai_screenshots = {
                        "success": True,
                        "skipped": True,
                        "reason": "headless_player",
                    }
                    write_json(artifact_dir / "king-goal-ai-field-screenshots.json", king_goal_ai_screenshots)
                else:
                    king_goal_ai_screenshots = capture_ai_field_screenshots(
                        clients["host"],
                        after_move_host,
                        artifact_dir,
                    )
                    if king_goal_ai_screenshots.get("success") is not True:
                        failures.extend(
                            king_goal_ai_screenshots.get("errors")
                            or ["king_goal_ai_field_screenshot_failed"]
                        )
            else:
                king_goal_verification = None
                write_json(artifact_dir / "king-goal-placement-verification.json", {
                    "success": True,
                    "skipped": True,
                    "reason": "verification_disabled_or_prepare_not_ready",
                })
                king_goal_ai_screenshots = {
                    "success": True,
                    "skipped": True,
                    "reason": "verification_disabled_or_prepare_not_ready",
                }
                write_json(artifact_dir / "king-goal-ai-field-screenshots.json", king_goal_ai_screenshots)
            assertions = build_assertions(
                before_move_host,
                after_move_host,
                after_move_client,
                final_comparison,
                bot_statuses,
                move_results,
                king_goal_verification,
                goal_rejection,
            )
            write_json(artifact_dir / "two-humanbot-two-ai-assertions.json", assertions)
            if assertions.get("success") is not True:
                failures.extend(assertions.get("errors") or ["assertions_failed"])

        logs_recent = {
            name: safe_request(client.logs_recent)
            for name, client in clients.items()
        }
        for name, logs in logs_recent.items():
            write_json(artifact_dir / f"{name}-logs-recent.json", logs)
            peer_client = clients.get(name)
            if args.headless_player:
                write_json(artifact_dir / f"{name}-screenshot.json", {
                    "success": True,
                    "skipped": True,
                    "reason": "headless_player",
                    "headlessPlayer": True,
                })
            elif peer_client is None:
                write_json(artifact_dir / f"{name}-screenshot.json", {
                    "success": False,
                    "error": {
                        "code": "peer_client_missing",
                        "details": name,
                    },
                })
            else:
                write_json(
                    artifact_dir / f"{name}-prepare-visual-capture.json",
                    safe_request(lambda peer_client=peer_client: peer_client.command(name="prepare_visual_capture")),
                )
                time.sleep(0.25)
                write_json(artifact_dir / f"{name}-screenshot.json", safe_request(peer_client.screenshot))
            for line in ((logs.get("data") or {}).get("lines") or []):
                if isinstance(line, str) and "[MPTEST]" in line and ("result=fail" in line or " phase=error" in line):
                    failures.append(f"mptest_failure_log:{name}:{line}")

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            processes,
            baseline_pids=baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(failures),
            strict_cleanup=args.strict_cleanup,
        )
        copied = collect_player_log(artifact_dir, "last-player")
        logs = [artifact_dir / f"{name}.stdout.log" for name in ("host", "client")]
        logs.extend(artifact_dir / f"{name}.stderr.log" for name in ("host", "client"))
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    result = write_standard_result(
        artifact_dir,
        CASE_NAME,
        failures,
        cleanup_report,
        headless_player=args.headless_player,
        extra={
            "comparisonSuccess": final_comparison.get("success") is True,
            "gameToEndPass": game_end_move_result.get("gameToEndPass") if game_end_move_result else None,
            "boundedProgressionPass": game_end_move_result.get("boundedProgressionPass") if game_end_move_result else None,
            "finalStatus": game_end_move_result.get("finalStatus") if game_end_move_result else None,
            "prepareMoveRounds": game_end_move_result.get("prepareMoveRounds") if game_end_move_result else None,
            "successfulPrepareMoveCommands": game_end_move_result.get("successfulPrepareMoveCommands") if game_end_move_result else None,
            "prepareWallRounds": game_end_move_result.get("prepareWallRounds") if game_end_move_result else None,
            "successfulPlaceWallCommands": game_end_move_result.get("successfulPlaceWallCommands") if game_end_move_result else None,
            "successfulRemoveWallCommands": game_end_move_result.get("successfulRemoveWallCommands") if game_end_move_result else None,
            "gameEndMoveResultPath": "game-to-end-move-result.json" if game_end_move_result else None,
            "kingGoalPlacementVerification": king_goal_verification.get("success") if king_goal_verification else None,
            "kingGoalPlacementVerificationPath": "king-goal-placement-verification.json" if args.verify_king_goal_placement else None,
            "kingGoalAiFieldScreenshots": king_goal_ai_screenshots.get("success") if king_goal_ai_screenshots else None,
            "kingGoalAiFieldScreenshotsPath": "king-goal-ai-field-screenshots.json" if args.verify_king_goal_placement else None,
            "goalCellMoveRejection": goal_rejection.get("success") if goal_rejection else None,
            "goalCellMoveRejectionPath": "goal-cell-rejection.json" if args.verify_king_goal_placement else None,
        },
    )
    print(json.dumps(result, indent=2))
    return 0 if result["success"] else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=6201)
    parser.add_argument("--bot-duration-seconds", type=int, default=90)
    parser.add_argument("--bot-stop-at-round", type=int, default=0)
    parser.add_argument("--bot-max-commands", type=int, default=8)
    parser.add_argument("--bot-prepare-mode", choices=["full", "augment-only", "skip"], default="full")
    parser.add_argument("--move-round", type=int, default=1)
    parser.add_argument("--move-every-prepare-until-game-over", action="store_true")
    parser.add_argument("--wall-command-every-prepare", action="store_true")
    parser.add_argument("--max-duration-seconds", type=int, default=1800)
    parser.add_argument("--max-rounds", type=int, default=0)
    parser.add_argument("--allow-max-round-result", action="store_true")
    parser.add_argument("--poll-interval-seconds", type=float, default=2.0)
    parser.add_argument("--game-over-timeout", type=int, default=120)
    parser.add_argument("--continue-game-end-on-move-error", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    parser.add_argument("--verify-king-goal-placement", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=90)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--target-prepare-timeout", type=int, default=420)
    parser.add_argument("--bot-timeout", type=int, default=120)
    parser.add_argument("--request-timeout", type=float, default=10.0)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=20.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    args = parser.parse_args()
    if args.move_round < 1:
        raise SystemExit("--move-round must be >= 1")
    if args.max_duration_seconds <= 0:
        raise SystemExit("--max-duration-seconds must be > 0")
    if args.max_rounds < 0:
        raise SystemExit("--max-rounds must be >= 0")
    if args.game_over_timeout <= 0:
        raise SystemExit("--game-over-timeout must be > 0")
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
