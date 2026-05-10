from __future__ import annotations

import datetime as dt
import json
import pathlib
import secrets
import socket
import subprocess
import time
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[3]
PROJECT = "Mdfproject"
ARTIFACT_ROOT = ROOT / "artifacts" / "mp"


def utc_stamp() -> str:
    return dt.datetime.utcnow().strftime("%Y%m%d-%H%M%S")


def make_artifact_dir(case_name: str, root: pathlib.Path | None = None) -> pathlib.Path:
    base = root or ARTIFACT_ROOT
    stem = f"{utc_stamp()}-{case_name}"
    path = base / stem
    suffix = 2
    while path.exists():
        path = base / f"{stem}-{suffix}"
        suffix += 1
    path.mkdir(parents=True, exist_ok=True)
    (path / "snapshots").mkdir(exist_ok=True)
    (path / "screenshots").mkdir(exist_ok=True)
    return path


def free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        sock.bind(("127.0.0.1", 0))
        return int(sock.getsockname()[1])


def new_token() -> str:
    return secrets.token_urlsafe(24)


def new_session(prefix: str = "mp") -> str:
    return f"{prefix}-{utc_stamp()}-{secrets.token_hex(3)}"


def hash_for_log(value: str) -> str:
    h = 2166136261
    for ch in value or "":
        h ^= ord(ch)
        h = (h * 16777619) & 0xFFFFFFFF
    return f"{h:08X}" if value else "empty"


def write_json(path: pathlib.Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding="utf-8")


def read_json(path: pathlib.Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def run_command(
    args: list[str],
    artifact_dir: pathlib.Path,
    timeout: int = 120,
    check: bool = False,
    label: str | None = None,
) -> subprocess.CompletedProcess[str]:
    transcript = artifact_dir / "command-transcript.log"
    started = time.time()
    with transcript.open("a", encoding="utf-8") as fh:
        fh.write("$ " + " ".join(args) + "\n")
    proc = subprocess.run(
        args,
        cwd=ROOT,
        text=True,
        capture_output=True,
        timeout=timeout,
        encoding="utf-8",
        errors="replace",
    )
    with transcript.open("a", encoding="utf-8") as fh:
        if label:
            fh.write(f"[label] {label}\n")
        fh.write(proc.stdout)
        if proc.stderr:
            fh.write("\n[stderr]\n")
            fh.write(proc.stderr)
        fh.write(f"\n[exit] {proc.returncode} elapsed={time.time() - started:.2f}s\n")
    if check and proc.returncode != 0:
        raise subprocess.CalledProcessError(proc.returncode, args, proc.stdout, proc.stderr)
    return proc


def parse_json_object(text: str) -> Any:
    stripped = text.strip()
    if not stripped:
        return {}
    start = stripped.find("{")
    end = stripped.rfind("}")
    if start < 0 or end < start:
        return {}
    return json.loads(stripped[start : end + 1])


def unity_cli(args: list[str], artifact_dir: pathlib.Path, timeout: int = 120, check: bool = False) -> subprocess.CompletedProcess[str]:
    return run_command(["unity-cli", "--project", PROJECT] + args, artifact_dir, timeout=timeout, check=check)


def unity_cli_json(args: list[str], artifact_dir: pathlib.Path, timeout: int = 120, check: bool = False) -> Any:
    proc = unity_cli(args, artifact_dir, timeout=timeout, check=check)
    return parse_json_object(proc.stdout)


def wait_unity_ready(artifact_dir: pathlib.Path, timeout_seconds: int = 60) -> bool:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        proc = unity_cli(["status"], artifact_dir, timeout=30)
        status_text = (proc.stdout + "\n" + proc.stderr).lower()
        if proc.returncode == 0 and ("ready" in status_text or "playing" in status_text):
            return True
        time.sleep(1)
    return False


def latest_player_path() -> pathlib.Path | None:
    builds = ROOT / "artifacts" / "builds"
    if not builds.exists():
        return None
    candidates: list[tuple[float, pathlib.Path]] = []
    for metadata in builds.glob("*/build-metadata.json"):
        try:
            data = read_json(metadata)
            if data.get("developmentBuild") is False:
                continue
            output = pathlib.Path(data.get("outputPath", ""))
            if output.exists():
                candidates.append((metadata.stat().st_mtime, output))
        except Exception:
            continue
    if not candidates:
        return None
    return sorted(candidates, reverse=True)[0][1]


def normalize_snapshot_response(data: Any) -> Any:
    if isinstance(data, dict) and data.get("success") is True and isinstance(data.get("data"), dict):
        inner = data["data"]
        if isinstance(inner, dict) and "snapshot" in inner:
            return inner["snapshot"]
        return inner
    return data


SCENE_ALIASES = {
    "Title": "00_Title",
    "00_Title": "00_Title",
    "MatchingLobby": "01_MatchingLobby",
    "TestMatching": "01_MatchingLobby",
    "01_MatchingLobby": "01_MatchingLobby",
    "JoinLobby": "02_JoinLobby",
    "02_JoinLobby": "02_JoinLobby",
    "Game": "03_Game",
    "03_Game": "03_Game",
}


def normalize_scene_name(scene: Any) -> str:
    if scene is None:
        return ""
    text = str(scene)
    return SCENE_ALIASES.get(text, text)


def scene_matches(actual: Any, expected: str) -> bool:
    return normalize_scene_name(actual) == normalize_scene_name(expected)


def write_standard_result(
    artifact_dir: pathlib.Path,
    case_name: str,
    failures: list[str],
    cleanup_report: dict[str, Any] | None = None,
    headless_player: bool = False,
    extra: dict[str, Any] | None = None,
) -> dict[str, Any]:
    cleanup_report = cleanup_report or {
        "cleanupStatus": "UNKNOWN",
        "cleanupSuccess": False,
        "orphanedPids": [],
    }
    cleanup_status = cleanup_report.get("cleanupStatus")
    cleanup_success = cleanup_report.get("cleanupSuccess") is True
    orphaned_pids = cleanup_report.get("orphanedPids") if isinstance(cleanup_report.get("orphanedPids"), list) else []
    functional_success = not failures
    result = {
        "case": case_name,
        "artifactDir": str(artifact_dir),
        "success": functional_success and cleanup_status == "PASS" and cleanup_success and orphaned_pids == [],
        "functionalSuccess": functional_success,
        "cleanupStatus": cleanup_status if isinstance(cleanup_status, str) else "UNKNOWN",
        "cleanupSuccess": cleanup_success,
        "cleanupReportPath": "cleanup-report.json" if (artifact_dir / "cleanup-report.json").exists() else None,
        "orphanedPids": orphaned_pids,
        "headlessPlayer": headless_player,
        "failures": failures,
    }
    if extra:
        result.update(extra)
    write_json(artifact_dir / "result.json", result)
    return result


def snapshot_not_ready_reasons(data: Any, expected_players: int, scene: str) -> list[str]:
    state = normalize_snapshot_response(data)
    if not isinstance(state, dict):
        return ["snapshot_not_dict"]

    reasons: list[str] = []
    if not scene_matches(state.get("scene"), scene):
        reasons.append(f"scene expected={scene} actual={state.get('scene')}")

    players = state.get("players") or []
    if len(players) != expected_players:
        reasons.append(f"players.count expected={expected_players} actual={len(players)}")

    game = state.get("game") or {}
    if game.get("hasGameManagers") is not True:
        reasons.append("game.hasGameManagers_not_true")
    if game.get("isSequenceTransitioning") is True:
        reasons.append(
            "game.sequenceTransitioning "
            f"{game.get('transitionFromState')}->{game.get('transitionToState')} "
            f"remaining={game.get('sequenceTransitionRemaining')}"
        )

    for index, player in enumerate(players):
        player_id = player.get("playerId", index) if isinstance(player, dict) else index
        field = player.get("field") if isinstance(player, dict) else None
        if not isinstance(field, dict):
            reasons.append(f"player.{player_id}.field_missing")
            continue
        if field.get("ready") is not True:
            reasons.append(f"player.{player_id}.field.ready_not_true")
        if field.get("wallHash") in (None, "", "unknown"):
            reasons.append(f"player.{player_id}.field.wallHash_unknown")

    return reasons


def snapshot_ready(data: Any, expected_players: int, scene: str) -> bool:
    return not snapshot_not_ready_reasons(data, expected_players, scene)


def session_not_ready_reasons(data: Any, expected_players: int, scene: str) -> list[str]:
    state = normalize_snapshot_response(data)
    if not isinstance(state, dict):
        return ["snapshot_not_dict"]

    reasons: list[str] = []
    if not scene_matches(state.get("scene"), scene):
        reasons.append(f"scene expected={scene} actual={state.get('scene')}")

    runner = state.get("runner") or {}
    if runner.get("isRunning") is not True:
        reasons.append("runner.isRunning_not_true")
    if runner.get("activePlayerCount") != expected_players:
        reasons.append(f"runner.activePlayerCount expected={expected_players} actual={runner.get('activePlayerCount')}")

    return reasons


def session_ready(data: Any, expected_players: int, scene: str) -> bool:
    return not session_not_ready_reasons(data, expected_players, scene)


def wait_build_peer_started(
    start_action: Any,
    artifact_dir: pathlib.Path,
    label: str,
    session: str,
    scene: str,
    max_players: int,
    timeout_seconds: int,
) -> bool:
    deadline = time.time() + timeout_seconds
    latest: Any = {}
    while time.time() < deadline:
        try:
            latest = start_action(session=session, scene=scene, maxPlayers=max_players)
        except Exception as exc:
            latest = {"success": False, "error": {"code": type(exc).__name__}}
        write_json(artifact_dir / f"{label}-start-latest.json", latest)
        if isinstance(latest, dict) and latest.get("success") is True:
            state = normalize_snapshot_response(latest)
            runner = state.get("runner") if isinstance(state, dict) else None
            if isinstance(runner, dict) and runner.get("isRunning") is True:
                return True
        time.sleep(1)

    write_json(artifact_dir / f"{label}-start-timeout.json", latest)
    return False


def failure_summary(path: pathlib.Path, title: str, details: list[str]) -> None:
    text = "# " + title + "\n\n" + "\n".join(f"- {line}" for line in details) + "\n"
    path.write_text(text, encoding="utf-8")
