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
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from launch_player import PlayerProcess, launch_player


CASE_NAME = "durable-command-soak"


def snapshot_body(snapshot: dict[str, Any]) -> dict[str, Any]:
    state = normalize_snapshot_response(snapshot)
    return state if isinstance(state, dict) else {}


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def players_by_id(snapshot: dict[str, Any]) -> dict[int, dict[str, Any]]:
    result: dict[int, dict[str, Any]] = {}
    for player in snapshot_body(snapshot).get("players") or []:
        player_id = player.get("playerId")
        if isinstance(player_id, int):
            result[player_id] = player
    return result


def shop(player: dict[str, Any]) -> dict[str, Any]:
    value = player.get("shop")
    return value if isinstance(value, dict) else {}


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


def wait_ready(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "latest")
        client_state = dump_state(client, artifact_dir, "build-client", "latest")
        if snapshot_ready(host_state, 2, scene) and snapshot_ready(client_state, 2, scene):
            return host_state, client_state, True
        time.sleep(2)
    return host_state, client_state, False


def choose_target_player(snapshot: dict[str, Any], min_gold: int) -> int | None:
    candidates = sorted(
        players_by_id(snapshot).values(),
        key=lambda player: (player.get("gold") or 0, -(player.get("playerId") or 0)),
        reverse=True,
    )
    for player in candidates:
        if (player.get("gold") or 0) >= min_gold and (shop(player).get("available") is True):
            return player.get("playerId")
    return None


def wait_command_agreement(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    target_player_id: int,
    gold_before: int,
    cost: int,
    revision_before: int,
    iteration: int,
    timeout: int,
) -> tuple[dict[str, Any], dict[str, Any], bool, dict[str, Any]]:
    deadline = time.time() + timeout
    latest_host: dict[str, Any] = {}
    latest_client: dict[str, Any] = {}
    expected_gold = gold_before - cost
    while time.time() < deadline:
        latest_host = dump_state(host, artifact_dir, "build-host", f"iter-{iteration}-latest")
        latest_client = dump_state(client, artifact_dir, "build-client", f"iter-{iteration}-latest")
        host_player = players_by_id(latest_host).get(target_player_id) or {}
        client_player = players_by_id(latest_client).get(target_player_id) or {}
        host_shop = shop(host_player)
        client_shop = shop(client_player)
        evidence = {
            "expectedGold": expected_gold,
            "hostGold": host_player.get("gold"),
            "clientGold": client_player.get("gold"),
            "hostRevision": host_shop.get("revision"),
            "clientRevision": client_shop.get("revision"),
            "hostItemsHash": host_shop.get("itemsHash"),
            "clientItemsHash": client_shop.get("itemsHash"),
        }
        gold_mutated = host_player.get("gold") == expected_gold
        revision_mutated = (host_shop.get("revision") or 0) > revision_before
        peers_agree = (
            host_player.get("gold") == client_player.get("gold")
            and host_shop.get("revision") == client_shop.get("revision")
            and host_shop.get("itemsHash") == client_shop.get("itemsHash")
            and host_shop.get("count") == client_shop.get("count")
        )
        if gold_mutated and revision_mutated and peers_agree:
            return latest_host, latest_client, True, evidence
        time.sleep(0.5)
    return latest_host, latest_client, False, {}


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("cmd")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    failures: list[str] = []
    iteration_reports: list[dict[str, Any]] = []

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "hostPort": host_port,
        "clientPort": client_port,
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "iterations": args.iterations,
        "commandProfile": "ai_behavior_reroll_shop",
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
            scenario="durable_command_soak",
        )
        host = AutomationClient(host_port, host_token)
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
            scenario="durable_command_soak",
        )
        client = AutomationClient(client_port, client_token)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")

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

        host_pre, client_pre, ready = wait_ready(host, client, artifact_dir, args.state_timeout, args.scene)
        write_json(artifact_dir / "snapshots" / "build-host-pre.json", host_pre)
        write_json(artifact_dir / "snapshots" / "build-client-pre.json", client_pre)
        if not ready:
            failures.append("pre_command_state_ready_timeout")

        target_player_id = choose_target_player(host_pre, args.iterations * 2)
        if target_player_id is None:
            failures.append("NEEDS_GAMEPLAY_HOOK:no_safe_reroll_target_with_controlled_gold")
        else:
            for iteration in range(1, args.iterations + 1):
                before_host = dump_state(host, artifact_dir, "build-host", f"iter-{iteration}-before")
                host_player_before = players_by_id(before_host).get(target_player_id) or {}
                host_shop_before = shop(host_player_before)
                gold_before = int(host_player_before.get("gold") or 0)
                revision_before = int(host_shop_before.get("revision") or 0)

                command_response = host.command(name="reroll_shop", playerId=target_player_id)
                write_json(artifact_dir / f"command-iter-{iteration}.json", command_response)
                if not command_response.get("success"):
                    failures.append(f"durable_command_iter_{iteration}_request_failed")
                    iteration_reports.append({"iteration": iteration, "request": command_response})
                    break

                data = command_response.get("data") or {}
                cost = int(data.get("cost") or 0)
                command_gold_before = int(data.get("goldBefore") or gold_before)
                after_host, after_client, agreed, evidence = wait_command_agreement(
                    host,
                    client,
                    artifact_dir,
                    target_player_id,
                    command_gold_before,
                    cost,
                    revision_before,
                    iteration,
                    args.command_timeout,
                )
                write_json(artifact_dir / "snapshots" / f"build-host-iter-{iteration}-after.json", after_host)
                write_json(artifact_dir / "snapshots" / f"build-client-iter-{iteration}-after.json", after_client)
                iteration_reports.append({
                    "iteration": iteration,
                    "targetPlayerId": target_player_id,
                    "request": command_response,
                    "agreement": agreed,
                    "evidence": evidence,
                })
                if not agreed:
                    failures.append(f"durable_command_iter_{iteration}_peer_agreement_timeout")
                    break

        write_json(artifact_dir / "durable-command-report.json", {
            "command": "reroll_shop",
            "iterations": args.iterations,
            "targetPlayerId": target_player_id,
            "reports": iteration_reports,
            "failures": failures,
        })
        write_json(artifact_dir / "build-host-logs-recent.json", host.logs_recent())
        write_json(artifact_dir / "build-client-logs-recent.json", client.logs_recent())
        write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        for peer, proc, port, token in (
            ("build-client", client_proc, client_port, client_token),
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

        player_log = collect_player_log(artifact_dir, "durable-command-last")
        logs = [
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client.stdout.log",
            artifact_dir / "build-client.stderr.log",
        ]
        if player_log:
            logs.append(player_log)
        write_timeline(artifact_dir, logs)

    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=8001)
    parser.add_argument("--iterations", type=int, default=3)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--command-timeout", type=int, default=20)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
