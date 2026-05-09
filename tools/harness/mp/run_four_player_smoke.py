#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
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
from launch_player import PlayerProcess, launch_player


CASE_NAME = "four-player-smoke"


def wait_session_states(clients: dict[str, AutomationClient], artifact_dir: pathlib.Path, timeout: int, scene: str) -> bool:
    deadline = time.time() + timeout
    stable_matches = 0
    while time.time() < deadline:
        ready_by_peer: dict[str, bool] = {}
        reasons_by_peer: dict[str, list[str]] = {}
        for name, client in clients.items():
            snap = client.dump_state()
            write_json(artifact_dir / "snapshots" / f"{name}-lobby-latest.json", snap)
            ready_by_peer[name] = session_ready(snap, 4, scene)
            reasons_by_peer[name] = session_not_ready_reasons(snap, 4, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "ready": ready_by_peer,
            "reasons": reasons_by_peer,
            "stableMatches": stable_matches,
        })
        if all(ready_by_peer.values()):
            stable_matches += 1
            if stable_matches >= 2:
                return True
        else:
            stable_matches = 0
        time.sleep(1)
    return False


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("four")
    peers = [
        {"name": "host", "role": "host", "seed": args.seed},
        {"name": "client-1", "role": "client", "seed": args.seed + 1},
        {"name": "client-2", "role": "client", "seed": args.seed + 2},
        {"name": "client-3", "role": "client", "seed": args.seed + 3},
    ]
    for peer in peers:
        peer["token"] = new_token()
        peer["connection"] = new_token()
        peer["port"] = free_port()

    procs: list[tuple[dict, PlayerProcess]] = []
    failures: list[str] = []
    write_json(artifact_dir / "run.json", {"case": CASE_NAME, "session": session, "scene": "Game", "lobbyScene": args.lobby_scene, "peers": [{k: v for k, v in p.items() if k not in {"token", "connection"}} for p in peers], "dryRun": args.dry_run})
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    try:
        for index, peer in enumerate(peers):
            proc = launch_player(
                player_path,
                peer["role"],
                session,
                int(peer["port"]),
                str(peer["token"]),
                str(peer["connection"]),
                artifact_dir,
                str(peer["name"]),
                max_players=4,
                scene=args.lobby_scene,
                case_name=CASE_NAME,
                auto_start=False,
                load_game=False,
                seed=int(peer["seed"]),
                scenario="four_player_smoke",
            )
            procs.append((peer, proc))
            if index == 0:
                time.sleep(args.host_lead_seconds)

        clients = {peer["name"]: AutomationClient(int(peer["port"]), str(peer["token"])) for peer in peers}
        for peer in peers:
            ping = clients[peer["name"]].wait_ping(timeout_seconds=args.ping_timeout)
            write_json(artifact_dir / f"{peer['name']}-ping.json", ping)
            if not ping.get("success"):
                failures.append(f"{peer['name']}_ping_timeout")

        host_client = clients["host"]
        if not wait_build_peer_started(host_client.start_host, artifact_dir, "host", session, args.lobby_scene, 4, args.start_timeout):
            failures.append("host_start_timeout")
        for peer in peers:
            name = str(peer["name"])
            if name == "host":
                continue
            if not wait_build_peer_started(clients[name].join, artifact_dir, name, session, args.lobby_scene, 4, args.start_timeout):
                failures.append(f"{name}_join_timeout")

        if not wait_session_states(clients, artifact_dir, args.lobby_timeout, args.lobby_scene):
            failures.append("session_join_timeout")

        load_result = host_client.load_game("Game")
        write_json(artifact_dir / "host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        deadline = time.time() + args.state_timeout
        latest: dict[str, dict] = {}
        stable_matches = 0
        while time.time() < deadline:
            latest = {}
            ready_by_peer: dict[str, bool] = {}
            reasons_by_peer: dict[str, list[str]] = {}
            for peer in peers:
                name = str(peer["name"])
                snap = clients[name].dump_state()
                latest[name] = snap
                write_json(artifact_dir / "snapshots" / f"{name}-latest.json", snap)
                ready_by_peer[name] = snapshot_ready(snap, 4, "Game")
                reasons_by_peer[name] = snapshot_not_ready_reasons(snap, 4, "Game")

            comparisons: dict[str, dict] = {}
            all_ready = all(ready_by_peer.values())
            if all_ready and latest.get("host") is not None:
                for peer in peers:
                    name = str(peer["name"])
                    if name == "host":
                        continue
                    comparisons[name] = compare_snapshots(latest["host"], latest[name])
                    write_json(artifact_dir / f"comparison-host-vs-{name}-latest.json", comparisons[name])

            all_match = all_ready and comparisons and all(result.get("success") for result in comparisons.values())
            write_json(artifact_dir / "state-wait-latest.json", {
                "ready": ready_by_peer,
                "reasons": reasons_by_peer,
                "comparisons": comparisons,
                "stableMatches": stable_matches,
            })
            if all_match:
                stable_matches += 1
                if stable_matches >= 2:
                    break
            else:
                stable_matches = 0
            time.sleep(2)
        else:
            failures.append("four_player_state_timeout")

        baseline = latest.get("host")
        for peer in peers:
            name = str(peer["name"])
            snap = latest.get(name) or clients[name].dump_state()
            write_json(artifact_dir / "snapshots" / f"{name}-final.json", snap)
            if name != "host" and baseline is not None:
                result = compare_snapshots(baseline, snap)
                write_json(artifact_dir / f"comparison-host-vs-{name}.json", result)
                if not result["success"]:
                    failures.append(f"snapshot_mismatch_{name}")
            write_json(artifact_dir / f"{name}-screenshot.json", clients[name].screenshot())
            write_json(artifact_dir / f"{name}-logs-recent.json", clients[name].logs_recent())

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        for peer, proc in reversed(procs):
            name = str(peer["name"])
            try:
                AutomationClient(int(peer["port"]), str(peer["token"]), timeout=2.0).quit()
                proc.process.wait(timeout=10)
                proc.close_logs()
            except Exception:
                proc.terminate()
        copied = collect_player_log(artifact_dir, "last-player")
        logs = []
        for peer in peers:
            logs.append(artifact_dir / f"{peer['name']}.stdout.log")
            logs.append(artifact_dir / f"{peer['name']}.stderr.log")
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--seed", type=int, default=5001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--host-lead-seconds", type=float, default=3.0)
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=75)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
