#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log
from common import (
    failure_summary,
    free_port,
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
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
from long_progression_common import (
    checkpoint_key,
    dump_state,
    game,
    mptest_failures,
    nested,
    players,
    progression_metrics,
    safe_request,
    to_int,
    unique_player_ids,
    wait_checkpoint_comparison,
    write_final_timeline,
)
from run_human_bot_3round_progression import (
    collect_bot_status,
    wait_session_states,
    wait_stable_states,
)


CASE_NAME = "human-bot-game-to-end"
EXPECTED_PLAYERS = 2
STALL_WINDOW_SECONDS = 180


def bot_args(args: argparse.Namespace, persona: str, seed: int, journal_path: pathlib.Path) -> list[str]:
    result = [
        "--mpHumanBot",
        "--mpBotPersona",
        persona,
        "--mpBotSeed",
        str(seed),
        "--mpBotDurationSeconds",
        str(args.max_duration_seconds),
        "--mpBotStopAtRound",
        "0",
        "--mpBotMaxCommands",
        str(args.max_commands_per_bot),
        "--mpBotRecordJournal",
        str(journal_path),
    ]
    if args.bot_prepare_mode == "augment-only":
        result.append("--mpBotPrepareAugmentOnly")
    elif args.bot_prepare_mode == "skip":
        result.append("--mpBotSkipPrepare")
    return result


def start_bot(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    peer: str,
    *,
    persona: str,
    seed: int,
    args: argparse.Namespace,
    journal_path: pathlib.Path,
) -> dict[str, Any]:
    result = safe_request(lambda: client.bot_start(
        persona=persona,
        seed=seed,
        durationSeconds=args.max_duration_seconds,
        stopAtRound=0,
        maxCommands=args.max_commands_per_bot,
        skipPrepare=args.bot_prepare_mode == "skip",
        prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
        journalPath=str(journal_path),
    ))
    write_json(artifact_dir / f"{peer}-bot-start.json", result)
    return result


def snapshot_row(snapshot: Any, elapsed_seconds: float) -> dict[str, Any]:
    g = game(snapshot)
    player_rows = []
    for player in players(snapshot):
        player_rows.append({
            "playerId": player.get("playerId"),
            "health": player.get("health"),
            "gold": player.get("gold"),
            "monsterAliveCount": nested(player, "monsters", "aliveCount"),
            "shopRevision": nested(player, "shop", "revision"),
            "isAI": player.get("isAI"),
            "isConnected": player.get("isConnected"),
        })
    return {
        "elapsedSeconds": round(elapsed_seconds, 3),
        "currentRound": g.get("currentRound"),
        "currentState": g.get("currentState"),
        "battlePhase": g.get("battlePhase"),
        "players": player_rows,
        "commands": snapshot.get("data", snapshot).get("commands") if isinstance(snapshot, dict) else {},
    }


def player_healths(snapshot: Any) -> dict[int, int]:
    result: dict[int, int] = {}
    for player in players(snapshot):
        player_id = player.get("playerId")
        health = player.get("health")
        if isinstance(player_id, int) and isinstance(health, int):
            result[player_id] = health
    return result


def max_monster_alive(snapshot: Any) -> int:
    counts = [to_int(nested(player, "monsters", "aliveCount")) for player in players(snapshot)]
    return max(counts) if counts else 0


def command_seq(snapshot: Any) -> int:
    commands = snapshot.get("data", snapshot).get("commands") if isinstance(snapshot, dict) else {}
    return max(
        to_int(commands.get("acceptedBattleCommandSeq")) if isinstance(commands, dict) else 0,
        to_int(commands.get("spawnMonsterSeq")) if isinstance(commands, dict) else 0,
        to_int(commands.get("useMagicScrollSeq")) if isinstance(commands, dict) else 0,
        to_int(commands.get("activateSkillSeq")) if isinstance(commands, dict) else 0,
    )


def progress_summary(timeline: list[dict[str, Any]]) -> dict[str, Any]:
    rounds = [to_int(row.get("currentRound")) for row in timeline]
    health_samples: list[tuple[int, int]] = []
    monster_counts: list[int] = []
    command_counts: list[int] = []
    for row in timeline:
        for player in row.get("players") or []:
            if isinstance(player, dict):
                health_samples.append((to_int(player.get("playerId"), -1), to_int(player.get("health"), -1)))
                monster_counts.append(to_int(player.get("monsterAliveCount")))
        commands = row.get("commands") if isinstance(row.get("commands"), dict) else {}
        command_counts.append(max(
            to_int(commands.get("acceptedBattleCommandSeq")),
            to_int(commands.get("spawnMonsterSeq")),
            to_int(commands.get("useMagicScrollSeq")),
            to_int(commands.get("activateSkillSeq")),
        ))

    first_healths: dict[int, int] = {}
    last_healths: dict[int, int] = {}
    for player_id, health in health_samples:
        if player_id < 0 or health < 0:
            continue
        first_healths.setdefault(player_id, health)
        last_healths[player_id] = health

    hp_changed = any(last_healths.get(player_id) != first_healths.get(player_id) for player_id in first_healths)
    return {
        "roundIncreased": bool(rounds and max(rounds) > min(rounds)),
        "maxRound": max(rounds) if rounds else 0,
        "hpChanged": hp_changed,
        "monstersSpawned": max(monster_counts) > 0 if monster_counts else False,
        "commandsExecuted": max(command_counts) > 0 if command_counts else False,
    }


def recent_progress(timeline: list[dict[str, Any]], window_seconds: int) -> bool:
    if len(timeline) < 2:
        return False
    latest_elapsed = to_int(timeline[-1].get("elapsedSeconds"))
    recent = [row for row in timeline if latest_elapsed - to_int(row.get("elapsedSeconds")) <= window_seconds]
    if len(recent) < 2:
        return False
    return any(progress_summary([recent[0], row]).get(key) for row in recent[1:] for key in (
        "roundIncreased",
        "hpChanged",
        "monstersSpawned",
        "commandsExecuted",
    ))


def final_ranking(snapshot: Any) -> dict[str, Any]:
    rows = []
    for player in players(snapshot):
        player_id = player.get("playerId")
        health = player.get("health")
        if isinstance(player_id, int) and isinstance(health, int):
            rows.append({"playerId": player_id, "health": health})
    ranked = sorted(rows, key=lambda row: (-row["health"], row["playerId"]))
    return {
        "ranking": ranked,
        "winnerPlayerId": ranked[0]["playerId"] if ranked else None,
        "eliminatedPlayerIds": [row["playerId"] for row in ranked if row["health"] <= 0],
    }


def command_sequences_agree(left: Any, right: Any) -> bool:
    left_commands = left.get("data", left).get("commands") if isinstance(left, dict) else {}
    right_commands = right.get("data", right).get("commands") if isinstance(right, dict) else {}
    for key in (
        "acceptedBattleCommandSeq",
        "spawnMonsterSeq",
        "useMagicScrollSeq",
        "activateSkillSeq",
        "rejectedBattleCommandCount",
    ):
        if to_int(left_commands.get(key)) != to_int(right_commands.get(key)):
            return False
    return True


def classify_endurance(
    final_host: Any,
    final_client: Any,
    final_comparison: dict[str, Any],
    checkpoints: list[dict[str, Any]],
    timeline: list[dict[str, Any]],
    mptest_failure_lines: list[str],
    cleanup_report: dict[str, Any],
    *,
    limit_reason: str,
) -> dict[str, Any]:
    host_game = game(final_host)
    client_game = game(final_client)
    host_game_over = host_game.get("currentState") == "GameOver"
    client_game_over = client_game.get("currentState") == "GameOver"
    progress = progress_summary(timeline)
    stalled = not recent_progress(timeline, STALL_WINDOW_SECONDS)
    failed_checkpoints = [checkpoint.get("checkpointId") for checkpoint in checkpoints if checkpoint.get("success") is not True]
    errors: list[str] = []
    warnings: list[str] = []

    if not unique_player_ids(final_host):
        errors.append("host.player_ids_not_unique")
    if not unique_player_ids(final_client):
        errors.append("client.player_ids_not_unique")
    if final_comparison.get("success") is not True:
        errors.extend(f"final_snapshot:{error}" for error in final_comparison.get("errors") or ["comparison_failed"])
    if failed_checkpoints:
        errors.extend(f"checkpoint_failed:{checkpoint_id}" for checkpoint_id in failed_checkpoints)
    if not command_sequences_agree(final_host, final_client):
        errors.append("command_sequence_divergence")
    if mptest_failure_lines:
        errors.extend(f"mptest_failure_log:{line}" for line in mptest_failure_lines)
    if cleanup_report.get("cleanupStatus") != "PASS":
        errors.append(f"cleanup_failed:{cleanup_report.get('cleanupStatus')}")

    game_to_end_pass = host_game_over and client_game_over and not errors
    if game_to_end_pass:
        final_status = "PASS"
    elif host_game_over != client_game_over:
        final_status = "FAIL"
        errors.append("game_over_state_divergence")
    elif errors:
        final_status = "FAIL"
    elif stalled:
        final_status = "STALLED"
    elif progress.get("roundIncreased") or progress.get("hpChanged") or progress.get("monstersSpawned") or progress.get("commandsExecuted"):
        final_status = "NEEDS_TUNING"
    else:
        final_status = "TIMEOUT"

    if final_status != "PASS" and limit_reason == "max_rounds_reached":
        warnings.append("max_rounds_reached_before_game_over")
    elif final_status != "PASS" and limit_reason == "max_duration_reached":
        warnings.append("max_duration_reached_before_game_over")

    return {
        "finalStatus": final_status,
        "gameToEndPass": game_to_end_pass,
        "limitReason": limit_reason,
        "progress": progress,
        "stalled": stalled,
        "errors": errors,
        "warnings": warnings,
        "hostGame": host_game,
        "clientGame": client_game,
        "finalComparison": final_comparison,
        "finalRanking": final_ranking(final_host),
    }


def should_capture_checkpoint(
    host_key: tuple[int, str] | None,
    client_key: tuple[int, str] | None,
    seen: set[tuple[int, str]],
    last_timed_checkpoint_at: float,
    *,
    start_time: float,
    args: argparse.Namespace,
) -> tuple[bool, bool]:
    if host_key is None or host_key != client_key:
        return False, False
    new_key = host_key not in seen
    elapsed_since_timed = time.time() - last_timed_checkpoint_at
    timed = args.checkpoint_every_seconds > 0 and elapsed_since_timed >= args.checkpoint_every_seconds
    state_capture = args.checkpoint_every_state and new_key
    round_capture = args.checkpoint_every_round and new_key and host_key[1] in {"Prepare", "GameOver"}
    return state_capture or round_capture or timed, timed


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hbotend")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    host_seed = args.seed
    client_seed = args.seed + 1
    host_bot_seed = args.seed + 100
    client_bot_seed = args.seed + 101
    host_journal_path = artifact_dir / "build-host-bot.jsonl"
    client_journal_path = artifact_dir / "build-client-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    failures: list[str] = []
    checkpoints: list[dict[str, Any]] = []
    checkpoint_keys_seen: set[tuple[int, str]] = set()
    progress_timeline: list[dict[str, Any]] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {"cleanupStatus": "PASS", "cleanupSuccess": True, "orphanedPids": []}
    final_host: dict[str, Any] = {}
    final_client: dict[str, Any] = {}
    final_comparison: dict[str, Any] = {"success": False, "errors": ["not_run"], "warnings": []}
    classification: dict[str, Any] = {"finalStatus": "FAIL", "errors": ["not_started"]}
    limit_reason = "none"
    start_time = time.time()
    last_timed_checkpoint_at = start_time

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
        "seed": args.seed,
        "hostBotSeed": host_bot_seed,
        "clientBotSeed": client_bot_seed,
        "maxDurationSeconds": args.max_duration_seconds,
        "maxRounds": args.max_rounds,
        "maxCommandsPerBot": args.max_commands_per_bot,
        "allowTimeoutResult": args.allow_timeout_result,
        "botPersona": args.bot_persona,
        "botPrepareMode": args.bot_prepare_mode,
        "hostHumanBot": args.host_human_bot,
        "checkpointEveryRound": args.checkpoint_every_round,
        "checkpointEveryState": args.checkpoint_every_state,
        "checkpointEverySeconds": args.checkpoint_every_seconds,
        "headlessPlayer": args.headless_player,
        "orphanPressure": orphan_gate,
    })

    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")
        result = {
            "case": CASE_NAME,
            "artifactDir": str(artifact_dir),
            "success": False,
            "gameToEndPass": False,
            "finalStatus": "NEEDS_ENVIRONMENT",
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "failures": failures,
            "orphanPressure": orphan_gate,
        }
        write_json(artifact_dir / "game-to-end-result.json", result)
        write_json(artifact_dir / "result.json", result)
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} blocked", failures)
        print(json.dumps(result, indent=2))
        return 2

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
            max_players=EXPECTED_PLAYERS,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=host_seed,
            scenario=CASE_NAME,
            extra_args=bot_args(args, args.bot_persona, host_bot_seed, host_journal_path) if args.host_human_bot else [],
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")
        if args.host_human_bot:
            write_json(artifact_dir / "build-host-bot-paused.json", safe_request(lambda: host.bot_stop(reason="endurance_pre_checkpoint_pause")))
        else:
            write_json(artifact_dir / "build-host-bot-paused.json", {"success": True, "message": "host HumanBot disabled"})

        client_proc = launch_player(
            player_path,
            "client",
            session,
            client_port,
            client_token,
            client_connection,
            artifact_dir,
            "build-client",
            max_players=EXPECTED_PLAYERS,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=client_seed,
            scenario=CASE_NAME,
            extra_args=bot_args(args, args.bot_persona, client_bot_seed, client_journal_path),
            headless_player=args.headless_player,
        )
        client = AutomationClient(client_port, client_token, timeout=args.request_timeout)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")
        write_json(artifact_dir / "build-client-bot-paused.json", safe_request(lambda: client.bot_stop(reason="endurance_pre_checkpoint_pause")))

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, args.lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
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

        host_before, client_before, before_ready = wait_stable_states(
            host, client, artifact_dir, args.state_timeout, args.scene, "before-endurance-bot"
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-endurance-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-before-endurance-bot.json", client_before)
        if not before_ready:
            failures.append("before_endurance_bot_state_ready_timeout")

        for peer, automation, bot_seed, journal in [
            ("build-host", host, host_bot_seed, host_journal_path),
            ("build-client", client, client_bot_seed, client_journal_path),
        ]:
            if peer == "build-host" and not args.host_human_bot:
                continue
            start_result = start_bot(
                automation,
                artifact_dir,
                peer,
                persona=args.bot_persona,
                seed=bot_seed,
                args=args,
                journal_path=journal,
            )
            if start_result.get("success") is not True:
                failures.append(f"{peer}_bot_start_failed")

        deadline = time.time() + args.max_duration_seconds
        while time.time() < deadline and not failures:
            final_host = dump_state(host, artifact_dir, "build-host", "endurance-latest")
            final_client = dump_state(client, artifact_dir, "build-client", "endurance-latest")
            elapsed = time.time() - start_time
            progress_timeline.append(snapshot_row(final_host, elapsed))
            write_json(artifact_dir / "progress-timeline.json", progress_timeline)

            host_key = checkpoint_key(final_host)
            client_key = checkpoint_key(final_client)
            capture, timed = should_capture_checkpoint(
                host_key,
                client_key,
                checkpoint_keys_seen,
                last_timed_checkpoint_at,
                start_time=start_time,
                args=args,
            )
            if capture and host_key is not None:
                checkpoint_keys_seen.add(host_key)
                if timed:
                    last_timed_checkpoint_at = time.time()
                checkpoints.append(wait_checkpoint_comparison(
                    host,
                    client,
                    artifact_dir,
                    index=len(checkpoints) + 1,
                    round_number=host_key[0],
                    current_state=host_key[1],
                    expected_players=EXPECTED_PLAYERS,
                    scene=args.scene,
                    timeout=args.checkpoint_timeout,
                ))

            current_game = game(final_host)
            current_round = to_int(current_game.get("currentRound"))
            current_state = current_game.get("currentState")
            write_json(artifact_dir / "game-to-end-wait-latest.json", {
                "hostGame": current_game,
                "clientGame": game(final_client),
                "maxRounds": args.max_rounds,
                "deadlineSecondsRemaining": max(0, deadline - time.time()),
                "progress": progress_summary(progress_timeline),
            })
            if current_state == "GameOver":
                limit_reason = "game_over_reached"
                break
            if args.max_rounds > 0 and current_round >= args.max_rounds:
                limit_reason = "max_rounds_reached"
                break
            time.sleep(max(0.5, args.poll_interval_seconds))

        if limit_reason == "none":
            limit_reason = "max_duration_reached" if time.time() >= deadline else "stopped"

        if not final_host:
            final_host = dump_state(host, artifact_dir, "build-host", "final")
        if not final_client:
            final_client = dump_state(client, artifact_dir, "build-client", "final")
        write_json(artifact_dir / "final-snapshot-host.json", final_host)
        write_json(artifact_dir / "final-snapshot-client.json", final_client)
        write_json(artifact_dir / "snapshots" / "build-host-final.json", final_host)
        write_json(artifact_dir / "snapshots" / "build-client-final.json", final_client)

        final_key = checkpoint_key(final_host)
        if final_key is not None and final_key == checkpoint_key(final_client):
            checkpoints.append(wait_checkpoint_comparison(
                host,
                client,
                artifact_dir,
                index=len(checkpoints) + 1,
                round_number=final_key[0],
                current_state=final_key[1],
                expected_players=EXPECTED_PLAYERS,
                scene=args.scene,
                timeout=args.checkpoint_timeout,
            ))

        final_comparison = compare_snapshots(final_host, final_client)
        write_json(artifact_dir / "final-comparison.json", final_comparison)

        host_status = collect_bot_status(host, artifact_dir, "build-host", "final") if args.host_human_bot else {
            "success": True,
            "data": {"enabled": False, "running": False, "commandsIssued": 0, "stopReason": "observer_peer"},
        }
        client_status = collect_bot_status(client, artifact_dir, "build-client", "final")
        write_json(artifact_dir / "bot-status-summary.json", {"host": host_status, "client": client_status})

        if args.headless_player:
            skipped_screenshot = {"success": True, "skipped": True, "reason": "headless_player", "headlessPlayer": True}
            write_json(artifact_dir / "build-host-screenshot.json", skipped_screenshot)
            write_json(artifact_dir / "build-client-screenshot.json", skipped_screenshot)
        else:
            write_json(artifact_dir / "build-host-screenshot.json", safe_request(host.screenshot))
            write_json(artifact_dir / "build-client-screenshot.json", safe_request(client.screenshot))

        host_logs = safe_request(host.logs_recent)
        client_logs = safe_request(client.logs_recent)
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        write_json(artifact_dir / "build-client-logs-recent.json", client_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_logs))
    finally:
        functional_failures = list(failures)
        cleanup_processes = [proc for proc in (client_proc, host_proc) if proc is not None]
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            cleanup_processes,
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(functional_failures),
            strict_cleanup=args.strict_cleanup,
        )

        collect_player_log(artifact_dir, "build-host-or-last")
        write_final_timeline(artifact_dir, ["build-host", "build-client"])
        write_json(artifact_dir / "progress-timeline.json", progress_timeline)

        metrics = progression_metrics(artifact_dir, checkpoints, cleanup_report)
        write_json(artifact_dir / "bot-metrics-summary.json", metrics)
        write_json(artifact_dir / "checkpoint-summary.json", {
            "case": CASE_NAME,
            "checkpoints": checkpoints,
            "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
            "statesReached": metrics.get("summary", {}).get("statesReached"),
        })

        mptest_failure_lines = [failure.removeprefix("mptest_failure_log:") for failure in failures if failure.startswith("mptest_failure_log:")]
        classification = classify_endurance(
            final_host,
            final_client,
            final_comparison,
            checkpoints,
            progress_timeline,
            mptest_failure_lines,
            cleanup_report,
            limit_reason=limit_reason,
        )
        accepted_timeout_result = args.allow_timeout_result and classification.get("finalStatus") in {"NEEDS_TUNING", "TIMEOUT", "STALLED"}
        success = classification.get("gameToEndPass") is True or accepted_timeout_result
        functional_success = not [failure for failure in functional_failures if not failure.startswith("mptest_failure_log:")] and success
        result = {
            "case": CASE_NAME,
            "artifactDir": str(artifact_dir),
            "success": functional_success and cleanup_report.get("cleanupSuccess") is True,
            "functionalSuccess": functional_success,
            "gameToEndPass": classification.get("gameToEndPass") is True,
            "finalStatus": classification.get("finalStatus"),
            "allowTimeoutResult": args.allow_timeout_result,
            "timeoutResultAccepted": accepted_timeout_result,
            "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupReportPath": "cleanup-report.json",
            "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
            "statesReached": metrics.get("summary", {}).get("statesReached"),
            "checkpointSummaryPath": "checkpoint-summary.json",
            "progressTimelinePath": "progress-timeline.json",
            "botMetricsSummaryPath": "bot-metrics-summary.json",
            "finalSnapshotHostPath": "final-snapshot-host.json",
            "finalSnapshotClientPath": "final-snapshot-client.json",
            "headlessPlayer": args.headless_player,
            "classification": classification,
            "failures": failures,
        }
        write_json(artifact_dir / "game-to-end-result.json", result)
        write_json(artifact_dir / "result.json", result)

        if classification.get("finalStatus") in {"NEEDS_TUNING", "TIMEOUT", "STALLED"}:
            failure_summary(artifact_dir / "timeout-summary.md", f"{CASE_NAME} {classification.get('finalStatus')}", classification.get("warnings") or [])
        if not result["success"]:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures + list(classification.get("errors") or []))

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "success": result["success"],
        "gameToEndPass": result["gameToEndPass"],
        "finalStatus": result["finalStatus"],
        "cleanupStatus": result["cleanupStatus"],
        "maxRoundReached": result.get("maxRoundReached"),
        "failures": result.get("failures"),
    }, indent=2))
    return 0 if result["success"] else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--max-duration-seconds", type=int, default=2400)
    parser.add_argument("--max-rounds", type=int, default=20)
    parser.add_argument("--max-commands-per-bot", type=int, default=1000)
    parser.add_argument("--allow-timeout-result", action="store_true")
    parser.add_argument("--seed", type=int, default=9101)
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-prepare-mode", choices=["augment-only", "skip", "full"], default="augment-only")
    parser.add_argument("--checkpoint-every-round", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--checkpoint-every-state", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--checkpoint-every-seconds", type=int, default=120)
    parser.add_argument("--host-human-bot", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--headless-player", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=90)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--checkpoint-timeout", type=int, default=25)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    parser.add_argument("--poll-interval-seconds", type=float, default=2.0)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    args = parser.parse_args()
    if args.max_duration_seconds <= 0:
        raise SystemExit("--max-duration-seconds must be > 0")
    if args.max_rounds < 0:
        raise SystemExit("--max-rounds must be >= 0")
    if args.max_commands_per_bot <= 0:
        raise SystemExit("--max-commands-per-bot must be > 0")
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
