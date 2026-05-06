#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time

from common import ROOT, make_artifact_dir, unity_cli_json, wait_unity_ready, write_json


CASES = {
    "editor-host-build-client": "run_editor_host_build_client.py",
    "build-host-editor-client": "run_build_host_editor_client.py",
    "build-host-build-client": "run_build_host_build_client.py",
    "human-bot-prepare": "run_human_bot_prepare_progression.py",
    "battle-spawn-monster-command": "run_battle_spawn_monster_command.py",
    "magic-scroll-command": "run_magic_scroll_command.py",
    "human-bot-battle-progression": "run_human_bot_battle_progression.py",
    "progressed-host-migration-after-battle": "run_progressed_host_migration_after_battle.py",
    "progressed-reconnect-after-battle": "run_progressed_reconnect_after_battle.py",
    "progressed-disconnect-after-battle": "run_progressed_disconnect_after_battle.py",
    "battle-seed-sweep": "run_battle_seed_sweep.py",
    "human-bot-4p-progression": "run_human_bot_4p_progression.py",
    "ai-fill-smoke": "run_ai_fill_smoke.py",
    "disconnect-ai-takeover": "run_disconnect_ai_takeover.py",
    "same-token-reconnect": "run_same_token_reconnect.py",
    "four-player-smoke": "run_four_player_smoke.py",
}

DEFAULT_CASES = [
    "editor-host-build-client",
    "build-host-editor-client",
    "build-host-build-client",
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
]


def run_case(case: str, matrix_dir: pathlib.Path, args: argparse.Namespace) -> dict:
    script = pathlib.Path(__file__).with_name(CASES[case])
    command = [sys.executable, str(script), "--artifact-root", str(matrix_dir)]
    if args.player_path:
        command.extend(["--player-path", args.player_path])
    if args.dry_run:
        command.append("--dry-run")
    proc = subprocess.run(command, cwd=ROOT, text=True, capture_output=True, encoding="utf-8", errors="replace")
    result = {
        "case": case,
        "command": command,
        "exitCode": proc.returncode,
        "stdout": proc.stdout,
        "stderr": proc.stderr,
    }
    return result


def cleanup_editor_between_cases(matrix_dir: pathlib.Path, label: str, args: argparse.Namespace) -> dict:
    cleanup_dir = matrix_dir / f"_cleanup-{label}"
    cleanup_dir.mkdir(parents=True, exist_ok=True)
    result: dict = {"label": label, "steps": []}
    for command in (["mp_stop"], ["editor", "stop"]):
        step = {"command": command}
        try:
            step["response"] = unity_cli_json(command, cleanup_dir, timeout=90)
            step["ok"] = True
        except Exception as exc:
            step["ok"] = False
            step["error"] = str(exc)
        result["steps"].append(step)

    result["ready"] = wait_unity_ready(cleanup_dir, timeout_seconds=60)
    if args.inter_case_delay_seconds > 0:
        time.sleep(args.inter_case_delay_seconds)
    write_json(cleanup_dir / "cleanup.json", result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--case", choices=list(CASES.keys()) + ["all"], default="all")
    parser.add_argument("--player-path")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--inter-case-delay-seconds", type=float, default=2.0)
    args = parser.parse_args()

    matrix_dir = make_artifact_dir("matrix")
    selected = DEFAULT_CASES if args.case == "all" else [args.case]
    cleanups: list[dict] = []
    results: list[dict] = []
    for index, case in enumerate(selected):
        cleanups.append(cleanup_editor_between_cases(matrix_dir, f"before-{index + 1}-{case}", args))
        results.append(run_case(case, matrix_dir, args))
        cleanups.append(cleanup_editor_between_cases(matrix_dir, f"after-{index + 1}-{case}", args))

    summary = {
        "matrixDir": str(matrix_dir),
        "dryRun": args.dry_run,
        "cleanups": cleanups,
        "results": results,
        "success": all(item["exitCode"] == 0 for item in results),
    }
    write_json(matrix_dir / "matrix-summary.json", summary)
    print(json.dumps(summary, indent=2))
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
