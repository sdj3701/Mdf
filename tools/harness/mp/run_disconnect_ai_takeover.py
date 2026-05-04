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
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player


CASE_NAME = "disconnect-ai-takeover"


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = client.dump_state()
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def players(snapshot: dict[str, Any]) -> list[dict[str, Any]]:
    state = normalize_snapshot_response(snapshot)
    if not isinstance(state, dict):
        return []
    return [player for player in state.get("players") or [] if isinstance(player, dict)]


def local_player(snapshot: dict[str, Any]) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("isLocal") is True:
            return player
    return None


def player_by_id(snapshot: dict[str, Any], player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def has_unique_player_ids(snapshot: dict[str, Any]) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids))


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


def wait_states(
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
        host_state = dump_state(host, artifact_dir, "build-host", "latest")
        client_state = dump_state(client, artifact_dir, "build-client", "latest")
        host_ready = snapshot_ready(host_state, 2, scene)
        client_ready = snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        write_json(artifact_dir / "state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, 2, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, 2, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        write_json(artifact_dir / "comparison-latest.json", comparison)
        if host_ready and client_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, False


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
        "uniquePlayerIds": has_unique_player_ids(snapshot),
        "targetPlayerId": target_player_id,
        "targetExists": target is not None,
        "targetIsAI": target.get("isAI") is True if isinstance(target, dict) else False,
        "targetConnected": target.get("isConnected") is True if isinstance(target, dict) else None,
        "targetPlayerRef": target.get("playerRef") if isinstance(target, dict) else None,
        "targetFieldReady": field.get("ready") is True if isinstance(field, dict) else False,
        "targetAiRegistered": ai.get("controllerRegistered") is True if isinstance(ai, dict) else False,
    }


def takeover_ready(snapshot: dict[str, Any], target_player_id: int, scene: str) -> bool:
    assertions = takeover_assertions(snapshot, target_player_id)
    return (
        assertions["scene"] == scene
        and assertions["activePlayerCount"] == 1
        and assertions["playerCount"] == 2
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
    target_player_id: int,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], bool]:
    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        snapshot = dump_state(host, artifact_dir, "build-host", "post-disconnect-latest")
        assertions = takeover_assertions(snapshot, target_player_id)
        ready = takeover_ready(snapshot, target_player_id, scene)
        write_json(artifact_dir / "disconnect-ai-takeover-wait-latest.json", {
            "ready": ready,
            "assertions": assertions,
            "stableMatches": stable_matches,
        })
        if ready:
            stable_matches += 1
            if stable_matches >= 2:
                return snapshot, True
        else:
            stable_matches = 0
        time.sleep(2)
    return snapshot, False


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("dait")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
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
            scenario="disconnect_ai_takeover",
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
            scenario="disconnect_ai_takeover",
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

        host_pre, client_pre, ready = wait_states(host, client, artifact_dir, args.state_timeout, args.scene)
        write_json(artifact_dir / "snapshots" / "build-host-before-disconnect.json", host_pre)
        write_json(artifact_dir / "snapshots" / "build-client-before-disconnect.json", client_pre)
        if not ready:
            failures.append("state_ready_timeout")

        client_local = local_player(client_pre)
        if not isinstance(client_local, dict):
            failures.append("client_local_player_missing")
        else:
            target_player_id = int(client_local.get("playerId", -1))

        if target_player_id < 0:
            failures.append("target_player_id_invalid")
        else:
            write_json(artifact_dir / "disconnect-target.json", {
                "playerId": target_player_id,
                "connectionTokenHash": client_local.get("connectionTokenHash") if client_local else "unknown",
            })

        if client_proc is not None:
            client_proc.process.kill()
            client_proc.process.wait(timeout=10)
            client_proc.close_logs()

        if target_player_id >= 0:
            host_post, takeover_ok = wait_takeover(host, artifact_dir, target_player_id, args.takeover_timeout, args.scene)
            write_json(artifact_dir / "snapshots" / "build-host-post-takeover.json", host_post)
            assertions = takeover_assertions(host_post, target_player_id)
            write_json(artifact_dir / "disconnect-ai-takeover-assertions.json", assertions)
            if not takeover_ok:
                failures.append("disconnect_ai_takeover_timeout")

        write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        write_json(artifact_dir / "build-host-logs-recent.json", host.logs_recent())
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

    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--seed", type=int, default=4101)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--takeover-timeout", type=int, default=90)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
