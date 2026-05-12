#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import failure_summary, free_port, latest_player_path, make_artifact_dir, new_session, new_token, normalize_snapshot_response, scene_matches, write_json, write_standard_result
from launch_player import PlayerProcess, launch_player, mdf_player_pids, write_case_cleanup_report, write_orphan_pressure_report


CASE_NAME = "ai-fill-smoke"


def ai_fill_ready(snapshot: dict, expected_players: int, expected_ai: int) -> bool:
    state = normalize_snapshot_response(snapshot)
    players = state.get("players") or []
    ai_count = sum(1 for player in players if player.get("isAI") is True)
    return (
        scene_matches(state.get("scene"), "Game")
        and len(players) == expected_players
        and ai_count == expected_ai
        and all((player.get("field") or {}).get("ready") for player in players)
    )


def run(args: argparse.Namespace) -> int:
    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
        "headlessPlayer": args.headless_player,
    }
    failures: list[str] = []

    if args.dry_run:
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "playerPath": str(player_path) if player_path else None,
            "dryRun": True,
            "headlessPlayer": args.headless_player,
        })
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    if player_path is None or not player_path.exists():
        failures.append("player_path_missing")
        write_json(artifact_dir / "run.json", {
            "case": CASE_NAME,
            "playerPath": str(player_path) if player_path else None,
            "dryRun": False,
            "headlessPlayer": args.headless_player,
        })
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {"cleanupStatus": "NEEDS_ENVIRONMENT", "cleanupSuccess": False, "orphanedPids": []},
            headless_player=args.headless_player,
        )
        print(json.dumps(result, indent=2))
        return 2

    session = args.session or new_session("aifill")
    token = new_token()
    connection_token = new_token()
    port = free_port()
    proc: PlayerProcess | None = None

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "expectedPlayers": 4,
        "expectedAiPlayers": 3,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
    })
    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
    )
    if orphan_gate.get("blocked"):
        failures.append("orphan_pressure_gate_blocked")
        result = write_standard_result(
            artifact_dir,
            CASE_NAME,
            failures,
            {
                "cleanupStatus": "NEEDS_ENVIRONMENT",
                "cleanupSuccess": False,
                "orphanedPids": [proc.get("pid") for proc in orphan_gate.get("processes") or [] if isinstance(proc, dict)],
            },
            headless_player=args.headless_player,
            extra={"orphanPressure": orphan_gate},
        )
        print(json.dumps(result, indent=2))
        return 2

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
            headless_player=args.headless_player,
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

        if args.headless_player:
            write_json(artifact_dir / "build-host-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
        else:
            write_json(artifact_dir / "build-host-screenshot.json", client.screenshot())
        write_json(artifact_dir / "build-host-logs-recent.json", client.logs_recent())
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [proc] if proc is not None else [],
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(failures),
            strict_cleanup=args.strict_cleanup,
        )
        copied = collect_player_log(artifact_dir, "build-host")
        logs = [artifact_dir / "build-host.stdout.log", artifact_dir / "build-host.stderr.log"]
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    result = write_standard_result(artifact_dir, CASE_NAME, failures, cleanup_report, headless_player=args.headless_player)
    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if result["success"] else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--seed", type=int, default=4001)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--cleanup-report", action="store_true", help="Compatibility flag; cleanup-report.json is always written.")
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-check", action="store_true", help="Compatibility flag; orphan pressure check is always performed.")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
