#!/usr/bin/env python3
"""Build or smoke-launch the MDF Development player."""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import pathlib
import secrets
import socket
import subprocess
import sys
import time
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[3]
DEFAULT_PROJECT = "Mdfproject"


def utc_stamp() -> str:
    return dt.datetime.utcnow().strftime("%Y%m%d-%H%M%S")


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def run_command(args: list[str], cwd: pathlib.Path, transcript: pathlib.Path | None = None) -> subprocess.CompletedProcess[str]:
    if transcript is not None:
        transcript.parent.mkdir(parents=True, exist_ok=True)
        with transcript.open("a", encoding="utf-8") as fh:
            fh.write("$ " + " ".join(args) + "\n")

    proc = subprocess.run(args, cwd=cwd, text=True, capture_output=True, encoding="utf-8", errors="replace")

    if transcript is not None:
        with transcript.open("a", encoding="utf-8") as fh:
            fh.write(proc.stdout)
            if proc.stderr:
                fh.write("\n[stderr]\n")
                fh.write(proc.stderr)
            fh.write(f"\n[exit] {proc.returncode}\n")

    return proc


def parse_json_object(text: str) -> Any:
    text = text.strip()
    if not text:
        return {}

    start = text.find("{")
    end = text.rfind("}")
    if start < 0 or end < start:
        return {}

    return json.loads(text[start : end + 1])


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


def hash_for_log(value: str) -> str:
    h = 2166136261
    for ch in value:
        h ^= ord(ch)
        h = (h * 16777619) & 0xFFFFFFFF
    return f"{h:08X}"


def build_player(args: argparse.Namespace, output_dir: pathlib.Path) -> tuple[pathlib.Path, dict[str, Any]]:
    transcript = output_dir / "build-player-transcript.log"
    cmd = [
        "unity-cli",
        "--project",
        args.project,
        "mp_build_player",
        "--output_dir",
        str(output_dir),
        "--player_name",
        args.player_name,
    ]
    if args.build_target:
        cmd.extend(["--build_target", args.build_target])
    if not args.development_build:
        cmd.extend(["--development_build", "false"])
    if not args.allow_debugging:
        cmd.extend(["--allow_debugging", "false"])

    proc = run_command(cmd, ROOT, transcript)
    data = parse_json_object(proc.stdout)
    output_path = find_value(data, "outputPath")
    metadata_path = find_value(data, "metadataPath") or str(output_dir / "build-metadata.json")

    if proc.returncode != 0:
        raise RuntimeError(f"mp_build_player failed with exit code {proc.returncode}; see {transcript}")
    if not output_path:
        raise RuntimeError(f"mp_build_player did not return outputPath; see {transcript}")

    player_path = pathlib.Path(output_path)
    if not player_path.is_absolute():
        player_path = (ROOT / player_path).resolve()
    if not player_path.exists():
        raise RuntimeError(f"Build output does not exist: {player_path}")

    metadata = {
        "outputPath": str(player_path),
        "metadataPath": str(metadata_path),
        "unityCliResponse": data,
        "transcript": str(transcript),
    }
    (output_dir / "build-player-wrapper.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    return player_path, metadata


def launch_smoke(player_path: pathlib.Path, output_dir: pathlib.Path, args: argparse.Namespace) -> dict[str, Any]:
    token = secrets.token_urlsafe(24)
    port = args.automation_port or free_port()
    session = args.session or f"mp-build-smoke-{utc_stamp()}"
    artifact_dir = output_dir / "launch-smoke-artifacts"
    artifact_dir.mkdir(parents=True, exist_ok=True)
    stdout_path = output_dir / "launch-smoke.stdout.log"
    stderr_path = output_dir / "launch-smoke.stderr.log"

    cmd = [
        str(player_path),
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
        "build_launch_smoke",
        "--mpArtifactDir",
        str(artifact_dir),
        "--mpSeed",
        str(args.seed),
    ]

    redacted_cmd = ["<redacted-token>" if item == token else item for item in cmd]
    with (output_dir / "launch-smoke-command.json").open("w", encoding="utf-8") as fh:
        json.dump(
            {
                "command": redacted_cmd,
                "session": session,
                "automationPort": port,
                "automationTokenHash": hash_for_log(token),
            },
            fh,
            indent=2,
        )

    with stdout_path.open("w", encoding="utf-8") as stdout, stderr_path.open("w", encoding="utf-8") as stderr:
        proc = subprocess.Popen(cmd, cwd=ROOT, stdout=stdout, stderr=stderr)
        deadline = time.time() + args.exit_after_seconds + args.launch_timeout_padding
        while time.time() < deadline and proc.poll() is None:
            time.sleep(0.25)
        timed_out = proc.poll() is None
        if timed_out:
            proc.kill()
            proc.wait(timeout=10)

    player_log_source = player_log_path()
    copied_player_log = ""
    if player_log_source.exists():
        copied_player_log = str(output_dir / "Player.log")
        (output_dir / "Player.log").write_bytes(player_log_source.read_bytes())

    result = {
        "playerPath": str(player_path),
        "exitCode": proc.returncode,
        "timedOut": timed_out,
        "stdout": str(stdout_path),
        "stderr": str(stderr_path),
        "playerLogSource": str(player_log_source),
        "playerLogCopied": copied_player_log,
        "artifactDir": str(artifact_dir),
        "session": session,
        "automationPort": port,
        "automationTokenHash": hash_for_log(token),
    }
    (output_dir / "launch-smoke.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


def player_log_path() -> pathlib.Path:
    if os.name == "nt":
        local_app = pathlib.Path(os.environ.get("LOCALAPPDATA", ""))
        return (local_app.parent / "LocalLow" / "DefaultCompany" / "Mdfproject" / "Player.log").resolve()
    return pathlib.Path.home() / ".config" / "unity3d" / "DefaultCompany" / "Mdfproject" / "Player.log"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", default=DEFAULT_PROJECT)
    parser.add_argument("--output-dir")
    parser.add_argument("--player-name", default="MDF-MPTest")
    parser.add_argument("--build-target")
    parser.add_argument("--development-build", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--allow-debugging", action=argparse.BooleanOptionalAction, default=True)
    parser.add_argument("--launch-smoke", action="store_true")
    parser.add_argument("--player-path")
    parser.add_argument("--scene", default="Title")
    parser.add_argument("--session")
    parser.add_argument("--automation-port", type=int, default=0)
    parser.add_argument("--exit-after-seconds", type=int, default=5)
    parser.add_argument("--launch-timeout-padding", type=int, default=20)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--skip-build", action="store_true")
    args = parser.parse_args()

    output_dir = pathlib.Path(args.output_dir) if args.output_dir else ROOT / "artifacts" / "builds" / utc_stamp()
    if not output_dir.is_absolute():
        output_dir = (ROOT / output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    if args.skip_build:
        if not args.player_path:
            raise SystemExit("--skip-build requires --player-path")
        player_path = pathlib.Path(args.player_path)
        if not player_path.is_absolute():
            player_path = (ROOT / player_path).resolve()
        metadata: dict[str, Any] = {"outputPath": str(player_path), "skippedBuild": True}
    else:
        player_path, metadata = build_player(args, output_dir)

    result: dict[str, Any] = {
        "outputDir": str(output_dir),
        "build": metadata,
    }
    if args.launch_smoke:
        result["launchSmoke"] = launch_smoke(player_path, output_dir, args)

    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(json.dumps({"success": False, "error": str(exc)}), file=sys.stderr)
        raise SystemExit(1)
