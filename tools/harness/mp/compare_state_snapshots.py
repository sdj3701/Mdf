#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from typing import Any

from common import normalize_snapshot_response, read_json, write_json


UNKNOWN = "unknown"


def compare_snapshots(left: dict[str, Any], right: dict[str, Any]) -> dict[str, Any]:
    left = normalize_snapshot_response(left)
    right = normalize_snapshot_response(right)
    errors: list[str] = []
    warnings: list[str] = []

    compare_equal(errors, "session", left.get("session"), right.get("session"))
    compare_equal(errors, "scene", left.get("scene"), right.get("scene"))

    lg = left.get("game") or {}
    rg = right.get("game") or {}
    compare_equal(errors, "game.currentState", lg.get("currentState"), rg.get("currentState"))
    compare_equal(errors, "game.currentRound", lg.get("currentRound"), rg.get("currentRound"))
    compare_known(errors, "game.battleOpponentsHash", lg.get("battleOpponentsHash"), rg.get("battleOpponentsHash"))
    compare_known(errors, "game.matchFirstAttackerHash", lg.get("matchFirstAttackerHash"), rg.get("matchFirstAttackerHash"))

    left_players = sorted(left.get("players") or [], key=lambda p: p.get("playerId", 9999))
    right_players = sorted(right.get("players") or [], key=lambda p: p.get("playerId", 9999))
    compare_equal(errors, "players.count", len(left_players), len(right_players))
    right_by_id = {p.get("playerId"): p for p in right_players}
    for lp in left_players:
        player_id = lp.get("playerId")
        rp = right_by_id.get(player_id)
        if rp is None:
            errors.append(f"player.{player_id}.missing")
            continue
        compare_equal(errors, f"player.{player_id}.health", lp.get("health"), rp.get("health"))
        compare_equal(errors, f"player.{player_id}.gold", lp.get("gold"), rp.get("gold"))
        compare_equal(errors, f"player.{player_id}.wallCount", lp.get("wallCount"), rp.get("wallCount"))
        compare_known(errors, f"player.{player_id}.shop.itemsHash", nested(lp, "shop", "itemsHash"), nested(rp, "shop", "itemsHash"))
        compare_known(errors, f"player.{player_id}.field.gridHash", nested(lp, "field", "gridHash"), nested(rp, "field", "gridHash"))
        compare_known(errors, f"player.{player_id}.field.placedUnitsHash", nested(lp, "field", "placedUnitsHash"), nested(rp, "field", "placedUnitsHash"))
        compare_known(errors, f"player.{player_id}.field.wallHash", nested(lp, "field", "wallHash"), nested(rp, "field", "wallHash"))
        compare_equal(errors, f"player.{player_id}.monsters.aliveCount", nested(lp, "monsters", "aliveCount"), nested(rp, "monsters", "aliveCount"))
        compare_known(errors, f"player.{player_id}.monsters.livingHash", nested(lp, "monsters", "livingHash"), nested(rp, "monsters", "livingHash"))

    for side, snapshot in (("left", left), ("right", right)):
        if snapshot.get("errors"):
            warnings.extend(f"{side}.snapshot_error {err}" for err in snapshot["errors"])

    return {"success": not errors, "errors": errors, "warnings": warnings}


def nested(obj: dict[str, Any], *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def compare_equal(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_known(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left in (None, "", UNKNOWN) or right in (None, "", UNKNOWN):
        return
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("left")
    parser.add_argument("right")
    parser.add_argument("--output")
    args = parser.parse_args()
    result = compare_snapshots(read_json(args.left), read_json(args.right))
    if args.output:
        write_json(args.output, result)
    print(json.dumps(result, indent=2))
    return 0 if result["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
