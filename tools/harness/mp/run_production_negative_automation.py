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

if os.name == "nt":
    import winreg

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


def player_log_path(company_name: str = "DefaultCompany", product_name: str = "Mdfproject") -> pathlib.Path:
    if os.name == "nt":
        local_app = pathlib.Path(os.environ.get("LOCALAPPDATA", ""))
        return (local_app.parent / "LocalLow" / company_name / product_name / "Player.log").resolve()
    return pathlib.Path.home() / ".config" / "unity3d" / company_name / product_name / "Player.log"


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


def inspect_player_prefs_for_token(
    token: str,
    company_name: str = "DefaultCompany",
    product_name: str = "Mdfproject",
) -> dict[str, Any]:
    """Check the platform PlayerPrefs store without changing it."""
    if os.name == "nt":
        registry_path = f"Software\\{company_name}\\{product_name}"
        matches: list[str] = []
        values_seen = 0
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, registry_path) as key:
                index = 0
                while True:
                    try:
                        name, value, _ = winreg.EnumValue(key, index)
                    except OSError:
                        break
                    index += 1
                    values_seen += 1
                    if token in str(value):
                        matches.append(name)
        except FileNotFoundError:
            pass
        except OSError as exc:
            return {
                "inspected": False,
                "storage": f"HKCU\\{registry_path}",
                "tokenFound": False,
                "error": f"{type(exc).__name__}: {exc}",
            }

        return {
            "inspected": True,
            "storage": f"HKCU\\{registry_path}",
            "valuesSeen": values_seen,
            "tokenFound": bool(matches),
            "matchingValueNames": matches,
        }

    candidates = [
        pathlib.Path.home() / ".config" / "unity3d" / company_name / product_name / "prefs",
        pathlib.Path.home() / "Library" / "Preferences" / f"unity.{company_name}.{product_name}.plist",
    ]
    token_bytes = token.encode("utf-8")
    matches: list[str] = []
    inspected_paths: list[str] = []
    for candidate in candidates:
        inspected_paths.append(str(candidate))
        if candidate.exists() and token_bytes in candidate.read_bytes():
            matches.append(str(candidate))
    return {
        "inspected": True,
        "storage": inspected_paths,
        "tokenFound": bool(matches),
        "matchingPaths": matches,
    }


def inspect_log_for_harness_activity(log_path: pathlib.Path) -> dict[str, Any]:
    log_exists = log_path.exists()
    text = log_path.read_text(encoding="utf-8", errors="replace") if log_exists else ""
    harness_markers = [
        marker
        for marker in ("[MPTEST]", '"phase":"bootstrap"', "phase=bootstrap")
        if marker in text
    ]
    autostart_markers = [
        marker
        for marker in ("phase=autostart", '"phase":"autostart"', "[SyncAugments] autostart")
        if marker in text
    ]
    return {
        "logExists": log_exists,
        "harnessMarkers": harness_markers,
        "autostartMarkers": autostart_markers,
        "bootstrapAbsent": log_exists and not harness_markers,
        "autostartAbsent": log_exists and not autostart_markers and not harness_markers,
    }


def inspect_build_for_bootstrap_type(player_path: pathlib.Path) -> dict[str, Any]:
    build_root = player_path.parent
    needles = (b"MPTestBootstrap", "MPTestBootstrap".encode("utf-16-le"))
    data_root = player_path.with_suffix("")
    data_root = data_root.parent / f"{data_root.name}_Data"

    # Only executable managed code or IL2CPP metadata can prove that the QA type is in the
    # release. Unity 2021 serializes Editor type-layout names into globalgamemanagers.assets;
    # those inert cache strings can remain even when the player Assembly-CSharp.dll does not
    # contain the type. Treating every arbitrary build file as executable caused false fails.
    runtime_candidates: set[pathlib.Path] = set()
    managed_root = data_root / "Managed"
    if managed_root.exists():
        runtime_candidates.update(managed_root.rglob("*.dll"))
    for name in ("GameAssembly.dll", "UnityPlayer.dll", player_path.name):
        candidate = build_root / name
        if candidate.is_file():
            runtime_candidates.add(candidate)
    metadata_root = data_root / "il2cpp_data" / "Metadata"
    if metadata_root.exists():
        runtime_candidates.update(metadata_root.rglob("global-metadata.dat"))

    runtime_matches: list[str] = []
    for candidate in sorted(runtime_candidates):
        try:
            found = file_contains_any(candidate, needles)
        except OSError:
            continue
        if found:
            runtime_matches.append(str(candidate))

    # Keep the known Unity type-database locations visible for diagnostics without using
    # them as a release-code verdict.
    metadata_cache_matches: list[str] = []
    for relative in ("globalgamemanagers", "globalgamemanagers.assets"):
        candidate = data_root / relative
        if not candidate.is_file():
            continue
        try:
            if file_contains_any(candidate, needles):
                metadata_cache_matches.append(str(candidate))
        except OSError:
            continue

    return {
        "inspected": True,
        "buildRoot": str(build_root),
        "runtimeFilesScanned": len(runtime_candidates),
        "bootstrapTypeFound": bool(runtime_matches),
        "runtimeCodeMatches": runtime_matches,
        "metadataCacheMatches": metadata_cache_matches,
    }


def file_contains_any(path: pathlib.Path, needles: tuple[bytes, ...]) -> bool:
    overlap = max(len(needle) for needle in needles) - 1
    tail = b""
    with path.open("rb") as stream:
        while True:
            chunk = stream.read(1024 * 1024)
            if not chunk:
                return False
            data = tail + chunk
            if any(needle in data for needle in needles):
                return True
            tail = data[-overlap:] if overlap > 0 else b""


def launch_and_probe(
    player_path: pathlib.Path,
    artifact_dir: pathlib.Path,
    args: argparse.Namespace,
    metadata: dict[str, Any],
) -> dict[str, Any]:
    token = new_token()
    connection_token = f"prodneg-prefs-{new_token()}"
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
        "--mpAutoStart",
        "--mpLoadGame",
        "--mpExitAfterSeconds",
        str(args.exit_after_seconds),
        "--mpConnectionToken",
        connection_token,
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
    redacted_cmd = ["<redacted-token>" if item in (token, connection_token) else item for item in cmd]
    write_json(
        command_path,
        {
            "command": redacted_cmd,
            "session": session,
            "automationPort": port,
            "automationTokenHash": hash_for_log(token),
            "connectionTokenHash": hash_for_log(connection_token),
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
            redaction_secrets=[token, connection_token],
            stdout_handle=stdout,
            stderr_handle=stderr,
            job=job,
        )
        launched_at = time.time()
        ping = probe_ping(port, token, args.ping_timeout)
        observation_seconds = max(args.observation_seconds, args.exit_after_seconds + 2.0)
        deadline = launched_at + observation_seconds
        while time.time() < deadline and proc.process.poll() is None:
            time.sleep(0.25)
        remained_running_until_external_cleanup = proc.process.poll() is None
        early_exit_code = proc.process.returncode
        cleanup_report = write_case_cleanup_report(
            artifact_runtime_dir,
            [proc],
            baseline_pids=baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            strict_cleanup=True,
        )

    company_name = str(metadata.get("companyName") or "DefaultCompany")
    product_name = str(metadata.get("productName") or "Mdfproject")
    prefs = inspect_player_prefs_for_token(connection_token, company_name, product_name)
    log_isolation = inspect_log_for_harness_activity(prod_player_log_path)
    build_isolation = inspect_build_for_bootstrap_type(player_path)

    copied_player_log = ""
    source_log_value = metadata.get("playerLogPath")
    source_log = pathlib.Path(source_log_value) if source_log_value else player_log_path(company_name, product_name)
    if source_log.exists():
        copied_player_log = str(artifact_dir / "Player.log")
        (artifact_dir / "Player.log").write_bytes(source_log.read_bytes())

    return {
        "playerPath": str(player_path),
        "exitCode": proc.process.returncode,
        "earlyExitCode": early_exit_code,
        "remainedRunningUntilExternalCleanup": remained_running_until_external_cleanup,
        "externalCleanupRequired": remained_running_until_external_cleanup,
        "stdout": str(stdout_path),
        "stderr": str(stderr_path),
        "playerLog": str(prod_player_log_path),
        "playerLogSource": str(source_log),
        "playerLogCopied": copied_player_log,
        "session": session,
        "automationPort": port,
        "automationTokenHash": hash_for_log(token),
        "connectionTokenHash": hash_for_log(connection_token),
        "ping": ping,
        "playerPrefs": prefs,
        "logIsolation": log_isolation,
        "buildIsolation": build_isolation,
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
    parser.add_argument(
        "--metadata-path",
        help="build-metadata.json for --skip-build (defaults to the player directory)",
    )
    parser.add_argument("--skip-build", action="store_true")
    parser.add_argument("--player-name", default="MDF-MPTest-ProductionNegative")
    parser.add_argument("--build-target")
    parser.add_argument("--build-timeout", type=int, default=900)
    parser.add_argument("--scene", default="00_Title")
    parser.add_argument("--session")
    parser.add_argument("--automation-port", type=int, default=0)
    parser.add_argument("--exit-after-seconds", type=int, default=5)
    parser.add_argument("--observation-seconds", type=float, default=8.0)
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
        if not player_path.exists():
            raise SystemExit(f"--player-path does not exist: {player_path}")

        metadata_path = pathlib.Path(args.metadata_path) if args.metadata_path else player_path.parent / "build-metadata.json"
        if not metadata_path.is_absolute():
            metadata_path = (ROOT / metadata_path).resolve()
        if not metadata_path.exists():
            raise SystemExit(
                "--skip-build requires verifiable non-development build metadata; "
                f"not found: {metadata_path}"
            )

        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        metadata["skippedBuild"] = True
        metadata["metadataPath"] = str(metadata_path)
    else:
        player_path, metadata = build_non_development_player(artifact_dir, args)

    launch = launch_and_probe(player_path, artifact_dir, args, metadata)
    development_build = metadata.get("developmentBuild")
    cleanup_success = launch.get("cleanupSuccess") is True
    ping_disabled = launch["ping"].get("responded") is False
    bootstrap_absent = launch["logIsolation"].get("bootstrapAbsent") is True
    autostart_absent = launch["logIsolation"].get("autostartAbsent") is True
    player_prefs_untouched = (
        launch["playerPrefs"].get("inspected") is True
        and launch["playerPrefs"].get("tokenFound") is False
    )
    bootstrap_type_absent = launch["buildIsolation"].get("bootstrapTypeFound") is False
    auto_exit_absent = launch.get("remainedRunningUntilExternalCleanup") is True
    success = all(
        (
            development_build is False,
            ping_disabled,
            bootstrap_absent,
            bootstrap_type_absent,
            autostart_absent,
            player_prefs_untouched,
            auto_exit_absent,
            cleanup_success,
        )
    )
    report = {
        "success": success,
        "productionAutomationDisabled": success,
        "releaseIsolation": {
            "automationServerDisabled": ping_disabled,
            "bootstrapAbsentFromLog": bootstrap_absent,
            "bootstrapTypeAbsentFromBuild": bootstrap_type_absent,
            "autostartAbsent": autostart_absent,
            "autoExitAbsent": auto_exit_absent,
            "connectionTokenAbsentFromPlayerPrefs": player_prefs_untouched,
        },
        "developmentBuild": development_build,
        "metadata": metadata,
        "launch": launch,
        "failures": [],
    }
    if development_build is not False:
        report["failures"].append("build_metadata_not_non_development")
    if launch["ping"].get("responded"):
        report["failures"].append("automation_ping_responded_in_non_development_build")
    if not bootstrap_absent:
        report["failures"].append("mptest_bootstrap_activity_found_in_player_log")
    if not bootstrap_type_absent:
        report["failures"].append("mptest_bootstrap_type_found_in_non_development_build")
    if not autostart_absent:
        report["failures"].append("mptest_autostart_activity_found_in_player_log")
    if not player_prefs_untouched:
        report["failures"].append("mptest_connection_token_found_in_player_prefs")
    if not auto_exit_absent:
        report["failures"].append(f"player_exited_before_external_cleanup:{launch.get('earlyExitCode')}")
    if not cleanup_success:
        report["failures"].append(f"cleanup_failed:{launch.get('cleanupStatus')}")

    report_path = artifact_dir / "production-negative-automation.json"
    write_json(report_path, report)
    print(json.dumps({"artifactDir": str(artifact_dir), "report": report}, indent=2))
    return 0 if success else 1


if __name__ == "__main__":
    raise SystemExit(main())
