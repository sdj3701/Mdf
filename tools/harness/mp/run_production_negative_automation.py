#!/usr/bin/env python3
"""Prove normal non-development builds do not expose the MP automation server."""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import time
import urllib.error
import urllib.request
from typing import Any

from common import (
    ROOT,
    free_port,
    hash_for_log,
    make_artifact_dir,
    new_session,
    new_token,
    parse_json_object,
    run_command,
    utc_stamp,
    write_json,
)
from launch_player import PlayerProcess, launch_mdf_process, mdf_player_pids, write_case_cleanup_report


def find_value(obj: Any, key: str) -> Any:
    if isinstance(obj, dict):
        if key in obj:
            return obj[key]
        for value in obj.values():
            found = find_value(value, key)
            if found is not None:
                return found
    elif isinstance(obj, list):
        for value in obj:
            found = find_value(value, key)
            if found is not None:
                return found
    return None


def player_log_path() -> pathlib.Path:
    if os.name == "nt":
        local_app = pathlib.Path(os.environ.get("LOCALAPPDATA", ""))
        return (local_app.parent / "LocalLow" / "DefaultCompany" / "Mdfproject" / "Player.log").resolve()
    return pathlib.Path.home() / ".config" / "unity3d" / "DefaultCompany" / "Mdfproject" / "Player.log"


def build_non_development_player(artifact_dir: pathlib.Path, args: argparse.Namespace) -> tuple[pathlib.Path, dict[str, Any]]:
    output_dir = pathlib.Path(args.output_dir) if args.output_dir else ROOT / "artifacts" / "builds" / f"{utc_stamp()}-production-negative"
    if not output_dir.is_absolute():
        output_dir = (ROOT / output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        "unity-cli",
        "--project",
        args.project,
        "mp_build_player",
        "--output_dir",
        str(output_dir),
        "--player_name",
        args.player_name,
        "--development_build",
        "false",
        "--allow_debugging",
        "false",
    ]
    if args.build_target:
        cmd.extend(["--build_target", args.build_target])

    proc = run_command(cmd, artifact_dir, timeout=args.build_timeout, label="build_non_development_player")
    data = parse_json_object(proc.stdout)
    output_path = find_value(data, "outputPath")
    metadata_path = find_value(data, "metadataPath") or str(output_dir / "build-metadata.json")

    if proc.returncode != 0:
        raise RuntimeError(f"mp_build_player non-development build failed with exit code {proc.returncode}")
    if not output_path:
        raise RuntimeError("mp_build_player did not return outputPath")

    player_path = pathlib.Path(output_path)
    if not player_path.is_absolute():
        player_path = (ROOT / player_path).resolve()
    if not player_path.exists():
        raise RuntimeError(f"Build output does not exist: {player_path}")

    metadata_file = pathlib.Path(metadata_path)
    if not metadata_file.is_absolute():
        metadata_file = (ROOT / metadata_file).resolve()
    metadata = json.loads(metadata_file.read_text(encoding="utf-8")) if metadata_file.exists() else {}
    write_json(
        artifact_dir / "build-wrapper.json",
        {
            "outputPath": str(player_path),
            "metadataPath": str(metadata_file),
            "metadata": metadata,
            "unityCliResponse": data,
        },
    )
    return player_path, metadata


def probe_ping(port: int, token: str, timeout_seconds: float) -> dict[str, Any]:
    deadline = time.time() + timeout_seconds
    attempts: list[dict[str, Any]] = []
    url = f"http://127.0.0.1:{port}/ping"
    headers = {
        "Authorization": f"Bearer {token}",
        "X-MPTest-Token": token,
    }

    while time.time() < deadline:
        request = urllib.request.Request(url, method="GET", headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=0.75) as response:
                body = response.read().decode("utf-8", errors="replace")
                return {
                    "responded": True,
                    "status": response.status,
                    "body": body,
                    "attempts": attempts,
                }
        except Exception as exc:
            attempts.append({"type": type(exc).__name__, "message": str(exc)})
            time.sleep(0.25)

    return {
        "responded": False,
        "attempts": attempts[-10:],
    }


def launch_and_probe(player_path: pathlib.Path, artifact_dir: pathlib.Path, args: argparse.Namespace) -> dict[str, Any]:
    token = new_token()
    port = args.automation_port or free_port()
    session = args.session or new_session("prodneg")
    stdout_path = artifact_dir / "production-negative.stdout.log"
    stderr_path = artifact_dir / "production-negative.stderr.log"
    prod_player_log_path = artifact_dir / "production-negative.Player.log"
    command_path = artifact_dir / "launch-command.json"
    case_name = "production_negative_automation"
    artifact_runtime_dir = artifact_dir / "runtime-artifacts"
    artifact_runtime_dir.mkdir(parents=True, exist_ok=True)

    cmd = [
        str(player_path),
        "-logFile",
        str(prod_player_log_path),
        "--mpTest",
        "--mpRole",
        "host",
        "--mpSession",
        session,
        "--mpScene",
        args.scene,
        "--mpExitAfterSeconds",
        str(args.exit_after_seconds),
        "--mpAutomationPort",
        str(port),
        "--mpAutomationToken",
        token,
        "--mpCase",
        case_name,
        "--mpArtifactDir",
        str(artifact_runtime_dir),
        "--mpSeed",
        str(args.seed),
    ]
    redacted_cmd = ["<redacted-token>" if item == token else item for item in cmd]
    write_json(
        command_path,
        {
            "command": redacted_cmd,
            "session": session,
            "automationPort": port,
            "automationTokenHash": hash_for_log(token),
            "playerLog": str(prod_player_log_path),
        },
    )

    baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    with stdout_path.open("w", encoding="utf-8") as stdout, stderr_path.open("w", encoding="utf-8") as stderr:
        process, job = launch_mdf_process(
            cmd,
            ROOT,
            stdout,
            stderr,
            job_name=f"MDF-MPTest-production-negative-{os.getpid()}-{int(time.time() * 1000)}",
        )
        proc = PlayerProcess(
            process=process,
            stdout_path=stdout_path,
            stderr_path=stderr_path,
            player_log_path=prod_player_log_path,
            command_path=command_path,
            artifact_dir=artifact_runtime_dir,
            peer_name="production-negative",
            port=port,
            token=token,
            redaction_secrets=[token],
            stdout_handle=stdout,
            stderr_handle=stderr,
            job=job,
        )
        ping = probe_ping(port, token, args.ping_timeout)
        deadline = time.time() + args.exit_after_seconds + args.launch_timeout_padding
        while time.time() < deadline and proc.process.poll() is None:
            time.sleep(0.25)
        timed_out = proc.process.poll() is None
        cleanup_report = write_case_cleanup_report(
            artifact_runtime_dir,
            [proc],
            baseline_pids=baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            strict_cleanup=True,
        )

    copied_player_log = ""
    source_log = player_log_path()
    if source_log.exists():
        copied_player_log = str(artifact_dir / "Player.log")
        (artifact_dir / "Player.log").write_bytes(source_log.read_bytes())

    return {
        "playerPath": str(player_path),
        "exitCode": proc.process.returncode,
        "timedOut": timed_out,
        "stdout": str(stdout_path),
        "stderr": str(stderr_path),
        "playerLog": str(prod_player_log_path),
        "playerLogSource": str(source_log),
        "playerLogCopied": copied_player_log,
        "session": session,
        "automationPort": port,
        "automationTokenHash": hash_for_log(token),
        "ping": ping,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
        "cleanupReportPath": str(artifact_runtime_dir / "cleanup-report.json"),
        "orphanedPids": cleanup_report.get("orphanedPids") or [],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", default="Mdfproject")
    parser.add_argument("--output-dir")
    parser.add_argument("--player-path")
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--player-name", default="MDF-MPTest-ProductionNegative")
    parser.add_argument("--build-target")
    parser.add_argument("--build-timeout", type=int, default=900)
    parser.add_argument("--scene", default="00_Title")
    parser.add_argument("--session")
    parser.add_argument("--automation-port", type=int, default=0)
    parser.add_argument("--exit-after-seconds", type=int, default=5)
    parser.add_argument("--launch-timeout-padding", type=int, default=20)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--ping-timeout", type=float, default=8.0)
    parser.add_argument("--seed", type=int, default=0)
    args = parser.parse_args()

    artifact_dir = make_artifact_dir("production-negative-automation")

    if args.skip_build:
        if not args.player_path:
            raise SystemExit("--skip-build requires --player-path")
        player_path = pathlib.Path(args.player_path)
        if not player_path.is_absolute():
            player_path = (ROOT / player_path).resolve()
        metadata: dict[str, Any] = {"skippedBuild": True, "developmentBuild": None}
    else:
        player_path, metadata = build_non_development_player(artifact_dir, args)

    launch = launch_and_probe(player_path, artifact_dir, args)
    development_build = metadata.get("developmentBuild")
    cleanup_success = launch.get("cleanupSuccess") is True
    success = development_build is False and launch["ping"].get("responded") is False and not launch["timedOut"] and cleanup_success
    report = {
        "success": success,
        "productionAutomationDisabled": success,
        "developmentBuild": development_build,
        "metadata": metadata,
        "launch": launch,
        "failures": [],
    }
    if development_build is not False:
        report["failures"].append("build_metadata_not_non_development")
    if launch["ping"].get("responded"):
        report["failures"].append("automation_ping_responded_in_non_development_build")
    if launch["timedOut"]:
        report["failures"].append("player_did_not_exit_after_timeout")
    if not cleanup_success:
        report["failures"].append(f"cleanup_failed:{launch.get('cleanupStatus')}")

    report_path = artifact_dir / "production-negative-automation.json"
    write_json(report_path, report)
    print(json.dumps({"artifactDir": str(artifact_dir), "report": report}, indent=2))
    return 0 if success else 1


if __name__ == "__main__":
    raise SystemExit(main())
