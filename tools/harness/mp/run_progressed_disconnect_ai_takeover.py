#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
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
    wait_build_peer_started,
    write_json,
)
from launch_player import PlayerProcess, launch_player
from progressed_human_bot_common import (
    dump_state,
    local_player,
    mptest_failures,
    nested,
    player_by_id,
    players,
    preservation_assertions,
    random_outcome_summary,
    unique_player_ids,
    wait_bot_progression,
    wait_session_states,
    wait_stable_states,
)


CASE_NAME = "progressed-disconnect-ai-takeover"


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
        "targetPlayerRef": target.get("playerRef") if isinstance(target, dict) else None,
        "targetFieldReady": field.get("ready") is True if isinstance(field, dict) else False,
        "targetAiRegistered": ai.get("controllerRegistered") is True if isinstance(ai, dict) else False,
        "targetShopHash": nested(target, "shop", "itemsHash"),
        "targetAugmentSelectedHash": nested(target, "augment", "selectedHash"),
        "targetWallHash": nested(target, "field", "wallHash"),
        "targetUnitsHash": nested(target, "field", "placedUnitsHash"),
    }


def takeover_ready(assertions: dict[str, Any], scene: str, expected_players: int) -> bool:
    return (
        assertions["scene"] == scene
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
    import time

    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {}
    latest_preservation: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        snapshot = dump_state(host, artifact_dir, "build-host", "post-disconnect-latest")
        latest_assertions = takeover_assertions(snapshot, target_player_id)
        latest_preservation = preservation_assertions(
            checkpoint_snapshot,
            snapshot,
            target_player_id,
            "progressed_disconnect_takeover",
        )
        ready = takeover_ready(latest_assertions, scene, expected_players) and latest_preservation["success"]
        write_json(artifact_dir / "progressed-disconnect-ai-takeover-wait-latest.json", {
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
    session = args.session or new_session("pdait")
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
    target_player_id = -1

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
        "clientConnectionTokenHash": hash_for_log(client_connection),
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
            max_players=2,
            scene=args.lobby_scene,
            case_name=CASE_NAME,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario="progressed_disconnect_ai_takeover",
            extra_args=freeze_game_flow_args(),
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
            scenario="progressed_disconnect_ai_takeover",
            extra_args=bot_args(args, bot_journal_path, bot_seed),
        )
        client = AutomationClient(client_port, client_token, timeout=args.request_timeout)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

        pause_result = client.bot_stop(reason="phase22_pre_checkpoint_pause")
        write_json(artifact_dir / "build-client-bot-paused.json", pause_result)
        if pause_result.get("success") is not True:
            failures.append("bot_pause_failed")

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(
            host,
            client,
            artifact_dir,
            args.lobby_timeout,
            args.lobby_scene,
            2,
            "build-client",
        )
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
            2,
            "before-bot",
            "build-client",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-before-bot.json", client_before)
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
        write_json(artifact_dir / "build-client-bot-start.json", start_result)
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
            "build-client",
            args.stable_samples,
        )
        write_json(artifact_dir / "snapshots" / "build-host-progressed-checkpoint.json", host_progressed)
        write_json(artifact_dir / "snapshots" / "build-client-progressed-checkpoint.json", client_progressed)
        write_json(artifact_dir / "human-bot-progressed-assertions.json", progression)
        write_json(artifact_dir / "random-outcome-summary.json", random_outcome_summary(host_progressed, client_progressed))
        if not progressed:
            failures.append("bot_no_meaningful_command")
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
            write_result(artifact_dir, failures)
            return 1

        target_player_id = int(progression.get("botPlayerId", -1)) if isinstance(progression, dict) else -1
        client_local = local_player(client_progressed)
        if target_player_id < 0 and isinstance(client_local, dict):
            target_player_id = int(client_local.get("playerId", -1))
        if target_player_id < 0:
            failures.append("target_player_id_invalid")
        else:
            write_json(artifact_dir / "disconnect-target.json", {
                "playerId": target_player_id,
                "connectionTokenHash": client_local.get("connectionTokenHash") if isinstance(client_local, dict) else "unknown",
                "expectedConnectionTokenHash": hash_for_log(client_connection),
            })

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
            "progressed": {
                "host": "snapshots/build-host-progressed-checkpoint.json",
                "client": "snapshots/build-client-progressed-checkpoint.json",
                "assertions": "human-bot-progressed-assertions.json",
            },
        })

        if client_proc is not None:
            client_proc.process.kill()
            client_proc.process.wait(timeout=10)
            client_proc.close_logs()

        if target_player_id >= 0:
            host_post, takeover, preservation, takeover_ok = wait_takeover(
                host,
                artifact_dir,
                host_progressed,
                target_player_id,
                args.takeover_timeout,
                args.scene,
                2,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-takeover.json", host_post)
            write_json(artifact_dir / "progressed-disconnect-ai-takeover-assertions.json", takeover)
            write_json(artifact_dir / "progressed-preservation-assertions.json", preservation)
            if not takeover_ok:
                failures.append("progressed_disconnect_ai_takeover_timeout")

        write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        host_logs = host.logs_recent()
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs))
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        if host_proc is not None:
            try:
                automation = AutomationClient(host_port, host_token, timeout=2.0)
                write_json(artifact_dir / "build-host-quit.json", automation.quit())
                host_proc.process.wait(timeout=10)
                host_proc.close_logs()
            except Exception:
                host_proc.terminate()
        if client_proc is not None and client_proc.process.poll() is None:
            client_proc.terminate()
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
    parser.add_argument("--seed", type=int, default=3001)
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
    parser.add_argument("--takeover-timeout", type=int, default=90)
    parser.add_argument("--request-timeout", type=float, default=15.0)
    parser.add_argument("--stable-samples", type=int, default=1)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
