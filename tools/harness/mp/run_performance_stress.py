#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time

from automation_client import AutomationClient


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the MPTest-only 60+ moving ground-monster load on an existing authority peer.")
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--monster-count", type=int, default=60)
    parser.add_argument("--hold-seconds", type=float, default=15.0)
    parser.add_argument("--attacker-player-id", type=int, default=-1)
    parser.add_argument("--target-player-id", type=int, default=-1)
    parser.add_argument("--monster-data-key")
    parser.add_argument("--wait-battle-seconds", type=float, default=180.0)
    parser.add_argument("--timeout-seconds", type=float, default=180.0)
    parser.add_argument("--output")
    args = parser.parse_args()

    client = AutomationClient(args.port, args.token, timeout=10.0)
    deadline = time.time() + args.wait_battle_seconds
    while time.time() < deadline:
        snapshot = client.dump_state()
        data = snapshot.get("data") or {}
        state = (data.get("game") or {}).get("currentState")
        if state in ("Battle1", "Battle2"):
            break
        time.sleep(0.5)
    else:
        print(json.dumps({"success": False, "error": "battle_wait_timeout"}, indent=2))
        return 2

    request = {
        "action": "start",
        "monsterCount": args.monster_count,
        "holdSeconds": args.hold_seconds,
        "attackerPlayerId": args.attacker_player_id,
        "targetPlayerId": args.target_player_id,
    }
    if args.monster_data_key:
        request["monsterDataKey"] = args.monster_data_key
    started = client.performance_stress(**request)
    if started.get("success") is not True:
        print(json.dumps(started, indent=2))
        return 2

    deadline = time.time() + args.timeout_seconds
    status = started
    while time.time() < deadline:
        status = client.performance_stress(action="status")
        phase = ((status.get("data") or {}).get("phase"))
        if phase in ("completed", "failed", "cancelled"):
            break
        time.sleep(0.5)
    else:
        status = client.performance_stress(action="stop", reason="probe_timeout")

    if args.output:
        output = pathlib.Path(args.output)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(status, indent=2), encoding="utf-8")
    print(json.dumps(status, indent=2))
    payload = status.get("data") or {}
    return 0 if status.get("success") is True and payload.get("phase") == "completed" and payload.get("success") is True else 1


if __name__ == "__main__":
    raise SystemExit(main())
