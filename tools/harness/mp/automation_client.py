#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import time
import urllib.error
import urllib.request
from typing import Any


class AutomationClient:
    def __init__(self, port: int, token: str, host: str = "127.0.0.1", timeout: float = 5.0):
        self.base_url = f"http://{host}:{port}"
        self.token = token
        self.timeout = timeout

    def request(self, method: str, path: str, data: dict[str, Any] | None = None) -> dict[str, Any]:
        body = None
        headers = {
            "Authorization": f"Bearer {self.token}",
            "X-MPTest-Token": self.token,
        }
        if data is not None:
            body = json.dumps(data).encode("utf-8")
            headers["Content-Type"] = "application/json"
        req = urllib.request.Request(self.base_url + path, data=body, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as resp:
                return json.loads(resp.read().decode("utf-8", errors="replace"))
        except urllib.error.HTTPError as exc:
            payload = exc.read().decode("utf-8", errors="replace")
            try:
                return json.loads(payload)
            except Exception:
                return {"success": False, "message": payload, "error": {"code": f"http_{exc.code}"}}

    def wait_ping(self, timeout_seconds: float = 30.0, interval: float = 0.5) -> dict[str, Any]:
        deadline = time.time() + timeout_seconds
        last: dict[str, Any] = {"success": False, "error": {"code": "automation_ping_timeout"}}
        while time.time() < deadline:
            try:
                last = self.ping()
                if last.get("success"):
                    return last
            except Exception as exc:
                last = {"success": False, "error": {"code": "ping_exception", "details": type(exc).__name__}}
            time.sleep(interval)
        return last

    def ping(self) -> dict[str, Any]:
        return self.request("GET", "/ping")

    def dump_state(self) -> dict[str, Any]:
        return self.request("GET", "/dumpState")

    def logs_recent(self) -> dict[str, Any]:
        return self.request("GET", "/logs/recent")

    def quit(self) -> dict[str, Any]:
        return self.request("POST", "/quit", {})

    def start_host(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/startHost", kwargs)

    def join(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/join", kwargs)

    def load_game(self, scene: str = "Game") -> dict[str, Any]:
        return self.request("POST", "/loadGame", {"scene": scene})

    def command(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/command", kwargs)

    def bot_start(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/bot/start", kwargs)

    def bot_stop(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/bot/stop", kwargs)

    def bot_status(self) -> dict[str, Any]:
        return self.request("GET", "/bot/status")

    def bot_journal(self) -> dict[str, Any]:
        return self.request("GET", "/bot/journal")

    def assert_state(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/assertState", kwargs)

    def freeze_game_flow(self, enabled: bool = True, reason: str = "automation") -> dict[str, Any]:
        return self.request("POST", "/test/freezeGameFlow", {"enabled": enabled, "reason": reason})

    def apply_status_effect(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/applyStatusEffect", kwargs)

    def apply_stat_buff(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/applyStatBuff", kwargs)

    def apply_zone(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/applyZone", kwargs)

    def inject_pending_combat_load(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/injectPendingCombatLoad", kwargs)

    def performance_stress(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/performanceStress", kwargs)

    def projectile_expiry_stress(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/projectileExpiryStress", kwargs)

    def destroy_wall_under_load(self, **kwargs: Any) -> dict[str, Any]:
        return self.request("POST", "/test/destroyWallUnderLoad", kwargs)

    def screenshot(self, path: str | None = None) -> dict[str, Any]:
        suffix = "" if not path else "?path=" + urllib.request.pathname2url(path)
        return self.request("GET", "/screenshot" + suffix)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("endpoint", choices=["ping", "dumpState", "logs"])
    args = parser.parse_args()
    client = AutomationClient(args.port, args.token)
    if args.endpoint == "ping":
        data = client.ping()
    elif args.endpoint == "dumpState":
        data = client.dump_state()
    else:
        data = client.logs_recent()
    print(json.dumps(data, indent=2))
    return 0 if data.get("success", False) else 1


if __name__ == "__main__":
    raise SystemExit(main())
