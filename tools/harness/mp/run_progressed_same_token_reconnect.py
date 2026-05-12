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
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player
from progressed_human_bot_common import (
    dump_state,
    local_player,
    mptest_failures,
    nested,
    player_by_id,
    player_role_state_assertions,
    players,
    preservation_assertions,
    random_outcome_summary,
    unique_player_ids,
    wait_bot_progression,
    wait_session_states,
    wait_stable_states,
)


CASE_NAME = "progressed-same-token-reconnect"


def write_result(artifact_dir: pathlib.Path, failures: list[str]) -> None:
    write_json(artifact_dir / "result.json", {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "success": not failures,
        "failures": failures,
    })


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
        "uniquePlayerIds": unique_player_ids(snapshot),
        "targetPlayerId": target_player_id,
        "targetExists": target is not None,
        "targetIsAI": target.get("isAI") is True if isinstance(target, dict) else False,
        "targetConnected": target.get("isConnected") is True if isinstance(target, dict) else None,
        "targetFieldReady": field.get("ready") is True if isinstance(field, dict) else False,
        "targetAiRegistered": ai.get("controllerRegistered") is True if isinstance(ai, dict) else False,
        "targetShopHash": nested(target, "shop", "itemsHash"),
        "targetAugmentSelectedHash": nested(target, "augment", "selectedHash"),
        "targetWallHash": nested(target, "field", "wallHash"),
        "targetUnitsHash": nested(target, "field", "placedUnitsHash"),
    }


def takeover_ready(assertions: dict[str, Any], scene: str, expected_players: int) -> bool:
    return (
        scene_matches(assertions["scene"], scene)
        and assertions["activePlayerCount"] == expected_players - 1
        and assertions["playerCount"] == expected_players
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
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {}
    latest_preservation: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        snapshot = dump_state(host, artifact_dir, "build-host", "takeover-latest")
        latest_assertions = takeover_assertions(snapshot, target_player_id)
        latest_preservation = preservation_assertions(
            checkpoint_snapshot,
            snapshot,
            target_player_id,
            "progressed_same_token_takeover",
        )
        ready = takeover_ready(latest_assertions, scene, expected_players) and latest_preservation["success"]
        write_json(artifact_dir / "takeover-wait-latest.json", {
            "ready": ready,
            "assertions": latest_assertions,
            "preservation": latest_preservation,
            "stableMatches": stable_matches,
        })
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return snapshot, latest_assertions, latest_preservation, True
        else:
            stable_matches = 0
        time.sleep(2)
    return snapshot, latest_assertions, latest_preservation, False


def compare_reconnect_target(host_snapshot: dict[str, Any], client_snapshot: dict[str, Any], target_player_id: int) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    if not isinstance(host_state, dict) or not isinstance(client_state, dict):
        return {"success": False, "errors": ["snapshot_not_dict"], "warnings": warnings}

    compare_value(errors, "session", host_state.get("session"), client_state.get("session"))
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

    fields = [
        ("health", ("health",)),
        ("gold", ("gold",)),
        ("wallCount", ("wallCount",)),
        ("shop.itemsHash", ("shop", "itemsHash")),
        ("shop.revision", ("shop", "revision")),
        ("augment.presentedCount", ("augment", "presentedCount")),
        ("augment.presentedHash", ("augment", "presentedHash")),
        ("augment.selectedCount", ("augment", "selectedCount")),
        ("augment.selectedHash", ("augment", "selectedHash")),
        ("field.gridHash", ("field", "gridHash")),
        ("field.permanentWallCount", ("field", "permanentWallCount")),
        ("field.wallHash", ("field", "wallHash")),
        ("field.placedUnitCount", ("field", "placedUnitCount")),
        ("field.placedUnitsHash", ("field", "placedUnitsHash")),
        ("field.ready", ("field", "ready")),
        ("isAI", ("isAI",)),
        ("isConnected", ("isConnected",)),
        ("ai.controllerRegistered", ("ai", "controllerRegistered")),
    ]
    for name, path in fields:
        compare_value(errors, f"target.{target_player_id}.{name}", nested(host_target, *path), nested(client_target, *path))

    for side, snapshot in (("host", host_state), ("client", client_state)):
        if snapshot.get("errors"):
            warnings.extend(f"{side}.snapshot_error {err}" for err in snapshot["errors"])

    return {"success": not errors, "errors": errors, "warnings": warnings}


def compare_value(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def reconnect_assertions(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
) -> dict[str, Any]:
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    host_runner = host_state.get("runner") if isinstance(host_state, dict) else {}
    host_target = player_by_id(host_snapshot, target_player_id)
    client_local = local_player(client_snapshot)
    full_comparison = compare_snapshots(host_snapshot, client_snapshot)
    role_state_comparison = player_role_state_assertions(host_snapshot, client_snapshot, "full_world")
    target_comparison = compare_reconnect_target(host_snapshot, client_snapshot, target_player_id)
    preservation = preservation_assertions(
        checkpoint_snapshot,
        host_snapshot,
        target_player_id,
        "progressed_same_token_reconnect",
    )
    host_field = host_target.get("field") if isinstance(host_target, dict) else {}
    return {
        "hostActivePlayerCount": host_runner.get("activePlayerCount") if isinstance(host_runner, dict) else None,
        "hostPlayerCount": len(players(host_snapshot)),
        "clientPlayerCount": len(players(client_snapshot)),
        "hostUniquePlayerIds": unique_player_ids(host_snapshot),
        "clientUniquePlayerIds": unique_player_ids(client_snapshot),
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
        "roleStateComparison": role_state_comparison,
        "progressedPreservation": preservation,
    }


def reconnect_ready(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
    scene: str,
    expected_players: int,
) -> bool:
    host_state = normalize_snapshot_response(host_snapshot)
    client_state = normalize_snapshot_response(client_snapshot)
    if not isinstance(host_state, dict) or not isinstance(client_state, dict):
        return False
    if not scene_matches(host_state.get("scene"), scene) or not scene_matches(client_state.get("scene"), scene):
        return False
    if not snapshot_ready(host_snapshot, expected_players, scene) or not snapshot_ready(client_snapshot, expected_players, scene):
        return False
    assertions = reconnect_assertions(host_snapshot, client_snapshot, checkpoint_snapshot, target_player_id, expected_token_hash)
    return (
        assertions["hostActivePlayerCount"] == expected_players
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
        and assertions["fullComparison"]["success"] is True
        and assertions["roleStateComparison"]["success"] is True
        and assertions["progressedPreservation"]["success"] is True
    )


def wait_reconnect(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "reconnect-latest")
        client_state = dump_state(client, artifact_dir, "build-client-b", "reconnect-latest")
        latest_assertions = reconnect_assertions(
            host_state,
            client_state,
            checkpoint_snapshot,
            target_player_id,
            expected_token_hash,
        )
        ready = reconnect_ready(
            host_state,
            client_state,
            checkpoint_snapshot,
            target_player_id,
            expected_token_hash,
            scene,
            expected_players,
        )
        write_json(artifact_dir / "reconnect-wait-latest.json", {
            "ready": ready,
            "assertions": latest_assertions,
            "hostReasons": snapshot_not_ready_reasons(host_state, expected_players, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, expected_players, scene),
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", latest_assertions["targetComparison"])
        write_json(artifact_dir / "full-comparison-latest.json", latest_assertions["fullComparison"])
        write_json(artifact_dir / "role-state-comparison-latest.json", latest_assertions["roleStateComparison"])
        write_json(artifact_dir / "progressed-preservation-latest.json", latest_assertions["progressedPreservation"])
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, latest_assertions, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, latest_assertions, False


def bot_args(args: argparse.Namespace, journal_path: pathlib.Path, bot_seed: int) -> list[str]:
    return [
        "--mpDisableAiFill",
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


def disable_ai_fill_args() -> list[str]:
    return ["--mpDisableAiFill", "--mpFreezeGameFlow"]


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("pstr")
    host_token = new_token()
    client_a_token = new_token()
    client_b_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    client_connection_hash = hash_for_log(client_connection)
    host_port = free_port()
    client_a_port = free_port()
    client_b_port = free_port()
    bot_seed = args.bot_seed if args.bot_seed is not None else args.seed
    bot_journal_path = artifact_dir / "build-client-a-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_a_proc: PlayerProcess | None = None
    client_b_proc: PlayerProcess | None = None
    failures: list[str] = []
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
        "expectedPlayers": args.expected_players,
        "seed": args.seed,
        "botSeed": bot_seed,
        "botPersona": args.bot_persona,
        "botJournalPath": str(bot_journal_path),
        "clientConnectionTokenHash": client_connection_hash,
        "dryRun": args.dry_run,
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
            max_players=args.max_players,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="progressed_same_token_reconnect",
            extra_args=disable_ai_fill_args(),
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
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
            scenario="progressed_same_token_reconnect",
            extra_args=bot_args(args, bot_journal_path, bot_seed),
        )
        client_a = AutomationClient(client_a_port, client_a_token, timeout=args.request_timeout)
        client_a_ping = client_a.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-a-ping.json", client_a_ping)
        if not client_a_ping.get("success"):
            failures.append("client_a_automation_ping_timeout")

        pause_result = client_a.bot_stop(reason="phase22_pre_checkpoint_pause")
        write_json(artifact_dir / "build-client-a-bot-paused.json", pause_result)
        if pause_result.get("success") is not True:
            failures.append("bot_pause_failed")

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
            args.expected_players,
            "build-client-a",
        )
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-a-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_before, client_before, before_comparison, before_ready = wait_stable_states(
            host,
            client_a,
            artifact_dir,
            args.state_timeout,
            args.scene,
            args.expected_players,
            "before-bot",
            "build-client-a",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-a-before-bot.json", client_before)
        write_json(artifact_dir / "before-bot-comparison.json", before_comparison)
        if not before_ready:
            failures.append("before_bot_state_ready_timeout")
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        start_result = client_a.bot_start(
            persona=args.bot_persona,
            seed=bot_seed,
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=args.bot_stop_at_round,
            maxCommands=args.bot_max_commands,
            journalPath=str(bot_journal_path),
        )
        write_json(artifact_dir / "build-client-a-bot-start.json", start_result)
        if start_result.get("success") is not True:
            failures.append("bot_start_failed")

        host_progressed, client_progressed, progression, progressed = wait_bot_progression(
            host,
            client_a,
            artifact_dir,
            host_before,
            args.bot_timeout,
            args.scene,
            args.expected_players,
            args.min_commands,
            "build-client-a",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-progressed-checkpoint.json", host_progressed)
        write_json(artifact_dir / "snapshots" / "build-client-a-progressed-checkpoint.json", client_progressed)
        write_json(artifact_dir / "human-bot-progressed-assertions.json", progression)
        write_json(artifact_dir / "random-outcome-summary.json", random_outcome_summary(host_progressed, client_progressed))
        if not progressed:
            failures.append("bot_no_meaningful_command")
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        target_player_id = int(progression.get("botPlayerId", -1)) if isinstance(progression, dict) else -1
        client_a_local = local_player(client_progressed)
        if target_player_id < 0 and isinstance(client_a_local, dict):
            target_player_id = int(client_a_local.get("playerId", -1))
        if target_player_id < 0:
            failures.append("target_player_id_invalid")
        else:
            write_json(artifact_dir / "reconnect-target.json", {
                "playerId": target_player_id,
                "connectionTokenHash": client_a_local.get("connectionTokenHash") if isinstance(client_a_local, dict) else "unknown",
                "expectedConnectionTokenHash": client_connection_hash,
            })

        write_json(artifact_dir / "checkpoint-summary.json", {
            "beforeBot": {
                "host": "snapshots/build-host-before-bot.json",
                "client": "snapshots/build-client-a-before-bot.json",
                "comparison": "before-bot-comparison.json",
            },
            "afterFirstAccepted": {
                "host": "snapshots/build-host-after-first-accepted.json",
                "client": "snapshots/build-client-a-after-first-accepted.json",
                "comparison": "after-first-accepted-comparison.json",
                "evidence": "accepted-command-evidence.json",
            },
            "progressed": {
                "host": "snapshots/build-host-progressed-checkpoint.json",
                "client": "snapshots/build-client-a-progressed-checkpoint.json",
                "assertions": "human-bot-progressed-assertions.json",
            },
        })

        if client_a_proc is not None:
            client_a_proc.process.kill()
            client_a_proc.process.wait(timeout=10)
            client_a_proc.close_logs()

        host_takeover: dict[str, Any] = {}
        if target_player_id < 0:
            failures.append("target_player_id_invalid")
        else:
            host_takeover, takeover, takeover_preservation, takeover_ok = wait_takeover(
                host,
                artifact_dir,
                host_progressed,
                target_player_id,
                args.takeover_timeout,
                args.scene,
                args.expected_players,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-takeover.json", host_takeover)
            write_json(artifact_dir / "takeover-assertions.json", takeover)
            write_json(artifact_dir / "progressed-takeover-preservation-assertions.json", takeover_preservation)
            if not takeover_ok:
                failures.append("progressed_disconnect_ai_takeover_timeout")
                failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
                write_result(artifact_dir, failures)
                return 1

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
            scenario="progressed_same_token_reconnect",
            extra_args=disable_ai_fill_args(),
        )
        client_b = AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout)
        client_b_ping = client_b.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-b-ping.json", client_b_ping)
        if not client_b_ping.get("success"):
            failures.append("client_b_automation_ping_timeout")

        if not wait_build_peer_started(client_b.join, artifact_dir, "build-client-b", session, args.scene, args.max_players, args.start_timeout):
            failures.append("client_b_rejoin_timeout")

        if target_player_id >= 0:
            host_final, client_final, reconnect, reconnect_ok = wait_reconnect(
                host,
                client_b,
                artifact_dir,
                host_progressed,
                target_player_id,
                client_connection_hash,
                args.reconnect_timeout,
                args.scene,
                args.expected_players,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-reconnect.json", host_final)
            write_json(artifact_dir / "snapshots" / "build-client-b-post-reconnect.json", client_final)
            write_json(artifact_dir / "same-token-reconnect-assertions.json", reconnect)
            write_json(artifact_dir / "same-token-reconnect-target-assertions.json", {
                "success": reconnect["targetComparison"]["success"],
                "targetPlayerId": target_player_id,
                "assertions": reconnect,
            })
            write_json(artifact_dir / "same-token-reconnect-fullworld-assertions.json", {
                "success": reconnect["fullComparison"]["success"],
                "comparison": reconnect["fullComparison"],
            })
            write_json(artifact_dir / "same-token-reconnect-role-state-assertions.json", {
                "success": reconnect["roleStateComparison"]["success"],
                "comparison": reconnect["roleStateComparison"],
            })
            write_json(artifact_dir / "progressed-preservation-assertions.json", reconnect["progressedPreservation"])
            write_json(artifact_dir / "comparison.json", reconnect["targetComparison"])
            write_json(artifact_dir / "full-comparison.json", reconnect["fullComparison"])
            if not reconnect_ok:
                failures.append("progressed_same_token_reconnect_timeout")

        write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        if client_b_proc is not None:
            write_json(artifact_dir / "build-client-b-screenshot.json", client_b.screenshot())
        host_logs = host.logs_recent()
        client_b_logs = client_b.logs_recent() if client_b_proc is not None else {}
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        write_json(artifact_dir / "build-client-b-logs-recent.json", client_b_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_b_logs))
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        for peer, proc, port, token in (
            ("build-client-b", client_b_proc, client_b_port, client_b_token),
            ("build-host", host_proc, host_port, host_token),
        ):
            if proc is None:
                continue
            try:
                automation = AutomationClient(port, token, timeout=2.0)
                write_json(artifact_dir / f"{peer}-quit.json", automation.quit())
                proc.process.wait(timeout=10)
                proc.close_logs()
            except Exception:
                proc.terminate()
        if client_a_proc is not None and client_a_proc.process.poll() is None:
            client_a_proc.terminate()
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

    write_result(artifact_dir, failures)
    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=3002)
    parser.add_argument("--bot-seed", type=int)
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-duration-seconds", type=int, default=60)
    parser.add_argument("--bot-stop-at-round", type=int, default=2)
    parser.add_argument("--bot-max-commands", type=int, default=1)
    parser.add_argument("--min-commands", type=int, default=1)
    parser.add_argument("--max-players", type=int, default=3)
    parser.add_argument("--expected-players", type=int, default=2)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--bot-timeout", type=int, default=120)
    parser.add_argument("--takeover-timeout", type=int, default=90)
    parser.add_argument("--reconnect-timeout", type=int, default=120)
    parser.add_argument("--request-timeout", type=float, default=15.0)
    parser.add_argument("--stable-samples", type=int, default=1)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
