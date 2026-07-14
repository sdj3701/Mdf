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
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    normalize_snapshot_response,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)
from progressed_human_bot_common import (
    dump_state,
    local_player,
    mptest_failures,
    nested,
    player_by_id,
    players,
    random_outcome_summary,
    unique_player_ids,
    wait_bot_progression,
    wait_session_states,
    wait_stable_states,
)
from run_host_migration_probe import read_host_migration_config


CASE_NAME = "progressed-host-migration-e2e"
UNKNOWN = "unknown"


def as_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def write_result(artifact_dir: pathlib.Path, failures: list[str], cleanup_report: dict[str, Any] | None = None) -> None:
    result = {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "success": not failures,
        "failures": failures,
    }
    if cleanup_report is not None:
        result.update({
            "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupReportPath": "cleanup-report.json",
            "orphanedPids": cleanup_report.get("orphanedPids") or [],
        })
    write_json(artifact_dir / "result.json", result)


def snapshot_body(snapshot: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(snapshot)
    return normalized if isinstance(normalized, dict) else {}


def migration_state(snapshot: Any) -> dict[str, Any]:
    migration = snapshot_body(snapshot).get("hostMigration")
    return migration if isinstance(migration, dict) else {}


def permanent_wall_state(snapshot: Any, player_id: int) -> dict[str, Any]:
    player = player_by_id(snapshot, player_id)
    if not isinstance(player, dict):
        return {}
    return {
        "normalStock": player.get("wallCount"),
        "permanentStock": player.get("permanentWallPlacementCount"),
        "stockRevision": player.get("permanentWallStockRevision"),
        "layoutRevision": player.get("permanentWallLayoutRevision"),
        "ownedCount": nested(player, "field", "playerPlacedPermanentWallCount"),
        "ownedHash": nested(player, "field", "playerPlacedPermanentWallHash"),
        "wallHash": nested(player, "field", "wallHash"),
    }


def migration_proof_report(snapshot: Any) -> dict[str, Any]:
    errors: list[str] = []
    migration = migration_state(snapshot)
    if migration.get("onHostMigrationCount", 0) <= 0:
        errors.append("on_host_migration_not_observed")
    if migration.get("nonNullTokenCount", 0) <= 0:
        errors.append("host_migration_token_not_observed")
    if migration.get("startGameSuccessCount", 0) <= 0:
        errors.append("token_start_game_success_not_observed")
    if migration.get("resumeCount", 0) <= 0:
        errors.append("host_migration_resume_not_observed")
    if migration.get("completeCount", 0) <= 0:
        errors.append("migration_complete_not_observed")
    if migration.get("failureCount", 0) > 0:
        errors.append("migration_failure_event_observed")
    if migration.get("recoverySucceeded") is not True:
        errors.append("recovery_success_not_observed")
    return {
        "success": not errors,
        "errors": errors,
        "migration": migration,
    }


def known_hash(value: Any) -> bool:
    return isinstance(value, str) and bool(value) and value != UNKNOWN


def random_progression_evidence(snapshot: Any, progression: dict[str, Any]) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    random_entries: list[dict[str, Any]] = []
    for player in players(snapshot):
        entry = {
            "playerId": player.get("playerId"),
            "shopHash": nested(player, "shop", "itemsHash"),
            "shopRevision": nested(player, "shop", "revision"),
            "augmentPresentedHash": nested(player, "augment", "presentedHash"),
            "augmentSelectedHash": nested(player, "augment", "selectedHash"),
            "fieldWallHash": nested(player, "field", "wallHash"),
            "fieldUnitsHash": nested(player, "field", "placedUnitsHash"),
        }
        random_entries.append(entry)

    has_shop = any(known_hash(entry["shopHash"]) for entry in random_entries)
    has_augment = any(
        known_hash(entry["augmentPresentedHash"]) or known_hash(entry["augmentSelectedHash"])
        for entry in random_entries
    )
    if not has_shop and not has_augment:
        errors.append("no_random_shop_or_augment_outcome_hash")

    deltas = progression.get("meaningfulDeltas") if isinstance(progression, dict) else []
    field_delta = any(
        isinstance(delta, dict) and str(delta.get("field", "")).startswith("field.")
        for delta in deltas or []
    )
    if not field_delta:
        warnings.append("no_field_or_unit_delta_before_migration")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "randomOutcomes": random_entries,
        "meaningfulDeltas": deltas or [],
    }


def durable_player_fingerprint(player: dict[str, Any]) -> dict[str, Any]:
    return {
        "playerId": player.get("playerId"),
        "connectionTokenHash": player.get("connectionTokenHash"),
        "health": player.get("health"),
        "gold": player.get("gold"),
        "wallCount": player.get("wallCount"),
        "permanentWallPlacementCount": player.get("permanentWallPlacementCount"),
        "permanentWallStockRevision": player.get("permanentWallStockRevision"),
        "permanentWallLayoutRevision": player.get("permanentWallLayoutRevision"),
        "isActivelyFighting": player.get("isActivelyFighting"),
        "isAttackerInCurrentBattle": player.get("isAttackerInCurrentBattle"),
        "blackMagicCurrent": player.get("blackMagicCurrent"),
        "blackMagicMaximum": player.get("blackMagicMaximum"),
        "blackMagicMaxBonus": player.get("blackMagicMaxBonus"),
        "blackMagicRevision": player.get("blackMagicRevision"),
        "blackMagicSequenceId": player.get("blackMagicSequenceId"),
        "shop": {
            "available": nested(player, "shop", "available"),
            "revision": nested(player, "shop", "revision"),
            "round": nested(player, "shop", "round"),
            "count": nested(player, "shop", "count"),
            "itemsHash": nested(player, "shop", "itemsHash"),
        },
        "augment": {
            "available": nested(player, "augment", "available"),
            "presentedCount": nested(player, "augment", "presentedCount"),
            "presentedHash": nested(player, "augment", "presentedHash"),
            "selectedCount": nested(player, "augment", "selectedCount"),
            "selectedHash": nested(player, "augment", "selectedHash"),
        },
        "field": {
            "ready": nested(player, "field", "ready"),
            "gridHash": nested(player, "field", "gridHash"),
            "placedUnitCount": nested(player, "field", "placedUnitCount"),
            "placedUnitsHash": nested(player, "field", "placedUnitsHash"),
            "unitRuntimeStateHash": nested(player, "field", "unitRuntimeStateHash"),
            "destructibleWallCount": nested(player, "field", "destructibleWallCount"),
            "permanentWallCount": nested(player, "field", "permanentWallCount"),
            "playerPlacedPermanentWallCount": nested(player, "field", "playerPlacedPermanentWallCount"),
            "playerPlacedPermanentWallHash": nested(player, "field", "playerPlacedPermanentWallHash"),
            "wallHash": nested(player, "field", "wallHash"),
            "destructibleWallHealthHash": nested(player, "field", "destructibleWallHealthHash"),
            "pathReady": nested(player, "field", "pathReady"),
            "goalReady": nested(player, "field", "goalReady"),
        },
        "monsters": {
            "ready": nested(player, "monsters", "ready"),
            "aliveCount": nested(player, "monsters", "aliveCount"),
            "livingHash": nested(player, "monsters", "livingHash"),
        },
    }


def unit_attack_cooldowns(player: dict[str, Any] | None) -> dict[str, int]:
    parts = nested(player or {}, "field", "unitAttackCooldownParts") or []
    result: dict[str, int] = {}
    for part in parts:
        text = str(part)
        marker = ":attackCooldownDs="
        if marker not in text:
            continue
        identity, value = text.rsplit(marker, 1)
        try:
            result[identity] = int(value)
        except ValueError:
            continue
    return result


def compare_unit_attack_cooldowns(
    errors: list[str],
    checkpoint_snapshot: Any,
    post_snapshot: Any,
    tolerance_deciseconds: int = 2,
) -> None:
    for before_player in players(checkpoint_snapshot):
        player_id = before_player.get("playerId")
        after_player = player_by_id(post_snapshot, player_id)
        before = unit_attack_cooldowns(before_player)
        after = unit_attack_cooldowns(after_player)
        if set(before) != set(after):
            errors.append(f"unit_attack_cooldown_identity_mismatch:P{player_id}")
            continue
        for identity, before_value in before.items():
            after_value = after[identity]
            if abs(before_value - after_value) > tolerance_deciseconds:
                errors.append(
                    f"unit_attack_cooldown_drift:P{player_id}:{identity}:before={before_value}:after={after_value}"
                )


def durable_fingerprint(snapshot: Any) -> dict[str, Any]:
    body = snapshot_body(snapshot)
    game = body.get("game") or {}
    return {
        "session": body.get("session"),
        "scene": body.get("scene"),
        "game": {
            "currentState": game.get("currentState"),
            "currentRound": game.get("currentRound"),
            "battleOpponentsHash": game.get("battleOpponentsHash"),
            "matchFirstAttackerHash": game.get("matchFirstAttackerHash"),
            "survivorBossPendingCount": game.get("survivorBossPendingCount"),
            "survivorBossPendingHash": game.get("survivorBossPendingHash"),
            "survivorBossAssignmentCount": game.get("survivorBossAssignmentCount"),
            "survivorBossAssignmentHash": game.get("survivorBossAssignmentHash"),
        },
        "players": [
            durable_player_fingerprint(player)
            for player in sorted(players(snapshot), key=lambda item: item.get("playerId", 9999))
        ],
    }


def compare_values(errors: list[str], mismatches: list[dict[str, Any]], field: str, before: Any, after: Any) -> None:
    # An optional hash can legitimately be unavailable at the pre-migration
    # checkpoint (for example, pairing data outside Battle).  A value first
    # becoming observable after recovery is not state loss.  Once a concrete
    # value was captured, however, recovery must preserve it exactly.
    if before == UNKNOWN:
        return
    if before != after:
        errors.append(f"{field} before={before} after={after}")
        mismatches.append({"field": field, "before": before, "after": after})


def compare_nested(
    errors: list[str],
    mismatches: list[dict[str, Any]],
    prefix: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> None:
    for key, value in before.items():
        field = f"{prefix}.{key}" if prefix else key
        after_value = after.get(key)
        if isinstance(value, dict) and isinstance(after_value, dict):
            compare_nested(errors, mismatches, field, value, after_value)
        else:
            compare_values(errors, mismatches, field, value, after_value)


def progressed_migration_assertions(
    checkpoint_snapshot: Any,
    post_snapshot: Any,
    bot_player_id: int,
    expected_players: int,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    mismatches: list[dict[str, Any]] = []
    post_body = snapshot_body(post_snapshot)
    runner = post_body.get("runner") if isinstance(post_body, dict) else {}
    game = post_body.get("game") if isinstance(post_body, dict) else {}

    if runner.get("gameMode") != "Host" or runner.get("isServer") is not True:
        errors.append("survivor_not_promoted_to_host")
    if game.get("hasGameManagers") is not True:
        errors.append("game_managers_missing_after_migration")
    host_migration = post_body.get("hostMigration") if isinstance(post_body, dict) else {}
    if isinstance(host_migration, dict):
        if host_migration.get("restorePendingAsync") not in (0, None):
            errors.append(f"migration_restore_async_pending:{host_migration.get('restorePendingAsync')}")
        if host_migration.get("restoreFailed") not in (0, None):
            errors.append(f"migration_restore_failed:{host_migration.get('restoreFailed')}")
        if host_migration.get("restoreMissing") not in (0, None):
            errors.append(f"migration_restore_missing:{host_migration.get('restoreMissing')}")
        if host_migration.get("restoreTerminal") is not True:
            errors.append("migration_restore_report_not_terminal")
    if len(players(post_snapshot)) != expected_players:
        errors.append(f"player_count expected={expected_players} actual={len(players(post_snapshot))}")
    if not unique_player_ids(post_snapshot):
        errors.append("duplicate_player_id")
    if not any(player.get("isConnected") is True and player.get("isAI") is False for player in players(post_snapshot)):
        errors.append("no_connected_human_survivor")

    bot_player = player_by_id(post_snapshot, bot_player_id)
    checkpoint_bot_player = player_by_id(checkpoint_snapshot, bot_player_id)
    if bot_player is None:
        errors.append(f"bot_player_missing_after_migration:{bot_player_id}")
    else:
        if bot_player.get("isConnected") is not True:
            errors.append(f"bot_player_not_connected_after_migration:{bot_player_id}")
        if bot_player.get("isAI") is not False:
            errors.append(f"bot_player_is_ai_after_migration:{bot_player_id}")
        if nested(bot_player, "ai", "controllerRegistered") is not False:
            errors.append(f"bot_ai_controller_registered_after_migration:{bot_player_id}")

    checkpoint_token_hash = (
        checkpoint_bot_player.get("connectionTokenHash")
        if isinstance(checkpoint_bot_player, dict)
        else None
    )
    post_token_hash = bot_player.get("connectionTokenHash") if isinstance(bot_player, dict) else None
    if not known_hash(checkpoint_token_hash) or len(checkpoint_token_hash) != 64:
        errors.append(f"checkpoint_connection_token_hash_invalid:{bot_player_id}")
    if not known_hash(post_token_hash) or len(post_token_hash) != 64:
        errors.append(f"post_migration_connection_token_hash_invalid:{bot_player_id}")

    pre_fp = durable_fingerprint(checkpoint_snapshot)
    post_fp = durable_fingerprint(post_snapshot)
    compare_nested(errors, mismatches, "", pre_fp, post_fp)
    compare_unit_attack_cooldowns(errors, checkpoint_snapshot, post_snapshot)

    test = post_body.get("test") if isinstance(post_body, dict) else {}
    bot_status = test.get("bot") if isinstance(test, dict) else None
    if isinstance(bot_status, dict):
        if bot_status.get("running") is True:
            errors.append("bot_running_after_migration_without_explicit_resume")
        if bot_status.get("lastError") not in (None, ""):
            errors.append(f"bot_last_error_after_migration:{bot_status.get('lastError')}")
    else:
        warnings.append("bot_status_missing_after_migration")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "mismatches": mismatches,
        "botPlayerId": bot_player_id,
        "preFingerprint": pre_fp,
        "postFingerprint": post_fp,
        "postRunner": runner,
        "postGame": game,
        "postBotStatus": bot_status,
    }


def wait_post_migration(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    checkpoint_snapshot: dict[str, Any],
    bot_player_id: int,
    expected_players: int,
    timeout: int,
    stable_samples: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    latest: dict[str, Any] = {}
    latest_proof: dict[str, Any] = {"success": False, "errors": ["not_started"]}
    latest_assertions: dict[str, Any] = {"success": False, "errors": ["not_started"]}
    stable_matches = 0
    while time.time() < deadline:
        latest = dump_state(client, artifact_dir, "survivor-client", "post-migration-latest")
        latest_proof = migration_proof_report(latest)
        latest_assertions = progressed_migration_assertions(
            checkpoint_snapshot,
            latest,
            bot_player_id,
            expected_players,
        )
        ready = latest_proof["success"] and latest_assertions["success"]
        write_json(artifact_dir / "host-migration-latest.json", latest_proof["migration"])
        write_json(artifact_dir / "post-migration-wait-latest.json", {
            "ready": ready,
            "proof": latest_proof,
            "assertions": latest_assertions,
            "stableMatches": stable_matches,
            "requiredStableSamples": stable_samples,
        })
        if migration_state(latest).get("failureCount", 0) > 0:
            return latest, latest_proof, latest_assertions, False
        if ready:
            stable_matches += 1
            if stable_matches >= stable_samples:
                return latest, latest_proof, latest_assertions, True
        else:
            stable_matches = 0
        time.sleep(1)
    return latest, latest_proof, latest_assertions, False


def field_units_hash(snapshot: Any, player_id: int) -> Any:
    player = player_by_id(snapshot, player_id)
    return nested(player, "field", "placedUnitsHash") if isinstance(player, dict) else None


def post_migration_move_unit_assertions(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    before_snapshot: Any,
    player_id: int,
    timeout: int,
) -> tuple[dict[str, Any], dict[str, Any]]:
    errors: list[str] = []
    warnings: list[str] = []
    command_response = client.command(name="move_unit", playerId=player_id)
    write_json(artifact_dir / "post-migration-move-unit-command.json", command_response)
    if command_response.get("success") is not True:
        error = command_response.get("error") or {}
        errors.append(f"move_unit_command_failed:{error.get('code') or command_response.get('message')}")
        return before_snapshot if isinstance(before_snapshot, dict) else {}, {
            "success": False,
            "errors": errors,
            "warnings": warnings,
            "command": command_response,
        }

    before_hash = field_units_hash(before_snapshot, player_id)
    latest: dict[str, Any] = before_snapshot if isinstance(before_snapshot, dict) else {}
    after_hash = before_hash
    deadline = time.time() + timeout
    while time.time() < deadline:
        latest = dump_state(client, artifact_dir, "survivor-client", "post-migration-move-latest")
        after_hash = field_units_hash(latest, player_id)
        write_json(artifact_dir / "post-migration-move-unit-latest.json", {
            "playerId": player_id,
            "beforeHash": before_hash,
            "afterHash": after_hash,
            "snapshotErrors": snapshot_body(latest).get("errors") or [],
        })
        if before_hash is not None and after_hash is not None and after_hash != before_hash:
            break
        time.sleep(1)

    body = snapshot_body(latest)
    snapshot_errors = body.get("errors") if isinstance(body, dict) else []
    if snapshot_errors:
        errors.extend(f"snapshot_error_after_move:{err}" for err in snapshot_errors)
    if before_hash is None or after_hash is None:
        errors.append(f"move_unit_hash_missing before={before_hash} after={after_hash}")
    elif after_hash == before_hash:
        errors.append(f"move_unit_hash_unchanged:{before_hash}")

    report = {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "command": command_response,
        "playerId": player_id,
        "beforeHash": before_hash,
        "afterHash": after_hash,
    }
    write_json(artifact_dir / "post-migration-move-unit-result.json", report)
    write_json(artifact_dir / "snapshots" / "survivor-client-post-migration-move.json", latest)
    return latest, report


def bot_args(args: argparse.Namespace, journal_path: pathlib.Path, bot_seed: int) -> list[str]:
    return [
        "--mpFreezeGameFlow",
        "--mpHumanBot",
        "--mpBotPersona",
        args.bot_persona,
        "--mpBotSeed",
        str(bot_seed),
        "--mpBotDurationSeconds",
        str(args.bot_duration_seconds),
        "--mpBotStopAtRound",
        str(args.bot_stop_at_round),
        "--mpBotMaxCommands",
        str(args.bot_max_commands),
        "--mpBotRecordJournal",
        str(journal_path),
    ]


def freeze_game_flow_args() -> list[str]:
    return ["--mpFreezeGameFlow"]


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("phm")
    config = read_host_migration_config()
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    bot_seed = args.bot_seed if args.bot_seed is not None else args.seed
    bot_journal_path = artifact_dir / "survivor-client-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    host_was_killed = False
    failures: list[str] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    bot_player_id = -1
    post_move_report: dict[str, Any] | None = None
    permanent_wall_position: dict[str, int] | None = None
    permanent_wall_checkpoint: dict[str, Any] | None = None
    post_migration_permanent_wall_remove: dict[str, Any] | None = None
    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        [host_token, client_token, host_connection, client_connection],
    )

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientPort": client_port,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "seed": args.seed,
        "botSeed": bot_seed,
        "botPersona": args.bot_persona,
        "botJournalPath": str(bot_journal_path),
        "postMigrationMoveUnit": args.post_migration_move_unit,
        "config": config,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
        "orphanPressure": orphan_gate,
    })
    if not config["enableAutoUpdate"]:
        failures.append("NEEDS_PROJECT_SUPPORT:host_migration_auto_update_disabled")
    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True, "config": config}, indent=2))
        return 0
    if failures:
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
        write_result(artifact_dir, failures, cleanup_report)
        return 1

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
            scenario="progressed_host_migration_e2e",
            extra_args=freeze_game_flow_args(),
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
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
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario="progressed_host_migration_e2e",
            extra_args=bot_args(args, bot_journal_path, bot_seed),
            headless_player=args.headless_player,
        )
        client = AutomationClient(client_port, client_token, timeout=args.request_timeout)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "survivor-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

        pause_result = client.bot_stop(reason="phase23_pre_checkpoint_pause")
        write_json(artifact_dir / "survivor-client-bot-paused.json", pause_result)
        if pause_result.get("success") is not True:
            failures.append("bot_pause_failed")

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "survivor-client", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(
            host,
            client,
            artifact_dir,
            args.lobby_timeout,
            args.lobby_scene,
            2,
            "survivor-client",
        )
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "survivor-client-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_before, client_before, before_comparison, before_ready = wait_stable_states(
            host,
            client,
            artifact_dir,
            args.state_timeout,
            args.scene,
            2,
            "before-bot",
            "survivor-client",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "survivor-client-before-bot.json", client_before)
        write_json(artifact_dir / "before-bot-comparison.json", before_comparison)
        if not before_ready:
            failures.append("before_bot_state_ready_timeout")
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        start_result = client.bot_start(
            persona=args.bot_persona,
            seed=bot_seed,
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=args.bot_stop_at_round,
            maxCommands=args.bot_max_commands,
            journalPath=str(bot_journal_path),
        )
        write_json(artifact_dir / "survivor-client-bot-start.json", start_result)
        if start_result.get("success") is not True:
            failures.append("bot_start_failed")

        host_progressed, client_progressed, progression, progressed = wait_bot_progression(
            host,
            client,
            artifact_dir,
            host_before,
            args.bot_timeout,
            args.scene,
            2,
            args.min_commands,
            "survivor-client",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-progressed-checkpoint.json", host_progressed)
        write_json(artifact_dir / "snapshots" / "survivor-client-progressed-checkpoint.json", client_progressed)
        write_json(artifact_dir / "human-bot-progressed-assertions.json", progression)
        checkpoint_comparison = compare_snapshots(host_progressed, client_progressed)
        write_json(artifact_dir / "progressed-checkpoint-comparison.json", checkpoint_comparison)
        random_summary = random_outcome_summary(host_progressed, client_progressed)
        write_json(artifact_dir / "random-outcome-summary.json", random_summary)
        random_evidence = random_progression_evidence(host_progressed, progression)
        write_json(artifact_dir / "progressed-random-evidence.json", random_evidence)
        if not progressed:
            failures.append("bot_no_meaningful_command")
        if checkpoint_comparison.get("success") is not True:
            failures.extend(f"progressed_checkpoint_comparison:{err}" for err in checkpoint_comparison.get("errors") or ["failed"])
        if random_evidence.get("success") is not True:
            failures.extend(f"progressed_random_evidence:{err}" for err in random_evidence.get("errors") or ["failed"])
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        bot_player_id = int(progression.get("botPlayerId", -1)) if isinstance(progression, dict) else -1
        survivor_local = local_player(client_progressed)
        if bot_player_id < 0 and isinstance(survivor_local, dict):
            bot_player_id = int(survivor_local.get("playerId", -1))
        if bot_player_id < 0:
            failures.append("bot_player_id_invalid")
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        stop_result = client.bot_stop(reason="phase23_pre_migration_checkpoint_reached")
        write_json(artifact_dir / "survivor-client-bot-stop-before-migration.json", stop_result)

        permanent_before = permanent_wall_state(host_progressed, bot_player_id)
        grant_permanent = host.command(name="grant_permanent_walls", playerId=bot_player_id, amount=3)
        write_json(artifact_dir / "pre-migration-grant-permanent-walls.json", grant_permanent)
        place_permanent = host.command(name="place_wall", playerId=bot_player_id, wallKind="permanent")
        write_json(artifact_dir / "pre-migration-place-permanent-wall.json", place_permanent)
        place_data = place_permanent.get("data") if isinstance(place_permanent, dict) else None
        place_position = place_data.get("position") if isinstance(place_data, dict) else None
        if isinstance(place_position, dict):
            permanent_wall_position = {
                "x": as_int(place_position.get("x")),
                "y": as_int(place_position.get("y")),
                "z": as_int(place_position.get("z")),
            }

        if grant_permanent.get("success") is not True:
            failures.append("pre_migration_permanent_wall_grant_failed")
        if place_permanent.get("success") is not True or permanent_wall_position is None:
            failures.append("pre_migration_permanent_wall_place_failed")

        expected_permanent_stock = as_int(permanent_before.get("permanentStock")) + 2
        expected_owned_count = as_int(permanent_before.get("ownedCount")) + 1
        permanent_checkpoint_ready = False
        permanent_checkpoint_comparison: dict[str, Any] = {"success": False, "errors": ["not_started"]}
        deadline = time.time() + args.state_timeout
        while time.time() < deadline and not failures:
            latest_host = dump_state(host, artifact_dir, "build-host", "permanent-wall-checkpoint-latest")
            latest_client = dump_state(client, artifact_dir, "survivor-client", "permanent-wall-checkpoint-latest")
            host_wall_state = permanent_wall_state(latest_host, bot_player_id)
            client_wall_state = permanent_wall_state(latest_client, bot_player_id)
            permanent_checkpoint_comparison = compare_snapshots(latest_host, latest_client)
            if (
                permanent_checkpoint_comparison.get("success") is True
                and host_wall_state == client_wall_state
                and as_int(host_wall_state.get("normalStock")) == as_int(permanent_before.get("normalStock"))
                and as_int(host_wall_state.get("permanentStock")) == expected_permanent_stock
                and as_int(host_wall_state.get("ownedCount")) == expected_owned_count
            ):
                host_progressed = latest_host
                client_progressed = latest_client
                permanent_wall_checkpoint = host_wall_state
                permanent_checkpoint_ready = True
                break
            time.sleep(0.25)

        write_json(artifact_dir / "pre-migration-permanent-wall-comparison.json", permanent_checkpoint_comparison)
        write_json(artifact_dir / "pre-migration-permanent-wall-checkpoint.json", {
            "success": permanent_checkpoint_ready,
            "playerId": bot_player_id,
            "position": permanent_wall_position,
            "before": permanent_before,
            "after": permanent_wall_checkpoint,
            "expectedPermanentStock": expected_permanent_stock,
            "expectedOwnedCount": expected_owned_count,
        })
        write_json(artifact_dir / "snapshots" / "build-host-progressed-checkpoint.json", host_progressed)
        write_json(artifact_dir / "snapshots" / "survivor-client-progressed-checkpoint.json", client_progressed)
        checkpoint_comparison = permanent_checkpoint_comparison
        write_json(artifact_dir / "progressed-checkpoint-comparison.json", checkpoint_comparison)
        if not permanent_checkpoint_ready:
            failures.append("pre_migration_permanent_wall_checkpoint_timeout")

        write_json(artifact_dir / "migration-target.json", {
            "botPlayerId": bot_player_id,
            "survivorConnectionTokenHash": survivor_local.get("connectionTokenHash") if isinstance(survivor_local, dict) else "unknown",
        })
        write_json(artifact_dir / "checkpoint-summary.json", {
            "beforeBot": {
                "host": "snapshots/build-host-before-bot.json",
                "client": "snapshots/survivor-client-before-bot.json",
                "comparison": "before-bot-comparison.json",
            },
            "afterFirstAccepted": {
                "host": "snapshots/build-host-after-first-accepted.json",
                "client": "snapshots/survivor-client-after-first-accepted.json",
                "comparison": "after-first-accepted-comparison.json",
                "evidence": "accepted-command-evidence.json",
            },
            "progressed": {
                "host": "snapshots/build-host-progressed-checkpoint.json",
                "client": "snapshots/survivor-client-progressed-checkpoint.json",
                "comparison": "progressed-checkpoint-comparison.json",
                "assertions": "human-bot-progressed-assertions.json",
                "randomOutcomes": "random-outcome-summary.json",
                "randomEvidence": "progressed-random-evidence.json",
            },
        })

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

        post, proof, migration_assertions, migrated = wait_post_migration(
            client,
            artifact_dir,
            client_progressed,
            bot_player_id,
            2,
            args.migration_timeout,
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "survivor-client-post-migration.json", post)
        write_json(artifact_dir / "host-migration-proof.json", proof)
        write_json(artifact_dir / "progressed-host-migration-assertions.json", migration_assertions)
        write_json(artifact_dir / "durable-state-report.json", {
            "success": migration_assertions.get("success") is True,
            "mismatches": migration_assertions.get("mismatches") or [],
            "preFingerprint": migration_assertions.get("preFingerprint"),
            "postFingerprint": migration_assertions.get("postFingerprint"),
        })
        write_json(artifact_dir / "host-migration-e2e-result.json", {
            "hostWasKilled": host_was_killed,
            "config": config,
            "migration": proof.get("migration"),
            "proof": proof,
            "assertions": migration_assertions,
            "failures": failures,
        })
        if not migrated:
            failures.append("progressed_host_migration_timeout")
        failures.extend(f"host_migration_proof:{err}" for err in proof.get("errors") or [])
        failures.extend(f"progressed_migration:{err}" for err in migration_assertions.get("errors") or [])

        if not failures and permanent_wall_position is not None and permanent_wall_checkpoint is not None:
            remove_response = client.command(
                name="remove_wall",
                playerId=bot_player_id,
                position=permanent_wall_position,
            )
            write_json(artifact_dir / "post-migration-remove-permanent-wall-command.json", remove_response)
            expected_post_remove_stock = as_int(permanent_wall_checkpoint.get("permanentStock")) + 1
            expected_post_remove_owned = max(0, as_int(permanent_wall_checkpoint.get("ownedCount")) - 1)
            latest_remove_state = post
            remove_succeeded = False
            deadline = time.time() + args.post_migration_move_timeout
            while time.time() < deadline and remove_response.get("success") is True:
                latest_remove_state = dump_state(client, artifact_dir, "survivor-client", "post-migration-permanent-wall-remove-latest")
                latest_wall_state = permanent_wall_state(latest_remove_state, bot_player_id)
                if (
                    as_int(latest_wall_state.get("normalStock")) == as_int(permanent_wall_checkpoint.get("normalStock"))
                    and as_int(latest_wall_state.get("permanentStock")) == expected_post_remove_stock
                    and as_int(latest_wall_state.get("ownedCount")) == expected_post_remove_owned
                ):
                    remove_succeeded = True
                    post = latest_remove_state
                    break
                time.sleep(0.25)

            post_migration_permanent_wall_remove = {
                "success": remove_response.get("success") is True and remove_succeeded,
                "playerId": bot_player_id,
                "position": permanent_wall_position,
                "checkpoint": permanent_wall_checkpoint,
                "after": permanent_wall_state(latest_remove_state, bot_player_id),
                "command": remove_response,
            }
            write_json(artifact_dir / "post-migration-permanent-wall-remove-result.json", post_migration_permanent_wall_remove)
            write_json(artifact_dir / "snapshots" / "survivor-client-post-migration-permanent-wall-remove.json", latest_remove_state)
            if post_migration_permanent_wall_remove.get("success") is not True:
                failures.append("post_migration_permanent_wall_remove_failed")

        if args.post_migration_move_unit and not failures:
            post, post_move_report = post_migration_move_unit_assertions(
                client,
                artifact_dir,
                post,
                bot_player_id,
                args.post_migration_move_timeout,
            )
            failures.extend(f"post_migration_move_unit:{err}" for err in post_move_report.get("errors") or [])

        write_json(artifact_dir / "host-migration-e2e-result.json", {
            "hostWasKilled": host_was_killed,
            "config": config,
            "migration": proof.get("migration"),
            "proof": proof,
            "assertions": migration_assertions,
            "permanentWallCheckpoint": permanent_wall_checkpoint,
            "postMigrationPermanentWallRemove": post_migration_permanent_wall_remove,
            "postMigrationMoveUnit": post_move_report,
            "failures": failures,
        })

        write_json(artifact_dir / "survivor-client-screenshot.json", client.screenshot())
        client_logs = client.logs_recent()
        write_json(artifact_dir / "survivor-client-logs-recent.json", client_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(client_logs))
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        functional_failures = list(failures)
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc for proc in (client_proc, host_proc) if proc is not None],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(functional_failures),
            strict_cleanup=args.strict_cleanup,
        )
        if args.strict_cleanup and cleanup_report.get("cleanupStatus") != "PASS":
            failures.append(f"cleanup_failed:{cleanup_report.get('cleanupStatus')}")

        player_log = collect_player_log(artifact_dir, "progressed-host-migration-last")
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "survivor-client.stdout.log",
            artifact_dir / "survivor-client.stderr.log",
        ]
        if player_log:
            logs.append(player_log)
        write_timeline(artifact_dir, logs)

    write_result(artifact_dir, failures, cleanup_report)
    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "failures": failures,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
        "orphanedPids": cleanup_report.get("orphanedPids") or [],
    }, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=4001)
    parser.add_argument("--bot-seed", type=int)
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-duration-seconds", type=int, default=90)
    parser.add_argument("--bot-stop-at-round", type=int, default=2)
    parser.add_argument("--bot-max-commands", type=int, default=2)
    parser.add_argument("--min-commands", type=int, default=2)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--bot-timeout", type=int, default=150)
    parser.add_argument("--migration-timeout", type=int, default=120)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    parser.add_argument("--stable-samples", type=int, default=2)
    parser.add_argument("--post-migration-move-unit", action="store_true")
    parser.add_argument("--post-migration-move-timeout", type=int, default=30)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
