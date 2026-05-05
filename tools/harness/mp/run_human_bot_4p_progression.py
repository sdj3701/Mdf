#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import time
from typing import Any

from automation_client import AutomationClient
from collect_artifacts import collect_player_log, write_timeline
from common import (
    failure_summary,
    free_port,
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    normalize_snapshot_response,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import PlayerProcess, launch_player


CASE_NAME = "human-bot-4p-progression"
MEANINGFUL_COMMANDS = {"SelectAugment", "BuyUnit", "PlaceWall", "MoveUnit", "RerollShop"}


def safe_request(call: Any) -> dict[str, Any]:
    try:
        return call()
    except Exception as exc:
        return {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    data = safe_request(client.dump_state)
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def state(data: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(data)
    return normalized if isinstance(normalized, dict) else {}


def players(snapshot: Any) -> list[dict[str, Any]]:
    return [player for player in state(snapshot).get("players") or [] if isinstance(player, dict)]


def player_by_id(snapshot: Any, player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def local_player_id(snapshot: Any) -> int:
    for player in players(snapshot):
        if player.get("isLocal") is True and player.get("hasInputAuthority") is True:
            player_id = player.get("playerId")
            if isinstance(player_id, int):
                return player_id
    return -1


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def unique_player_ids(snapshot: Any) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids)) and all(isinstance(player_id, int) and player_id >= 0 for player_id in ids)


def bot_payload(response: dict[str, Any]) -> dict[str, Any]:
    data = response.get("data") if isinstance(response, dict) else None
    return data if isinstance(data, dict) else {}


def meaningful_deltas(before_snapshot: Any, after_snapshot: Any, player_id: int) -> list[dict[str, Any]]:
    before = player_by_id(before_snapshot, player_id)
    after = player_by_id(after_snapshot, player_id)
    if before is None or after is None:
        return []
    fields = [
        ("gold", ("gold",)),
        ("wallCount", ("wallCount",)),
        ("shop.revision", ("shop", "revision")),
        ("shop.itemsHash", ("shop", "itemsHash")),
        ("augment.selectedCount", ("augment", "selectedCount")),
        ("augment.selectedHash", ("augment", "selectedHash")),
        ("field.wallHash", ("field", "wallHash")),
        ("field.placedUnitCount", ("field", "placedUnitCount")),
        ("field.placedUnitsHash", ("field", "placedUnitsHash")),
    ]
    deltas: list[dict[str, Any]] = []
    for name, path in fields:
        old = nested(before, *path)
        new = nested(after, *path)
        if old != new:
            deltas.append({"field": name, "before": old, "after": new})
    return deltas


def random_outcome_summary(host_snapshot: Any, peer_snapshots: dict[str, Any]) -> dict[str, Any]:
    summary: dict[str, Any] = {"session": state(host_snapshot).get("session"), "scene": state(host_snapshot).get("scene"), "players": []}
    peers_by_id = {
        peer: {player.get("playerId"): player for player in players(snapshot)}
        for peer, snapshot in peer_snapshots.items()
    }
    for host_player in players(host_snapshot):
        player_id = host_player.get("playerId")
        matches: dict[str, bool] = {}
        for peer, by_id in peers_by_id.items():
            peer_player = by_id.get(player_id)
            matches[peer] = peer_player is not None \
                and nested(host_player, "shop", "itemsHash") == nested(peer_player, "shop", "itemsHash") \
                and nested(host_player, "augment", "presentedHash") == nested(peer_player, "augment", "presentedHash") \
                and nested(host_player, "augment", "selectedHash") == nested(peer_player, "augment", "selectedHash") \
                and nested(host_player, "field", "wallHash") == nested(peer_player, "field", "wallHash") \
                and nested(host_player, "field", "placedUnitsHash") == nested(peer_player, "field", "placedUnitsHash")
        summary["players"].append({
            "playerId": player_id,
            "shopHash": nested(host_player, "shop", "itemsHash"),
            "shopRevision": nested(host_player, "shop", "revision"),
            "augmentPresentedHash": nested(host_player, "augment", "presentedHash"),
            "augmentSelectedHash": nested(host_player, "augment", "selectedHash"),
            "fieldWallHash": nested(host_player, "field", "wallHash"),
            "fieldUnitsHash": nested(host_player, "field", "placedUnitsHash"),
            "matches": matches,
        })
    return summary


def wait_session_states(
    clients: dict[str, AutomationClient],
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
) -> bool:
    deadline = time.time() + timeout
    stable_matches = 0
    while time.time() < deadline:
        ready_by_peer: dict[str, bool] = {}
        reasons_by_peer: dict[str, list[str]] = {}
        for name, client in clients.items():
            snap = dump_state(client, artifact_dir, name, "lobby-latest")
            ready_by_peer[name] = session_ready(snap, 4, scene)
            reasons_by_peer[name] = session_not_ready_reasons(snap, 4, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "ready": ready_by_peer,
            "reasons": reasons_by_peer,
            "stableMatches": stable_matches,
        })
        if all(ready_by_peer.values()):
            stable_matches += 1
            if stable_matches >= 2:
                return True
        else:
            stable_matches = 0
        time.sleep(1)
    return False


def wait_stable_states(
    clients: dict[str, AutomationClient],
    peer_names: list[str],
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    label: str,
) -> tuple[dict[str, dict[str, Any]], dict[str, dict[str, Any]], bool]:
    deadline = time.time() + timeout
    latest: dict[str, dict[str, Any]] = {}
    comparisons: dict[str, dict[str, Any]] = {}
    stable_matches = 0
    while time.time() < deadline:
        ready_by_peer: dict[str, bool] = {}
        reasons_by_peer: dict[str, list[str]] = {}
        latest = {}
        comparisons = {}
        for name in peer_names:
            snap = dump_state(clients[name], artifact_dir, name, f"{label}-latest")
            latest[name] = snap
            ready_by_peer[name] = snapshot_ready(snap, 4, scene)
            reasons_by_peer[name] = snapshot_not_ready_reasons(snap, 4, scene)

        all_ready = all(ready_by_peer.values())
        if all_ready:
            for name in peer_names:
                if name == "host":
                    continue
                comparisons[name] = compare_snapshots(latest["host"], latest[name])
                write_json(artifact_dir / f"{label}-comparison-host-vs-{name}-latest.json", comparisons[name])

        all_match = all_ready and comparisons and all(result.get("success") for result in comparisons.values())
        write_json(artifact_dir / f"{label}-state-wait-latest.json", {
            "ready": ready_by_peer,
            "reasons": reasons_by_peer,
            "comparisons": comparisons,
            "stableMatches": stable_matches,
        })
        if all_match:
            stable_matches += 1
            if stable_matches >= 2:
                return latest, comparisons, True
        else:
            stable_matches = 0
        time.sleep(2)
    return latest, comparisons, False


def start_bots(
    clients: dict[str, AutomationClient],
    bot_peers: list[dict[str, Any]],
    artifact_dir: pathlib.Path,
    args: argparse.Namespace,
) -> dict[str, dict[str, Any]]:
    results: dict[str, dict[str, Any]] = {}
    for peer in bot_peers:
        name = str(peer["name"])
        result = safe_request(lambda client=clients[name], peer=peer: client.bot_start(
            persona=str(peer["persona"]),
            seed=int(peer["botSeed"]),
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=args.bot_stop_at_round,
            maxCommands=args.bot_max_commands,
            journalPath=str(artifact_dir / f"{name}-bot.jsonl"),
        ))
        results[name] = result
        write_json(artifact_dir / f"{name}-bot-start.json", result)
    return results


def collect_bot_statuses(
    clients: dict[str, AutomationClient],
    bot_peers: list[dict[str, Any]],
    artifact_dir: pathlib.Path,
    label: str,
) -> dict[str, dict[str, Any]]:
    statuses: dict[str, dict[str, Any]] = {}
    for peer in bot_peers:
        name = str(peer["name"])
        status = safe_request(clients[name].bot_status)
        journal = safe_request(clients[name].bot_journal)
        statuses[name] = status
        write_json(artifact_dir / f"{name}-bot-status-{label}.json", status)
        write_json(artifact_dir / f"{name}-bot-journal-{label}.json", journal)
    return statuses


def progression_assertions(
    before_host: Any,
    latest: dict[str, Any],
    comparisons: dict[str, dict[str, Any]],
    statuses: dict[str, dict[str, Any]],
    bot_peers: list[dict[str, Any]],
    scene: str,
    min_total_commands: int,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []
    host_snapshot = latest.get("host", {})
    host_state = state(host_snapshot)
    bot_results: dict[str, Any] = {}
    total_commands = 0

    if not snapshot_ready(host_snapshot, 4, scene):
        errors.extend("host." + reason for reason in snapshot_not_ready_reasons(host_snapshot, 4, scene))
    if not unique_player_ids(host_snapshot):
        errors.append("host.player_ids_not_unique")
    runner = host_state.get("runner") or {}
    if runner.get("activePlayerCount") != 4:
        errors.append(f"host.activePlayerCount expected=4 actual={runner.get('activePlayerCount')}")

    for name, result in comparisons.items():
        if result.get("success") is not True:
            errors.extend(f"{name}.{err}" for err in result.get("errors") or ["comparison_failed"])
        warnings.extend(f"{name}.{warn}" for warn in result.get("warnings") or [])

    for side, snapshot in latest.items():
        if not snapshot_ready(snapshot, 4, scene):
            errors.extend(f"{side}.{reason}" for reason in snapshot_not_ready_reasons(snapshot, 4, scene))
        if not unique_player_ids(snapshot):
            errors.append(f"{side}.player_ids_not_unique")
        for player in players(snapshot):
            player_id = player.get("playerId")
            if player.get("isConnected") is not True:
                errors.append(f"{side}.player.{player_id}.not_connected")
            if player.get("isAI") is not False:
                errors.append(f"{side}.player.{player_id}.is_ai")
            if nested(player, "ai", "controllerRegistered") is not False:
                errors.append(f"{side}.player.{player_id}.ai_controller_registered")

    for peer in bot_peers:
        name = str(peer["name"])
        status = bot_payload(statuses.get(name) or {})
        commands_issued = int(status.get("commandsIssued") or 0)
        total_commands += commands_issued
        player_id = status.get("playerId")
        if not isinstance(player_id, int) or player_id < 0:
            player_id = local_player_id(latest.get(name, {}))
        deltas = meaningful_deltas(before_host, host_snapshot, player_id)
        if commands_issued < 1:
            errors.append(f"{name}.commandsIssued expected>=1 actual={commands_issued}")
        if status.get("lastCommandType") not in MEANINGFUL_COMMANDS:
            warnings.append(f"{name}.lastCommandType_not_meaningful actual={status.get('lastCommandType')}")
        if not deltas:
            errors.append(f"{name}.no_meaningful_durable_delta playerId={player_id}")
        bot_results[name] = {
            "playerId": player_id,
            "persona": peer.get("persona"),
            "commandsIssued": commands_issued,
            "lastDecision": status.get("lastDecision"),
            "lastCommandType": status.get("lastCommandType"),
            "deltas": deltas,
        }

    if total_commands < min_total_commands:
        errors.append(f"totalBotCommands expected>={min_total_commands} actual={total_commands}")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings,
        "totalBotCommands": total_commands,
        "bots": bot_results,
        "hostGame": host_state.get("game"),
    }


def log_lines(response: dict[str, Any]) -> list[str]:
    data = response.get("data") if isinstance(response, dict) else None
    lines = data.get("lines") if isinstance(data, dict) else None
    return [line for line in lines or [] if isinstance(line, str)]


def mptest_failures(responses: dict[str, dict[str, Any]]) -> list[str]:
    failures: list[str] = []
    for peer, response in responses.items():
        for line in log_lines(response):
            if "[MPTEST]" in line and ("result=fail" in line or " phase=error" in line):
                failures.append(f"{peer}:{line}")
    return failures


def run(args: argparse.Namespace) -> int:
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("hbot4")
    peers: list[dict[str, Any]] = [
        {"name": "host", "role": "host", "seed": args.seed, "persona": "none", "bot": False},
        {"name": "client-1", "role": "client", "seed": args.seed + 1, "persona": "balanced", "bot": True},
        {"name": "client-2", "role": "client", "seed": args.seed + 2, "persona": "maze", "bot": True},
        {"name": "client-3", "role": "client", "seed": args.seed + 3, "persona": "shop", "bot": True},
    ]
    for index, peer in enumerate(peers):
        peer["token"] = new_token()
        peer["connection"] = new_token()
        peer["port"] = free_port()
        peer["botSeed"] = args.seed + 100 + index

    bot_peers = [peer for peer in peers if peer.get("bot")]
    peer_names = [str(peer["name"]) for peer in peers]
    procs: list[tuple[dict[str, Any], PlayerProcess]] = []
    failures: list[str] = []

    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "session": session,
        "playerPath": str(player_path),
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "seed": args.seed,
        "peers": [{k: v for k, v in peer.items() if k not in {"token", "connection"}} for peer in peers],
        "dryRun": args.dry_run,
    })
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": CASE_NAME, "dryRun": True}, indent=2))
        return 0

    try:
        for index, peer in enumerate(peers):
            extra_args: list[str] = []
            if peer.get("bot"):
                extra_args = [
                    "--mpHumanBot",
                    "--mpBotPersona",
                    str(peer["persona"]),
                    "--mpBotSeed",
                    str(peer["botSeed"]),
                    "--mpBotDurationSeconds",
                    str(args.bot_duration_seconds),
                    "--mpBotStopAtRound",
                    str(args.bot_stop_at_round),
                    "--mpBotMaxCommands",
                    str(args.bot_max_commands),
                    "--mpBotRecordJournal",
                    str(artifact_dir / f"{peer['name']}-bot.jsonl"),
                ]
            proc = launch_player(
                player_path,
                str(peer["role"]),
                session,
                int(peer["port"]),
                str(peer["token"]),
                str(peer["connection"]),
                artifact_dir,
                str(peer["name"]),
                max_players=4,
                scene=args.lobby_scene,
                case_name=CASE_NAME,
                auto_start=False,
                load_game=False,
                seed=int(peer["seed"]),
                scenario="human_bot_4p_progression",
                extra_args=extra_args,
            )
            procs.append((peer, proc))
            if index == 0 and args.host_lead_seconds > 0:
                time.sleep(args.host_lead_seconds)

        clients = {
            str(peer["name"]): AutomationClient(int(peer["port"]), str(peer["token"]), timeout=args.request_timeout)
            for peer in peers
        }
        for peer in peers:
            name = str(peer["name"])
            ping = clients[name].wait_ping(timeout_seconds=args.ping_timeout)
            write_json(artifact_dir / f"{name}-ping.json", ping)
            if not ping.get("success"):
                failures.append(f"{name}_ping_timeout")

        for peer in bot_peers:
            name = str(peer["name"])
            paused = safe_request(lambda client=clients[name]: client.bot_stop(reason="phase21_pre_checkpoint_pause"))
            write_json(artifact_dir / f"{name}-bot-paused.json", paused)
            if paused.get("success") is not True:
                failures.append(f"{name}_bot_pause_failed")

        host_client = clients["host"]
        if not wait_build_peer_started(host_client.start_host, artifact_dir, "host", session, args.lobby_scene, 4, args.start_timeout):
            failures.append("host_start_timeout")
        for peer in peers:
            name = str(peer["name"])
            if name == "host":
                continue
            if not wait_build_peer_started(clients[name].join, artifact_dir, name, session, args.lobby_scene, 4, args.start_timeout):
                failures.append(f"{name}_join_timeout")

        if not wait_session_states(clients, artifact_dir, args.lobby_timeout, args.lobby_scene):
            failures.append("session_join_timeout")

        load_result = host_client.load_game(args.scene)
        write_json(artifact_dir / "host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        before_latest, before_comparisons, before_ready = wait_stable_states(
            clients,
            peer_names,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-bot",
        )
        for name, snapshot in before_latest.items():
            write_json(artifact_dir / "snapshots" / f"{name}-before-bot.json", snapshot)
        write_json(artifact_dir / "before-bot-comparisons.json", before_comparisons)
        if not before_ready:
            failures.append("before_bot_state_ready_timeout")

        start_results = start_bots(clients, bot_peers, artifact_dir, args)
        for name, result in start_results.items():
            if result.get("success") is not True:
                failures.append(f"{name}_bot_start_failed")

        deadline = time.time() + args.bot_timeout
        latest: dict[str, dict[str, Any]] = {}
        comparisons: dict[str, dict[str, Any]] = {}
        assertions: dict[str, Any] = {"success": False, "errors": ["not_started"]}
        stable_successes = 0
        while time.time() < deadline:
            latest, comparisons, _ = wait_stable_states(clients, peer_names, artifact_dir, 6, args.scene, "bot")
            statuses = collect_bot_statuses(clients, bot_peers, artifact_dir, "latest")
            assertions = progression_assertions(
                before_latest.get("host", {}),
                latest,
                comparisons,
                statuses,
                bot_peers,
                args.scene,
                args.min_total_commands,
            )
            write_json(artifact_dir / "human-bot-4p-assertions-latest.json", assertions)
            peer_snapshots = {name: snap for name, snap in latest.items() if name != "host"}
            write_json(artifact_dir / "random-outcome-summary-latest.json", random_outcome_summary(latest.get("host", {}), peer_snapshots))
            for name, comparison in comparisons.items():
                write_json(artifact_dir / f"comparison-host-vs-{name}-latest.json", comparison)
            if assertions["success"]:
                stable_successes += 1
                if stable_successes >= 2:
                    break
            else:
                stable_successes = 0
            time.sleep(2)
        else:
            failures.append("bot_progression_timeout")

        for name, snapshot in latest.items():
            write_json(artifact_dir / "snapshots" / f"{name}-prepare-progressed.json", snapshot)
        write_json(artifact_dir / "human-bot-4p-assertions.json", assertions)
        write_json(artifact_dir / "random-outcome-summary.json", random_outcome_summary(
            latest.get("host", {}),
            {name: snap for name, snap in latest.items() if name != "host"},
        ))
        write_json(artifact_dir / "checkpoint-summary.json", {
            "beforeBot": {
                "snapshots": {name: f"snapshots/{name}-before-bot.json" for name in peer_names},
                "comparisons": "before-bot-comparisons.json",
            },
            "prepareProgressed": {
                "snapshots": {name: f"snapshots/{name}-prepare-progressed.json" for name in peer_names},
                "assertions": "human-bot-4p-assertions.json",
                "randomOutcomes": "random-outcome-summary.json",
            },
        })
        if not assertions.get("success"):
            failures.extend(assertions.get("errors") or ["human_bot_4p_assertions_failed"])

        logs_recent: dict[str, dict[str, Any]] = {}
        for name in peer_names:
            write_json(artifact_dir / f"{name}-screenshot.json", safe_request(clients[name].screenshot))
            logs_recent[name] = safe_request(clients[name].logs_recent)
            write_json(artifact_dir / f"{name}-logs-recent.json", logs_recent[name])
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(logs_recent))

        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)
    finally:
        for peer, proc in reversed(procs):
            name = str(peer["name"])
            try:
                automation = AutomationClient(int(peer["port"]), str(peer["token"]), timeout=2.0)
                write_json(artifact_dir / f"{name}-quit.json", automation.quit())
                proc.process.wait(timeout=10)
                proc.close_logs()
            except Exception:
                proc.terminate()
        copied = collect_player_log(artifact_dir, "last-player")
        logs = []
        for peer in peers:
            logs.append(artifact_dir / f"{peer['name']}.stdout.log")
            logs.append(artifact_dir / f"{peer['name']}.stderr.log")
        if copied:
            logs.append(copied)
        write_timeline(artifact_dir, logs)

    print(json.dumps({"artifactDir": str(artifact_dir), "failures": failures}, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=2001)
    parser.add_argument("--bot-duration-seconds", type=int, default=90)
    parser.add_argument("--bot-stop-at-round", type=int, default=2)
    parser.add_argument("--bot-max-commands", type=int, default=1)
    parser.add_argument("--min-total-commands", type=int, default=3)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--host-lead-seconds", type=float, default=3.0)
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=90)
    parser.add_argument("--state-timeout", type=int, default=120)
    parser.add_argument("--bot-timeout", type=int, default=150)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
