#!/usr/bin/env python3
"""One-command Windows bootstrap for the MDF harness.

This script is normally launched by SETUP_MDF_HARNESS.bat from the repo root.
It assumes Unity 2021.3.45f1 and unity-cli are installed and Mdfproject is open.
"""

from __future__ import annotations

import argparse
import ctypes
import datetime as dt
import json
import os
import pathlib
import subprocess
import sys
from dataclasses import dataclass, asdict
from typing import Iterable


ROOT = pathlib.Path(__file__).resolve().parents[2]
ARTIFACT_ROOT = ROOT / "artifacts" / "bootstrap"
STABLE_PLAYER_DIR = ROOT / "artifacts" / "builds" / "mptest-current"
STABLE_PLAYER_EXE = STABLE_PLAYER_DIR / "MDF-MPTest.exe"


@dataclass
class StepResult:
    name: str
    command: list[str]
    status: str
    returnCode: int | None = None
    elapsedSeconds: float = 0.0
    nextAction: str = ""


def is_admin() -> bool:
    if os.name != "nt":
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def utc_stamp() -> str:
    return utc_now().strftime("%Y%m%d-%H%M%S")


def make_artifact_dir() -> pathlib.Path:
    stem = f"{utc_stamp()}-harness-bootstrap"
    path = ARTIFACT_ROOT / stem
    suffix = 2
    while path.exists():
        path = ARTIFACT_ROOT / f"{stem}-{suffix}"
        suffix += 1
    return path


def rel(path: pathlib.Path) -> str:
    try:
        return path.relative_to(ROOT).as_posix()
    except Exception:
        return str(path)


def command_text(command: Iterable[str]) -> str:
    return " ".join(str(part) for part in command)


def write_transcript_header(path: pathlib.Path, args: argparse.Namespace) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as fh:
        fh.write("MDF harness bootstrap\n")
        fh.write(f"timestampUtc={utc_now().isoformat()}\n")
        fh.write(f"repoRoot={ROOT}\n")
        fh.write(f"python={sys.executable}\n")
        fh.write(f"dryRun={args.dry_run}\n")
        fh.write("\n")


def run_step(
    name: str,
    command: list[str],
    transcript: pathlib.Path,
    *,
    dry_run: bool,
    timeout: int,
    next_action: str,
    required: bool = True,
) -> StepResult:
    started = utc_now()
    with transcript.open("a", encoding="utf-8") as fh:
        fh.write(f"\n## {name}\n")
        fh.write("$ " + command_text(command) + "\n")

    if dry_run:
        with transcript.open("a", encoding="utf-8") as fh:
            fh.write("[dry-run] command not executed\n")
        return StepResult(name=name, command=command, status="DRY_RUN", nextAction=next_action)

    try:
        proc = subprocess.run(
            command,
            cwd=ROOT,
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
        elapsed = (utc_now() - started).total_seconds()
    except subprocess.TimeoutExpired as exc:
        with transcript.open("a", encoding="utf-8") as fh:
            fh.write(f"[timeout] {timeout}s\n")
            if exc.stdout:
                fh.write(str(exc.stdout))
            if exc.stderr:
                fh.write("\n[stderr]\n" + str(exc.stderr))
        return StepResult(name=name, command=command, status="FAIL", elapsedSeconds=float(timeout), nextAction=next_action)

    with transcript.open("a", encoding="utf-8") as fh:
        if proc.stdout:
            fh.write(proc.stdout)
        if proc.stderr:
            fh.write("\n[stderr]\n")
            fh.write(proc.stderr)
        fh.write(f"\n[exit] {proc.returncode} elapsed={elapsed:.2f}s\n")

    status = "PASS" if proc.returncode == 0 else ("WARN" if not required else "FAIL")
    return StepResult(
        name=name,
        command=command,
        status=status,
        returnCode=proc.returncode,
        elapsedSeconds=elapsed,
        nextAction="" if status == "PASS" else next_action,
    )


def write_summary(path: pathlib.Path, results: list[StepResult], args: argparse.Namespace, artifact_dir: pathlib.Path) -> None:
    failed = [result for result in results if result.status == "FAIL"]
    player_dir = pathlib.Path(args.build_output_dir)
    if not player_dir.is_absolute():
        player_dir = (ROOT / player_dir).resolve()
    summary = {
        "success": not failed,
        "timestampUtc": utc_now().isoformat(),
        "repoRoot": str(ROOT),
        "artifactDir": str(artifact_dir),
        "stablePlayerPath": str(player_dir / "MDF-MPTest.exe"),
        "dryRun": args.dry_run,
        "firewallApplied": any(result.name == "configure firewall" and result.status == "PASS" for result in results),
        "nextAction": failed[0].nextAction if failed else "",
        "steps": [asdict(result) for result in results],
    }
    path.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")
    latest = ARTIFACT_ROOT / "bootstrap-harness-latest.json"
    latest.parent.mkdir(parents=True, exist_ok=True)
    latest.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")


def stop_on_failure(result: StepResult, results: list[StepResult], summary_path: pathlib.Path, args: argparse.Namespace, artifact_dir: pathlib.Path) -> int | None:
    if result.status != "FAIL":
        return None
    write_summary(summary_path, results, args, artifact_dir)
    print(f"FAIL {result.name}: {result.nextAction}")
    print(f"Summary: {summary_path}")
    return 1


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Print and record planned steps without executing them.")
    parser.add_argument("--skip-build", action="store_true", help="Skip Development player build.")
    parser.add_argument("--skip-launch-smoke", action="store_true", help="Build player without launch smoke.")
    parser.add_argument("--skip-editmode", action="store_true", help="Skip EditMode tests.")
    parser.add_argument("--no-firewall", action="store_true", help="Do not try to configure the stable player firewall rule.")
    parser.add_argument("--build-output-dir", default=str(STABLE_PLAYER_DIR), help="Stable Development player output directory.")
    parser.add_argument("--cleanup-timeout-seconds", default="20", help="Player cleanup timeout for launch smoke.")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    artifact_dir = make_artifact_dir()
    transcript = artifact_dir / "bootstrap-transcript.log"
    summary_path = artifact_dir / "bootstrap-summary.json"
    write_transcript_header(transcript, args)

    results: list[StepResult] = []

    def step(name: str, command: list[str], timeout: int, next_action: str, required: bool = True) -> int | None:
        result = run_step(
            name,
            command,
            transcript,
            dry_run=args.dry_run,
            timeout=timeout,
            next_action=next_action,
            required=required,
        )
        results.append(result)
        print(f"{result.status:7} {name}")
        return stop_on_failure(result, results, summary_path, args, artifact_dir) if required else None

    if not (ROOT / "Mdfproject/ProjectSettings/ProjectVersion.txt").exists():
        print("FAIL repo layout: run SETUP_MDF_HARNESS.bat from the MDF repo root.")
        return 1

    py = sys.executable
    steps = [
        ("install git hooks", [py, "tools/harness/install_git_hooks.py"], 60, "Fix git installation or run from inside a normal clone."),
        ("bootstrap environment audit", [py, "tools/harness/bootstrap_dev_env.py"], 180, "Install missing prerequisites, open Mdfproject in Unity, then rerun."),
        ("validate overlay", [py, "tools/harness/validate_overlay.py"], 60, "Restore missing harness docs/scripts listed by validate_overlay.py."),
        ("precommit self-test", [py, "tools/harness/precommit.py", "--self-test"], 180, "Fix failing precommit self-test before feature work."),
        ("precommit all", [py, "tools/harness/precommit.py", "--all"], 300, "Fix precommit errors/warnings before using the harness."),
        ("unity status", ["unity-cli", "--project", "Mdfproject", "status"], 60, "Open Mdfproject in Unity and wait until unity-cli reports ready."),
        ("unity compile", ["unity-cli", "--project", "Mdfproject", "editor", "refresh", "--compile"], 300, "Fix compile/import errors in Unity."),
        ("unity console errors", ["unity-cli", "--project", "Mdfproject", "console", "--type", "error", "--stacktrace", "user"], 120, "Fix user-code Unity console errors."),
    ]
    if not args.skip_editmode:
        steps.append(("unity EditMode tests", ["unity-cli", "--project", "Mdfproject", "test", "--mode", "EditMode"], 300, "Fix failing EditMode tests."))

    for name, command, timeout, next_action in steps:
        maybe_code = step(name, command, timeout, next_action)
        if maybe_code is not None:
            return maybe_code

    if not args.skip_build:
        build_command = [
            py,
            "tools/harness/mp/build_player.py",
            "--output-dir",
            args.build_output_dir,
            "--headless-player",
            "--orphan-threshold",
            "0",
            "--cleanup-timeout-seconds",
            args.cleanup_timeout_seconds,
        ]
        if not args.skip_launch_smoke:
            build_command.extend(["--launch-smoke", "--exit-after-seconds", "5"])
        maybe_code = step(
            "build Development player",
            build_command,
            900,
            "Fix Unity build errors or pass --skip-build for source-only setup.",
        )
        if maybe_code is not None:
            return maybe_code

    if not args.no_firewall:
        player_path = pathlib.Path(args.build_output_dir)
        if not player_path.is_absolute():
            player_path = (ROOT / player_path).resolve()
        player_exe = player_path / "MDF-MPTest.exe"
        if is_admin() or args.dry_run:
            step(
                "configure firewall",
                [py, "tools/harness/mp/configure_windows_firewall.py", "--player-path", str(player_exe), "--apply"],
                60,
                "Run SETUP_MDF_HARNESS.bat from an Administrator shell, or pass --no-firewall.",
                required=False,
            )
        else:
            firewall_command = [py, "tools/harness/mp/configure_windows_firewall.py", "--player-path", str(player_exe), "--apply"]
            result = StepResult(
                name="configure firewall",
                command=firewall_command,
                status="WARN",
                nextAction="Run SETUP_MDF_HARNESS.bat as Administrator to create the block-inbound rule, or run the printed command manually.",
            )
            results.append(result)
            print("WARN    configure firewall: Administrator shell required; skipped.")
            print("$ " + command_text(firewall_command))

    write_summary(summary_path, results, args, artifact_dir)
    print("BOOTSTRAP PASS")
    print(f"Summary: {summary_path}")
    print(f"Stable player: {pathlib.Path(args.build_output_dir) / 'MDF-MPTest.exe'}")
    print("Codex users still need to trust the repo-local .codex config in their Codex environment.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
