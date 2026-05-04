#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
from dataclasses import dataclass

from common import ROOT, hash_for_log


@dataclass
class PlayerProcess:
    process: subprocess.Popen
    stdout_path: pathlib.Path
    stderr_path: pathlib.Path
    command_path: pathlib.Path
    stdout_handle: object
    stderr_handle: object

    def terminate(self, timeout: float = 10.0) -> None:
        try:
            if self.process.poll() is None:
                self.process.terminate()
                try:
                    self.process.wait(timeout=timeout)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=timeout)
        finally:
            self.close_logs()

    def close_logs(self) -> None:
        for handle in (self.stdout_handle, self.stderr_handle):
            try:
                handle.close()
            except Exception:
                pass


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
) -> PlayerProcess:
    stdout_path = artifact_dir / f"{peer_name}.stdout.log"
    stderr_path = artifact_dir / f"{peer_name}.stderr.log"
    command_path = artifact_dir / f"{peer_name}.command.json"
    stdout_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        str(player_path),
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
            },
            indent=2,
        ),
        encoding="utf-8",
    )

    stdout = stdout_path.open("w", encoding="utf-8")
    stderr = stderr_path.open("w", encoding="utf-8")
    process = subprocess.Popen(cmd, cwd=ROOT, stdout=stdout, stderr=stderr)
    return PlayerProcess(
        process=process,
        stdout_path=stdout_path,
        stderr_path=stderr_path,
        command_path=command_path,
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
