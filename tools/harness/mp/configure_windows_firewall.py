#!/usr/bin/env python3
"""Create a Windows Firewall rule for the MDF MP test player.

The default rule blocks inbound traffic for the selected player exe. This is
usually enough to stop the Windows Defender Firewall prompt without opening
public inbound access. Use --action allow only when a direct LAN/P2P test
explicitly needs inbound connections.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import json
import os
import pathlib
import subprocess
import sys
from typing import Any

from common import ROOT, latest_player_path


def is_windows() -> bool:
    return os.name == "nt"


def is_admin() -> bool:
    if not is_windows():
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def path_hash(path: pathlib.Path) -> str:
    return hashlib.sha256(str(path).lower().encode("utf-8")).hexdigest()[:8]


def normalize_profiles(values: list[str]) -> str:
    if not values:
        return "any"
    parts: list[str] = []
    for value in values:
        for part in value.split(","):
            cleaned = part.strip().lower()
            if cleaned:
                parts.append(cleaned)
    allowed = {"domain", "private", "public", "any"}
    invalid = [part for part in parts if part not in allowed]
    if invalid:
        raise ValueError(f"Invalid profile(s): {', '.join(invalid)}")
    if "any" in parts:
        return "any"
    deduped: list[str] = []
    for part in parts:
        if part not in deduped:
            deduped.append(part)
    return ",".join(deduped) if deduped else "any"


def resolve_player_path(value: str | None) -> pathlib.Path:
    if value:
        path = pathlib.Path(value)
        if not path.is_absolute():
            path = (ROOT / path).resolve()
        return path
    latest = latest_player_path()
    if latest is None:
        raise FileNotFoundError(
            "No Development player found. Build one first or pass --player-path."
        )
    return latest.resolve()


def build_netsh_commands(player_path: pathlib.Path, action: str, profiles: str) -> tuple[str, list[list[str]]]:
    rule_name = f"MDF MPTest {action} inbound {path_hash(player_path)}"
    delete_cmd = [
        "netsh",
        "advfirewall",
        "firewall",
        "delete",
        "rule",
        f"name={rule_name}",
        f"program={player_path}",
    ]
    add_cmd = [
        "netsh",
        "advfirewall",
        "firewall",
        "add",
        "rule",
        f"name={rule_name}",
        "dir=in",
        f"action={action}",
        f"program={player_path}",
        "enable=yes",
        f"profile={profiles}",
    ]
    return rule_name, [delete_cmd, add_cmd]


def run_commands(commands: list[list[str]]) -> list[dict[str, Any]]:
    results: list[dict[str, Any]] = []
    for command in commands:
        proc = subprocess.run(
            command,
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
        )
        results.append(
            {
                "command": command,
                "returnCode": proc.returncode,
                "stdout": proc.stdout.strip(),
                "stderr": proc.stderr.strip(),
            }
        )
        if proc.returncode != 0 and command[3] != "delete":
            break
    return results


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path", help="Path to MDF-MPTest.exe. Defaults to the latest Development player.")
    parser.add_argument(
        "--action",
        choices=["block", "allow"],
        default="block",
        help="Firewall action. Default blocks inbound to suppress prompts without opening access.",
    )
    parser.add_argument(
        "--profile",
        action="append",
        default=[],
        help="Firewall profile: domain, private, public, any, or comma-separated values. Default: any.",
    )
    parser.add_argument("--apply", action="store_true", help="Actually modify Windows Firewall rules.")
    parser.add_argument("--allow-missing-player", action="store_true", help="Allow planning a rule before the exe exists.")
    args = parser.parse_args()

    result: dict[str, Any] = {
        "success": False,
        "applied": False,
        "platform": os.name,
        "action": args.action,
    }

    if not is_windows():
        result.update(
            {
                "success": True,
                "skipped": True,
                "reason": "Windows Firewall rules are only needed on Windows.",
            }
        )
        print(json.dumps(result, indent=2))
        return 0

    try:
        player_path = resolve_player_path(args.player_path)
        profiles = normalize_profiles(args.profile)
    except Exception as exc:
        result.update({"error": str(exc)})
        print(json.dumps(result, indent=2), file=sys.stderr)
        return 2

    if not player_path.exists() and not args.allow_missing_player:
        result.update(
            {
                "playerPath": str(player_path),
                "error": "Player exe does not exist. Build it first or pass --allow-missing-player for dry-run planning.",
            }
        )
        print(json.dumps(result, indent=2), file=sys.stderr)
        return 2

    rule_name, commands = build_netsh_commands(player_path, args.action, profiles)
    result.update(
        {
            "success": True,
            "playerPath": str(player_path),
            "ruleName": rule_name,
            "profiles": profiles,
            "commands": commands,
            "note": (
                "Default block inbound suppresses the Windows Defender prompt without allowing public inbound access. "
                "Use a stable build path so the same rule continues to match future visual MP test runs."
            ),
        }
    )

    if not args.apply:
        print(json.dumps(result, indent=2))
        return 0

    if not is_admin():
        result.update(
            {
                "success": False,
                "needsAdmin": True,
                "error": "Run this command from an elevated Administrator shell to modify Windows Firewall.",
            }
        )
        print(json.dumps(result, indent=2), file=sys.stderr)
        return 5

    command_results = run_commands(commands)
    add_result = command_results[-1] if command_results else {}
    applied = add_result.get("returnCode") == 0
    result.update(
        {
            "success": applied,
            "applied": applied,
            "commandResults": command_results,
        }
    )
    print(json.dumps(result, indent=2))
    return 0 if applied else 1


if __name__ == "__main__":
    raise SystemExit(main())
