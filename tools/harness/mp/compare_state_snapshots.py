#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
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
    compare_equal(errors, "game.battlePhase", lg.get("battlePhase", "None"), rg.get("battlePhase", "None"))
    compare_equal(errors, "game.currentRound", lg.get("currentRound"), rg.get("currentRound"))
    in_battle = is_battle_phase(lg.get("battlePhase")) or is_battle_phase(rg.get("battlePhase"))
    compare_known_or_required(errors, "game.battleOpponentsHash", lg.get("battleOpponentsHash"), rg.get("battleOpponentsHash"), required=in_battle)
    compare_known_or_required(errors, "game.matchFirstAttackerHash", lg.get("matchFirstAttackerHash"), rg.get("matchFirstAttackerHash"), required=in_battle)
    compare_known_or_required(errors, "game.battleActiveHash", lg.get("battleActiveHash"), rg.get("battleActiveHash"), required=in_battle)
    compare_presence_count(errors, "game.survivorBossPendingCount", lg.get("survivorBossPendingCount"), rg.get("survivorBossPendingCount"))
    compare_presence_count(errors, "game.survivorBossAssignmentCount", lg.get("survivorBossAssignmentCount"), rg.get("survivorBossAssignmentCount"))
    compare_known_or_required(
        errors,
        "game.survivorBossPendingHash",
        lg.get("survivorBossPendingHash"),
        rg.get("survivorBossPendingHash"),
        required=count_positive(lg.get("survivorBossPendingCount")) or count_positive(rg.get("survivorBossPendingCount")),
    )
    compare_known_or_required(
        errors,
        "game.survivorBossAssignmentHash",
        lg.get("survivorBossAssignmentHash"),
        rg.get("survivorBossAssignmentHash"),
        required=count_positive(lg.get("survivorBossAssignmentCount")) or count_positive(rg.get("survivorBossAssignmentCount")),
    )

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
        compare_known_or_missing(errors, f"player.{player_id}.attackMonsterPoolHash", lp.get("attackMonsterPoolHash"), rp.get("attackMonsterPoolHash"))
        compare_known_or_missing(errors, f"player.{player_id}.ownedScrollsHash", lp.get("ownedScrollsHash"), rp.get("ownedScrollsHash"))
        compare_equal(errors, f"player.{player_id}.ownedScrollRevision", lp.get("ownedScrollRevision"), rp.get("ownedScrollRevision"))
        compare_known_or_missing(errors, f"player.{player_id}.manualSkillReadyHash", lp.get("manualSkillReadyHash"), rp.get("manualSkillReadyHash"))
        compare_equal(errors, f"player.{player_id}.isActivelyFighting", lp.get("isActivelyFighting"), rp.get("isActivelyFighting"))
        compare_equal(errors, f"player.{player_id}.isAttackerInCurrentBattle", lp.get("isAttackerInCurrentBattle"), rp.get("isAttackerInCurrentBattle"))
        compare_known(errors, f"player.{player_id}.shop.itemsHash", nested(lp, "shop", "itemsHash"), nested(rp, "shop", "itemsHash"))
        compare_equal(errors, f"player.{player_id}.augment.available", nested(lp, "augment", "available"), nested(rp, "augment", "available"))
        compare_equal(errors, f"player.{player_id}.augment.selectedCount", nested(lp, "augment", "selectedCount"), nested(rp, "augment", "selectedCount"))
        compare_equal(errors, f"player.{player_id}.augment.presentedCount", nested(lp, "augment", "presentedCount"), nested(rp, "augment", "presentedCount"))
        compare_known(errors, f"player.{player_id}.augment.presentedHash", nested(lp, "augment", "presentedHash"), nested(rp, "augment", "presentedHash"))
        compare_known(errors, f"player.{player_id}.augment.selectedHash", nested(lp, "augment", "selectedHash"), nested(rp, "augment", "selectedHash"))
        compare_equal(errors, f"player.{player_id}.augment.activeEffectCount", nested(lp, "augment", "activeEffectCount"), nested(rp, "augment", "activeEffectCount"))
        compare_equal(errors, f"player.{player_id}.augment.activeTargetCount", nested(lp, "augment", "activeTargetCount"), nested(rp, "augment", "activeTargetCount"))
        compare_known(errors, f"player.{player_id}.augment.activeEffectHash", nested(lp, "augment", "activeEffectHash"), nested(rp, "augment", "activeEffectHash"))
        compare_known(errors, f"player.{player_id}.augment.activeTargetHash", nested(lp, "augment", "activeTargetHash"), nested(rp, "augment", "activeTargetHash"))
        compare_known(errors, f"player.{player_id}.field.gridHash", nested(lp, "field", "gridHash"), nested(rp, "field", "gridHash"))
        compare_equal(errors, f"player.{player_id}.field.aliveUnitCount", nested(lp, "field", "aliveUnitCount"), nested(rp, "field", "aliveUnitCount"))
        compare_equal(errors, f"player.{player_id}.field.deadUnitCount", nested(lp, "field", "deadUnitCount"), nested(rp, "field", "deadUnitCount"))
        compare_known(errors, f"player.{player_id}.field.deadUnitsHash", nested(lp, "field", "deadUnitsHash"), nested(rp, "field", "deadUnitsHash"))
        compare_known(errors, f"player.{player_id}.field.placedUnitsHash", nested(lp, "field", "placedUnitsHash"), nested(rp, "field", "placedUnitsHash"))
        compare_known(errors, f"player.{player_id}.field.wallHash", nested(lp, "field", "wallHash"), nested(rp, "field", "wallHash"))
        compare_equal(errors, f"player.{player_id}.monsters.aliveCount", nested(lp, "monsters", "aliveCount"), nested(rp, "monsters", "aliveCount"))
        compare_known(errors, f"player.{player_id}.monsters.livingHash", nested(lp, "monsters", "livingHash"), nested(rp, "monsters", "livingHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.typeHash", nested(lp, "monsters", "typeHash"), nested(rp, "monsters", "typeHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.typeCountHpHash", nested(lp, "monsters", "typeCountHpHash"), nested(rp, "monsters", "typeCountHpHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.ownerOriginHash", nested(lp, "monsters", "ownerOriginHash"), nested(rp, "monsters", "ownerOriginHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.targetPlayerHash", nested(lp, "monsters", "targetPlayerHash"), nested(rp, "monsters", "targetPlayerHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.hpBucketHash", nested(lp, "monsters", "hpBucketHash"), nested(rp, "monsters", "hpBucketHash"))
        compare_known_or_missing(errors, f"player.{player_id}.monsters.bossPoolIdentityHash", nested(lp, "monsters", "bossPoolIdentityHash"), nested(rp, "monsters", "bossPoolIdentityHash"))

    for side, snapshot in (("left", left), ("right", right)):
        if snapshot.get("errors"):
            warnings.extend(f"{side}.snapshot_error {err}" for err in snapshot["errors"])

    lc = left.get("commands") or {}
    rc = right.get("commands") or {}
    compare_nullable(errors, "commands.lastSequence", lc.get("lastSequence"), rc.get("lastSequence"))
    compare_nullable(errors, "commands.queueDepth", lc.get("queueDepth"), rc.get("queueDepth"))
    has_command_counters = has_any_command_counter(lc) or has_any_command_counter(rc)
    compare_known_or_required(errors, "commands.lastCommand", lc.get("lastCommand"), rc.get("lastCommand"), required=has_command_counters)
    compare_equal(errors, "commands.acceptedBattleCommandSeq", lc.get("acceptedBattleCommandSeq", 0), rc.get("acceptedBattleCommandSeq", 0))
    compare_equal(errors, "commands.spawnMonsterSeq", lc.get("spawnMonsterSeq", 0), rc.get("spawnMonsterSeq", 0))
    compare_equal(errors, "commands.useMagicScrollSeq", lc.get("useMagicScrollSeq", 0), rc.get("useMagicScrollSeq", 0))
    compare_equal(errors, "commands.activateSkillSeq", lc.get("activateSkillSeq", 0), rc.get("activateSkillSeq", 0))
    compare_equal(errors, "commands.rejectedBattleCommandCount", lc.get("rejectedBattleCommandCount", 0), rc.get("rejectedBattleCommandCount", 0))

    le = normalize_effects(left.get("effects"))
    re = normalize_effects(right.get("effects"))
    compare_equal(errors, "effects.activeBuffCount", le.get("activeBuffCount"), re.get("activeBuffCount"))
    compare_equal(errors, "effects.activeStatusCount", le.get("activeStatusCount"), re.get("activeStatusCount"))
    compare_equal(errors, "effects.zoneCount", le.get("zoneCount"), re.get("zoneCount"))
    compare_known_or_missing(errors, "effects.activeBuffHash", le.get("activeBuffHash"), re.get("activeBuffHash"))
    compare_known_or_missing(errors, "effects.activeStatusHash", le.get("activeStatusHash"), re.get("activeStatusHash"))
    compare_known_or_missing(errors, "effects.zoneHash", le.get("zoneHash"), re.get("zoneHash"))

    return {"success": not errors, "errors": errors, "warnings": warnings}


def nested(obj: dict[str, Any], *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def is_battle_phase(value: Any) -> bool:
    return value in ("Battle1", "Battle2")


def count_positive(value: Any) -> bool:
    return isinstance(value, int) and value > 0


def has_any_command_counter(commands: dict[str, Any]) -> bool:
    return any(
        count_positive(commands.get(key))
        for key in (
            "acceptedBattleCommandSeq",
            "spawnMonsterSeq",
            "useMagicScrollSeq",
            "activateSkillSeq",
            "rejectedBattleCommandCount",
        )
    )


def normalize_effects(obj: Any) -> dict[str, Any]:
    if isinstance(obj, dict):
        return {
            "activeBuffCount": obj.get("activeBuffCount", 0),
            "activeStatusCount": obj.get("activeStatusCount", 0),
            "zoneCount": obj.get("zoneCount", 0),
            "activeBuffHash": obj.get("activeBuffHash", UNKNOWN),
            "activeStatusHash": obj.get("activeStatusHash", UNKNOWN),
            "zoneHash": obj.get("zoneHash", UNKNOWN),
        }
    return {
        "activeBuffCount": 0,
        "activeStatusCount": 0,
        "zoneCount": 0,
        "activeBuffHash": UNKNOWN,
        "activeStatusHash": UNKNOWN,
        "zoneHash": UNKNOWN,
    }


def compare_equal(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_known(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left in (None, "", UNKNOWN) or right in (None, "", UNKNOWN):
        return
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_known_or_missing(errors: list[str], field: str, left: Any, right: Any) -> None:
    left_unknown = left in (None, "", UNKNOWN)
    right_unknown = right in (None, "", UNKNOWN)
    if left_unknown and right_unknown:
        return
    if left_unknown or right_unknown:
        errors.append(f"{field} left={left if not left_unknown else 'unknown'} right={right if not right_unknown else 'unknown'}")
        return
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_known_or_required(errors: list[str], field: str, left: Any, right: Any, *, required: bool) -> None:
    if required:
        compare_required_known(errors, field, left, right)
        return
    compare_known(errors, field, left, right)


def compare_required_known(errors: list[str], field: str, left: Any, right: Any) -> None:
    left_unknown = left in (None, "", UNKNOWN)
    right_unknown = right in (None, "", UNKNOWN)
    if left_unknown or right_unknown:
        errors.append(f"{field} left={left if not left_unknown else 'unknown'} right={right if not right_unknown else 'unknown'}")
        return
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_nullable(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left is None or right is None:
        return
    if left != right:
        errors.append(f"{field} left={left} right={right}")


def compare_presence_count(errors: list[str], field: str, left: Any, right: Any) -> None:
    if left is not None and right is not None:
        compare_equal(errors, field, left, right)
        return
    left_count = left if isinstance(left, int) else 0
    right_count = right if isinstance(right, int) else 0
    if left_count > 0 or right_count > 0:
        errors.append(f"{field} left={left if left is not None else 'unknown'} right={right if right is not None else 'unknown'}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("left")
    parser.add_argument("right")
    parser.add_argument("--output")
    args = parser.parse_args()
    result = compare_snapshots(read_json(Path(args.left)), read_json(Path(args.right)))
    if args.output:
        write_json(Path(args.output), result)
    print(json.dumps(result, indent=2))
    return 0 if result["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
