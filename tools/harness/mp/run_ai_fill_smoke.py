#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import failure_summary, free_port, latest_player_path, make_artifact_dir, new_session, new_token, normalize_snapshot_response, write_json
from launch_player import PlayerProcess, launch_player


CASE_NAME = "ai-fill-smoke"


def ai_fill_ready(snapshot: dict, expected_players: int, expected_ai: int) -> bool:
    state = normalize_snapshot_response(snapshot)
    players = state.get("players") or []
    ai_count = sum(1 for player in players if player.get("isAI") is True)
    return (
        state.get("scene") == "Game"
        and len(players) == expected_players
        and ai_count == expected_ai
        and all((player.get("field") or {}).get("ready") for player in players)
    )


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built player found. Run Phase 9 build first or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("aifill")
    token = new_token()
    connection_token = new_token()
    port = free_port()
    proc: PlayerProcess | None = None
    failures: list[str] = []

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "expectedPlayers": 4,
        "expectedAiPlayers": 3,
        "dryRun": args.dry_run,
    })
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    try:
        proc = launch_player(
            player_path,
            "host",
            session,
            port,
            token,
            connection_token,
            artifact_dir,
            "build-host",
            max_players=4,
            scene="Game",
            case_name=CASE_NAME,
            auto_start=True,
            load_game=True,
            seed=args.seed,
            scenario="ai_fill_smoke",
        )
        client = AutomationClient(port, token)
        ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", ping)
        if not ping.get("success"):
            failures.append("automation_ping_timeout")

        deadline = time.time() + args.state_timeout
        snapshot = {}
        while time.time() < deadline:
            snapshot = client.dump_state()
            write_json(artifact_dir / "snapshots" / "build-host-latest.json", snapshot)
            if ai_fill_ready(snapshot, 4, 3):
                break
            time.sleep(2)
        else:
            failures.append("ai_fill_state_timeout")

        write_json(artifact_dir / "snapshots" / "build-host-final.json", snapshot)
        state = normalize_snapshot_response(snapshot)
        players = state.get("players") or []
        ai_count = sum(1 for player in players if player.get("isAI") is True)
        assertions = {
            "playerCount": len(players),
            "aiCount": ai_count,
            "uniquePlayerIds": sorted({player.get("playerId") for player in players}),
            "allFieldsReady": all((player.get("field") or {}).get("ready") for player in players),
        }
        write_json(artifact_dir / "ai-fill-assertions.json", assertions)
        if len(players) != 4 or ai_count != 3 or not assertions["allFieldsReady"]:
            failures.append("ai_fill_assertion_failed")

        write_json(artifact_dir / "build-host-screenshot.json", client.screenshot())
        write_json(artifact_dir / "build-host-logs-recent.json", client.logs_recent())
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        if proc is not None:
            try:
                automation = AutomationClient(port, token, timeout=2.0)
                write_json(artifact_dir / "build-host-quit.json", automation.quit())
                proc.process.wait(timeout=10)
                proc.close_logs()
            except Exception:
                proc.terminate()
        copied = collect_player_log(artifact_dir, "build-host")
        logs = [artifact_dir / "build-host.stdout.log", artifact_dir / "build-host.stderr.log"]
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
    parser.add_argument("--seed", type=int, default=4001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--state-timeout", type=int, default=90)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
