#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys

from common import ROOT, make_artifact_dir, write_json


CASES = {
    "editor-host-build-client": "run_editor_host_build_client.py",
    "build-host-editor-client": "run_build_host_editor_client.py",
    "build-host-build-client": "run_build_host_build_client.py",
    "ai-fill-smoke": "run_ai_fill_smoke.py",
    "disconnect-ai-takeover": "run_disconnect_ai_takeover.py",
    "same-token-reconnect": "run_same_token_reconnect.py",
    "four-player-smoke": "run_four_player_smoke.py",
}


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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--case", choices=list(CASES.keys()) + ["all"], default="all")
    parser.add_argument("--player-path")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    matrix_dir = make_artifact_dir("matrix")
    selected = list(CASES.keys()) if args.case == "all" else [args.case]
    results = [run_case(case, matrix_dir, args) for case in selected]
    summary = {
        "matrixDir": str(matrix_dir),
        "dryRun": args.dry_run,
        "results": results,
        "success": all(item["exitCode"] == 0 for item in results),
    }
    write_json(matrix_dir / "matrix-summary.json", summary)
    print(json.dumps(summary, indent=2))
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
