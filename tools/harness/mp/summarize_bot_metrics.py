#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import re
from collections import Counter, defaultdict
from typing import Any

from common import read_json, write_json


SCHEMA_VERSION = 1
BATTLE_PHASES = {"Battle1", "Battle2"}
OBSERVE_COMMANDS = {"", "Observe", "observe", None}


def read_json_if_exists(path: pathlib.Path) -> Any:
    if not path.exists():
        return None
    try:
        return read_json(path)
    except Exception as exc:
        return {"readError": f"{type(exc).__name__}: {exc}"}


def iter_jsonl(path: pathlib.Path) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            parsed = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(parsed, dict):
            rows.append(parsed)
    return rows


def normalize_snapshot(data: Any) -> dict[str, Any]:
    if isinstance(data, dict) and data.get("success") is True and isinstance(data.get("data"), dict):
        inner = data["data"]
        if isinstance(inner, dict) and isinstance(inner.get("snapshot"), dict):
            return inner["snapshot"]
        return inner
    return data if isinstance(data, dict) else {}


def to_int(value: Any, default: int = 0) -> int:
    if isinstance(value, bool):
        return int(value)
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        return int(value)
    if isinstance(value, str):
        try:
            return int(float(value))
        except ValueError:
            return default
    return default


def known_command(value: Any) -> str | None:
    if value in OBSERVE_COMMANDS:
        return None
    text = str(value).strip()
    return text if text and text not in OBSERVE_COMMANDS else None


def counter_to_dict(counter: Counter[str]) -> dict[str, int]:
    return {key: counter[key] for key in sorted(counter)}


def find_journals(artifact_dir: pathlib.Path) -> list[pathlib.Path]:
    return sorted(path for path in artifact_dir.glob("*bot*.jsonl") if path.is_file())


def load_journal_entries(artifact_dir: pathlib.Path) -> tuple[list[dict[str, Any]], list[str]]:
    entries: list[dict[str, Any]] = []
    paths: list[str] = []
    for path in find_journals(artifact_dir):
        rows = iter_jsonl(path)
        if rows:
            entries.extend(rows)
            paths.append(str(path))
    return entries, paths


def load_timeline(artifact_dir: pathlib.Path) -> tuple[list[dict[str, Any]], str | None]:
    path = artifact_dir / "mptest.timeline.jsonl"
    rows = iter_jsonl(path)
    return rows, str(path) if rows else None


def snapshot_candidates(artifact_dir: pathlib.Path) -> list[dict[str, Any]]:
    names = [
        "snapshots/build-host-prepare-progressed.json",
        "snapshots/build-client-prepare-progressed.json",
        "snapshots/build-host-after-first-accepted.json",
        "snapshots/build-client-after-first-accepted.json",
        "snapshots/build-host-before-bot.json",
        "snapshots/build-host-before-battle-bot.json",
        "snapshots/build-host-battle-progressed.json",
        "snapshots/build-host-battle-checkpoint-frozen.json",
        "snapshots/build-host-post-reconnect.json",
        "snapshots/build-client-post-host-migration.json",
        "snapshots/build-client-battle-progressed.json",
    ]
    snapshots: list[dict[str, Any]] = []
    for name in names:
        data = read_json_if_exists(artifact_dir / name)
        normalized = normalize_snapshot(data)
        if normalized:
            snapshots.append(normalized)
    return snapshots


def player_gold(snapshot: dict[str, Any], player_id: int | None) -> int | None:
    if player_id is None:
        return None
    players = snapshot.get("players")
    if not isinstance(players, list):
        return None
    for player in players:
        if isinstance(player, dict) and player.get("playerId") == player_id:
            gold = player.get("gold")
            return to_int(gold) if gold is not None else None
    return None


def max_round_from_snapshots(snapshots: list[dict[str, Any]]) -> int:
    maximum = 0
    for snapshot in snapshots:
        game = snapshot.get("game") if isinstance(snapshot.get("game"), dict) else {}
        maximum = max(maximum, to_int(game.get("currentRound")))
    return maximum


def battle_from_snapshots(snapshots: list[dict[str, Any]]) -> bool:
    for snapshot in snapshots:
        game = snapshot.get("game") if isinstance(snapshot.get("game"), dict) else {}
        if game.get("currentState") in BATTLE_PHASES or game.get("battlePhase") in BATTLE_PHASES:
            return True
    return False


def command_counters_from_evidence(artifact_dir: pathlib.Path) -> dict[str, int]:
    evidence = read_json_if_exists(artifact_dir / "battle-command-evidence.json")
    commands = evidence.get("commands") if isinstance(evidence, dict) and isinstance(evidence.get("commands"), dict) else {}
    return {
        "acceptedBattleCommandSeq": to_int(commands.get("acceptedBattleCommandSeq")),
        "spawnMonsterSeq": to_int(commands.get("spawnMonsterSeq")),
        "useMagicScrollSeq": to_int(commands.get("useMagicScrollSeq")),
        "activateSkillSeq": to_int(commands.get("activateSkillSeq")),
        "rejectedBattleCommandCount": to_int(commands.get("rejectedBattleCommandCount")),
    }


def gold_spent_from_journal(entries: list[dict[str, Any]]) -> int:
    last_gold_by_player: dict[int, int] = {}
    spent = 0
    for entry in entries:
        if entry.get("kind") != "bot_decision":
            continue
        player_id = entry.get("playerId")
        if not isinstance(player_id, int):
            continue
        observed = entry.get("observed") if isinstance(entry.get("observed"), dict) else {}
        if "gold" not in observed:
            continue
        gold = to_int(observed.get("gold"), -1)
        if gold < 0:
            continue
        previous = last_gold_by_player.get(player_id)
        if previous is not None and gold < previous:
            spent += previous - gold
        last_gold_by_player[player_id] = gold
    return spent


def gold_spent_from_snapshots(snapshots: list[dict[str, Any]], player_id: int | None) -> int:
    if len(snapshots) < 2 or player_id is None:
        return 0
    first = player_gold(snapshots[0], player_id)
    last = player_gold(snapshots[-1], player_id)
    if first is None or last is None:
        return 0
    return max(0, first - last)


def parse_spawned_count(msg: Any) -> int:
    if not isinstance(msg, str):
        return 0
    match = re.search(r"spawned=(\d+)", msg)
    return int(match.group(1)) if match else 0


def summarize_artifact(artifact_dir: pathlib.Path) -> dict[str, Any]:
    artifact_dir = artifact_dir.resolve()
    run = read_json_if_exists(artifact_dir / "run.json") or {}
    result = read_json_if_exists(artifact_dir / "result.json") or {}
    evidence = read_json_if_exists(artifact_dir / "battle-command-evidence.json") or {}
    journal_entries, journal_paths = load_journal_entries(artifact_dir)
    timeline_entries, timeline_path = load_timeline(artifact_dir)
    snapshots = snapshot_candidates(artifact_dir)
    command_counters = command_counters_from_evidence(artifact_dir)

    requested_by_type: Counter[str] = Counter()
    issued_by_type: Counter[str] = Counter()
    rejected_by_reason: Counter[str] = Counter()
    observe_by_reason: Counter[str] = Counter()
    gold_cost_fields_total = 0
    max_round = max_round_from_snapshots(snapshots)
    battle_reached = battle_from_snapshots(snapshots) or bool(evidence.get("battleObserved"))
    bot_player_id: int | None = None
    scroll_targets = 0
    scroll_target_selections = 0
    monster_spawn_executed = 0
    scroll_effects = 0
    manual_skill_executed = 0
    first_reroll_sold_slot_count: int | None = None
    reroll_before_three_sold = False
    final_composition: dict[str, int] = {}

    for entry in journal_entries:
        if entry.get("kind") != "bot_decision":
            continue
        if isinstance(entry.get("playerId"), int):
            bot_player_id = entry.get("playerId")
        max_round = max(max_round, to_int(entry.get("round")))
        if entry.get("gameState") in BATTLE_PHASES:
            battle_reached = True
        decision = entry.get("decision") if isinstance(entry.get("decision"), dict) else {}
        command = known_command(decision.get("commandType"))
        if command:
            requested_by_type[command] += 1
        fields = decision.get("fields") if isinstance(decision.get("fields"), dict) else {}
        if fields:
            if any(key in fields for key in ("meleeCount", "rangedDpsCount", "healerCount", "fieldUnitCount")):
                final_composition = {
                    "melee": to_int(fields.get("meleeCount")),
                    "rangedDps": to_int(fields.get("rangedDpsCount")),
                    "healer": to_int(fields.get("healerCount")),
                    "unknown": to_int(fields.get("unknownUnitRoleCount")),
                    "total": to_int(fields.get("fieldUnitCount")),
                    "targetMelee": to_int(fields.get("targetMelee")),
                    "targetRangedDps": to_int(fields.get("targetRangedDps")),
                    "targetHealer": to_int(fields.get("targetHealer")),
                    "distance": to_int(fields.get("compositionDistance")),
                }
        if command == "RerollShop":
            gold_cost_fields_total += to_int(fields.get("cost"))
            sold_slots = to_int(fields.get("soldSlotCount"), -1)
            if first_reroll_sold_slot_count is None:
                first_reroll_sold_slot_count = sold_slots if sold_slots >= 0 else None
            if sold_slots >= 0 and sold_slots < 3:
                reroll_before_three_sold = True

    for entry in timeline_entries:
        phase = entry.get("phase")
        result_value = entry.get("result")
        command = known_command(entry.get("commandType") or entry.get("code"))
        max_round = max(max_round, to_int(entry.get("round")))
        if entry.get("gameState") in BATTLE_PHASES or phase in {
            "battle_decision_policy",
            "battle_command_request",
            "battle_command_accepted",
            "battle_command_executed",
        }:
            battle_reached = True

        if phase == "human_bot_decision" and result_value == "begin" and command:
            issued_by_type[command] += 1
            if isinstance(entry.get("playerId"), str):
                bot_player_id = to_int(entry.get("playerId"), bot_player_id if bot_player_id is not None else -1)
            if command == "RerollShop":
                sold_slots = to_int(entry.get("soldSlotCount"), -1)
                if first_reroll_sold_slot_count is None and sold_slots >= 0:
                    first_reroll_sold_slot_count = sold_slots
                if sold_slots >= 0 and sold_slots < 3:
                    reroll_before_three_sold = True
        elif phase == "human_bot_decision" and result_value == "fail":
            reason = str(entry.get("errorCode") or entry.get("msg") or entry.get("code") or "unknown")
            rejected_by_reason[reason] += 1
        elif phase == "human_bot_decision" and result_value == "info" and entry.get("code") == "observe":
            reason = str(entry.get("msg") or "observe")
            observe_by_reason[reason] += 1

        if phase in {"battle_command_rejected", "battle_spawn_rejected", "scroll_rejected", "skill_command_rejected"}:
            reason = str(entry.get("errorCode") or entry.get("msg") or entry.get("code") or "unknown")
            rejected_by_reason[reason] += 1

        host_side = entry.get("role") == "host" or entry.get("mode") == "Host" or "build-host" in str(entry.get("logSource") or "")
        if not host_side:
            continue
        if phase == "battle_spawn_executed" and result_value == "pass":
            monster_spawn_executed += to_int(entry.get("count")) or parse_spawned_count(entry.get("msg")) or 1
        elif phase == "scroll_effect_applied" and result_value == "pass":
            scroll_effects += 1
            scroll_targets += to_int(entry.get("targetCount"))
        elif phase == "scroll_target_selected" and result_value == "pass":
            scroll_target_selections += 1
        elif phase == "skill_command_executed" and result_value == "pass":
            manual_skill_executed += 1

    if command_counters["rejectedBattleCommandCount"] and not rejected_by_reason:
        rejected_by_reason["unknown_battle_command_rejection"] = command_counters["rejectedBattleCommandCount"]

    snapshots_gold_spent = gold_spent_from_snapshots(snapshots, bot_player_id)
    journal_gold_spent = gold_spent_from_journal(journal_entries)
    gold_spent = max(gold_cost_fields_total, journal_gold_spent, snapshots_gold_spent)
    by_type = issued_by_type or requested_by_type

    monster_spawns = max(monster_spawn_executed, command_counters["spawnMonsterSeq"])
    scroll_uses = max(scroll_effects, command_counters["useMagicScrollSeq"])
    manual_skill_uses = max(manual_skill_executed, command_counters["activateSkillSeq"])
    units_bought = by_type.get("BuyUnit", 0)
    reroll_count = by_type.get("RerollShop", 0)

    summary = {
        "commandsIssued": sum(by_type.values()),
        "commandsIssuedByType": counter_to_dict(by_type),
        "commandsRequestedByType": counter_to_dict(requested_by_type),
        "decisionsRejectedByReason": counter_to_dict(rejected_by_reason),
        "observeReasons": counter_to_dict(observe_by_reason),
        "goldSpent": gold_spent,
        "rerollCount": reroll_count,
        "unitsBought": units_bought,
        "buyToRerollRatio": round(units_bought / reroll_count, 3) if reroll_count > 0 else None,
        "firstRerollSoldSlotCount": first_reroll_sold_slot_count,
        "rerollBeforeThreeSold": reroll_before_three_sold,
        "wallsPlaced": by_type.get("PlaceWall", 0),
        "monsterSpawns": monster_spawns,
        "scrollUses": scroll_uses,
        "scrollTargetCount": scroll_targets,
        "scrollTargetSelections": scroll_target_selections,
        "manualSkillUses": manual_skill_uses,
        "roundReached": max_round,
        "battleReached": battle_reached,
        "finalFieldUnitTotal": final_composition.get("total", 0),
        "finalFieldUnitCountsByRole": {
            "melee": final_composition.get("melee", 0),
            "rangedDps": final_composition.get("rangedDps", 0),
            "healer": final_composition.get("healer", 0),
            "unknown": final_composition.get("unknown", 0),
        },
        "compositionDistanceFromTarget": final_composition.get("distance"),
    }

    return {
        "schemaVersion": SCHEMA_VERSION,
        "artifactDir": str(artifact_dir),
        "case": run.get("case") or result.get("case") or artifact_dir.name,
        "success": result.get("success"),
        "sources": {
            "journals": journal_paths,
            "timeline": timeline_path,
            "battleCommandEvidence": str(artifact_dir / "battle-command-evidence.json") if (artifact_dir / "battle-command-evidence.json").exists() else None,
            "snapshots": [str(path) for path in sorted((artifact_dir / "snapshots").glob("*.json"))[:12]] if (artifact_dir / "snapshots").exists() else [],
        },
        "commandCounters": command_counters,
        "summary": summary,
        "observedWeaknesses": observed_weaknesses(summary),
    }


def observed_weaknesses(summary: dict[str, Any]) -> list[str]:
    weaknesses: list[str] = []
    if summary.get("battleReached") is not True:
        weaknesses.append("battle_not_reached")
    if to_int(summary.get("unitsBought")) <= 0:
        weaknesses.append("no_units_bought")
    if summary.get("rerollBeforeThreeSold") is True:
        weaknesses.append("reroll_before_three_sold")
    if to_int(summary.get("rerollCount")) > to_int(summary.get("unitsBought")):
        weaknesses.append("reroll_count_exceeds_buy_count")
    if to_int(summary.get("finalFieldUnitTotal")) <= 0:
        weaknesses.append("no_final_field_units")
    if to_int(summary.get("wallsPlaced")) <= 0:
        weaknesses.append("no_walls_placed")
    if to_int(summary.get("monsterSpawns")) <= 0:
        weaknesses.append("no_monster_spawns")
    if to_int(summary.get("scrollUses")) <= 0:
        weaknesses.append("no_scroll_uses")
    if to_int(summary.get("manualSkillUses")) <= 0:
        weaknesses.append("no_manual_skill_uses")
    if summary.get("decisionsRejectedByReason"):
        weaknesses.append("decision_rejections_present")
    return weaknesses[:3]


def write_metrics_summary(artifact_dir: pathlib.Path, output: pathlib.Path | None = None) -> dict[str, Any]:
    metrics = summarize_artifact(artifact_dir)
    out = output or artifact_dir / "bot-metrics-summary.json"
    write_json(out, metrics)
    return metrics


def aggregate_seed_metrics(seed_metrics: list[dict[str, Any]]) -> dict[str, Any]:
    totals: dict[str, Any] = {
        "seeds": len(seed_metrics),
        "commandsIssued": 0,
        "commandsIssuedByType": Counter(),
        "decisionsRejectedByReason": Counter(),
        "goldSpent": 0,
        "rerollCount": 0,
        "unitsBought": 0,
        "buyToRerollRatio": None,
        "firstRerollSoldSlotCount": None,
        "rerollBeforeThreeSold": False,
        "wallsPlaced": 0,
        "monsterSpawns": 0,
        "scrollUses": 0,
        "scrollTargetCount": 0,
        "manualSkillUses": 0,
        "maxRoundReached": 0,
        "battleReachedCount": 0,
        "finalFieldUnitTotalMax": 0,
        "compositionDistanceFromTargetMin": None,
    }
    weakness_counts: Counter[str] = Counter()
    for metrics in seed_metrics:
        summary = metrics.get("summary") if isinstance(metrics.get("summary"), dict) else {}
        totals["commandsIssued"] += to_int(summary.get("commandsIssued"))
        totals["commandsIssuedByType"].update(summary.get("commandsIssuedByType") or {})
        totals["decisionsRejectedByReason"].update(summary.get("decisionsRejectedByReason") or {})
        for key in ("goldSpent", "rerollCount", "unitsBought", "wallsPlaced", "monsterSpawns", "scrollUses", "scrollTargetCount", "manualSkillUses"):
            totals[key] += to_int(summary.get(key))
        if summary.get("rerollBeforeThreeSold") is True:
            totals["rerollBeforeThreeSold"] = True
        first_reroll = summary.get("firstRerollSoldSlotCount")
        if isinstance(first_reroll, int) and (
            totals["firstRerollSoldSlotCount"] is None or first_reroll < totals["firstRerollSoldSlotCount"]
        ):
            totals["firstRerollSoldSlotCount"] = first_reroll
        totals["finalFieldUnitTotalMax"] = max(totals["finalFieldUnitTotalMax"], to_int(summary.get("finalFieldUnitTotal")))
        distance = summary.get("compositionDistanceFromTarget")
        if isinstance(distance, int) and (
            totals["compositionDistanceFromTargetMin"] is None or distance < totals["compositionDistanceFromTargetMin"]
        ):
            totals["compositionDistanceFromTargetMin"] = distance
        totals["maxRoundReached"] = max(totals["maxRoundReached"], to_int(summary.get("roundReached")))
        if summary.get("battleReached") is True:
            totals["battleReachedCount"] += 1
        weakness_counts.update(metrics.get("observedWeaknesses") or [])

    totals["commandsIssuedByType"] = counter_to_dict(totals["commandsIssuedByType"])
    totals["decisionsRejectedByReason"] = counter_to_dict(totals["decisionsRejectedByReason"])
    totals["buyToRerollRatio"] = round(totals["unitsBought"] / totals["rerollCount"], 3) if totals["rerollCount"] > 0 else None
    return {
        "schemaVersion": SCHEMA_VERSION,
        "summary": totals,
        "topObservedWeaknesses": [key for key, _ in weakness_counts.most_common(3)],
        "seedMetrics": seed_metrics,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("artifact_dir")
    parser.add_argument("--output")
    args = parser.parse_args()
    artifact_dir = pathlib.Path(args.artifact_dir)
    output = pathlib.Path(args.output) if args.output else None
    metrics = write_metrics_summary(artifact_dir, output)
    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "output": str(output or artifact_dir / "bot-metrics-summary.json"),
        "summary": metrics.get("summary"),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
