from __future__ import annotations

import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from common import (
    normalize_snapshot_response,
    scene_matches,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    write_json,
)
from compare_state_snapshots import compare_snapshots


MEANINGFUL_COMMANDS = {"SelectAugment", "BuyUnit", "PlaceWall", "MoveUnit", "RerollShop"}
UNKNOWN = "unknown"


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    try:
        data = client.dump_state()
    except Exception as exc:
        data = {
            "success": False,
            "message": "dumpState request failed",
            "error": {
                "code": type(exc).__name__,
                "details": str(exc),
            },
        }
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def state(data: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(data)
    return normalized if isinstance(normalized, dict) else {}


def players(snapshot: Any) -> list[dict[str, Any]]:
    return [player for player in state(snapshot).get("players") or [] if isinstance(player, dict)]


def player_by_id(snapshot: Any, player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def local_player(snapshot: Any) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("isLocal") is True and player.get("hasInputAuthority") is True:
            return player
    return None


def local_player_id(snapshot: Any) -> int:
    player = local_player(snapshot)
    player_id = player.get("playerId") if isinstance(player, dict) else None
    return player_id if isinstance(player_id, int) else -1


def unique_player_ids(snapshot: Any) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids)) and all(isinstance(player_id, int) and player_id >= 0 for player_id in ids)


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def bot_payload(response: dict[str, Any]) -> dict[str, Any]:
    data = response.get("data") if isinstance(response, dict) else None
    return data if isinstance(data, dict) else {}


def meaningful_deltas(before_snapshot: Any, after_snapshot: Any, player_id: int) -> list[dict[str, Any]]:
    before = player_by_id(before_snapshot, player_id)
    after = player_by_id(after_snapshot, player_id)
    if before is None or after is None:
        return []

    fields = [
        ("gold", ("gold",)),
        ("wallCount", ("wallCount",)),
        ("permanentWallPlacementCount", ("permanentWallPlacementCount",)),
        ("permanentWallStockRevision", ("permanentWallStockRevision",)),
        ("permanentWallLayoutRevision", ("permanentWallLayoutRevision",)),
        ("shop.revision", ("shop", "revision")),
        ("shop.itemsHash", ("shop", "itemsHash")),
        ("augment.selectedCount", ("augment", "selectedCount")),
        ("augment.selectedHash", ("augment", "selectedHash")),
        ("field.wallHash", ("field", "wallHash")),
        ("field.placedUnitCount", ("field", "placedUnitCount")),
        ("field.placedUnitsHash", ("field", "placedUnitsHash")),
    ]
    deltas: list[dict[str, Any]] = []
    for name, path in fields:
        old = nested(before, *path)
        new = nested(after, *path)
        if old != new:
            deltas.append({"field": name, "before": old, "after": new})
    return deltas


def monotonic_revision(before_snapshot: Any, after_snapshot: Any, player_id: int) -> bool:
    before = player_by_id(before_snapshot, player_id)
    after = player_by_id(after_snapshot, player_id)
    before_revision = nested(before, "shop", "revision")
    after_revision = nested(after, "shop", "revision")
    if isinstance(before_revision, int) and isinstance(after_revision, int):
        return after_revision >= before_revision
    return True


def random_outcome_summary(host_snapshot: Any, client_snapshot: Any) -> dict[str, Any]:
    host_state = state(host_snapshot)
    client_by_id = {player.get("playerId"): player for player in players(client_snapshot)}
    summary: dict[str, Any] = {
        "session": host_state.get("session"),
        "scene": host_state.get("scene"),
        "players": [],
    }
    for host_player in players(host_snapshot):
        player_id = host_player.get("playerId")
        client_player = client_by_id.get(player_id)
        summary["players"].append({
            "playerId": player_id,
            "shopHash": nested(host_player, "shop", "itemsHash"),
            "shopRevision": nested(host_player, "shop", "revision"),
            "augmentPresentedHash": nested(host_player, "augment", "presentedHash"),
            "augmentSelectedHash": nested(host_player, "augment", "selectedHash"),
            "fieldWallHash": nested(host_player, "field", "wallHash"),
            "fieldUnitsHash": nested(host_player, "field", "placedUnitsHash"),
            "matchesClient": client_player is not None
            and nested(host_player, "shop", "itemsHash") == nested(client_player, "shop", "itemsHash")
            and nested(host_player, "augment", "presentedHash") == nested(client_player, "augment", "presentedHash")
            and nested(host_player, "augment", "selectedHash") == nested(client_player, "augment", "selectedHash")
            and nested(host_player, "field", "wallHash") == nested(client_player, "field", "wallHash")
            and nested(host_player, "field", "placedUnitsHash") == nested(client_player, "field", "placedUnitsHash"),
        })
    return summary


def wait_session_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    connected_players: int,
    client_peer: str,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "lobby-latest")
        client_state = dump_state(client, artifact_dir, client_peer, "lobby-latest")
        host_ready = session_ready(host_state, connected_players, scene)
        client_ready = session_ready(client_state, connected_players, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": session_not_ready_reasons(host_state, connected_players, scene),
            "clientReasons": session_not_ready_reasons(client_state, connected_players, scene),
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


def wait_stable_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    expected_players: int,
    label: str,
    client_peer: str,
    required_stable_samples: int = 1,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        client_state = dump_state(client, artifact_dir, client_peer, f"{label}-latest")
        host_ready = snapshot_ready(host_state, expected_players, scene)
        client_ready = snapshot_ready(client_state, expected_players, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        write_json(artifact_dir / f"{label}-state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, expected_players, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, expected_players, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
            "requiredStableSamples": required_stable_samples,
        })
        write_json(artifact_dir / f"{label}-comparison-latest.json", comparison)
        if host_ready and client_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= required_stable_samples:
                return host_state, client_state, comparison, True
        else:
            stable_matches = 0
        time.sleep(1)
    return host_state, client_state, comparison, False


def progression_assertions(
    before_host: Any,
    host_snapshot: Any,
    client_snapshot: Any,
    bot_status: dict[str, Any],
    comparison: dict[str, Any],
    scene: str,
    expected_players: int,
    min_commands: int,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    host_state = state(host_snapshot)
    client_state = state(client_snapshot)
    commands_issued = int(bot_status.get("commandsIssued") or 0)
    bot_player_id = bot_status.get("playerId")
    if not isinstance(bot_player_id, int) or bot_player_id < 0:
        bot_player_id = local_player_id(client_snapshot)

    if not snapshot_ready(host_snapshot, expected_players, scene):
        errors.extend("host." + reason for reason in snapshot_not_ready_reasons(host_snapshot, expected_players, scene))
    if not snapshot_ready(client_snapshot, expected_players, scene):
        errors.extend("client." + reason for reason in snapshot_not_ready_reasons(client_snapshot, expected_players, scene))
    if comparison.get("success") is not True:
        errors.extend(comparison.get("errors") or ["snapshot_comparison_failed"])
    if not unique_player_ids(host_snapshot):
        errors.append("host.player_ids_not_unique")
    if not unique_player_ids(client_snapshot):
        errors.append("client.player_ids_not_unique")
    if commands_issued < min_commands:
        errors.append(f"bot.commandsIssued expected>={min_commands} actual={commands_issued}")
    if bot_status.get("lastCommandType") not in MEANINGFUL_COMMANDS:
        warnings.append(f"bot.lastCommandType_not_meaningful actual={bot_status.get('lastCommandType')}")

    host_bot = player_by_id(host_snapshot, bot_player_id)
    client_bot = player_by_id(client_snapshot, bot_player_id)
    for side, player in (("host", host_bot), ("client", client_bot)):
        if player is None:
            errors.append(f"{side}.bot_player_missing:{bot_player_id}")
            continue
        if player.get("isConnected") is not True:
            errors.append(f"{side}.bot_player_not_connected:{bot_player_id}")
        if player.get("isAI") is not False:
            errors.append(f"{side}.bot_player_is_ai:{bot_player_id}")
        if nested(player, "ai", "controllerRegistered") is not False:
            errors.append(f"{side}.bot_ai_controller_registered:{bot_player_id}")

    deltas = meaningful_deltas(before_host, host_snapshot, bot_player_id)
    if not deltas:
        errors.append("bot_no_meaningful_durable_delta")
    if not monotonic_revision(before_host, host_snapshot, bot_player_id):
        errors.append("shop_revision_not_monotonic")

    if not scene_matches(host_state.get("scene"), scene) or not scene_matches(client_state.get("scene"), scene):
        errors.append(f"scene_mismatch host={host_state.get('scene')} client={client_state.get('scene')} expected={scene}")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings + list(comparison.get("warnings") or []),
        "botPlayerId": bot_player_id,
        "commandsIssued": commands_issued,
        "lastDecision": bot_status.get("lastDecision"),
        "lastCommandType": bot_status.get("lastCommandType"),
        "meaningfulDeltas": deltas,
        "hostGame": host_state.get("game"),
        "clientGame": client_state.get("game"),
    }


def wait_bot_progression(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    before_host: dict[str, Any],
    timeout: int,
    scene: str,
    expected_players: int,
    min_commands: int,
    client_peer: str,
    required_stable_samples: int = 1,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {"success": False, "errors": ["bot_progression_not_started"]}
    first_accepted_written = False
    stable_successes = 0

    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", "bot-latest")
        latest_client = dump_state(client, artifact_dir, client_peer, "bot-latest")
        comparison = compare_snapshots(latest_host, latest_client) if (
            snapshot_ready(latest_host, expected_players, scene)
            and snapshot_ready(latest_client, expected_players, scene)
        ) else {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
        try:
            status_response = client.bot_status()
        except Exception as exc:
            status_response = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        try:
            journal_response = client.bot_journal()
        except Exception as exc:
            journal_response = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        bot_status = bot_payload(status_response)
        latest_assertions = progression_assertions(
            before_host,
            latest_host,
            latest_client,
            bot_status,
            comparison,
            scene,
            expected_players,
            min_commands,
        )

        write_json(artifact_dir / "comparison-latest.json", comparison)
        write_json(artifact_dir / "bot-status-latest.json", status_response)
        write_json(artifact_dir / "bot-journal-latest.json", journal_response)
        write_json(artifact_dir / "human-bot-progressed-assertions-latest.json", latest_assertions)
        write_json(artifact_dir / "random-outcome-summary-latest.json", random_outcome_summary(latest_host, latest_client))

        if latest_assertions.get("meaningfulDeltas") and not first_accepted_written:
            first_accepted_written = True
            write_json(artifact_dir / "snapshots" / "build-host-after-first-accepted.json", latest_host)
            write_json(artifact_dir / "snapshots" / f"{client_peer}-after-first-accepted.json", latest_client)
            write_json(artifact_dir / "after-first-accepted-comparison.json", comparison)
            write_json(artifact_dir / "accepted-command-evidence.json", latest_assertions)

        if latest_assertions["success"]:
            stable_successes += 1
            if stable_successes >= required_stable_samples:
                try:
                    client.bot_stop(reason="phase22_progressed_checkpoint_reached")
                except Exception:
                    pass
                return latest_host, latest_client, latest_assertions, True
        else:
            stable_successes = 0
        time.sleep(1)

    return latest_host, latest_client, latest_assertions, False


PRESERVED_TARGET_FIELDS = [
    ("health", ("health",)),
    ("gold", ("gold",)),
    ("wallCount", ("wallCount",)),
    ("permanentWallPlacementCount", ("permanentWallPlacementCount",)),
    ("permanentWallStockRevision", ("permanentWallStockRevision",)),
    ("permanentWallLayoutRevision", ("permanentWallLayoutRevision",)),
    ("shop.available", ("shop", "available")),
    ("shop.revision", ("shop", "revision")),
    ("shop.round", ("shop", "round")),
    ("shop.count", ("shop", "count")),
    ("shop.itemsHash", ("shop", "itemsHash")),
    ("augment.available", ("augment", "available")),
    ("augment.presentedCount", ("augment", "presentedCount")),
    ("augment.presentedHash", ("augment", "presentedHash")),
    ("augment.selectedCount", ("augment", "selectedCount")),
    ("augment.selectedHash", ("augment", "selectedHash")),
    ("field.ready", ("field", "ready")),
    ("field.gridHash", ("field", "gridHash")),
    ("field.placedUnitCount", ("field", "placedUnitCount")),
    ("field.placedUnitsHash", ("field", "placedUnitsHash")),
    ("field.destructibleWallCount", ("field", "destructibleWallCount")),
    ("field.permanentWallCount", ("field", "permanentWallCount")),
    ("field.playerPlacedPermanentWallCount", ("field", "playerPlacedPermanentWallCount")),
    ("field.playerPlacedPermanentWallHash", ("field", "playerPlacedPermanentWallHash")),
    ("field.wallHash", ("field", "wallHash")),
]


def target_fingerprint(snapshot: Any, player_id: int) -> dict[str, Any]:
    player = player_by_id(snapshot, player_id)
    if not isinstance(player, dict):
        return {}
    return {name: nested(player, *path) for name, path in PRESERVED_TARGET_FIELDS}


def preservation_assertions(
    checkpoint_snapshot: Any,
    after_snapshot: Any,
    target_player_id: int,
    label: str,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    checkpoint_target = player_by_id(checkpoint_snapshot, target_player_id)
    after_target = player_by_id(after_snapshot, target_player_id)
    if checkpoint_target is None:
        errors.append(f"{label}.checkpoint_target_missing:{target_player_id}")
    if after_target is None:
        errors.append(f"{label}.after_target_missing:{target_player_id}")
    if not unique_player_ids(after_snapshot):
        errors.append(f"{label}.after_player_ids_not_unique")

    checkpoint_fp = target_fingerprint(checkpoint_snapshot, target_player_id)
    after_fp = target_fingerprint(after_snapshot, target_player_id)
    if checkpoint_fp and after_fp:
        for key, before in checkpoint_fp.items():
            after = after_fp.get(key)
            if before != after:
                errors.append(f"{label}.target.{target_player_id}.{key} before={before} after={after}")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "targetPlayerId": target_player_id,
        "checkpointFingerprint": checkpoint_fp,
        "afterFingerprint": after_fp,
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


def player_role_state_assertions(left_snapshot: Any, right_snapshot: Any, label: str = "role_state") -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    left_players = {player.get("playerId"): player for player in players(left_snapshot)}
    right_players = {player.get("playerId"): player for player in players(right_snapshot)}
    if set(left_players) != set(right_players):
        errors.append(f"{label}.player_ids left={sorted(left_players)} right={sorted(right_players)}")
    for player_id in sorted(set(left_players).intersection(right_players)):
        left = left_players[player_id]
        right = right_players[player_id]
        for field, path in (
            ("isAI", ("isAI",)),
            ("isConnected", ("isConnected",)),
            ("ai.controllerRegistered", ("ai", "controllerRegistered")),
        ):
            left_value = nested(left, *path)
            right_value = nested(right, *path)
            if left_value != right_value:
                errors.append(f"{label}.player.{player_id}.{field} left={left_value} right={right_value}")
    return {"success": not errors, "errors": errors, "warnings": warnings}
