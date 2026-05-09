#!/usr/bin/env python3
from __future__ import annotations

import argparse
import ctypes
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
CREATE_SUSPENDED = 0x00000004
STARTF_USESTDHANDLES = 0x00000100
WAIT_TIMEOUT = 0x00000102
WAIT_FAILED = 0xFFFFFFFF
STILL_ACTIVE = 259
JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
JOB_OBJECT_BASIC_PROCESS_ID_LIST = 3
JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9


def _is_windows() -> bool:
    return platform.system().lower() == "windows"


if _is_windows():
    from ctypes import wintypes


    class _PROCESS_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("hProcess", wintypes.HANDLE),
            ("hThread", wintypes.HANDLE),
            ("dwProcessId", wintypes.DWORD),
            ("dwThreadId", wintypes.DWORD),
        ]


    class _STARTUPINFOW(ctypes.Structure):
        _fields_ = [
            ("cb", wintypes.DWORD),
            ("lpReserved", wintypes.LPWSTR),
            ("lpDesktop", wintypes.LPWSTR),
            ("lpTitle", wintypes.LPWSTR),
            ("dwX", wintypes.DWORD),
            ("dwY", wintypes.DWORD),
            ("dwXSize", wintypes.DWORD),
            ("dwYSize", wintypes.DWORD),
            ("dwXCountChars", wintypes.DWORD),
            ("dwYCountChars", wintypes.DWORD),
            ("dwFillAttribute", wintypes.DWORD),
            ("dwFlags", wintypes.DWORD),
            ("wShowWindow", wintypes.WORD),
            ("cbReserved2", wintypes.WORD),
            ("lpReserved2", ctypes.c_void_p),
            ("hStdInput", wintypes.HANDLE),
            ("hStdOutput", wintypes.HANDLE),
            ("hStdError", wintypes.HANDLE),
        ]


    class _IO_COUNTERS(ctypes.Structure):
        _fields_ = [
            ("ReadOperationCount", ctypes.c_ulonglong),
            ("WriteOperationCount", ctypes.c_ulonglong),
            ("OtherOperationCount", ctypes.c_ulonglong),
            ("ReadTransferCount", ctypes.c_ulonglong),
            ("WriteTransferCount", ctypes.c_ulonglong),
            ("OtherTransferCount", ctypes.c_ulonglong),
        ]


    class _JOBOBJECT_BASIC_LIMIT_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_longlong),
            ("PerJobUserTimeLimit", ctypes.c_longlong),
            ("LimitFlags", wintypes.DWORD),
            ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t),
            ("ActiveProcessLimit", wintypes.DWORD),
            ("Affinity", ctypes.c_size_t),
            ("PriorityClass", wintypes.DWORD),
            ("SchedulingClass", wintypes.DWORD),
        ]


    class _JOBOBJECT_EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("BasicLimitInformation", _JOBOBJECT_BASIC_LIMIT_INFORMATION),
            ("IoInfo", _IO_COUNTERS),
            ("ProcessMemoryLimit", ctypes.c_size_t),
            ("JobMemoryLimit", ctypes.c_size_t),
            ("PeakProcessMemoryUsed", ctypes.c_size_t),
            ("PeakJobMemoryUsed", ctypes.c_size_t),
        ]


def _kernel32() -> Any:
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
    kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
    kernel32.CreateJobObjectW.restype = wintypes.HANDLE
    kernel32.CreateProcessW.argtypes = [
        wintypes.LPCWSTR,
        wintypes.LPWSTR,
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.BOOL,
        wintypes.DWORD,
        ctypes.c_void_p,
        wintypes.LPCWSTR,
        ctypes.POINTER(_STARTUPINFOW),
        ctypes.POINTER(_PROCESS_INFORMATION),
    ]
    kernel32.CreateProcessW.restype = wintypes.BOOL
    kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel32.GetExitCodeProcess.restype = wintypes.BOOL
    kernel32.GetStdHandle.argtypes = [wintypes.DWORD]
    kernel32.GetStdHandle.restype = wintypes.HANDLE
    kernel32.QueryInformationJobObject.argtypes = [
        wintypes.HANDLE,
        ctypes.c_int,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD),
    ]
    kernel32.QueryInformationJobObject.restype = wintypes.BOOL
    kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
    kernel32.ResumeThread.restype = wintypes.DWORD
    kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
    kernel32.SetInformationJobObject.restype = wintypes.BOOL
    kernel32.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
    kernel32.TerminateJobObject.restype = wintypes.BOOL
    kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
    kernel32.TerminateProcess.restype = wintypes.BOOL
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.WaitForSingleObject.restype = wintypes.DWORD
    return kernel32


def _win_error(prefix: str) -> dict[str, Any]:
    code = ctypes.get_last_error()
    try:
        details = ctypes.FormatError(code)
    except Exception:
        details = f"Windows error {code}"
    return {"code": "WinError", "winError": code, "details": f"{prefix}: {details}"}


def _valid_handle(handle: Any) -> bool:
    return bool(handle) and int(handle) not in (0, -1)


class WindowsJobObject:
    def __init__(self, name: str | None = None, kill_on_close: bool = True) -> None:
        self.name = name
        self.kill_on_close_requested = kill_on_close
        self.kill_on_close_set = False
        self.job_created = False
        self.assigned_to_job = False
        self.terminate_job_attempted = False
        self.terminate_job_success = False
        self.errors: list[dict[str, Any]] = []
        self._kernel32 = _kernel32()
        self._handle = self._kernel32.CreateJobObjectW(None, name)
        if not _valid_handle(self._handle):
            self.errors.append(_win_error("CreateJobObjectW failed"))
            self._handle = None
            return
        self.job_created = True
        if kill_on_close:
            info = _JOBOBJECT_EXTENDED_LIMIT_INFORMATION()
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            ok = self._kernel32.SetInformationJobObject(
                self._handle,
                JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
                ctypes.byref(info),
                ctypes.sizeof(info),
            )
            if ok:
                self.kill_on_close_set = True
            else:
                self.errors.append(_win_error("SetInformationJobObject(KILL_ON_JOB_CLOSE) failed"))

    @property
    def handle(self) -> Any:
        return self._handle

    def assign(self, process_handle: Any) -> bool:
        if not _valid_handle(self._handle):
            self.errors.append({"code": "job_not_created", "details": "job handle is invalid"})
            return False
        ok = self._kernel32.AssignProcessToJobObject(self._handle, process_handle)
        self.assigned_to_job = bool(ok)
        if not ok:
            self.errors.append(_win_error("AssignProcessToJobObject failed"))
        return self.assigned_to_job

    def process_ids(self, max_count: int = 64) -> list[int]:
        if not _valid_handle(self._handle):
            return []
        pointer_size = ctypes.sizeof(ctypes.c_size_t)
        header_size = ctypes.sizeof(wintypes.DWORD) * 2
        buffer_size = header_size + pointer_size * max_count
        buffer = ctypes.create_string_buffer(buffer_size)
        returned = wintypes.DWORD(0)
        ok = self._kernel32.QueryInformationJobObject(
            self._handle,
            JOB_OBJECT_BASIC_PROCESS_ID_LIST,
            ctypes.byref(buffer),
            buffer_size,
            ctypes.byref(returned),
        )
        if not ok:
            return []
        count = int.from_bytes(buffer.raw[4:8], byteorder="little", signed=False)
        count = min(count, max_count)
        pids: list[int] = []
        for index in range(count):
            offset = header_size + pointer_size * index
            raw = buffer.raw[offset : offset + pointer_size]
            pids.append(int.from_bytes(raw, byteorder="little", signed=False))
        return pids

    def terminate(self, exit_code: int = 1) -> dict[str, Any]:
        self.terminate_job_attempted = True
        step: dict[str, Any] = {
            "step": "terminate_job",
            "ok": False,
            "jobCreated": self.job_created,
            "assignedToJob": self.assigned_to_job,
            "terminateJobAttempted": True,
            "pidsInJobBefore": self.process_ids(),
        }
        if not _valid_handle(self._handle):
            step["skipped"] = True
            step["reason"] = "no_job_handle"
            return step
        ok = self._kernel32.TerminateJobObject(self._handle, exit_code)
        self.terminate_job_success = bool(ok)
        step["ok"] = bool(ok)
        step["terminateJobSuccess"] = self.terminate_job_success
        if not ok:
            step["error"] = _win_error("TerminateJobObject failed")
        step["pidsInJobAfter"] = self.process_ids()
        return step

    def diagnostics(self) -> dict[str, Any]:
        return {
            "jobCreated": self.job_created,
            "assignedToJob": self.assigned_to_job,
            "killOnJobCloseRequested": self.kill_on_close_requested,
            "killOnJobCloseSet": self.kill_on_close_set,
            "terminateJobAttempted": self.terminate_job_attempted,
            "terminateJobSuccess": self.terminate_job_success,
            "pidsInJob": self.process_ids(),
            "errors": self.errors,
        }

    def close(self) -> dict[str, Any]:
        step = {"step": "close_job", "ok": True, "skipped": True}
        if _valid_handle(self._handle):
            step["skipped"] = False
            step["pidsInJobBeforeClose"] = self.process_ids()
            ok = self._kernel32.CloseHandle(self._handle)
            step["ok"] = bool(ok)
            if not ok:
                step["error"] = _win_error("CloseHandle(job) failed")
            self._handle = None
        return step


class WindowsLaunchedProcess:
    def __init__(self, handle: Any, pid: int, args: list[str]) -> None:
        self._kernel32 = _kernel32()
        self._handle = handle
        self.pid = pid
        self.args = args
        self.returncode: int | None = None

    def poll(self) -> int | None:
        if self.returncode is not None:
            return self.returncode
        code = wintypes.DWORD(0)
        if not self._kernel32.GetExitCodeProcess(self._handle, ctypes.byref(code)):
            raise OSError(_win_error("GetExitCodeProcess failed"))
        if code.value == STILL_ACTIVE:
            return None
        self.returncode = int(code.value)
        return self.returncode

    def wait(self, timeout: float | None = None) -> int:
        if self.returncode is not None:
            return self.returncode
        if timeout is None:
            timeout_ms = 0xFFFFFFFF
        else:
            timeout_ms = max(1, int(timeout * 1000))
        result = self._kernel32.WaitForSingleObject(self._handle, timeout_ms)
        if result == WAIT_TIMEOUT:
            raise subprocess.TimeoutExpired(self.args, timeout)
        if result == WAIT_FAILED:
            raise OSError(_win_error("WaitForSingleObject failed"))
        polled = self.poll()
        return int(polled if polled is not None else 0)

    def terminate(self) -> None:
        if self.poll() is not None:
            return
        if not self._kernel32.TerminateProcess(self._handle, 1):
            raise OSError(_win_error("TerminateProcess failed"))

    def kill(self) -> None:
        self.terminate()

    def close(self) -> None:
        if _valid_handle(self._handle):
            self._kernel32.CloseHandle(self._handle)
            self._handle = None


def launch_mdf_process(
    cmd: list[str],
    cwd: pathlib.Path,
    stdout: Any,
    stderr: Any,
    *,
    job_name: str | None = None,
) -> tuple[Any, WindowsJobObject | None]:
    if not _is_windows():
        return subprocess.Popen(cmd, cwd=cwd, stdout=stdout, stderr=stderr), None

    import msvcrt

    kernel32 = _kernel32()
    job = WindowsJobObject(job_name, kill_on_close=True)
    if not job.job_created:
        raise RuntimeError(f"Could not create Windows Job Object: {job.diagnostics()}")

    stdout_handle = msvcrt.get_osfhandle(stdout.fileno())
    stderr_handle = msvcrt.get_osfhandle(stderr.fileno())
    stdout_was_inheritable = os.get_handle_inheritable(stdout_handle)
    stderr_was_inheritable = os.get_handle_inheritable(stderr_handle)
    os.set_handle_inheritable(stdout_handle, True)
    os.set_handle_inheritable(stderr_handle, True)

    startup = _STARTUPINFOW()
    startup.cb = ctypes.sizeof(startup)
    startup.dwFlags = STARTF_USESTDHANDLES
    startup.hStdInput = 0
    startup.hStdOutput = stdout_handle
    startup.hStdError = stderr_handle
    proc_info = _PROCESS_INFORMATION()
    command_line = ctypes.create_unicode_buffer(subprocess.list2cmdline(cmd))
    try:
        ok = kernel32.CreateProcessW(
            cmd[0],
            command_line,
            None,
            None,
            True,
            CREATE_SUSPENDED,
            None,
            str(cwd),
            ctypes.byref(startup),
            ctypes.byref(proc_info),
        )
    finally:
        os.set_handle_inheritable(stdout_handle, stdout_was_inheritable)
        os.set_handle_inheritable(stderr_handle, stderr_was_inheritable)

    if not ok:
        error = _win_error("CreateProcessW failed")
        job.close()
        raise RuntimeError(f"Could not launch player process: {error}")

    process = WindowsLaunchedProcess(proc_info.hProcess, int(proc_info.dwProcessId), cmd)
    assigned = False
    try:
        assigned = job.assign(proc_info.hProcess)
        if not assigned:
            kernel32.TerminateProcess(proc_info.hProcess, 1)
            raise RuntimeError(f"Could not assign player process to Windows Job Object: {job.diagnostics()}")
        resumed = kernel32.ResumeThread(proc_info.hThread)
        if resumed == 0xFFFFFFFF:
            error = _win_error("ResumeThread failed")
            kernel32.TerminateProcess(proc_info.hProcess, 1)
            raise RuntimeError(f"Could not resume player process after Job Object assignment: {error}")
    finally:
        if _valid_handle(proc_info.hThread):
            kernel32.CloseHandle(proc_info.hThread)
        if not assigned:
            job.close()
    return process, job


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


def orphan_pressure_report(
    orphan_threshold: int,
    force_run_with_orphans: bool,
    secrets: list[str] | None = None,
) -> dict[str, Any]:
    processes = mdf_player_processes(secrets)
    live = [proc for proc in processes if isinstance(proc.get("pid"), int)]
    threshold = int(orphan_threshold)
    blocked = threshold >= 0 and len(live) > threshold and not force_run_with_orphans
    return {
        "status": "NEEDS_ENVIRONMENT" if blocked else "PASS",
        "blocked": blocked,
        "liveCount": len(live),
        "threshold": threshold,
        "forceRunWithOrphans": force_run_with_orphans,
        "processes": processes,
        "message": (
            f"live {PLAYER_PROCESS_NAME} count {len(live)} exceeds threshold {threshold}"
            if blocked
            else "orphan pressure gate passed"
        ),
    }


def write_orphan_pressure_report(
    artifact_dir: pathlib.Path,
    orphan_threshold: int,
    force_run_with_orphans: bool,
    secrets: list[str] | None = None,
) -> dict[str, Any]:
    artifact_dir.mkdir(parents=True, exist_ok=True)
    report = orphan_pressure_report(orphan_threshold, force_run_with_orphans, secrets)
    write_json(artifact_dir / "orphan-pressure.json", report)
    return report


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
    process: Any
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
    job: WindowsJobObject | None = None
    headless_player: bool = False

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
            "headlessPlayer": self.headless_player,
            "startedProcess": process_info(pid, self.redaction_secrets),
            "job": self.job.diagnostics() if self.job is not None else {
                "jobCreated": False,
                "assignedToJob": False,
                "terminateJobAttempted": False,
                "terminateJobSuccess": False,
                "pidsInJob": [],
                "reason": "not_windows",
            },
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
            self.close_job(report)
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
            if self.job is not None:
                report["steps"].append(self.job.terminate(exit_code=1))
                report["steps"].append(_wait_for_exit(self.process, timeout_seconds, "wait_after_terminate_job"))
            else:
                report["steps"].append({
                    "step": "terminate_job",
                    "ok": False,
                    "skipped": True,
                    "reason": "not_windows_or_no_job",
                    "jobCreated": False,
                    "assignedToJob": False,
                    "terminateJobAttempted": False,
                    "terminateJobSuccess": False,
                    "pidsInJob": [],
                })

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
        report["jobAfterCleanup"] = self.job.diagnostics() if self.job is not None else report["job"]
        self.close_job(report)
        return report

    def close_logs(self) -> None:
        for handle in (self.stdout_handle, self.stderr_handle):
            try:
                handle.close()
            except Exception:
                pass

    def close_job(self, report: dict[str, Any] | None = None) -> None:
        if self.job is not None:
            step = self.job.close()
            if report is not None:
                report.setdefault("steps", []).append(step)
            self.job = None
        close_process = getattr(self.process, "close", None)
        if callable(close_process):
            try:
                close_process()
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
        "headlessPlayer": any(bool(getattr(proc, "headless_player", False)) for proc in processes if proc is not None),
        "strategy": [
            "automation /quit",
            "wait for process exit",
            "terminate Windows Job Object",
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
    headless_player: bool = False,
) -> PlayerProcess:
    stdout_path = artifact_dir / f"{peer_name}.stdout.log"
    stderr_path = artifact_dir / f"{peer_name}.stderr.log"
    player_log_path = artifact_dir / f"{peer_name}.Player.log"
    command_path = artifact_dir / f"{peer_name}.command.json"
    stdout_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        str(player_path),
    ]
    if headless_player:
        cmd.extend(["-batchmode", "-nographics"])
    cmd.extend([
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
    ])
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
                "headlessPlayer": headless_player,
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    stdout = stdout_path.open("w", encoding="utf-8")
    stderr = stderr_path.open("w", encoding="utf-8")
    try:
        process, job = launch_mdf_process(
            cmd,
            ROOT,
            stdout,
            stderr,
            job_name=f"MDF-MPTest-{peer_name}-{os.getpid()}-{int(time.time() * 1000)}",
        )
    except Exception:
        stdout.close()
        stderr.close()
        raise
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
                "job": job.diagnostics() if job is not None else {
                    "jobCreated": False,
                    "assignedToJob": False,
                    "reason": "not_windows",
                },
                "headlessPlayer": headless_player,
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
        job=job,
        headless_player=headless_player,
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
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")
    args = parser.parse_args()

    artifact_dir = pathlib.Path(args.artifact_dir)
    orphan_report = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        [args.token, args.connection_token],
    )
    if orphan_report.get("blocked"):
        print(json.dumps({
            "success": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "orphanPressure": orphan_report,
            "artifactDir": str(artifact_dir),
        }, indent=2))
        return 2

    proc = launch_player(
        pathlib.Path(args.player_path),
        args.role,
        args.session,
        args.port,
        args.token,
        args.connection_token,
        artifact_dir,
        args.peer_name,
        args.max_players,
        args.scene,
        args.case_name,
        exit_after_seconds=args.exit_after_seconds,
        headless_player=args.headless_player,
    )
    print(json.dumps({
        "pid": proc.process.pid,
        "stdout": str(proc.stdout_path),
        "stderr": str(proc.stderr_path),
        "headlessPlayer": args.headless_player,
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
