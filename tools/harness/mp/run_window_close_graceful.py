#!/usr/bin/env python3
"""Verify that a visible Windows Development player handles native WM_CLOSE cleanly."""
from __future__ import annotations

import argparse
import ctypes
import json
import os
import pathlib
import subprocess
import time
from typing import Any

from automation_client import AutomationClient
from common import (
    ROOT,
    free_port,
    make_artifact_dir,
    new_session,
    new_token,
    normalize_snapshot_response,
    write_json,
    write_standard_result,
)
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)


CASE_NAME = "window-close-graceful"
WM_CLOSE = 0x0010


def _redact(value: str, secrets: list[str]) -> str:
    result = value
    for secret in secrets:
        if secret:
            result = result.replace(secret, "<redacted-token>")
    return result


def _runner_is_running(response: Any) -> bool:
    state = normalize_snapshot_response(response)
    if not isinstance(state, dict):
        return False
    runner = state.get("runner")
    return isinstance(runner, dict) and runner.get("isRunning") is True


def wait_for_runtime_ready(
    process: PlayerProcess,
    client: AutomationClient,
    timeout_seconds: float,
) -> dict[str, Any]:
    started = time.time()
    deadline = started + max(0.1, timeout_seconds)
    attempts = 0
    last_ping: dict[str, Any] = {"success": False, "error": {"code": "not_attempted"}}
    last_state: dict[str, Any] = {"success": False, "error": {"code": "not_attempted"}}

    while time.time() < deadline and process.process.poll() is None:
        attempts += 1
        try:
            last_ping = client.ping()
        except Exception as exc:
            last_ping = {
                "success": False,
                "error": {"code": type(exc).__name__, "details": str(exc)},
            }
        if last_ping.get("success") is True:
            try:
                last_state = client.dump_state()
            except Exception as exc:
                last_state = {
                    "success": False,
                    "error": {"code": type(exc).__name__, "details": str(exc)},
                }
            if _runner_is_running(last_state):
                return {
                    "ready": True,
                    "automationReady": True,
                    "runnerReady": True,
                    "attempts": attempts,
                    "elapsedSeconds": round(time.time() - started, 3),
                    "ping": last_ping,
                    "state": last_state,
                }
        time.sleep(0.25)

    return {
        "ready": False,
        "automationReady": last_ping.get("success") is True,
        "runnerReady": _runner_is_running(last_state),
        "attempts": attempts,
        "elapsedSeconds": round(time.time() - started, 3),
        "processExitCode": process.process.poll(),
        "ping": last_ping,
        "state": last_state,
    }


def _visible_windows_for_pid(pid: int) -> list[dict[str, Any]]:
    if os.name != "nt":
        return []

    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    enum_proc_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [enum_proc_type, wintypes.LPARAM]
    user32.EnumWindows.restype = wintypes.BOOL
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.IsWindowVisible.restype = wintypes.BOOL
    user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
    user32.GetWindowTextLengthW.restype = ctypes.c_int
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetWindowTextW.restype = ctypes.c_int

    windows: list[dict[str, Any]] = []

    @enum_proc_type
    def collect(hwnd: Any, _lparam: Any) -> bool:
        window_pid = wintypes.DWORD(0)
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(window_pid))
        if int(window_pid.value) != pid or not user32.IsWindowVisible(hwnd):
            return True
        length = max(0, int(user32.GetWindowTextLengthW(hwnd)))
        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buffer, length + 1)
        windows.append({"handle": int(hwnd), "title": buffer.value})
        return True

    if not user32.EnumWindows(collect, 0):
        error = ctypes.get_last_error()
        if error:
            raise ctypes.WinError(error)
    return windows


def post_native_window_close(
    pid: int,
    process: Any,
    timeout_seconds: float,
) -> dict[str, Any]:
    started = time.time()
    if os.name != "nt":
        return {
            "success": False,
            "method": "PostMessageW(WM_CLOSE)",
            "error": {"code": "windows_required"},
        }

    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.PostMessageW.restype = wintypes.BOOL
    deadline = started + max(0.1, timeout_seconds)
    observed: list[dict[str, Any]] = []

    while time.time() < deadline and process.poll() is None:
        candidates = _visible_windows_for_pid(pid)
        if candidates:
            observed = candidates
            # Unity's main player window normally has a title; prefer it over helper windows.
            target = sorted(candidates, key=lambda item: bool(item.get("title")), reverse=True)[0]
            ctypes.set_last_error(0)
            posted = bool(user32.PostMessageW(target["handle"], WM_CLOSE, 0, 0))
            error = ctypes.get_last_error()
            return {
                "success": posted,
                "method": "PostMessageW(WM_CLOSE)",
                "message": WM_CLOSE,
                "pid": pid,
                "target": target,
                "visibleWindows": candidates,
                "winError": error if not posted else 0,
                "elapsedSeconds": round(time.time() - started, 3),
            }
        time.sleep(0.2)

    return {
        "success": False,
        "method": "PostMessageW(WM_CLOSE)",
        "message": WM_CLOSE,
        "pid": pid,
        "visibleWindows": observed,
        "processExitCode": process.poll(),
        "elapsedSeconds": round(time.time() - started, 3),
        "error": {"code": "visible_window_timeout"},
    }


def wait_for_process_exit(process: Any, timeout_seconds: float) -> dict[str, Any]:
    started = time.time()
    try:
        exit_code = process.wait(timeout=max(0.1, timeout_seconds))
        return {
            "exited": True,
            "exitCode": exit_code,
            "elapsedSeconds": round(time.time() - started, 3),
        }
    except subprocess.TimeoutExpired:
        return {
            "exited": False,
            "exitCode": process.poll(),
            "elapsedSeconds": round(time.time() - started, 3),
            "error": {"code": "native_close_exit_timeout"},
        }


def evaluate_shutdown_log(text: str, secrets: list[str] | None = None) -> dict[str, Any]:
    secrets = secrets or []
    lines = text.splitlines()
    phases: list[tuple[str, tuple[str, ...]]] = [
        ("windowCloseIntercepted", ("window_close_intercepted",)),
        ("quitRequested", ("quit_requested",)),
        ("runnerShutdownBegin", ("runner_shutdown_begin",)),
        ("runnerShutdownFinished", ("runner_shutdown_complete", "runner_shutdown_timeout")),
        ("automationServerStop", ("automation_server_stop",)),
        ("applicationQuitCalled", ("application_quit_called",)),
    ]
    matches: list[dict[str, Any]] = []
    failures: list[str] = []
    search_from = 0

    for name, alternatives in phases:
        found_index = -1
        found_token = ""
        for index in range(search_from, len(lines)):
            token = next((item for item in alternatives if item in lines[index]), "")
            if token:
                found_index = index
                found_token = token
                break
        if found_index < 0:
            failures.append(f"missing_or_out_of_order:{name}")
            continue
        matches.append(
            {
                "phase": name,
                "token": found_token,
                "lineNumber": found_index + 1,
                "line": _redact(lines[found_index], secrets),
            }
        )
        search_from = found_index + 1

    return {
        "success": not failures and len(matches) == len(phases),
        "ordered": not failures and len(matches) == len(phases),
        "matches": matches,
        "failures": failures,
        "lineCount": len(lines),
    }


def read_player_log(path: pathlib.Path, timeout_seconds: float = 2.0) -> str:
    deadline = time.time() + max(0.1, timeout_seconds)
    last = ""
    while time.time() < deadline:
        try:
            last = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            last = ""
        if "application_quit_called" in last:
            return last
        time.sleep(0.1)
    return last


def run_self_check() -> dict[str, Any]:
    ordered = "\n".join(
        [
            "[MPTEST] phase=window_close_intercepted",
            "[MPTEST] phase=quit_requested",
            "[MPTEST] phase=runner_shutdown_begin",
            "[MPTEST] phase=runner_shutdown_complete",
            "[MPTEST] phase=automation_server_stop",
            "[MPTEST] phase=application_quit_called",
        ]
    )
    timeout_variant = ordered.replace("runner_shutdown_complete", "runner_shutdown_timeout")
    out_of_order = ordered.replace(
        "[MPTEST] phase=runner_shutdown_begin\n[MPTEST] phase=runner_shutdown_complete",
        "[MPTEST] phase=runner_shutdown_complete\n[MPTEST] phase=runner_shutdown_begin",
    )
    checks = {
        "completeVariant": evaluate_shutdown_log(ordered)["success"],
        "timeoutVariant": evaluate_shutdown_log(timeout_variant)["success"],
        "outOfOrderRejected": not evaluate_shutdown_log(out_of_order)["success"],
    }
    return {"success": all(checks.values()), "checks": checks}


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Launch a visible MDF Development player and verify native WM_CLOSE graceful shutdown.",
    )
    parser.add_argument("--player-path", help="Path to a Windows Development MDF-MPTest.exe.")
    parser.add_argument("--artifact-dir", help="Optional explicit artifact directory.")
    parser.add_argument("--session")
    parser.add_argument("--automation-port", type=int, default=0)
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--max-players", type=int, default=1)
    parser.add_argument("--seed", type=int, default=130713)
    parser.add_argument("--readiness-timeout", type=float, default=60.0)
    parser.add_argument("--window-timeout", type=float, default=10.0)
    parser.add_argument("--exit-timeout", type=float, default=15.0)
    parser.add_argument("--cleanup-timeout", type=float, default=10.0)
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--self-check", action="store_true", help="Test log-order assertions without launching a player.")
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.self_check:
        report = run_self_check()
        print(json.dumps(report, indent=2))
        return 0 if report["success"] else 1

    if os.name != "nt":
        raise SystemExit("run_window_close_graceful.py requires Windows")
    if not args.player_path:
        raise SystemExit("--player-path is required unless --self-check is used")
    if args.max_players < 1:
        raise SystemExit("--max-players must be at least 1")

    player_path = pathlib.Path(args.player_path)
    if not player_path.is_absolute():
        player_path = (ROOT / player_path).resolve()
    if not player_path.is_file():
        raise SystemExit(f"Player executable does not exist: {player_path}")

    if args.artifact_dir:
        artifact_dir = pathlib.Path(args.artifact_dir)
        if not artifact_dir.is_absolute():
            artifact_dir = (ROOT / artifact_dir).resolve()
        artifact_dir.mkdir(parents=True, exist_ok=True)
    else:
        artifact_dir = make_artifact_dir(CASE_NAME)

    token = new_token()
    connection_token = new_token()
    session = args.session or new_session("window-close")
    port = args.automation_port or free_port()
    secrets = [token, connection_token]
    baseline_pids = mdf_player_pids()
    orphan_pressure = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        secrets,
    )
    write_json(
        artifact_dir / "run.json",
        {
            "case": CASE_NAME,
            "playerPath": str(player_path),
            "session": session,
            "automationPort": port,
            "scene": args.scene,
            "maxPlayers": args.max_players,
            "seed": args.seed,
            "visibleWindowRequired": True,
            "closeMethod": "PostMessageW(WM_CLOSE)",
            "timeouts": {
                "readinessSeconds": args.readiness_timeout,
                "windowSeconds": args.window_timeout,
                "exitSeconds": args.exit_timeout,
                "cleanupSeconds": args.cleanup_timeout,
            },
        },
    )

    failures: list[str] = []
    process: PlayerProcess | None = None
    readiness: dict[str, Any] = {"ready": False, "skipped": True}
    window_close: dict[str, Any] = {"success": False, "skipped": True}
    natural_exit: dict[str, Any] = {"exited": False, "skipped": True}
    cleanup_report: dict[str, Any]

    if orphan_pressure.get("blocked"):
        failures.append("orphan_pressure_gate_blocked")
    else:
        try:
            process = launch_player(
                player_path=player_path,
                role="host",
                session=session,
                port=port,
                token=token,
                connection_token=connection_token,
                artifact_dir=artifact_dir,
                peer_name="window-close-host",
                max_players=args.max_players,
                scene=args.scene,
                case_name=CASE_NAME,
                auto_start=True,
                load_game=True,
                exit_after_seconds=0,
                seed=args.seed,
                scenario="game_smoke",
                headless_player=False,
            )
            client = AutomationClient(port, token, timeout=2.0)
            readiness = wait_for_runtime_ready(process, client, args.readiness_timeout)
            write_json(artifact_dir / "automation-ready.json", readiness)
            if not readiness.get("ready"):
                failures.append("automation_or_runner_not_ready")
            else:
                window_close = post_native_window_close(
                    int(process.process.pid),
                    process.process,
                    args.window_timeout,
                )
                write_json(artifact_dir / "wm-close.json", window_close)
                if not window_close.get("success"):
                    failures.append("native_wm_close_not_sent")
                else:
                    natural_exit = wait_for_process_exit(process.process, args.exit_timeout)
                    write_json(artifact_dir / "native-close-exit.json", natural_exit)
                    if not natural_exit.get("exited"):
                        failures.append("native_close_did_not_exit_within_deadline")
        except Exception as exc:
            failures.append(f"exception:{type(exc).__name__}:{exc}")
    try:
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            [process] if process is not None else [],
            baseline_pids=baseline_pids,
            timeout_seconds=args.cleanup_timeout,
            strict_cleanup=True,
        )
    except Exception as exc:
        failures.append(f"cleanup_exception:{type(exc).__name__}:{exc}")
        cleanup_report = {
            "artifactDir": str(artifact_dir),
            "cleanupStatus": "FAIL",
            "cleanupSuccess": False,
            "orphanedPids": sorted(mdf_player_pids() - baseline_pids),
            "error": {"code": type(exc).__name__, "details": str(exc)},
        }
        write_json(artifact_dir / "cleanup-report.json", cleanup_report)

    player_log_path = process.player_log_path if process is not None else artifact_dir / "window-close-host.Player.log"
    player_log = read_player_log(player_log_path)
    log_assertions = evaluate_shutdown_log(player_log, secrets)
    write_json(artifact_dir / "shutdown-log-assertions.json", log_assertions)
    if not log_assertions.get("success"):
        failures.extend(log_assertions.get("failures") or ["shutdown_log_sequence_failed"])

    result = write_standard_result(
        artifact_dir,
        CASE_NAME,
        failures,
        cleanup_report=cleanup_report,
        headless_player=False,
        extra={
            "playerPath": str(player_path),
            "session": session,
            "visibleWindow": True,
            "closeMethod": "PostMessageW(WM_CLOSE)",
            "automationReady": readiness.get("automationReady") is True,
            "runnerReady": readiness.get("runnerReady") is True,
            "wmCloseSent": window_close.get("success") is True,
            "nativeExitWithinDeadline": natural_exit.get("exited") is True,
            "nativeExit": natural_exit,
            "shutdownLogOrdered": log_assertions.get("ordered") is True,
            "shutdownLogAssertionsPath": "shutdown-log-assertions.json",
            "orphanPressure": orphan_pressure,
        },
    )
    print(json.dumps(result, indent=2))
    return 0 if result.get("success") is True else 1


if __name__ == "__main__":
    raise SystemExit(main())
