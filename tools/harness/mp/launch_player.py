#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import pathlib
import platform
import re
import subprocess
import time
from typing import Any
from dataclasses import dataclass

from common import ROOT, hash_for_log, write_json


PLAYER_PROCESS_NAME = "MDF-MPTest.exe"


def _is_windows() -> bool:
    return platform.system().lower() == "windows"


def _redact_text(value: str | None, secrets: list[str]) -> str | None:
    if value is None:
        return None
    redacted = value
    for secret in secrets:
        if secret:
            redacted = redacted.replace(secret, "<redacted-token>")
    redacted = re.sub(
        r"(?i)(--mp(?:Automation|Connection)Token(?:=|\s+))(?:(\"[^\"]*\")|('[^']*')|(\S+))",
        r"\1<redacted-token>",
        redacted,
    )
    return redacted


def _powershell_json(script: str, timeout: float = 5.0) -> Any:
    if not _is_windows():
        return None
    proc = subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        cwd=ROOT,
        text=True,
        capture_output=True,
        timeout=timeout,
        encoding="utf-8",
        errors="replace",
    )
    if proc.returncode != 0 or not proc.stdout.strip():
        return None
    return json.loads(proc.stdout)


def _normalize_process_info(raw: Any, secrets: list[str] | None = None) -> dict[str, Any] | None:
    if not isinstance(raw, dict):
        return None
    command_line = raw.get("CommandLine")
    secrets = secrets or []
    return {
        "pid": raw.get("ProcessId"),
        "parentPid": raw.get("ParentProcessId"),
        "name": raw.get("Name"),
        "commandLine": _redact_text(command_line, secrets) if isinstance(command_line, str) else command_line,
    }


def mdf_player_processes(secrets: list[str] | None = None) -> list[dict[str, Any]]:
    script = (
        "$items = Get-CimInstance Win32_Process -Filter \"Name = 'MDF-MPTest.exe'\" "
        "| Select-Object ProcessId,ParentProcessId,Name,CommandLine; "
        "@($items) | ConvertTo-Json -Depth 4"
    )
    try:
        data = _powershell_json(script)
    except Exception as exc:
        return [{"error": {"code": type(exc).__name__, "details": str(exc)}}]
    if data is None:
        return []
    if isinstance(data, dict):
        items = [data]
    elif isinstance(data, list):
        items = data
    else:
        items = []
    return [info for info in (_normalize_process_info(item, secrets) for item in items) if info]


def mdf_player_pids() -> set[int]:
    pids: set[int] = set()
    for proc in mdf_player_processes():
        pid = proc.get("pid")
        if isinstance(pid, int):
            pids.add(pid)
    return pids


def process_info(pid: int, secrets: list[str] | None = None) -> dict[str, Any] | None:
    if pid <= 0 or not _is_windows():
        return None
    script = (
        f"$item = Get-CimInstance Win32_Process -Filter \"ProcessId = {pid}\" "
        "| Select-Object ProcessId,ParentProcessId,Name,CommandLine; "
        "if ($null -eq $item) { '{}' } else { $item | ConvertTo-Json -Depth 4 }"
    )
    try:
        data = _powershell_json(script)
    except Exception:
        return None
    if data == {}:
        return None
    return _normalize_process_info(data, secrets)


def _file_record(path: pathlib.Path) -> dict[str, Any]:
    record: dict[str, Any] = {"path": str(path), "exists": path.exists()}
    if path.exists():
        try:
            stat = path.stat()
            record["sizeBytes"] = stat.st_size
            record["modifiedUtc"] = stat.st_mtime
        except OSError as exc:
            record["statError"] = {"code": type(exc).__name__, "details": str(exc)}
    return record


def _wait_for_exit(process: subprocess.Popen, timeout: float, step: str) -> dict[str, Any]:
    started = time.time()
    try:
        process.wait(timeout=max(0.1, timeout))
        return {"step": step, "ok": True, "elapsedSeconds": round(time.time() - started, 3), "exitCode": process.returncode}
    except subprocess.TimeoutExpired:
        return {"step": step, "ok": False, "elapsedSeconds": round(time.time() - started, 3), "error": "timeout"}


def _run_taskkill(pid: int, timeout: float) -> dict[str, Any]:
    result: dict[str, Any] = {"step": "taskkill_tree", "pid": pid, "ok": False, "skipped": True}
    if not _is_windows():
        result["reason"] = "not_windows"
        return result
    if pid <= 0:
        result["reason"] = "invalid_pid"
        return result
    try:
        proc = subprocess.run(
            ["taskkill", "/PID", str(pid), "/T", "/F"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            timeout=max(1.0, timeout),
            encoding="utf-8",
            errors="replace",
        )
        result.update({
            "skipped": False,
            "ok": proc.returncode == 0,
            "exitCode": proc.returncode,
            "stdout": proc.stdout,
            "stderr": proc.stderr,
        })
    except Exception as exc:
        result.update({"skipped": False, "error": {"code": type(exc).__name__, "details": str(exc)}})
    return result


def _wait_process_absent(pid: int, timeout: float, secrets: list[str] | None = None) -> dict[str, Any]:
    started = time.time()
    deadline = started + max(0.1, timeout)
    last = process_info(pid, secrets)
    while time.time() < deadline:
        last = process_info(pid, secrets)
        if last is None:
            return {"step": "wait_process_absent", "ok": True, "elapsedSeconds": round(time.time() - started, 3)}
        time.sleep(0.25)
    return {
        "step": "wait_process_absent",
        "ok": False,
        "elapsedSeconds": round(time.time() - started, 3),
        "process": last,
    }


@dataclass
class PlayerProcess:
    process: subprocess.Popen
    stdout_path: pathlib.Path
    stderr_path: pathlib.Path
    player_log_path: pathlib.Path
    command_path: pathlib.Path
    artifact_dir: pathlib.Path
    peer_name: str
    port: int
    token: str
    redaction_secrets: list[str]
    stdout_handle: object
    stderr_handle: object

    def terminate(self, timeout: float = 10.0) -> None:
        self.cleanup(timeout_seconds=timeout, graceful=False)

    def cleanup(
        self,
        timeout_seconds: float = 15.0,
        graceful: bool = True,
        leave_process: bool = False,
    ) -> dict[str, Any]:
        pid = int(self.process.pid or 0)
        report: dict[str, Any] = {
            "peer": self.peer_name,
            "pid": pid,
            "artifactDir": str(self.artifact_dir),
            "startedProcess": process_info(pid, self.redaction_secrets),
            "stdout": _file_record(self.stdout_path),
            "stderr": _file_record(self.stderr_path),
            "playerLog": _file_record(self.player_log_path),
            "steps": [],
        }

        if self.process.poll() is not None:
            report["cleanupStatus"] = "PASS"
            report["cleanupSuccess"] = True
            report["alreadyExited"] = True
            report["exitCode"] = self.process.returncode
            self.close_logs()
            return report

        if leave_process:
            report["cleanupStatus"] = "NEEDS_ENVIRONMENT"
            report["cleanupSuccess"] = False
            report["leftRunning"] = True
            report["afterProcess"] = process_info(pid, self.redaction_secrets)
            self.close_logs()
            return report

        if graceful:
            step: dict[str, Any] = {"step": "automation_quit", "ok": False}
            try:
                from automation_client import AutomationClient

                response = AutomationClient(self.port, self.token, timeout=2.0).quit()
                write_json(self.artifact_dir / f"{self.peer_name}-quit.json", response)
                step["response"] = response
                step["ok"] = response.get("success") is True
            except Exception as exc:
                step["error"] = {"code": type(exc).__name__, "details": str(exc)}
            report["steps"].append(step)
            wait = _wait_for_exit(self.process, timeout_seconds, "wait_after_quit")
            report["steps"].append(wait)

        if self.process.poll() is None:
            step = {"step": "terminate", "ok": False}
            try:
                self.process.terminate()
                step["ok"] = True
            except Exception as exc:
                step["error"] = {"code": type(exc).__name__, "details": str(exc)}
            report["steps"].append(step)
            report["steps"].append(_wait_for_exit(self.process, timeout_seconds, "wait_after_terminate"))

        if self.process.poll() is None:
            step = {"step": "kill", "ok": False}
            try:
                self.process.kill()
                step["ok"] = True
            except Exception as exc:
                step["error"] = {"code": type(exc).__name__, "details": str(exc)}
            report["steps"].append(step)
            report["steps"].append(_wait_for_exit(self.process, timeout_seconds, "wait_after_kill"))

        live_after_kill = process_info(pid, self.redaction_secrets)
        if self.process.poll() is None or live_after_kill is not None:
            if live_after_kill is not None:
                report["steps"].append({"step": "cim_alive_after_kill", "ok": False, "process": live_after_kill})
            report["steps"].append(_run_taskkill(pid, timeout_seconds))
            if self.process.poll() is None:
                report["steps"].append(_wait_for_exit(self.process, timeout_seconds, "wait_after_taskkill"))
            report["steps"].append(_wait_process_absent(pid, timeout_seconds, self.redaction_secrets))

        after_process = process_info(pid, self.redaction_secrets)
        still_running = self.process.poll() is None or after_process is not None
        report["afterProcess"] = after_process
        report["stdoutAfter"] = _file_record(self.stdout_path)
        report["stderrAfter"] = _file_record(self.stderr_path)
        report["playerLogAfter"] = _file_record(self.player_log_path)
        report["exitCode"] = self.process.returncode
        report["cleanupSuccess"] = not still_running
        report["cleanupStatus"] = "PASS" if not still_running else "NEEDS_ENVIRONMENT"
        self.close_logs()
        return report

    def close_logs(self) -> None:
        for handle in (self.stdout_handle, self.stderr_handle):
            try:
                handle.close()
            except Exception:
                pass


def write_case_cleanup_report(
    artifact_dir: pathlib.Path,
    processes: list[PlayerProcess],
    baseline_pids: set[int] | None = None,
    timeout_seconds: float = 15.0,
    leave_processes: bool = False,
    strict_cleanup: bool = False,
) -> dict[str, Any]:
    baseline_pids = baseline_pids or set()
    redaction_secrets: list[str] = []
    for proc in processes:
        redaction_secrets.extend(getattr(proc, "redaction_secrets", []) or [])
    reports = [
        proc.cleanup(
            timeout_seconds=timeout_seconds,
            graceful=True,
            leave_process=leave_processes,
        )
        for proc in processes
        if proc is not None
    ]
    after_processes = mdf_player_processes(redaction_secrets)
    launched_pids = {int(report["pid"]) for report in reports if isinstance(report.get("pid"), int)}
    orphaned = [
        proc
        for proc in after_processes
        if isinstance(proc.get("pid"), int)
        and int(proc["pid"]) not in baseline_pids
        and (int(proc["pid"]) in launched_pids or str(artifact_dir) in str(proc.get("commandLine") or ""))
    ]
    cleanup_success = all(report.get("cleanupSuccess") is True for report in reports) and not orphaned
    cleanup_status = "PASS" if cleanup_success else ("FAIL" if strict_cleanup else "NEEDS_ENVIRONMENT")
    result = {
        "artifactDir": str(artifact_dir),
        "baselinePids": sorted(baseline_pids),
        "processes": reports,
        "afterMdfProcesses": after_processes,
        "orphanedPids": [proc.get("pid") for proc in orphaned],
        "orphanedProcesses": orphaned,
        "cleanupSuccess": cleanup_success,
        "cleanupStatus": cleanup_status,
        "strictCleanup": strict_cleanup,
        "leaveProcesses": leave_processes,
        "timeoutSeconds": timeout_seconds,
        "strategy": [
            "automation /quit",
            "wait for process exit",
            "terminate process",
            "kill process",
            "Windows taskkill /PID <pid> /T /F fallback",
        ],
    }
    write_json(artifact_dir / "cleanup-report.json", result)
    return result


def launch_player(
    player_path: pathlib.Path,
    role: str,
    session: str,
    port: int,
    token: str,
    connection_token: str,
    artifact_dir: pathlib.Path,
    peer_name: str,
    max_players: int = 2,
    scene: str = "Game",
    case_name: str = "manual",
    auto_start: bool = True,
    load_game: bool = True,
    exit_after_seconds: int = 0,
    seed: int = 0,
    scenario: str = "game_smoke",
    extra_args: list[str] | None = None,
) -> PlayerProcess:
    stdout_path = artifact_dir / f"{peer_name}.stdout.log"
    stderr_path = artifact_dir / f"{peer_name}.stderr.log"
    player_log_path = artifact_dir / f"{peer_name}.Player.log"
    command_path = artifact_dir / f"{peer_name}.command.json"
    stdout_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        str(player_path),
        "-logFile",
        str(player_log_path),
        "--mpTest",
        "--mpRole",
        role,
        "--mpSession",
        session,
        "--mpMaxPlayers",
        str(max_players),
        "--mpScene",
        scene,
        "--mpAutomationPort",
        str(port),
        "--mpAutomationToken",
        token,
        "--mpConnectionToken",
        connection_token,
        "--mpCase",
        case_name,
        "--mpArtifactDir",
        str(artifact_dir),
        "--mpSeed",
        str(seed),
        "--mpScenario",
        scenario,
    ]
    if auto_start:
        cmd.append("--mpAutoStart")
    if load_game:
        cmd.append("--mpLoadGame")
    if exit_after_seconds > 0:
        cmd.extend(["--mpExitAfterSeconds", str(exit_after_seconds)])
    if extra_args:
        cmd.extend(extra_args)

    redacted = ["<automation-token>" if part == token else "<connection-token>" if part == connection_token else part for part in cmd]
    command_path.write_text(
        json.dumps(
            {
                "command": redacted,
                "automationTokenHash": hash_for_log(token),
                "connectionTokenHash": hash_for_log(connection_token),
                "port": port,
                "role": role,
                "session": session,
                "playerLog": str(player_log_path),
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    stdout = stdout_path.open("w", encoding="utf-8")
    stderr = stderr_path.open("w", encoding="utf-8")
    process = subprocess.Popen(cmd, cwd=ROOT, stdout=stdout, stderr=stderr)
    launch_info = process_info(process.pid, [token, connection_token]) or {
        "pid": process.pid,
        "parentPid": os.getpid(),
        "name": PLAYER_PROCESS_NAME,
        "commandLine": redacted,
    }
    command_path.write_text(
        json.dumps(
            {
                "command": redacted,
                "automationTokenHash": hash_for_log(token),
                "connectionTokenHash": hash_for_log(connection_token),
                "port": port,
                "role": role,
                "session": session,
                "playerLog": str(player_log_path),
                "process": launch_info,
                "artifactDir": str(artifact_dir),
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    return PlayerProcess(
        process=process,
        stdout_path=stdout_path,
        stderr_path=stderr_path,
        player_log_path=player_log_path,
        command_path=command_path,
        artifact_dir=artifact_dir,
        peer_name=peer_name,
        port=port,
        token=token,
        redaction_secrets=[token, connection_token],
        stdout_handle=stdout,
        stderr_handle=stderr,
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path", required=True)
    parser.add_argument("--role", choices=["host", "client"], required=True)
    parser.add_argument("--session", required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--connection-token", required=True)
    parser.add_argument("--artifact-dir", required=True)
    parser.add_argument("--peer-name", default="player")
    parser.add_argument("--max-players", type=int, default=2)
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--case-name", default="manual")
    parser.add_argument("--exit-after-seconds", type=int, default=0)
    args = parser.parse_args()

    proc = launch_player(
        pathlib.Path(args.player_path),
        args.role,
        args.session,
        args.port,
        args.token,
        args.connection_token,
        pathlib.Path(args.artifact_dir),
        args.peer_name,
        args.max_players,
        args.scene,
        args.case_name,
        exit_after_seconds=args.exit_after_seconds,
    )
    print(json.dumps({"pid": proc.process.pid, "stdout": str(proc.stdout_path), "stderr": str(proc.stderr_path)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
