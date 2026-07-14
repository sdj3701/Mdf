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
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)
from long_progression_common import (
    checkpoint_key,
    completion_satisfied,
    dump_state,
    game,
    mptest_failures,
    players,
    progression_metrics,
    safe_request,
    state,
    unique_player_ids,
    wait_checkpoint_comparison,
    write_final_timeline,
)


CASE_NAME = "human-bot-3round-progression"
EXPECTED_PLAYERS = 2


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
        host_ready = session_ready(host_state, EXPECTED_PLAYERS, scene)
        client_ready = session_ready(client_state, EXPECTED_PLAYERS, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": session_not_ready_reasons(host_state, EXPECTED_PLAYERS, scene),
            "clientReasons": session_not_ready_reasons(client_state, EXPECTED_PLAYERS, scene),
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
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        client_state = dump_state(client, artifact_dir, "build-client", f"{label}-latest")
        host_ready = snapshot_ready(host_state, EXPECTED_PLAYERS, scene)
        client_ready = snapshot_ready(client_state, EXPECTED_PLAYERS, scene)
        ready = host_ready and client_ready
        write_json(artifact_dir / f"{label}-state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, EXPECTED_PLAYERS, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, EXPECTED_PLAYERS, scene),
            "hostGame": game(host_state),
            "clientGame": game(client_state),
            "stableMatches": stable_matches,
        })
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(1)
    return host_state, client_state, False


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
        str(args.bot_max_commands),
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
    duration_seconds: int,
    stop_at_round: int = 0,
    max_commands: int,
    bot_prepare_mode: str,
    journal_path: pathlib.Path,
) -> dict[str, Any]:
    result = safe_request(lambda: client.bot_start(
        persona=persona,
        seed=seed,
        durationSeconds=duration_seconds,
        stopAtRound=stop_at_round,
        maxCommands=max_commands,
        skipPrepare=bot_prepare_mode == "skip",
        prepareAugmentOnly=bot_prepare_mode == "augment-only",
        journalPath=str(journal_path),
    ))
    write_json(artifact_dir / f"{peer}-bot-start.json", result)
    return result


def collect_bot_status(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    peer: str,
    label: str,
) -> dict[str, Any]:
    status = safe_request(client.bot_status)
    journal = safe_request(client.bot_journal)
    write_json(artifact_dir / f"{peer}-bot-status-{label}.json", status)
    write_json(artifact_dir / f"{peer}-bot-journal-{label}.json", journal)
    return status


def final_assertions(
    final_host: Any,
    final_client: Any,
    checkpoints: list[dict[str, Any]],
    completion_ok: bool,
    completion_reason: str,
    cleanup_report: dict[str, Any],
    strict_cleanup: bool,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    if not completion_ok:
        errors.append(completion_reason)
    if not unique_player_ids(final_host):
        errors.append("host.player_ids_not_unique")
    if not unique_player_ids(final_client):
        errors.append("client.player_ids_not_unique")
    if len(players(final_host)) != EXPECTED_PLAYERS:
        errors.append(f"host.expected_players_not_present:{len(players(final_host))}")
    if len(players(final_client)) != EXPECTED_PLAYERS:
        errors.append(f"client.expected_players_not_present:{len(players(final_client))}")
    failed_checkpoints = [checkpoint for checkpoint in checkpoints if checkpoint.get("success") is not True]
    if failed_checkpoints:
        errors.extend(f"checkpoint_failed:{checkpoint.get('checkpointId')}" for checkpoint in failed_checkpoints)
    for checkpoint in checkpoints:
        warnings.extend(str(warning) for warning in checkpoint.get("warnings") or [])
    if strict_cleanup and cleanup_report.get("cleanupStatus") != "PASS":
        errors.append(f"cleanup_failed:{cleanup_report.get('cleanupStatus')}")
    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "completion": {
            "success": completion_ok,
            "reason": completion_reason,
            "hostGame": game(final_host),
            "clientGame": game(final_client),
        },
        "checkpointCount": len(checkpoints),
        "failedCheckpoints": [checkpoint.get("checkpointId") for checkpoint in failed_checkpoints],
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
    }


def peer_completion_satisfied(
    final_host: Any,
    final_client: Any,
    checkpoints: list[dict[str, Any]],
    *,
    target_round: int,
    completion_mode: str,
    allow_early_game_over: bool,
) -> tuple[bool, str]:
    host_ok, host_reason = completion_satisfied(
        final_host,
        checkpoints,
        target_round=target_round,
        completion_mode=completion_mode,
        allow_early_game_over=allow_early_game_over,
    )
    client_ok, client_reason = completion_satisfied(
        final_client,
        checkpoints,
        target_round=target_round,
        completion_mode=completion_mode,
        allow_early_game_over=allow_early_game_over,
    )
    if host_ok and client_ok:
        return True, host_reason if host_reason == client_reason else f"host={host_reason};client={client_reason}"
    return False, f"peer_completion_not_met:host={host_reason};client={client_reason}"


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    if args.dry_run:
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "dryRun": True,
            "playerPath": args.player_path,
            "targetRound": args.target_round,
            "completionMode": args.completion_mode,
            "headlessPlayer": args.headless_player,
            "verifyBattle2Camera": args.verify_battle2_camera,
            "useLobbyStartGate": args.use_lobby_start_gate,
        })
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    session = args.session or new_session("hbot3r")
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
    effective_lobby_scene = "JoinLobby" if args.use_lobby_start_gate else args.lobby_scene
    host_journal_path = artifact_dir / "build-host-bot.jsonl"
    client_journal_path = artifact_dir / "build-client-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    failures: list[str] = []
    checkpoints: list[dict[str, Any]] = []
    checkpoint_keys_seen: set[tuple[int, str]] = set()
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
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
        "scene": args.scene,
        "lobbyScene": effective_lobby_scene,
        "useLobbyStartGate": args.use_lobby_start_gate,
        "seed": args.seed,
        "hostBotSeed": host_bot_seed,
        "clientBotSeed": client_bot_seed,
        "targetRound": args.target_round,
        "completionMode": args.completion_mode,
        "maxDurationSeconds": args.max_duration_seconds,
        "botMaxCommands": args.bot_max_commands,
        "botPrepareMode": args.bot_prepare_mode,
        "botPersona": args.bot_persona,
        "hostHumanBot": args.host_human_bot,
        "allowEarlyGameOver": args.allow_early_game_over,
        "checkpointEveryState": args.checkpoint_every_state,
        "checkpointEveryRound": args.checkpoint_every_round,
        "verifyBattle2Camera": args.verify_battle2_camera,
        "cameraCheckpointTimeout": args.camera_checkpoint_timeout,
        "headlessPlayer": args.headless_player,
        "dryRun": args.dry_run,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
        "orphanPressure": orphan_gate,
    })

    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")
        cleanup_report = {
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "cleanupSuccess": False,
            "orphanedPids": [
                proc.get("pid")
                for proc in orphan_gate.get("processes", [])
                if isinstance(proc, dict) and isinstance(proc.get("pid"), int)
            ],
        }
        result = {
            "case": CASE_NAME,
            "artifactDir": str(artifact_dir),
            "success": False,
            "functionalSuccess": False,
            "cleanupSuccess": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "orphanPressure": orphan_gate,
            "headlessPlayer": args.headless_player,
            "failures": failures,
        }
        write_json(artifact_dir / "long-progression-result.json", result)
        write_json(artifact_dir / "result.json", result)
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} blocked", failures)
        print(json.dumps({
            "artifactDir": str(artifact_dir),
            "success": False,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "failures": failures,
        }, indent=2))
        return 2

    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    final_host: dict[str, Any] = {}
    final_client: dict[str, Any] = {}
    completion_ok = False
    completion_reason = "not_started"

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
            scene=effective_lobby_scene,
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
            write_json(artifact_dir / "build-host-bot-paused.json", safe_request(lambda: host.bot_stop(reason="long_progression_pre_checkpoint_pause")))
        else:
            write_json(artifact_dir / "build-host-bot-paused.json", {
                "success": True,
                "message": "host HumanBot disabled; observer peer",
            })

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
            scene=effective_lobby_scene,
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
        write_json(artifact_dir / "build-client-bot-paused.json", safe_request(lambda: client.bot_stop(reason="long_progression_pre_checkpoint_pause")))

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, effective_lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, effective_lobby_scene, EXPECTED_PLAYERS, args.start_timeout):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(host, client, artifact_dir, args.lobby_timeout, effective_lobby_scene)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        lobby_gate_ready = True
        if args.use_lobby_start_gate:
            ready_requests = {
                "host": safe_request(lambda: host.lobby_ready(True)),
                "client": safe_request(lambda: client.lobby_ready(True)),
            }
            write_json(artifact_dir / "lobby-ready-requests.json", ready_requests)
            if any(response.get("success") is not True for response in ready_requests.values()):
                lobby_gate_ready = False
            else:
                ready_deadline = time.time() + args.lobby_timeout
                while time.time() < ready_deadline:
                    ready_status = {
                        "host": safe_request(host.lobby_status),
                        "client": safe_request(client.lobby_status),
                    }
                    write_json(artifact_dir / "lobby-ready-status-latest.json", ready_status)
                    if all(
                        response.get("success") is True
                        and (response.get("data") or {}).get("allReady") is True
                        for response in ready_status.values()
                    ):
                        break
                    time.sleep(0.25)
                else:
                    lobby_gate_ready = False

            if not lobby_gate_ready:
                failures.append("lobby_ready_sync_failed")

        if args.use_lobby_start_gate:
            load_result = host.lobby_start_game() if lobby_gate_ready else {
                "success": False,
                "message": "lobby ready synchronization failed",
            }
        else:
            load_result = host.load_game(args.scene)
        load_artifact_name = "build-host-lobby-start-game.json" if args.use_lobby_start_gate else "build-host-load-game.json"
        write_json(artifact_dir / load_artifact_name, load_result)
        if not load_result.get("success"):
            failures.append("host_lobby_start_gate_failed" if args.use_lobby_start_gate else "host_load_game_failed")

        host_before, client_before, before_ready = wait_stable_states(
            host,
            client,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-long-bot",
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-long-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-before-long-bot.json", client_before)
        if not before_ready:
            failures.append("before_long_bot_state_ready_timeout")

        if args.use_lobby_start_gate:
            match_prewarm_logs = {
                "host": safe_request(host.logs_recent),
                "client": safe_request(client.logs_recent),
            }
            write_json(artifact_dir / "match-prewarm-logs.json", match_prewarm_logs)
            host_log_lines = ((match_prewarm_logs["host"].get("data") or {}).get("lines") or [])
            client_log_lines = ((match_prewarm_logs["client"].get("data") or {}).get("lines") or [])
            if not any("phase=match_prewarm_gate" in line and "result=pass" in line for line in host_log_lines):
                failures.append("match_prewarm_gate_pass_log_missing")
            if not any("phase=match_prewarm_ack" in line and "result=pass" in line for line in host_log_lines):
                failures.append("match_prewarm_authority_ack_log_missing")
            if not any("phase=match_prewarm_peer" in line and "result=pass" in line for line in client_log_lines):
                failures.append("match_prewarm_client_ready_log_missing")

        before_key = checkpoint_key(host_before)
        if before_key is not None:
            checkpoint_keys_seen.add(before_key)
            checkpoint = wait_checkpoint_comparison(
                host,
                client,
                artifact_dir,
                index=len(checkpoints) + 1,
                round_number=before_key[0],
                current_state=before_key[1],
                expected_players=EXPECTED_PLAYERS,
                scene=args.scene,
                timeout=args.checkpoint_timeout,
                verify_battle2_camera=args.verify_battle2_camera,
                camera_timeout=args.camera_checkpoint_timeout,
            )
            checkpoints.append(checkpoint)

        bot_peers = [
            ("build-client", client, args.bot_persona, client_bot_seed, client_journal_path),
        ]
        if args.host_human_bot:
            bot_peers.insert(0, ("build-host", host, args.bot_persona, host_bot_seed, host_journal_path))

        for peer, automation, persona, bot_seed, journal in bot_peers:
            start_result = start_bot(
                automation,
                artifact_dir,
                peer,
                persona=persona,
                seed=bot_seed,
                duration_seconds=args.max_duration_seconds,
                max_commands=args.bot_max_commands,
                bot_prepare_mode=args.bot_prepare_mode,
                journal_path=journal,
            )
            if start_result.get("success") is not True:
                failures.append(f"{peer}_bot_start_failed")

        deadline = time.time() + args.max_duration_seconds
        poll_interval = max(0.5, args.poll_interval_seconds)
        while time.time() < deadline and not failures:
            final_host = dump_state(host, artifact_dir, "build-host", "long-latest")
            final_client = dump_state(client, artifact_dir, "build-client", "long-latest")
            host_key = checkpoint_key(final_host)
            client_key = checkpoint_key(final_client)
            if host_key is not None and host_key == client_key and host_key not in checkpoint_keys_seen:
                checkpoint_keys_seen.add(host_key)
                checkpoint = wait_checkpoint_comparison(
                    host,
                    client,
                    artifact_dir,
                    index=len(checkpoints) + 1,
                    round_number=host_key[0],
                    current_state=host_key[1],
                    expected_players=EXPECTED_PLAYERS,
                    scene=args.scene,
                    timeout=args.checkpoint_timeout,
                    verify_battle2_camera=args.verify_battle2_camera,
                    camera_timeout=args.camera_checkpoint_timeout,
                )
                checkpoints.append(checkpoint)

            completion_ok, completion_reason = peer_completion_satisfied(
                final_host,
                final_client,
                checkpoints,
                target_round=args.target_round,
                completion_mode=args.completion_mode,
                allow_early_game_over=args.allow_early_game_over,
            )
            write_json(artifact_dir / "long-progress-wait-latest.json", {
                "completionOk": completion_ok,
                "completionReason": completion_reason,
                "hostGame": game(final_host),
                "clientGame": game(final_client),
                "checkpointCount": len(checkpoints),
                "deadlineSecondsRemaining": max(0, deadline - time.time()),
            })
            if completion_ok:
                break
            time.sleep(poll_interval)

        if not final_host:
            final_host = dump_state(host, artifact_dir, "build-host", "final")
        if not final_client:
            final_client = dump_state(client, artifact_dir, "build-client", "final")

        final_key = checkpoint_key(final_host)
        if final_key is not None and final_key == checkpoint_key(final_client) and final_key not in checkpoint_keys_seen:
            checkpoint_keys_seen.add(final_key)
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
                verify_battle2_camera=args.verify_battle2_camera,
                camera_timeout=args.camera_checkpoint_timeout,
            ))

        write_json(artifact_dir / "snapshots" / "build-host-final.json", final_host)
        write_json(artifact_dir / "snapshots" / "build-client-final.json", final_client)

        completion_ok, completion_reason = peer_completion_satisfied(
            final_host,
            final_client,
            checkpoints,
            target_round=args.target_round,
            completion_mode=args.completion_mode,
            allow_early_game_over=args.allow_early_game_over,
        )
        if not completion_ok:
            failures.append(completion_reason)

        host_status = collect_bot_status(host, artifact_dir, "build-host", "final") if args.host_human_bot else {
            "success": True,
            "data": {
                "enabled": False,
                "running": False,
                "commandsIssued": 0,
                "stopReason": "observer_peer",
            },
        }
        client_status = collect_bot_status(client, artifact_dir, "build-client", "final")
        write_json(artifact_dir / "bot-status-summary.json", {
            "host": host_status,
            "client": client_status,
        })

        if args.headless_player:
            skipped_screenshot = {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            }
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
        if args.strict_cleanup and cleanup_report.get("cleanupStatus") != "PASS":
            failures.append(f"cleanup_failed:{cleanup_report.get('cleanupStatus')}")

        collect_player_log(artifact_dir, "build-host-or-last")
        write_final_timeline(artifact_dir, ["build-host", "build-client"])

        metrics = progression_metrics(artifact_dir, checkpoints, cleanup_report)
        write_json(artifact_dir / "bot-metrics-summary.json", metrics)
        checkpoint_summary = {
            "case": CASE_NAME,
            "targetRound": args.target_round,
            "completionMode": args.completion_mode,
            "verifyBattle2Camera": args.verify_battle2_camera,
            "useLobbyStartGate": args.use_lobby_start_gate,
            "checkpoints": checkpoints,
            "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
            "statesReached": metrics.get("summary", {}).get("statesReached"),
        }
        write_json(artifact_dir / "checkpoint-summary.json", checkpoint_summary)

        assertions = final_assertions(
            final_host,
            final_client,
            checkpoints,
            completion_ok,
            completion_reason,
            cleanup_report,
            args.strict_cleanup,
        )
        if assertions.get("success") is not True:
            for error in assertions.get("errors") or []:
                if error not in failures:
                    failures.append(error)

        functional_success = not functional_failures and assertions.get("success") is True
        cleanup_success = cleanup_report.get("cleanupSuccess") is True
        overall_success = functional_success and (cleanup_success or not args.strict_cleanup)
        result = {
            "case": CASE_NAME,
            "artifactDir": str(artifact_dir),
            "success": overall_success,
            "functionalSuccess": functional_success,
            "cleanupSuccess": cleanup_success,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupReportPath": "cleanup-report.json",
            "targetRound": args.target_round,
            "completionMode": args.completion_mode,
            "completion": assertions.get("completion"),
            "maxRoundReached": metrics.get("summary", {}).get("maxRoundReached"),
            "statesReached": metrics.get("summary", {}).get("statesReached"),
            "checkpointSummaryPath": "checkpoint-summary.json",
            "botMetricsSummaryPath": "bot-metrics-summary.json",
            "headlessPlayer": args.headless_player,
            "verifyBattle2Camera": args.verify_battle2_camera,
            "failures": failures,
            "assertions": assertions,
        }
        write_json(artifact_dir / "long-progression-result.json", result)
        write_json(artifact_dir / "result.json", result)
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "success": not failures and cleanup_report.get("cleanupStatus") == "PASS",
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "maxRoundReached": (progression_metrics(artifact_dir, checkpoints, cleanup_report).get("summary") or {}).get("maxRoundReached"),
        "completionReason": completion_reason,
        "failures": failures,
    }, indent=2))
    return 0 if not failures and cleanup_report.get("cleanupStatus") == "PASS" else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument(
        "--use-lobby-start-gate",
        action="store_true",
        help="Start from JoinLobby and invoke the production match prewarm/peer-ACK gate instead of the direct /loadGame shortcut.",
    )
    parser.add_argument("--target-round", type=int, default=3)
    parser.add_argument("--completion-mode", choices=["round-complete", "battle2-reached"], default="round-complete")
    parser.add_argument("--max-duration-seconds", type=int, default=900)
    parser.add_argument("--checkpoint-every-state", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--checkpoint-every-round", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--allow-early-game-over", action="store_true")
    parser.add_argument("--bot-persona", default="balanced")
    parser.add_argument("--bot-max-commands", type=int, default=500)
    parser.add_argument(
        "--bot-prepare-mode",
        choices=["augment-only", "skip", "full"],
        default="augment-only",
        help="Limit long HumanBot prepare actions. augment-only keeps the long run focused on phase and battle sync while avoiding prepare-placement drift.",
    )
    parser.add_argument("--host-human-bot", action="store_true")
    parser.add_argument("--seed", type=int, default=8101)
    parser.add_argument("--headless-player", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=60)
    parser.add_argument("--lobby-timeout", type=int, default=90)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--checkpoint-timeout", type=int, default=25)
    parser.add_argument(
        "--verify-battle2-camera",
        action=argparse.BooleanOptionalAction,
        default=False,
        help="At stable Battle2 checkpoints, inspect each peer's automatic camera state without requesting navigation.",
    )
    parser.add_argument("--camera-checkpoint-timeout", type=float, default=5.0)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    parser.add_argument("--poll-interval-seconds", type=float, default=1.0)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    args = parser.parse_args()
    if args.target_round < 1:
        raise SystemExit("--target-round must be >= 1")
    if args.max_duration_seconds <= 0:
        raise SystemExit("--max-duration-seconds must be > 0")
    if args.camera_checkpoint_timeout <= 0:
        raise SystemExit("--camera-checkpoint-timeout must be > 0")
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
