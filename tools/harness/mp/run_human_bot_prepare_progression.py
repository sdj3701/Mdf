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
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report
from summarize_bot_metrics import write_metrics_summary


CASE_NAME = "human-bot-prepare"
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


def unique_player_ids(snapshot: Any) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids)) and all(isinstance(player_id, int) and player_id >= 0 for player_id in ids)


def local_player_id(snapshot: Any) -> int:
    for player in players(snapshot):
        if player.get("isLocal") is True and player.get("hasInputAuthority") is True:
            player_id = player.get("playerId")
            if isinstance(player_id, int):
                return player_id
    return -1


def bot_payload(response: dict[str, Any]) -> dict[str, Any]:
    data = response.get("data") if isinstance(response, dict) else None
    return data if isinstance(data, dict) else {}


def snapshot_bot(snapshot: Any) -> dict[str, Any]:
    test = state(snapshot).get("test") or {}
    bot = test.get("bot") if isinstance(test, dict) else None
    return bot if isinstance(bot, dict) else {}


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def meaningful_deltas(before_snapshot: Any, after_snapshot: Any, player_id: int) -> list[dict[str, Any]]:
    before = player_by_id(before_snapshot, player_id)
    after = player_by_id(after_snapshot, player_id)
    if before is None or after is None:
        return []

    fields = [
        ("gold", ("gold",)),
        ("wallCount", ("wallCount",)),
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
    client_state = state(client_snapshot)
    summary: dict[str, Any] = {
        "session": host_state.get("session"),
        "scene": host_state.get("scene"),
        "players": [],
    }
    client_by_id = {player.get("playerId"): player for player in players(client_snapshot)}
    for host_player in players(host_snapshot):
        player_id = host_player.get("playerId")
        client_player = client_by_id.get(player_id)
        entry = {
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
        }
        summary["players"].append(entry)
    return summary


def wait_session_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
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


def wait_stable_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    label: str,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        client_state = dump_state(client, artifact_dir, "build-client", f"{label}-latest")
        host_ready = snapshot_ready(host_state, 2, scene)
        client_ready = snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        write_json(artifact_dir / f"{label}-state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, 2, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, 2, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / f"{label}-comparison-latest.json", comparison)
        if host_ready and client_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, comparison, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, comparison, False


def progression_assertions(
    before_host: Any,
    host_snapshot: Any,
    client_snapshot: Any,
    bot_status: dict[str, Any],
    comparison: dict[str, Any],
    scene: str,
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

    if not snapshot_ready(host_snapshot, 2, scene):
        errors.extend("host." + reason for reason in snapshot_not_ready_reasons(host_snapshot, 2, scene))
    if not snapshot_ready(client_snapshot, 2, scene):
        errors.extend("client." + reason for reason in snapshot_not_ready_reasons(client_snapshot, 2, scene))
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

    if host_state.get("scene") != scene or client_state.get("scene") != scene:
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
    min_commands: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {"success": False, "errors": ["bot_progression_not_started"]}
    first_accepted_written = False
    stable_successes = 0

    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", "bot-latest")
        latest_client = dump_state(client, artifact_dir, "build-client", "bot-latest")
        comparison = compare_snapshots(latest_host, latest_client) if (
            snapshot_ready(latest_host, 2, scene) and snapshot_ready(latest_client, 2, scene)
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
            min_commands,
        )

        write_json(artifact_dir / "comparison-latest.json", comparison)
        write_json(artifact_dir / "bot-status-latest.json", status_response)
        write_json(artifact_dir / "bot-journal-latest.json", journal_response)
        write_json(artifact_dir / "human-bot-prepare-assertions-latest.json", latest_assertions)
        write_json(artifact_dir / "random-outcome-summary-latest.json", random_outcome_summary(latest_host, latest_client))

        if latest_assertions.get("meaningfulDeltas") and not first_accepted_written:
            first_accepted_written = True
            write_json(artifact_dir / "snapshots" / "build-host-after-first-accepted.json", latest_host)
            write_json(artifact_dir / "snapshots" / "build-client-after-first-accepted.json", latest_client)
            write_json(artifact_dir / "after-first-accepted-comparison.json", comparison)
            write_json(artifact_dir / "accepted-command-evidence.json", latest_assertions)

        if latest_assertions["success"]:
            stable_successes += 1
            if stable_successes >= 2:
                return latest_host, latest_client, latest_assertions, True
        else:
            stable_successes = 0
        time.sleep(2)

    return latest_host, latest_client, latest_assertions, False


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


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hbot")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    bot_seed = args.bot_seed if args.bot_seed is not None else args.seed
    bot_journal_path = artifact_dir / "build-client-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    failures: list[str] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }

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
        "dryRun": args.dry_run,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
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
            scenario="human_bot_prepare_progression",
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
            "build-client",
            max_players=2,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario="human_bot_prepare_progression",
            extra_args=[
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
                str(bot_journal_path),
            ],
        )
        client = AutomationClient(client_port, client_token, timeout=args.request_timeout)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

        pause_result = client.bot_stop(reason="phase20_pre_checkpoint_pause")
        write_json(artifact_dir / "build-client-bot-paused.json", pause_result)
        if pause_result.get("success") is not True:
            failures.append("bot_pause_failed")

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

        host_before, client_before, before_comparison, before_ready = wait_stable_states(
            host,
            client,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-bot",
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-before-bot.json", client_before)
        write_json(artifact_dir / "before-bot-comparison.json", before_comparison)
        if not before_ready:
            failures.append("before_bot_state_ready_timeout")

        host_freeze = host.freeze_game_flow(True, reason="human_bot_prepare_progression_prepare_checkpoint")
        client_freeze = client.freeze_game_flow(True, reason="human_bot_prepare_progression_prepare_checkpoint")
        write_json(artifact_dir / "build-host-freeze-game-flow.json", host_freeze)
        write_json(artifact_dir / "build-client-freeze-game-flow.json", client_freeze)
        if host_freeze.get("success") is not True:
            failures.append("host_freeze_game_flow_failed")
        if client_freeze.get("success") is not True:
            failures.append("client_freeze_game_flow_failed")

        start_result = client.bot_start(
            persona=args.bot_persona,
            seed=bot_seed,
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=args.bot_stop_at_round,
            maxCommands=args.bot_max_commands,
            journalPath=str(bot_journal_path),
        )
        write_json(artifact_dir / "build-client-bot-start.json", start_result)
        if start_result.get("success") is not True:
            failures.append("bot_start_failed")

        host_after, client_after, assertions, progressed = wait_bot_progression(
            host,
            client,
            artifact_dir,
            host_before,
            args.bot_timeout,
            args.scene,
            args.min_commands,
        )
        write_json(artifact_dir / "snapshots" / "build-host-prepare-progressed.json", host_after)
        write_json(artifact_dir / "snapshots" / "build-client-prepare-progressed.json", client_after)
        write_json(artifact_dir / "human-bot-prepare-assertions.json", assertions)
        write_json(artifact_dir / "random-outcome-summary.json", random_outcome_summary(host_after, client_after))
        write_json(artifact_dir / "checkpoint-summary.json", {
            "beforeBot": {
                "host": "snapshots/build-host-before-bot.json",
                "client": "snapshots/build-client-before-bot.json",
                "comparison": "before-bot-comparison.json",
            },
            "afterFirstAccepted": {
                "host": "snapshots/build-host-after-first-accepted.json",
                "client": "snapshots/build-client-after-first-accepted.json",
                "comparison": "after-first-accepted-comparison.json",
                "evidence": "accepted-command-evidence.json",
            },
            "prepareProgressed": {
                "host": "snapshots/build-host-prepare-progressed.json",
                "client": "snapshots/build-client-prepare-progressed.json",
                "assertions": "human-bot-prepare-assertions.json",
            },
        })
        if not progressed:
            failures.append("bot_no_meaningful_command")

        write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        host_logs = host.logs_recent()
        client_logs = client.logs_recent()
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        write_json(artifact_dir / "build-client-logs-recent.json", client_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_logs))

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
        try:
            metrics = write_metrics_summary(artifact_dir)
            functional_success = not functional_failures
            cleanup_success = cleanup_report.get("cleanupSuccess") is True
            overall_success = functional_success and (cleanup_success or not args.strict_cleanup)
            write_json(artifact_dir / "result.json", {
                "case": CASE_NAME,
                "artifactDir": str(artifact_dir),
                "success": overall_success,
                "functionalSuccess": functional_success,
                "cleanupSuccess": cleanup_success,
                "cleanupStatus": cleanup_report.get("cleanupStatus"),
                "cleanupReportPath": "cleanup-report.json",
                "orphanedPids": cleanup_report.get("orphanedPids") or [],
                "failures": failures,
                "botMetricsSummaryPath": "bot-metrics-summary.json",
                "botMetrics": metrics.get("summary"),
            })
            if failures:
                failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
        except Exception as exc:
            write_json(artifact_dir / "bot-metrics-summary-error.json", {
                "success": False,
                "error": {"code": type(exc).__name__, "details": str(exc)},
            })

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "failures": failures,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
    }, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=1001)
    parser.add_argument("--bot-seed", type=int)
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-duration-seconds", type=int, default=60)
    parser.add_argument("--bot-stop-at-round", type=int, default=2)
    parser.add_argument("--bot-max-commands", type=int, default=1)
    parser.add_argument("--min-commands", type=int, default=1)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--bot-timeout", type=int, default=120)
    parser.add_argument("--request-timeout", type=float, default=15.0)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
