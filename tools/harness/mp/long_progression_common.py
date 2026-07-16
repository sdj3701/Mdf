from __future__ import annotations

import json
import pathlib
import time
from collections import Counter
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import write_timeline
from common import (
    normalize_snapshot_response,
    read_json,
    snapshot_not_ready_reasons,
    snapshot_ready,
    write_json,
)
from compare_state_snapshots import compare_snapshots


CAPTURE_STATES = {"Prepare", "Battle1", "Battle2", "GameOver"}
BATTLE_STATES = {"Battle1", "Battle2"}
STATE_ORDER = {
    "Setup": 0,
    "DataLoading": 1,
    "Prepare": 2,
    "Battle1": 3,
    "Battle2": 4,
    "GameOver": 5,
}
OBSERVE_COMMANDS = {"", "Observe", "observe", None}


def safe_request(call: Any) -> dict[str, Any]:
    try:
        result = call()
        return result if isinstance(result, dict) else {"success": True, "data": result}
    except Exception as exc:
        return {
            "success": False,
            "error": {
                "code": type(exc).__name__,
                "details": str(exc),
            },
        }


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = safe_request(client.dump_state)
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def state(data: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(data)
    return normalized if isinstance(normalized, dict) else {}


def game(data: Any) -> dict[str, Any]:
    payload = state(data)
    value = payload.get("game")
    return value if isinstance(value, dict) else {}


def players(data: Any) -> list[dict[str, Any]]:
    return [player for player in state(data).get("players") or [] if isinstance(player, dict)]


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def unique_player_ids(snapshot: Any) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids)) and all(isinstance(player_id, int) and player_id >= 0 for player_id in ids)


def checkpoint_key(snapshot: Any) -> tuple[int, str] | None:
    data = game(snapshot)
    current_state = data.get("currentState")
    current_round = data.get("currentRound")
    if not isinstance(current_round, int) or not isinstance(current_state, str):
        return None
    if current_state not in CAPTURE_STATES:
        return None
    return current_round, current_state


def checkpoint_id(index: int, round_number: int, current_state: str) -> str:
    return f"{index:03d}-r{round_number:02d}-{current_state.lower()}"


def checkpoint_summary(snapshot: Any) -> dict[str, Any]:
    data = state(snapshot)
    current_game = data.get("game") if isinstance(data.get("game"), dict) else {}
    commands = data.get("commands") if isinstance(data.get("commands"), dict) else {}
    effects = data.get("effects") if isinstance(data.get("effects"), dict) else {}
    player_rows: list[dict[str, Any]] = []
    for player in players(snapshot):
        player_rows.append({
            "playerId": player.get("playerId"),
            "health": player.get("health"),
            "gold": player.get("gold"),
            "wallCount": player.get("wallCount"),
            "isAI": player.get("isAI"),
            "isConnected": player.get("isConnected"),
            "shopHash": nested(player, "shop", "itemsHash"),
            "shopRevision": nested(player, "shop", "revision"),
            "fieldGridHash": nested(player, "field", "gridHash"),
            "fieldWallHash": nested(player, "field", "wallHash"),
            "fieldUnitsHash": nested(player, "field", "placedUnitsHash"),
            "attackMonsterPoolHash": player.get("attackMonsterPoolHash"),
            "blackMagicCurrent": player.get("blackMagicCurrent"),
            "blackMagicMaximum": player.get("blackMagicMaximum"),
            "blackMagicMaxBonus": player.get("blackMagicMaxBonus"),
            "blackMagicRevision": player.get("blackMagicRevision"),
            "blackMagicSequenceId": player.get("blackMagicSequenceId"),
            "ownedScrollsHash": player.get("ownedScrollsHash"),
            "monsterAliveCount": nested(player, "monsters", "aliveCount"),
            "monsterTypeHash": nested(player, "monsters", "typeHash"),
            "monsterOriginHash": nested(player, "monsters", "ownerOriginHash"),
            "monsterTargetHash": nested(player, "monsters", "targetPlayerHash"),
            "augmentActiveEffectHash": nested(player, "augment", "activeEffectHash"),
            "augmentActiveTargetHash": nested(player, "augment", "activeTargetHash"),
        })
    return {
        "scene": data.get("scene"),
        "game": {
            "currentRound": current_game.get("currentRound"),
            "currentState": current_game.get("currentState"),
            "battlePhase": current_game.get("battlePhase"),
            "battleOpponentsHash": current_game.get("battleOpponentsHash"),
            "matchFirstAttackerHash": current_game.get("matchFirstAttackerHash"),
            "battleActiveHash": current_game.get("battleActiveHash"),
        },
        "players": player_rows,
        "effects": effects,
        "commands": commands,
    }


def battle2_camera_expectation(snapshot: Any) -> tuple[dict[str, Any], list[str]]:
    current_game = game(snapshot)
    current_state = current_game.get("currentState")
    snapshot_players = players(snapshot)
    errors: list[str] = []

    if current_state != "Battle2":
        errors.append(f"camera_snapshot_not_battle2:{current_state}")

    local_players = [player for player in snapshot_players if player.get("isLocal") is True]
    if len(local_players) != 1:
        errors.append(f"camera_local_player_count:{len(local_players)}")
        return {
            "currentState": current_state,
            "localPlayerId": None,
            "expectedViewingPlayerId": None,
            "expectedAttackMode": None,
            "role": "unknown",
        }, errors

    local_player = local_players[0]
    local_player_id = local_player.get("playerId")
    if not isinstance(local_player_id, int) or local_player_id < 0:
        errors.append(f"camera_local_player_id_invalid:{local_player_id}")

    is_attacker = local_player.get("isAttackerInCurrentBattle")
    if not isinstance(is_attacker, bool):
        errors.append(f"camera_local_attacker_flag_invalid:{is_attacker}")
        is_attacker = False

    expected_viewing_player_id = local_player_id
    if is_attacker:
        opponent_ids = [
            player.get("playerId")
            for player in snapshot_players
            if isinstance(player.get("playerId"), int) and player.get("playerId") != local_player_id
        ]
        if len(opponent_ids) != 1:
            errors.append(f"camera_two_player_opponent_count:{len(opponent_ids)}")
            expected_viewing_player_id = None
        else:
            expected_viewing_player_id = opponent_ids[0]

    return {
        "currentState": current_state,
        "localPlayerId": local_player_id,
        "expectedViewingPlayerId": expected_viewing_player_id,
        "expectedAttackMode": is_attacker,
        "role": "attacker" if is_attacker else "defender",
    }, errors


def evaluate_battle2_camera_response(
    expectation: dict[str, Any],
    response: dict[str, Any],
) -> list[str]:
    errors: list[str] = []
    if response.get("success") is not True:
        error = response.get("error") if isinstance(response.get("error"), dict) else {}
        errors.append(f"camera_probe_failed:{error.get('code') or response.get('message') or 'unknown'}")

    payload = response.get("data")
    if not isinstance(payload, dict):
        errors.append("camera_probe_payload_missing")
        return errors

    expected_viewing = expectation.get("expectedViewingPlayerId")
    expected_own = expectation.get("localPlayerId")
    expected_attack_mode = expectation.get("expectedAttackMode")
    expected_fields = {
        "requestedPlayerId": expected_viewing,
        "requestNavigation": False,
        "ownPlayerId": expected_own,
        "viewingPlayerId": expected_viewing,
        "currentViewingMatchesRegistry": True,
        "targetOnCurrentRunner": True,
        "transitioning": False,
        "attackMode": expected_attack_mode,
        "switched": True,
    }
    for field, expected in expected_fields.items():
        actual = payload.get(field)
        if actual != expected:
            errors.append(f"camera_{field}_mismatch:expected={expected}:actual={actual}")
    return errors


def probe_battle2_camera_checkpoint(
    client: AutomationClient,
    snapshot: Any,
    artifact_dir: pathlib.Path,
    checkpoint_label: str,
    peer_label: str,
    timeout: float,
) -> dict[str, Any]:
    expectation, expectation_errors = battle2_camera_expectation(snapshot)
    expected_viewing = expectation.get("expectedViewingPlayerId")
    started = time.monotonic()
    deadline = started + max(0.1, timeout)
    attempts = 0
    response: dict[str, Any] = {}
    response_errors: list[str] = []

    if not expectation_errors and isinstance(expected_viewing, int):
        while True:
            attempts += 1
            response = safe_request(lambda: client.command(
                name="view_player_field",
                playerId=expected_viewing,
                requestNavigation=False,
            ))
            response_errors = evaluate_battle2_camera_response(expectation, response)
            if not response_errors or time.monotonic() >= deadline:
                break
            time.sleep(0.25)

    errors = list(expectation_errors)
    errors.extend(response_errors)
    report = {
        "success": not errors,
        "checkpointId": checkpoint_label,
        "peer": peer_label,
        "requestNavigation": False,
        "expectation": expectation,
        "attempts": attempts,
        "elapsedSeconds": max(0.0, time.monotonic() - started),
        "response": response,
        "errors": errors,
    }
    report_path = artifact_dir / "camera-checkpoints" / f"{checkpoint_label}-{peer_label}.json"
    write_json(report_path, report)
    return report


def verify_battle2_camera_checkpoints(
    host: AutomationClient,
    client: AutomationClient,
    host_snapshot: Any,
    client_snapshot: Any,
    artifact_dir: pathlib.Path,
    checkpoint_label: str,
    client_peer: str,
    timeout: float,
) -> dict[str, Any]:
    host_report = probe_battle2_camera_checkpoint(
        host,
        host_snapshot,
        artifact_dir,
        checkpoint_label,
        "build-host",
        timeout,
    )
    client_report = probe_battle2_camera_checkpoint(
        client,
        client_snapshot,
        artifact_dir,
        checkpoint_label,
        client_peer,
        timeout,
    )
    errors = [f"build-host:{error}" for error in host_report.get("errors") or []]
    errors.extend(f"{client_peer}:{error}" for error in client_report.get("errors") or [])
    report = {
        "success": not errors,
        "checkpointId": checkpoint_label,
        "requestNavigation": False,
        "host": f"camera-checkpoints/{checkpoint_label}-build-host.json",
        "client": f"camera-checkpoints/{checkpoint_label}-{client_peer}.json",
        "hostRole": nested(host_report, "expectation", "role"),
        "clientRole": nested(client_report, "expectation", "role"),
        "errors": errors,
    }
    write_json(artifact_dir / "camera-checkpoints" / f"{checkpoint_label}.json", report)
    return report


def wait_checkpoint_comparison(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    *,
    index: int,
    round_number: int,
    current_state: str,
    expected_players: int,
    scene: str,
    timeout: int,
    client_peer: str = "build-client",
    verify_battle2_camera: bool = False,
    camera_timeout: float = 5.0,
) -> dict[str, Any]:
    label = checkpoint_id(index, round_number, current_state)
    deadline = time.time() + timeout
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    latest_comparison: dict[str, Any] = {"success": False, "errors": ["checkpoint_not_started"], "warnings": []}
    selected_host: dict[str, Any] = {}
    selected_client: dict[str, Any] = {}
    selected_comparison: dict[str, Any] = latest_comparison
    ready = False
    stable_match = False

    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        latest_client = dump_state(client, artifact_dir, client_peer, f"{label}-latest")
        host_ready = snapshot_ready(latest_host, expected_players, scene)
        client_ready = snapshot_ready(latest_client, expected_players, scene)
        host_key = checkpoint_key(latest_host)
        client_key = checkpoint_key(latest_client)
        state_matches = host_key == (round_number, current_state) and client_key == (round_number, current_state)
        latest_comparison = compare_snapshots(latest_host, latest_client) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        if state_matches:
            selected_host = latest_host
            selected_client = latest_client
            selected_comparison = latest_comparison
            ready = host_ready and client_ready
            stable_match = ready and latest_comparison.get("success") is True
        write_json(artifact_dir / "checkpoint-comparisons" / f"{label}-latest.json", {
            "checkpointId": label,
            "ready": host_ready and client_ready and state_matches,
            "stableMatch": host_ready and client_ready and state_matches and latest_comparison.get("success") is True,
            "hostKey": host_key,
            "clientKey": client_key,
            "hostReasons": snapshot_not_ready_reasons(latest_host, expected_players, scene),
            "clientReasons": snapshot_not_ready_reasons(latest_client, expected_players, scene),
            "comparison": latest_comparison,
        })
        if stable_match:
            break
        if selected_host and host_key != (round_number, current_state) and client_key != (round_number, current_state):
            break
        time.sleep(1)

    matched_requested_state = bool(selected_host)
    state_slipped = (
        not matched_requested_state
        and checkpoint_key(latest_host) == checkpoint_key(latest_client)
        and checkpoint_key(latest_host) != (round_number, current_state)
    )
    state_slipped_success = state_slipped and latest_comparison.get("success") is True

    if not selected_host:
        selected_host = latest_host
    if not selected_client:
        selected_client = latest_client
    if selected_comparison is None:
        selected_comparison = latest_comparison
    if state_slipped_success:
        selected_comparison = latest_comparison

    write_json(artifact_dir / "snapshots" / "checkpoints" / f"{label}-host.json", selected_host)
    write_json(artifact_dir / "snapshots" / "checkpoints" / f"{label}-client.json", selected_client)
    comparison_path = artifact_dir / "checkpoint-comparisons" / f"{label}.json"
    checkpoint_success = stable_match or state_slipped_success
    checkpoint_warnings = list(selected_comparison.get("warnings") or [])
    if state_slipped_success:
        checkpoint_warnings.append("checkpoint_state_slipped_before_capture")
    camera_verification: dict[str, Any] | None = None
    camera_errors: list[str] = []
    if verify_battle2_camera and current_state == "Battle2":
        camera_verification = verify_battle2_camera_checkpoints(
            host,
            client,
            selected_host,
            selected_client,
            artifact_dir,
            label,
            client_peer,
            camera_timeout,
        )
        camera_errors = [f"camera:{error}" for error in camera_verification.get("errors") or []]
        checkpoint_success = checkpoint_success and camera_verification.get("success") is True

    checkpoint_errors = (
        []
        if stable_match or state_slipped_success
        else list(selected_comparison.get("errors") or ["checkpoint_comparison_failed"])
    )
    checkpoint_errors.extend(camera_errors)
    checkpoint = {
        "checkpointId": label,
        "index": index,
        "round": round_number,
        "state": current_state,
        "hostSnapshot": f"snapshots/checkpoints/{label}-host.json",
        "clientSnapshot": f"snapshots/checkpoints/{label}-client.json",
        "comparison": f"checkpoint-comparisons/{label}.json",
        "ready": ready,
        "success": checkpoint_success,
        "skipped": state_slipped_success,
        "hostSummary": checkpoint_summary(selected_host),
        "clientSummary": checkpoint_summary(selected_client),
        "errors": checkpoint_errors,
        "warnings": checkpoint_warnings,
    }
    comparison_report = {
        "checkpointId": label,
        "success": checkpoint_success,
        "skipped": state_slipped_success,
        "ready": ready,
        "comparison": selected_comparison,
        "hostSummary": checkpoint["hostSummary"],
        "clientSummary": checkpoint["clientSummary"],
    }
    if camera_verification is not None:
        checkpoint["cameraVerification"] = camera_verification
        comparison_report["cameraVerification"] = camera_verification
    write_json(comparison_path, comparison_report)
    return checkpoint


def completion_satisfied(
    snapshot: Any,
    checkpoints: list[dict[str, Any]],
    *,
    target_round: int,
    completion_mode: str,
    allow_early_game_over: bool,
) -> tuple[bool, str]:
    current_game = game(snapshot)
    current_round = current_game.get("currentRound")
    current_state = current_game.get("currentState")
    if current_state == "GameOver":
        return (True, "early_game_over_allowed") if allow_early_game_over else (False, "game_over_before_target")

    if completion_mode == "round-complete":
        if isinstance(current_round, int) and current_round >= target_round + 1 and current_state == "Prepare":
            return True, "target_round_complete"
        return False, "target_round_not_complete"

    reached_battle2 = False
    for checkpoint in checkpoints:
        checkpoint_round = checkpoint.get("round")
        checkpoint_state = checkpoint.get("state")
        if isinstance(checkpoint_round, int) and checkpoint_round > target_round:
            reached_battle2 = True
            break
        if checkpoint_round == target_round and STATE_ORDER.get(str(checkpoint_state), -1) >= STATE_ORDER["Battle2"]:
            reached_battle2 = True
            break
    if isinstance(current_round, int):
        if current_round > target_round:
            reached_battle2 = True
        elif current_round >= target_round and STATE_ORDER.get(str(current_state), -1) >= STATE_ORDER["Battle2"]:
            reached_battle2 = True
    return (True, "target_battle2_reached") if reached_battle2 else (False, "target_battle2_not_reached")


def known_command(value: Any) -> str | None:
    if value in OBSERVE_COMMANDS:
        return None
    text = str(value).strip()
    return text if text and text not in OBSERVE_COMMANDS else None


def iter_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            parsed = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(parsed, dict):
            rows.append(parsed)
    return rows


def to_int(value: Any, default: int = 0) -> int:
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return int(value)
    if isinstance(value, str):
        try:
            return int(float(value))
        except ValueError:
            return default
    return default


def command_metrics(artifact_dir: pathlib.Path, checkpoints: list[dict[str, Any]]) -> dict[str, Any]:
    commands_by_type: Counter[str] = Counter()
    requested_by_type: Counter[str] = Counter()
    rejected_by_reason: Counter[str] = Counter()
    for path in sorted(artifact_dir.glob("*bot*.jsonl")):
        for entry in iter_jsonl(path):
            if entry.get("kind") != "bot_decision":
                continue
            decision = entry.get("decision") if isinstance(entry.get("decision"), dict) else {}
            command = known_command(decision.get("commandType"))
            if command:
                requested_by_type[command] += 1

    timeline_path = artifact_dir / "mptest.timeline.jsonl"
    for entry in iter_jsonl(timeline_path):
        phase = entry.get("phase")
        result_value = entry.get("result")
        command = known_command(entry.get("commandType") or entry.get("code"))
        if phase == "human_bot_decision" and result_value == "begin" and command:
            commands_by_type[command] += 1
        elif phase == "human_bot_decision" and result_value == "fail":
            rejected_by_reason[str(entry.get("errorCode") or entry.get("msg") or "unknown")] += 1
        elif phase in {"battle_command_rejected", "battle_spawn_rejected", "scroll_rejected", "skill_command_rejected"}:
            rejected_by_reason[str(entry.get("errorCode") or entry.get("msg") or "unknown")] += 1

    if not commands_by_type:
        commands_by_type.update(requested_by_type)

    max_spawn = 0
    max_scroll = 0
    for checkpoint in checkpoints:
        commands = nested(checkpoint.get("hostSummary"), "commands") or {}
        max_spawn = max(max_spawn, to_int(commands.get("spawnMonsterSeq")))
        max_scroll = max(max_scroll, to_int(commands.get("useMagicScrollSeq")))

    buy_count = commands_by_type.get("BuyUnit", 0)
    reroll_count = commands_by_type.get("RerollShop", 0)
    return {
        "commandsByType": {key: commands_by_type[key] for key in sorted(commands_by_type)},
        "commandsRequestedByType": {key: requested_by_type[key] for key in sorted(requested_by_type)},
        "decisionsRejectedByReason": {key: rejected_by_reason[key] for key in sorted(rejected_by_reason)},
        "buyUnitToRerollShopRatio": round(buy_count / reroll_count, 3) if reroll_count > 0 else None,
        "BattleSpawnMonsterCommandCount": max_spawn,
        "UseMagicScrollCommandCount": max_scroll,
    }


def progression_metrics(
    artifact_dir: pathlib.Path,
    checkpoints: list[dict[str, Any]],
    cleanup_report: dict[str, Any],
) -> dict[str, Any]:
    states_reached: list[str] = []
    max_round = 0
    for checkpoint in checkpoints:
        round_number = to_int(checkpoint.get("round"))
        current_state = str(checkpoint.get("state") or "unknown")
        max_round = max(max_round, round_number)
        key = f"R{round_number}:{current_state}"
        if key not in states_reached:
            states_reached.append(key)

    metrics = {
        "maxRoundReached": max_round,
        "statesReached": states_reached,
        "checkpointCount": len(checkpoints),
        "checkpointSuccessCount": sum(1 for checkpoint in checkpoints if checkpoint.get("success") is True),
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
    }
    metrics.update(command_metrics(artifact_dir, checkpoints))
    return {
        "schemaVersion": 1,
        "artifactDir": str(artifact_dir),
        "summary": metrics,
    }


def log_lines(response: dict[str, Any]) -> list[str]:
    data = response.get("data") if isinstance(response, dict) else None
    lines = data.get("lines") if isinstance(data, dict) else None
    return [line for line in lines or [] if isinstance(line, str)]


def mptest_failures(*responses: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    for response in responses:
        for line in log_lines(response):
            if "[MPTEST]" in line and ("result=fail" in line or " phase=error" in line):
                failures.append(line)
    return failures


def read_result(path: pathlib.Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        data = read_json(path)
        return data if isinstance(data, dict) else {}
    except Exception:
        return {}


def write_final_timeline(artifact_dir: pathlib.Path, peer_names: list[str]) -> None:
    logs: list[pathlib.Path] = []
    for peer in peer_names:
        logs.append(artifact_dir / f"{peer}.Player.log")
        logs.append(artifact_dir / f"{peer}.stdout.log")
        logs.append(artifact_dir / f"{peer}.stderr.log")
    write_timeline(artifact_dir, logs)
