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
    hash_for_log,
    latest_player_path,
    make_artifact_dir,
    new_session,
    new_token,
    normalize_snapshot_response,
    read_json,
    scene_matches,
    session_not_ready_reasons,
    session_ready,
    snapshot_not_ready_reasons,
    snapshot_ready,
    wait_build_peer_started,
    write_json,
)
from compare_state_snapshots import compare_snapshots
from launch_player import (
    PlayerProcess,
    launch_player,
    mdf_player_pids,
    write_case_cleanup_report,
    write_orphan_pressure_report,
)
from summarize_bot_metrics import write_metrics_summary


UNKNOWN = "unknown"
BATTLE_PHASES = {"Battle1", "Battle2"}
BATTLE_COMMAND_TYPES = {"BattleSpawnMonster", "UseMagicScroll", "ActivateSkill"}


def write_bot_metrics_artifact(artifact_dir: pathlib.Path) -> dict[str, Any]:
    try:
        metrics = write_metrics_summary(artifact_dir)
        result_path = artifact_dir / "result.json"
        if result_path.exists():
            result = read_json(result_path)
            if isinstance(result, dict):
                result["botMetricsSummaryPath"] = "bot-metrics-summary.json"
                result["botMetrics"] = metrics.get("summary")
                write_json(result_path, result)
        return metrics
    except Exception as exc:
        error = {
            "success": False,
            "error": {
                "code": type(exc).__name__,
                "details": str(exc),
            },
        }
        write_json(artifact_dir / "bot-metrics-summary-error.json", error)
        return error


def add_common_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--session")
    parser.add_argument("--scene", default="Game")
    parser.add_argument("--lobby-scene", default="MatchingLobby")
    parser.add_argument("--seed", type=int, default=6101)
    parser.add_argument("--host-bot-seed", type=int)
    parser.add_argument("--client-bot-seed", type=int)
    parser.add_argument("--bot-persona", help="Compatibility alias for --host-bot-persona; also defaults client persona when --client-human-bot is used.")
    parser.add_argument("--host-bot-persona")
    parser.add_argument("--client-bot-persona")
    parser.add_argument("--bot-duration-seconds", type=int, default=180)
    parser.add_argument("--bot-max-commands", type=int, default=120)
    parser.add_argument("--min-bot-commands", type=int, default=1)
    parser.add_argument(
        "--bot-prepare-mode",
        choices=["augment-only", "skip", "full"],
        default="augment-only",
        help="Limit HumanBot prepare actions for battle E2E. augment-only avoids heavy maze planning while still allowing scroll/monster augment setup.",
    )
    parser.add_argument(
        "--client-human-bot",
        action="store_true",
        help="Also run the HumanBot driver on the client peer. By default the client observes host-driven battle progression.",
    )
    parser.add_argument(
        "--prefer-scroll-augment",
        action="store_true",
        help="Ask the test HumanBot prepare policy to prefer GrantMagicScroll augments when offered.",
    )
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--ping-timeout", type=int, default=45)
    parser.add_argument("--start-timeout", type=int, default=45)
    parser.add_argument("--lobby-timeout", type=int, default=60)
    parser.add_argument("--state-timeout", type=int, default=90)
    parser.add_argument("--battle-timeout", type=int, default=240)
    parser.add_argument("--request-timeout", type=float, default=20.0)
    parser.add_argument("--stable-samples", type=int, default=2)
    parser.add_argument("--host-migration-timeout", type=int, default=120)
    parser.add_argument("--takeover-timeout", type=int, default=90)
    parser.add_argument("--reconnect-timeout", type=int, default=120)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action="store_true")


def normalize_common_args(args: argparse.Namespace) -> None:
    default_persona = args.bot_persona or "balanced"
    if not args.host_bot_persona:
        args.host_bot_persona = default_persona
    if not args.client_bot_persona:
        args.client_bot_persona = default_persona


def apply_cleanup_result(
    artifact_dir: pathlib.Path,
    cleanup_report: dict[str, Any],
    strict_cleanup: bool,
    functional_failures: list[str],
    failures: list[str],
) -> None:
    if strict_cleanup and cleanup_report.get("cleanupStatus") != "PASS":
        failure = f"cleanup_failed:{cleanup_report.get('cleanupStatus')}"
        if failure not in failures:
            failures.append(failure)

    result_path = artifact_dir / "result.json"
    result: dict[str, Any] = {}
    if result_path.exists():
        try:
            loaded = read_json(result_path)
            if isinstance(loaded, dict):
                result = loaded
        except Exception as exc:
            result = {"resultReadError": {"code": type(exc).__name__, "details": str(exc)}}

    functional_success = not functional_failures and result.get("success", True) is not False
    cleanup_success = cleanup_report.get("cleanupSuccess") is True
    result.update({
        "artifactDir": str(artifact_dir),
        "success": functional_success and (cleanup_success or not strict_cleanup),
        "functionalSuccess": functional_success,
        "cleanupSuccess": cleanup_success,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupReportPath": "cleanup-report.json",
        "orphanedPids": cleanup_report.get("orphanedPids") or [],
        "failures": failures,
    })
    write_json(result_path, result)


def dump_state(client: AutomationClient, artifact_dir: pathlib.Path, peer: str, label: str) -> dict[str, Any]:
    try:
        data = client.dump_state()
    except Exception as exc:
        data = {
            "success": False,
            "message": "dumpState request failed",
            "error": {
                "code": type(exc).__name__,
                "details": str(exc),
            },
        }
    write_json(artifact_dir / "snapshots" / f"{peer}-{label}.json", data)
    return data


def state(data: Any) -> dict[str, Any]:
    normalized = normalize_snapshot_response(data)
    return normalized if isinstance(normalized, dict) else {}


def nested(obj: dict[str, Any] | None, *keys: str) -> Any:
    current: Any = obj
    for key in keys:
        if not isinstance(current, dict):
            return None
        current = current.get(key)
    return current


def players(snapshot: Any) -> list[dict[str, Any]]:
    return [player for player in state(snapshot).get("players") or [] if isinstance(player, dict)]


def player_by_id(snapshot: Any, player_id: int) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("playerId") == player_id:
            return player
    return None


def local_player(snapshot: Any) -> dict[str, Any] | None:
    for player in players(snapshot):
        if player.get("isLocal") is True and player.get("hasInputAuthority") is True:
            return player
    return None


def new_king_skill_verification() -> dict[str, Any]:
    return {
        "enabled": True,
        "success": False,
        "status": "waiting_for_local_defender",
        "commandAttempted": False,
        "commandAccepted": False,
        "commandPeer": None,
        "playerId": None,
        "baseline": None,
        "observed": None,
        "commandResponse": None,
        "errors": [],
    }


def local_king_skill_defender(snapshot: Any) -> dict[str, Any] | None:
    player = local_player(snapshot)
    if (
        in_battle(snapshot)
        and isinstance(player, dict)
        and player.get("isActivelyFighting") is True
        and player.get("isAttackerInCurrentBattle") is False
        and player.get("kingCanUseSkill") is True
    ):
        return player
    return None


def king_skill_observation(player: dict[str, Any] | None) -> dict[str, Any]:
    if not isinstance(player, dict):
        return {
            "present": False,
            "playerId": None,
            "kingSkillUsedThisDefense": None,
            "kingSkillPresentationSequence": None,
        }
    return {
        "present": True,
        "playerId": player.get("playerId"),
        "kingSkillUsedThisDefense": player.get("kingSkillUsedThisDefense"),
        "kingSkillPresentationSequence": player.get("kingSkillPresentationSequence"),
    }


def update_king_skill_verification(
    verification: dict[str, Any],
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    host_snapshot: Any,
    client_snapshot: Any,
) -> None:
    if verification.get("success") is True:
        return
    if (
        verification.get("commandAttempted") is True
        and verification.get("commandAccepted") is not True
    ):
        write_json(artifact_dir / "king-skill-verification.json", verification)
        return

    write_json(artifact_dir / "snapshots" / "build-host-king-skill-observed-latest.json", host_snapshot)
    write_json(artifact_dir / "snapshots" / "build-client-king-skill-observed-latest.json", client_snapshot)

    if verification.get("commandAttempted") is not True:
        candidate: tuple[str, AutomationClient, dict[str, Any]] | None = None
        for peer, automation_client, snapshot in (
            ("build-host", host, host_snapshot),
            ("build-client", client, client_snapshot),
        ):
            local_defender = local_king_skill_defender(snapshot)
            if isinstance(local_defender, dict):
                candidate = (peer, automation_client, local_defender)
                break

        if candidate is None:
            verification["status"] = "waiting_for_local_defender"
            verification["errors"] = []
            write_json(artifact_dir / "king-skill-verification.json", verification)
            return

        command_peer, command_client, candidate_player = candidate
        player_id = candidate_player.get("playerId")
        host_player = player_by_id(host_snapshot, player_id) if isinstance(player_id, int) else None
        client_player = player_by_id(client_snapshot, player_id) if isinstance(player_id, int) else None
        host_sequence = host_player.get("kingSkillPresentationSequence") if isinstance(host_player, dict) else None
        client_sequence = client_player.get("kingSkillPresentationSequence") if isinstance(client_player, dict) else None
        both_unconsumed = (
            isinstance(host_player, dict)
            and isinstance(client_player, dict)
            and host_player.get("kingSkillUsedThisDefense") is False
            and client_player.get("kingSkillUsedThisDefense") is False
        )
        if (
            not isinstance(player_id, int)
            or player_id < 0
            or not isinstance(host_sequence, int)
            or not isinstance(client_sequence, int)
            or not both_unconsumed
        ):
            verification["status"] = "waiting_for_shared_baseline"
            verification["errors"] = []
            write_json(artifact_dir / "king-skill-verification.json", verification)
            return

        verification.update({
            "status": "command_queued",
            "commandAttempted": True,
            "commandPeer": command_peer,
            "playerId": player_id,
            "baseline": {
                "host": king_skill_observation(host_player),
                "client": king_skill_observation(client_player),
                "localDefender": {
                    "peer": command_peer,
                    "playerId": player_id,
                    "hasInputAuthority": candidate_player.get("hasInputAuthority"),
                    "hasStateAuthority": candidate_player.get("hasStateAuthority"),
                    "isActivelyFighting": candidate_player.get("isActivelyFighting"),
                    "isAttackerInCurrentBattle": candidate_player.get("isAttackerInCurrentBattle"),
                    "kingCanUseSkill": candidate_player.get("kingCanUseSkill"),
                },
            },
            "errors": [],
        })
        write_json(artifact_dir / "snapshots" / "build-host-king-skill-before.json", host_snapshot)
        write_json(artifact_dir / "snapshots" / "build-client-king-skill-before.json", client_snapshot)
        try:
            command_response = command_client.command(name="activate_king_skill", playerId=player_id)
        except Exception as exc:
            command_response = {
                "success": False,
                "message": "activate_king_skill request failed",
                "error": {
                    "code": type(exc).__name__,
                    "details": str(exc),
                },
            }
        response_data = command_response.get("data") if isinstance(command_response, dict) else None
        response_player_id = response_data.get("playerId") if isinstance(response_data, dict) else None
        command_accepted = command_response.get("success") is True and response_player_id == player_id
        verification["commandResponse"] = command_response
        verification["commandAccepted"] = command_accepted
        if not command_accepted:
            error = command_response.get("error") if isinstance(command_response, dict) else None
            error_code = error.get("code") if isinstance(error, dict) else None
            verification["status"] = "command_rejected"
            verification["errors"] = [
                f"activate_king_skill_command_rejected:{error_code or command_response.get('message') or 'invalid_response'}"
            ]
            write_json(artifact_dir / "king-skill-verification.json", verification)
            return

    player_id = verification.get("playerId")
    baseline = verification.get("baseline")
    baseline_host = nested(baseline, "host", "kingSkillPresentationSequence") if isinstance(baseline, dict) else None
    baseline_client = nested(baseline, "client", "kingSkillPresentationSequence") if isinstance(baseline, dict) else None
    host_player = player_by_id(host_snapshot, player_id) if isinstance(player_id, int) else None
    client_player = player_by_id(client_snapshot, player_id) if isinstance(player_id, int) else None
    host_observation = king_skill_observation(host_player)
    client_observation = king_skill_observation(client_player)
    same_player_on_both = (
        isinstance(player_id, int)
        and host_observation.get("playerId") == player_id
        and client_observation.get("playerId") == player_id
    )
    host_used = host_observation.get("kingSkillUsedThisDefense") is True
    client_used = client_observation.get("kingSkillUsedThisDefense") is True
    host_sequence = host_observation.get("kingSkillPresentationSequence")
    client_sequence = client_observation.get("kingSkillPresentationSequence")
    host_sequence_increased = (
        isinstance(host_sequence, int)
        and isinstance(baseline_host, int)
        and host_sequence > baseline_host
    )
    client_sequence_increased = (
        isinstance(client_sequence, int)
        and isinstance(baseline_client, int)
        and client_sequence > baseline_client
    )
    errors: list[str] = []
    if not same_player_on_both:
        errors.append("same_player_id_not_present_on_both_peers")
    if not host_used:
        errors.append("host_king_skill_used_flag_not_observed")
    if not client_used:
        errors.append("client_king_skill_used_flag_not_observed")
    if not host_sequence_increased:
        errors.append("host_king_skill_presentation_sequence_not_increased")
    if not client_sequence_increased:
        errors.append("client_king_skill_presentation_sequence_not_increased")

    success = verification.get("commandAccepted") is True and not errors
    verification.update({
        "success": success,
        "status": "verified" if success else "waiting_for_replication",
        "observed": {
            "host": host_observation,
            "client": client_observation,
            "samePlayerIdOnBothPeers": same_player_on_both,
            "hostSequenceIncreased": host_sequence_increased,
            "clientSequenceIncreased": client_sequence_increased,
        },
        "errors": errors,
    })
    if success:
        write_json(artifact_dir / "snapshots" / "build-host-king-skill-after.json", host_snapshot)
        write_json(artifact_dir / "snapshots" / "build-client-king-skill-after.json", client_snapshot)
        write_json(
            artifact_dir / "king-skill-after-comparison.json",
            compare_snapshots(host_snapshot, client_snapshot),
        )
        verification["artifacts"] = {
            "hostBefore": "snapshots/build-host-king-skill-before.json",
            "clientBefore": "snapshots/build-client-king-skill-before.json",
            "hostAfter": "snapshots/build-host-king-skill-after.json",
            "clientAfter": "snapshots/build-client-king-skill-after.json",
            "comparison": "king-skill-after-comparison.json",
        }
    write_json(artifact_dir / "king-skill-verification.json", verification)


def unique_player_ids(snapshot: Any) -> bool:
    ids = [player.get("playerId") for player in players(snapshot)]
    return len(ids) == len(set(ids)) and all(isinstance(player_id, int) and player_id >= 0 for player_id in ids)


def bot_payload(response: dict[str, Any]) -> dict[str, Any]:
    data = response.get("data") if isinstance(response, dict) else None
    return data if isinstance(data, dict) else {}


def known(value: Any) -> bool:
    return value not in (None, "", UNKNOWN)


def in_battle(snapshot: Any) -> bool:
    game = state(snapshot).get("game") or {}
    return game.get("currentState") in BATTLE_PHASES or game.get("battlePhase") in BATTLE_PHASES


def command_snapshot(snapshot: Any) -> dict[str, Any]:
    commands = state(snapshot).get("commands")
    return commands if isinstance(commands, dict) else {}


def command_counter(snapshot: Any, key: str) -> int:
    value = command_snapshot(snapshot).get(key)
    return value if isinstance(value, int) else 0


def active_status_count(snapshot: Any) -> int:
    effects = state(snapshot).get("effects")
    if not isinstance(effects, dict):
        return 0
    value = effects.get("activeStatusCount")
    return value if isinstance(value, int) else 0


def active_buff_count(snapshot: Any) -> int:
    effects = state(snapshot).get("effects")
    if not isinstance(effects, dict):
        return 0
    value = effects.get("activeBuffCount")
    return value if isinstance(value, int) else 0


def active_zone_count(snapshot: Any) -> int:
    effects = state(snapshot).get("effects")
    if not isinstance(effects, dict):
        return 0
    value = effects.get("zoneCount")
    return value if isinstance(value, int) else 0


def network_budget_int(snapshot: Any, key: str) -> int:
    budget = state(snapshot).get("networkBudget")
    if not isinstance(budget, dict):
        return 0
    value = budget.get(key)
    return value if isinstance(value, int) else 0


def has_alive_monster_semantics(snapshot: Any) -> bool:
    for player in players(snapshot):
        monsters = player.get("monsters") if isinstance(player, dict) else None
        if not isinstance(monsters, dict):
            continue
        if (monsters.get("aliveCount") or 0) <= 0:
            continue
        required = ("typeHash", "ownerOriginHash", "targetPlayerHash", "hpBucketHash")
        if all(known(monsters.get(field)) for field in required):
            return True
    return False


def all_player_hashes_known(snapshot: Any, field: str) -> bool:
    return all(known(player.get(field)) for player in players(snapshot))


def bot_status_assertions(snapshot: Any, status: dict[str, Any], label: str, min_commands: int) -> list[str]:
    errors: list[str] = []
    commands = int(status.get("commandsIssued") or 0)
    player_id = status.get("playerId")
    if commands < min_commands:
        errors.append(f"{label}.bot.commandsIssued expected>={min_commands} actual={commands}")
    if not isinstance(player_id, int) or player_id < 0:
        errors.append(f"{label}.bot.playerId_missing")
        return errors

    player = player_by_id(snapshot, player_id)
    if player is None:
        errors.append(f"{label}.bot.player_missing:{player_id}")
        return errors
    if player.get("isConnected") is not True:
        errors.append(f"{label}.bot.player_not_connected:{player_id}")
    if player.get("isAI") is not False:
        errors.append(f"{label}.bot.player_is_ai:{player_id}")
    if nested(player, "ai", "controllerRegistered") is not False:
        errors.append(f"{label}.bot.ai_controller_registered:{player_id}")
    return errors


def human_peer_assertions(snapshot: Any, label: str) -> list[str]:
    errors: list[str] = []
    player = local_player(snapshot)
    if player is None:
        errors.append(f"{label}.local_player_missing")
        return errors
    player_id = player.get("playerId")
    if player.get("isConnected") is not True:
        errors.append(f"{label}.local_player_not_connected:{player_id}")
    if player.get("isAI") is not False:
        errors.append(f"{label}.local_player_is_ai:{player_id}")
    if nested(player, "ai", "controllerRegistered") is not False:
        errors.append(f"{label}.local_ai_controller_registered:{player_id}")
    return errors


def battle_assertions(
    host_snapshot: Any,
    client_snapshot: Any,
    host_status: dict[str, Any],
    client_status: dict[str, Any],
    comparison: dict[str, Any],
    scene: str,
    expected_players: int,
    min_bot_commands: int,
    require_spawn: bool,
    require_scroll: bool,
    require_any_battle_command: bool,
    battle_observed: bool,
    client_human_bot: bool,
    spawn_semantics_observed: bool,
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []

    if not snapshot_ready(host_snapshot, expected_players, scene):
        errors.extend("host." + reason for reason in snapshot_not_ready_reasons(host_snapshot, expected_players, scene))
    if not snapshot_ready(client_snapshot, expected_players, scene):
        errors.extend("client." + reason for reason in snapshot_not_ready_reasons(client_snapshot, expected_players, scene))
    if comparison.get("success") is not True:
        errors.extend(comparison.get("errors") or ["snapshot_comparison_failed"])
    commands = command_snapshot(host_snapshot)
    client_commands = command_snapshot(client_snapshot)
    accepted = command_counter(host_snapshot, "acceptedBattleCommandSeq")
    client_accepted = command_counter(client_snapshot, "acceptedBattleCommandSeq")
    spawn_seq = command_counter(host_snapshot, "spawnMonsterSeq")
    client_spawn_seq = command_counter(client_snapshot, "spawnMonsterSeq")
    scroll_seq = command_counter(host_snapshot, "useMagicScrollSeq")
    client_scroll_seq = command_counter(client_snapshot, "useMagicScrollSeq")
    skill_seq = command_counter(host_snapshot, "activateSkillSeq")
    client_skill_seq = command_counter(client_snapshot, "activateSkillSeq")
    rejected = command_counter(host_snapshot, "rejectedBattleCommandCount")
    last_command = commands.get("lastCommand")
    battle_command_evidence = accepted > 0 and spawn_seq + scroll_seq + skill_seq > 0

    if not battle_observed and not battle_command_evidence:
        errors.append(
            "battle_phase_not_reached_or_command_evidence_missing "
            f"host={nested(state(host_snapshot), 'game', 'currentState')}/{nested(state(host_snapshot), 'game', 'battlePhase')} "
            f"client={nested(state(client_snapshot), 'game', 'currentState')}/{nested(state(client_snapshot), 'game', 'battlePhase')}"
        )
    if not unique_player_ids(host_snapshot):
        errors.append("host.player_ids_not_unique")
    if not unique_player_ids(client_snapshot):
        errors.append("client.player_ids_not_unique")

    errors.extend(bot_status_assertions(host_snapshot, host_status, "host", min_bot_commands))
    if client_human_bot:
        errors.extend(bot_status_assertions(client_snapshot, client_status, "client", min_bot_commands))
    else:
        errors.extend(human_peer_assertions(client_snapshot, "client"))

    if require_any_battle_command and accepted <= 0:
        errors.append("acceptedBattleCommandSeq_not_advanced")
    if require_any_battle_command and spawn_seq + scroll_seq + skill_seq <= 0:
        errors.append("no_battle_command_execution_seq")
    if accepted != client_accepted:
        errors.append(f"acceptedBattleCommandSeq_mismatch host={accepted} client={client_accepted}")
    if spawn_seq != client_spawn_seq:
        errors.append(f"spawnMonsterSeq_mismatch host={spawn_seq} client={client_spawn_seq}")
    if scroll_seq != client_scroll_seq:
        errors.append(f"useMagicScrollSeq_mismatch host={scroll_seq} client={client_scroll_seq}")
    if skill_seq != client_skill_seq:
        errors.append(f"activateSkillSeq_mismatch host={skill_seq} client={client_skill_seq}")
    if accepted > 0 and last_command not in BATTLE_COMMAND_TYPES:
        errors.append(f"lastCommand_not_battle_command actual={last_command}")
    if require_spawn and spawn_seq <= 0:
        errors.append(f"spawnMonsterSeq_not_advanced actual={spawn_seq}")
    host_has_spawn_semantics = has_alive_monster_semantics(host_snapshot)
    client_has_spawn_semantics = has_alive_monster_semantics(client_snapshot)
    if require_spawn and not host_has_spawn_semantics and not spawn_semantics_observed:
        errors.append("host.spawn_monster_semantic_hash_missing")
    if require_spawn and not client_has_spawn_semantics and not spawn_semantics_observed:
        errors.append("client.spawn_monster_semantic_hash_missing")
    if require_spawn and not all_player_hashes_known(host_snapshot, "attackMonsterPoolHash"):
        errors.append("host.attackMonsterPoolHash_unknown")
    if require_spawn and not all_player_hashes_known(client_snapshot, "attackMonsterPoolHash"):
        errors.append("client.attackMonsterPoolHash_unknown")
    if require_scroll and scroll_seq <= 0:
        errors.append(f"useMagicScrollSeq_not_advanced actual={scroll_seq}")
    if require_scroll and not all_player_hashes_known(host_snapshot, "ownedScrollsHash"):
        errors.append("host.ownedScrollsHash_unknown")
    if require_scroll and not all_player_hashes_known(client_snapshot, "ownedScrollsHash"):
        errors.append("client.ownedScrollsHash_unknown")
    if rejected > 0:
        warnings.append(f"rejectedBattleCommandCount={rejected}")

    return {
        "success": not errors,
        "errors": errors,
        "warnings": warnings + list(comparison.get("warnings") or []),
        "hostGame": state(host_snapshot).get("game"),
        "clientGame": state(client_snapshot).get("game"),
        "commands": commands,
        "clientCommands": client_commands,
        "hostBot": host_status,
        "clientBot": client_status,
        "battleObserved": battle_observed,
        "clientHumanBot": client_human_bot,
        "spawnSemanticsObserved": spawn_semantics_observed,
        "hostHasSpawnSemantics": host_has_spawn_semantics,
        "clientHasSpawnSemantics": client_has_spawn_semantics,
        "requireSpawn": require_spawn,
        "requireScroll": require_scroll,
    }


def wait_session_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "lobby-latest")
        client_state = dump_state(client, artifact_dir, "build-client", "lobby-latest")
        host_ready = session_ready(host_state, 2, scene)
        client_ready = session_ready(client_state, 2, scene)
        write_json(artifact_dir / "session-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": session_not_ready_reasons(host_state, 2, scene),
            "clientReasons": session_not_ready_reasons(client_state, 2, scene),
            "stableMatches": stable_matches,
        })
        if host_ready and client_ready:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, True
        else:
            stable_matches = 0
        time.sleep(1)
    return host_state, client_state, False


def wait_stable_states(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    label: str,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
    stable_matches = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", f"{label}-latest")
        client_state = dump_state(client, artifact_dir, "build-client", f"{label}-latest")
        host_ready = snapshot_ready(host_state, 2, scene)
        client_ready = snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        write_json(artifact_dir / f"{label}-comparison-latest.json", comparison)
        write_json(artifact_dir / f"{label}-state-wait-latest.json", {
            "hostReady": host_ready,
            "clientReady": client_ready,
            "hostReasons": snapshot_not_ready_reasons(host_state, 2, scene),
            "clientReasons": snapshot_not_ready_reasons(client_state, 2, scene),
            "comparison": comparison,
            "stableMatches": stable_matches,
        })
        if host_ready and client_ready and comparison["success"]:
            stable_matches += 1
            if stable_matches >= 2:
                return host_state, client_state, comparison, True
        else:
            stable_matches = 0
        time.sleep(2)
    return host_state, client_state, comparison, False


def wait_battle_progression(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    timeout: int,
    scene: str,
    min_bot_commands: int,
    stable_samples: int,
    require_spawn: bool,
    require_scroll: bool,
    require_any_battle_command: bool,
    client_human_bot: bool,
    verify_king_skill: bool = False,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    latest_assertions: dict[str, Any] = {"success": False, "errors": ["battle_progression_not_started"]}
    stable_successes = 0
    wrote_battle_start = False
    battle_observed = False
    spawn_semantics_observed = False
    king_skill_verification = new_king_skill_verification() if verify_king_skill else None

    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "battle-latest")
        client_state = dump_state(client, artifact_dir, "build-client", "battle-latest")
        host_ready = snapshot_ready(host_state, 2, scene)
        client_ready = snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if host_ready and client_ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        try:
            host_status_response = host.bot_status()
        except Exception as exc:
            host_status_response = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        if client_human_bot:
            try:
                client_status_response = client.bot_status()
            except Exception as exc:
                client_status_response = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        else:
            client_status_response = {
                "success": True,
                "data": {
                    "enabled": False,
                    "running": False,
                    "commandsIssued": 0,
                    "playerId": None,
                    "stopReason": "observer_peer",
                },
            }
        try:
            host_journal = host.bot_journal()
        except Exception as exc:
            host_journal = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        if client_human_bot:
            try:
                client_journal = client.bot_journal()
            except Exception as exc:
                client_journal = {"success": False, "error": {"code": type(exc).__name__, "details": str(exc)}}
        else:
            client_journal = {"success": True, "data": {"enabled": False, "entries": []}}

        if in_battle(host_state) and in_battle(client_state):
            battle_observed = True

        if (
            require_spawn
            and host_ready
            and client_ready
            and comparison.get("success") is True
            and has_alive_monster_semantics(host_state)
            and has_alive_monster_semantics(client_state)
            and not spawn_semantics_observed
        ):
            spawn_semantics_observed = True
            write_json(artifact_dir / "snapshots" / "build-host-spawn-semantic.json", host_state)
            write_json(artifact_dir / "snapshots" / "build-client-spawn-semantic.json", client_state)
            write_json(artifact_dir / "spawn-semantic-comparison.json", comparison)

        latest_assertions = battle_assertions(
            host_state,
            client_state,
            bot_payload(host_status_response),
            bot_payload(client_status_response),
            comparison,
            scene,
            2,
            min_bot_commands,
            require_spawn,
            require_scroll,
            require_any_battle_command,
            battle_observed,
            client_human_bot,
            spawn_semantics_observed,
        )
        if king_skill_verification is not None:
            update_king_skill_verification(
                king_skill_verification,
                host,
                client,
                artifact_dir,
                host_state,
                client_state,
            )
            latest_assertions["kingSkillVerification"] = king_skill_verification
            if king_skill_verification.get("success") is not True:
                latest_assertions["success"] = False
                latest_assertions.setdefault("errors", []).append("king_skill_verification_pending")

        write_json(artifact_dir / "battle-comparison-latest.json", comparison)
        write_json(artifact_dir / "battle-command-evidence-latest.json", latest_assertions)
        write_json(artifact_dir / "build-host-bot-status-latest.json", host_status_response)
        write_json(artifact_dir / "build-client-bot-status-latest.json", client_status_response)
        write_json(artifact_dir / "build-host-bot-journal-latest.json", host_journal)
        write_json(artifact_dir / "build-client-bot-journal-latest.json", client_journal)

        if battle_observed and not wrote_battle_start:
            wrote_battle_start = True
            write_json(artifact_dir / "snapshots" / "build-host-battle-start.json", host_state)
            write_json(artifact_dir / "snapshots" / "build-client-battle-start.json", client_state)
            write_json(artifact_dir / "battle-start-comparison.json", comparison)

        if latest_assertions["success"]:
            stable_successes += 1
            if stable_successes >= stable_samples:
                return host_state, client_state, latest_assertions, True
        else:
            stable_successes = 0
        time.sleep(0.5)

    if king_skill_verification is not None and king_skill_verification.get("success") is not True:
        waiting_status = king_skill_verification.get("status")
        if waiting_status != "command_rejected":
            king_skill_verification["status"] = "timed_out"
        if not king_skill_verification.get("errors"):
            if waiting_status == "waiting_for_shared_baseline":
                king_skill_verification["errors"] = ["shared_king_skill_baseline_not_observed"]
            else:
                king_skill_verification["errors"] = ["eligible_local_defender_not_observed"]
        write_json(artifact_dir / "king-skill-verification.json", king_skill_verification)
        latest_assertions["kingSkillVerification"] = king_skill_verification
        latest_assertions["success"] = False
        latest_errors = latest_assertions.setdefault("errors", [])
        latest_errors[:] = [error for error in latest_errors if error != "king_skill_verification_pending"]
        latest_errors.extend(
            f"king_skill_verification:{error}"
            for error in king_skill_verification.get("errors") or []
        )
    return host_state, client_state, latest_assertions, False


def log_lines(response: dict[str, Any]) -> list[str]:
    data = response.get("data") if isinstance(response, dict) else None
    lines = data.get("lines") if isinstance(data, dict) else None
    return [line for line in lines or [] if isinstance(line, str)]


def mptest_failures(*responses: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    for response in responses:
        for line in log_lines(response):
            if "[MPTEST]" in line and ("result=fail" in line or " phase=error" in line):
                failures.append(line)
    return failures


def compare_value(errors: list[str], mismatches: list[dict[str, Any]], field: str, before: Any, after: Any) -> None:
    if before != after:
        errors.append(f"{field} before={before} after={after}")
        mismatches.append({"field": field, "before": before, "after": after})


def compare_nested(
    errors: list[str],
    mismatches: list[dict[str, Any]],
    prefix: str,
    before: dict[str, Any],
    after: dict[str, Any],
) -> None:
    for key, before_value in before.items():
        field = f"{prefix}.{key}" if prefix else key
        after_value = after.get(key)
        if isinstance(before_value, dict) and isinstance(after_value, dict):
            compare_nested(errors, mismatches, field, before_value, after_value)
        elif (
            isinstance(before_value, list)
            and isinstance(after_value, list)
            and all(isinstance(item, dict) and "playerId" in item for item in before_value + after_value)
        ):
            before_by_id = {item.get("playerId"): item for item in before_value}
            after_by_id = {item.get("playerId"): item for item in after_value}
            if set(before_by_id) != set(after_by_id):
                compare_value(errors, mismatches, field, sorted(before_by_id), sorted(after_by_id))
                continue
            for player_id in sorted(before_by_id):
                compare_nested(
                    errors,
                    mismatches,
                    f"{field}.{player_id}",
                    before_by_id[player_id],
                    after_by_id[player_id],
                )
        else:
            compare_value(errors, mismatches, field, before_value, after_value)


def player_battle_fingerprint(player: dict[str, Any]) -> dict[str, Any]:
    return {
        "playerId": player.get("playerId"),
        "health": player.get("health"),
        "gold": player.get("gold"),
        "wallCount": player.get("wallCount"),
        "permanentWallPlacementCount": player.get("permanentWallPlacementCount"),
        "permanentWallStockRevision": player.get("permanentWallStockRevision"),
        "permanentWallLayoutRevision": player.get("permanentWallLayoutRevision"),
        "isActivelyFighting": player.get("isActivelyFighting"),
        "isAttackerInCurrentBattle": player.get("isAttackerInCurrentBattle"),
        "blackMagicCurrent": player.get("blackMagicCurrent"),
        "blackMagicMaximum": player.get("blackMagicMaximum"),
        "blackMagicMaxBonus": player.get("blackMagicMaxBonus"),
        "blackMagicRevision": player.get("blackMagicRevision"),
        "blackMagicSequenceId": player.get("blackMagicSequenceId"),
        "attackMonsterPoolHash": player.get("attackMonsterPoolHash"),
        "ownedScrollsHash": player.get("ownedScrollsHash"),
        "ownedScrollRevision": player.get("ownedScrollRevision"),
        "manualSkillReadyHash": player.get("manualSkillReadyHash"),
        "shop": {
            "available": nested(player, "shop", "available"),
            "revision": nested(player, "shop", "revision"),
            "round": nested(player, "shop", "round"),
            "count": nested(player, "shop", "count"),
            "itemsHash": nested(player, "shop", "itemsHash"),
        },
        "augment": {
            "available": nested(player, "augment", "available"),
            "presentedCount": nested(player, "augment", "presentedCount"),
            "presentedHash": nested(player, "augment", "presentedHash"),
            "selectedCount": nested(player, "augment", "selectedCount"),
            "selectedHash": nested(player, "augment", "selectedHash"),
            "activeEffectCount": nested(player, "augment", "activeEffectCount"),
            "activeTargetCount": nested(player, "augment", "activeTargetCount"),
            "activeEffectHash": nested(player, "augment", "activeEffectHash"),
            "activeTargetHash": nested(player, "augment", "activeTargetHash"),
        },
        "field": {
            "ready": nested(player, "field", "ready"),
            "gridHash": nested(player, "field", "gridHash"),
            "placedUnitCount": nested(player, "field", "placedUnitCount"),
            "placedUnitsHash": nested(player, "field", "placedUnitsHash"),
            "destructibleWallCount": nested(player, "field", "destructibleWallCount"),
            "permanentWallCount": nested(player, "field", "permanentWallCount"),
            "wallHash": nested(player, "field", "wallHash"),
            "pathReady": nested(player, "field", "pathReady"),
            "goalReady": nested(player, "field", "goalReady"),
        },
        "monsters": {
            "aliveCount": nested(player, "monsters", "aliveCount"),
            "livingHash": nested(player, "monsters", "livingHash"),
            "typeHash": nested(player, "monsters", "typeHash"),
            "typeCountHpHash": nested(player, "monsters", "typeCountHpHash"),
            "ownerOriginHash": nested(player, "monsters", "ownerOriginHash"),
            "targetPlayerHash": nested(player, "monsters", "targetPlayerHash"),
            "hpBucketHash": nested(player, "monsters", "hpBucketHash"),
            "bossPoolIdentityHash": nested(player, "monsters", "bossPoolIdentityHash"),
        },
    }


def battle_world_fingerprint(snapshot: Any) -> dict[str, Any]:
    body = state(snapshot)
    game = body.get("game") if isinstance(body, dict) else {}
    commands = body.get("commands") if isinstance(body, dict) else {}
    effects = body.get("effects") if isinstance(body, dict) else {}
    return {
        "session": body.get("session"),
        "scene": body.get("scene"),
        "game": {
            "currentState": game.get("currentState") if isinstance(game, dict) else None,
            "battlePhase": game.get("battlePhase") if isinstance(game, dict) else None,
            "currentRound": game.get("currentRound") if isinstance(game, dict) else None,
            "firstAttackerPlayerId": game.get("firstAttackerPlayerId") if isinstance(game, dict) else None,
            "battleOpponentsHash": game.get("battleOpponentsHash") if isinstance(game, dict) else None,
            "matchFirstAttackerHash": game.get("matchFirstAttackerHash") if isinstance(game, dict) else None,
            "battleActiveHash": game.get("battleActiveHash") if isinstance(game, dict) else None,
            "survivorBossPendingHash": game.get("survivorBossPendingHash") if isinstance(game, dict) else None,
            "survivorBossAssignmentHash": game.get("survivorBossAssignmentHash") if isinstance(game, dict) else None,
            "survivorBossPendingCount": game.get("survivorBossPendingCount") if isinstance(game, dict) else None,
            "survivorBossAssignmentCount": game.get("survivorBossAssignmentCount") if isinstance(game, dict) else None,
        },
        "commands": {
            "acceptedBattleCommandSeq": commands.get("acceptedBattleCommandSeq") if isinstance(commands, dict) else None,
            "spawnMonsterSeq": commands.get("spawnMonsterSeq") if isinstance(commands, dict) else None,
            "useMagicScrollSeq": commands.get("useMagicScrollSeq") if isinstance(commands, dict) else None,
            "activateSkillSeq": commands.get("activateSkillSeq") if isinstance(commands, dict) else None,
            "rejectedBattleCommandCount": commands.get("rejectedBattleCommandCount") if isinstance(commands, dict) else None,
            "lastCommand": commands.get("lastCommand") if isinstance(commands, dict) else None,
        },
        "effects": {
            "activeBuffCount": effects.get("activeBuffCount") if isinstance(effects, dict) else None,
            "activeStatusCount": effects.get("activeStatusCount") if isinstance(effects, dict) else None,
            "zoneCount": effects.get("zoneCount") if isinstance(effects, dict) else None,
            "activeBuffHash": effects.get("activeBuffHash") if isinstance(effects, dict) else None,
            "activeStatusHash": effects.get("activeStatusHash") if isinstance(effects, dict) else None,
            "zoneHash": effects.get("zoneHash") if isinstance(effects, dict) else None,
        },
        "players": [
            player_battle_fingerprint(player)
            for player in sorted(players(snapshot), key=lambda item: item.get("playerId", 9999))
        ],
    }


def battle_preservation_assertions(
    checkpoint_snapshot: Any,
    after_snapshot: Any,
    label: str,
    expected_players: int = 2,
) -> dict[str, Any]:
    errors: list[str] = []
    mismatches: list[dict[str, Any]] = []
    after_players = players(after_snapshot)
    if len(after_players) != expected_players:
        errors.append(f"{label}.player_count expected={expected_players} actual={len(after_players)}")
    if not unique_player_ids(after_snapshot):
        errors.append(f"{label}.player_ids_not_unique")

    before_fp = battle_world_fingerprint(checkpoint_snapshot)
    after_fp = battle_world_fingerprint(after_snapshot)
    compare_nested(errors, mismatches, label, before_fp, after_fp)
    return {
        "success": not errors,
        "errors": errors,
        "warnings": [],
        "mismatches": mismatches,
        "checkpointFingerprint": before_fp,
        "afterFingerprint": after_fp,
    }


def freeze_battle_checkpoint(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    scene: str,
    client_human_bot: bool,
    stable_samples: int,
    reason: str,
    client_peer: str = "build-client",
    status_effect_payload: dict[str, Any] | None = None,
    require_active_status: bool = False,
    stat_buff_payload: dict[str, Any] | None = None,
    require_active_buff: bool = False,
    zone_payload: dict[str, Any] | None = None,
    require_active_zone: bool = False,
    pending_load_payload: dict[str, Any] | None = None,
    require_pending_load: bool = False,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    write_json(artifact_dir / "build-host-bot-stop-after-battle.json", host.bot_stop(reason=reason))
    if client_human_bot:
        write_json(artifact_dir / "build-client-bot-stop-after-battle.json", client.bot_stop(reason=reason))
    else:
        write_json(artifact_dir / "build-client-bot-stop-after-battle.json", {
            "success": True,
            "message": "client HumanBot disabled; observer peer",
        })

    write_json(artifact_dir / "build-host-freeze-game-flow.json", host.freeze_game_flow(True, reason=reason))
    write_json(artifact_dir / "build-client-freeze-game-flow.json", client.freeze_game_flow(True, reason=reason))
    status_apply_response: dict[str, Any] | None = None
    if status_effect_payload is not None:
        status_apply_response = host.apply_status_effect(**status_effect_payload)
        write_json(artifact_dir / "build-host-apply-status-effect.json", status_apply_response)
    stat_buff_apply_response: dict[str, Any] | None = None
    if stat_buff_payload is not None:
        stat_buff_apply_response = host.apply_stat_buff(**stat_buff_payload)
        write_json(artifact_dir / "build-host-apply-stat-buff.json", stat_buff_apply_response)
    zone_apply_response: dict[str, Any] | None = None
    if zone_payload is not None:
        zone_apply_response = host.apply_zone(**zone_payload)
        write_json(artifact_dir / "build-host-apply-zone.json", zone_apply_response)
    pending_load_response: dict[str, Any] | None = None
    expected_pending_fire = 0
    expected_pending_hit = 0
    if pending_load_payload is not None:
        expected_pending_fire = int(pending_load_payload.get("pendingFireCount") or pending_load_payload.get("pending_fire_count") or 0)
        expected_pending_hit = int(pending_load_payload.get("pendingHitCount") or pending_load_payload.get("pending_hit_count") or 0)
        pending_load_response = host.inject_pending_combat_load(**pending_load_payload)
        write_json(artifact_dir / "build-host-inject-pending-combat-load.json", pending_load_response)

    deadline = time.time() + 20
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    comparison: dict[str, Any] = {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
    stable = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "battle-checkpoint-frozen-latest")
        client_state = dump_state(client, artifact_dir, client_peer, "battle-checkpoint-frozen-latest")
        ready = snapshot_ready(host_state, 2, scene) and snapshot_ready(client_state, 2, scene)
        comparison = compare_snapshots(host_state, client_state) if ready else {
            "success": False,
            "errors": ["snapshot_not_ready"],
            "warnings": [],
        }
        host_active_status_count = active_status_count(host_state)
        client_active_status_count = active_status_count(client_state)
        host_active_buff_count = active_buff_count(host_state)
        client_active_buff_count = active_buff_count(client_state)
        host_active_zone_count = active_zone_count(host_state)
        client_active_zone_count = active_zone_count(client_state)
        host_pending_fire = network_budget_int(host_state, "currentPendingFireActive")
        client_pending_fire = network_budget_int(client_state, "currentPendingFireActive")
        host_pending_hit = network_budget_int(host_state, "currentPendingHitActive")
        client_pending_hit = network_budget_int(client_state, "currentPendingHitActive")
        active_status_ready = (
            not require_active_status or
            (host_active_status_count > 0 and client_active_status_count > 0)
        )
        active_buff_ready = (
            not require_active_buff or
            (host_active_buff_count > 0 and client_active_buff_count > 0)
        )
        active_zone_ready = (
            not require_active_zone or
            (host_active_zone_count > 0 and client_active_zone_count > 0)
        )
        pending_load_ready = (
            not require_pending_load or
            (
                pending_load_response is not None and
                pending_load_response.get("success") is True and
                host_pending_fire >= expected_pending_fire and
                client_pending_fire >= expected_pending_fire and
                host_pending_hit >= expected_pending_hit and
                client_pending_hit >= expected_pending_hit
            )
        )
        write_json(artifact_dir / "battle-checkpoint-freeze-wait-latest.json", {
            "ready": ready,
            "comparison": comparison,
            "stableMatches": stable,
            "requireActiveStatus": require_active_status,
            "activeStatusReady": active_status_ready,
            "hostActiveStatusCount": host_active_status_count,
            "clientActiveStatusCount": client_active_status_count,
            "statusApplyResponse": status_apply_response,
            "requireActiveBuff": require_active_buff,
            "activeBuffReady": active_buff_ready,
            "hostActiveBuffCount": host_active_buff_count,
            "clientActiveBuffCount": client_active_buff_count,
            "statBuffApplyResponse": stat_buff_apply_response,
            "requireActiveZone": require_active_zone,
            "activeZoneReady": active_zone_ready,
            "hostActiveZoneCount": host_active_zone_count,
            "clientActiveZoneCount": client_active_zone_count,
            "zoneApplyResponse": zone_apply_response,
            "requirePendingLoad": require_pending_load,
            "pendingLoadReady": pending_load_ready,
            "expectedPendingFire": expected_pending_fire,
            "expectedPendingHit": expected_pending_hit,
            "hostPendingFire": host_pending_fire,
            "clientPendingFire": client_pending_fire,
            "hostPendingHit": host_pending_hit,
            "clientPendingHit": client_pending_hit,
            "pendingLoadResponse": pending_load_response,
        })
        if ready and comparison.get("success") is True and active_status_ready and active_buff_ready and active_zone_ready and pending_load_ready:
            stable += 1
            if stable >= stable_samples:
                return host_state, client_state, comparison, True
        else:
            stable = 0
        time.sleep(1)
    return host_state, client_state, comparison, False


def poll_host_migration_after_battle(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    checkpoint_snapshot: dict[str, Any],
    killed_host_player_id: int,
    survivor_player_id: int,
    timeout: int,
    scene: str,
    expected_pending_fire: int = 0,
    expected_pending_hit: int = 0,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    result: dict[str, Any] = {"success": False, "errors": ["host_migration_not_observed"]}
    while time.time() < deadline:
        snapshot = dump_state(client, artifact_dir, "build-client", "post-host-migration-latest")
        hm = state(snapshot).get("hostMigration") or {}
        errors: list[str] = []
        if not snapshot_ready(snapshot, 2, scene):
            errors.extend(snapshot_not_ready_reasons(snapshot, 2, scene))
        if (hm.get("onHostMigrationCount") or 0) <= 0:
            errors.append("onHostMigrationCount_not_advanced")
        if (hm.get("nonNullTokenCount") or 0) <= 0:
            errors.append("nonNullTokenCount_not_advanced")
        if (hm.get("startGameSuccessCount") or 0) <= 0:
            errors.append("startGameSuccessCount_not_advanced")
        if (hm.get("resumeCount") or 0) <= 0:
            errors.append("resumeCount_not_advanced")
        if (hm.get("completeCount") or 0) <= 0:
            errors.append("completeCount_not_advanced")
        if hm.get("recoverySucceeded") is not True:
            errors.append(f"recoverySucceeded_not_true actual={hm.get('recoverySucceeded')}")
        if (hm.get("failureCount") or 0) > 0:
            errors.append(f"failureCount={hm.get('failureCount')}")
        if not unique_player_ids(snapshot):
            errors.append("player_ids_not_unique")
        body = state(snapshot)
        runner = body.get("runner") if isinstance(body, dict) else {}
        game = body.get("game") if isinstance(body, dict) else {}
        objects = body.get("objects") if isinstance(body, dict) else {}
        objects = objects if isinstance(objects, dict) else {}
        player_manager_count = objects.get("playerManagerCount")
        if player_manager_count != 2:
            errors.append(f"player_manager_count expected=2 actual={player_manager_count}")
        if runner.get("gameMode") != "Host" or runner.get("isServer") is not True:
            errors.append("survivor_not_promoted_to_host")
        if game.get("hasGameManagers") is not True:
            errors.append("game_managers_missing_after_migration")
        if not any(player.get("isConnected") is True and player.get("isAI") is False for player in players(snapshot)):
            errors.append("no_connected_human_survivor")
        survivor = player_by_id(snapshot, survivor_player_id)
        killed_host = player_by_id(snapshot, killed_host_player_id)
        if survivor is None:
            errors.append(f"survivor_player_missing:{survivor_player_id}")
        else:
            if survivor.get("isConnected") is not True:
                errors.append(f"survivor_not_connected:{survivor_player_id}")
            if survivor.get("isAI") is not False:
                errors.append(f"survivor_is_ai:{survivor_player_id}")
            if nested(survivor, "ai", "controllerRegistered") is not False:
                errors.append(f"survivor_ai_controller_registered:{survivor_player_id}")
        if killed_host is None:
            errors.append(f"killed_host_slot_missing:{killed_host_player_id}")
        else:
            if killed_host.get("isConnected") is not False:
                errors.append(f"killed_host_slot_connected:{killed_host_player_id}")
            if killed_host.get("isAI") is not True:
                errors.append(f"killed_host_slot_not_ai:{killed_host_player_id}")
            if nested(killed_host, "ai", "controllerRegistered") is not True:
                errors.append(f"killed_host_ai_controller_not_registered:{killed_host_player_id}")

        preservation = battle_preservation_assertions(
            checkpoint_snapshot,
            snapshot,
            "post_battle_host_migration",
            expected_players=2,
        )
        errors.extend(preservation.get("errors") or [])
        post_pending_fire = network_budget_int(snapshot, "currentPendingFireActive")
        post_pending_hit = network_budget_int(snapshot, "currentPendingHitActive")
        pending_load_after_migration = {
            "expectedPendingFire": expected_pending_fire,
            "expectedPendingHit": expected_pending_hit,
            "actualPendingFire": post_pending_fire,
            "actualPendingHit": post_pending_hit,
            "success": (
                post_pending_fire >= expected_pending_fire and
                post_pending_hit >= expected_pending_hit
            ),
        }
        if expected_pending_fire > 0 and post_pending_fire < expected_pending_fire:
            errors.append(
                f"pending_fire_not_restored expected>={expected_pending_fire} actual={post_pending_fire}"
            )
        if expected_pending_hit > 0 and post_pending_hit < expected_pending_hit:
            errors.append(
                f"pending_hit_not_restored expected>={expected_pending_hit} actual={post_pending_hit}"
            )

        result = {
            "success": not errors,
            "errors": errors,
            "hostMigration": hm,
            "postRunner": runner,
            "postGame": game,
            "survivorPlayerId": survivor_player_id,
            "killedHostPlayerId": killed_host_player_id,
            "survivor": survivor,
            "killedHost": killed_host,
            "battlePreservation": preservation,
            "pendingLoadAfterMigration": pending_load_after_migration,
        }
        write_json(artifact_dir / "post-battle-host-migration-latest.json", result)
        if result["success"]:
            return snapshot, result, True
        time.sleep(2)
    return snapshot, result, False


def probe_post_migration_portrait_navigation(
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    target_player_id: int,
    timeout: float = 5.0,
) -> dict[str, Any]:
    errors: list[str] = []
    deadline = time.time() + timeout
    readiness: dict[str, Any] = {}
    while time.time() < deadline:
        readiness = client.command(
            name="view_player_field",
            playerId=target_player_id,
            requestNavigation=False,
        )
        readiness_payload = readiness.get("data") if isinstance(readiness, dict) else None
        if (
            readiness.get("success") is True and
            isinstance(readiness_payload, dict) and
            readiness_payload.get("transitioning") is False
        ):
            break
        time.sleep(0.25)

    readiness_payload = readiness.get("data") if isinstance(readiness, dict) else None
    if readiness.get("success") is not True:
        error = readiness.get("error") or {}
        errors.append(f"portrait_view_readiness_failed:{error.get('code') or readiness.get('message')}")
    elif not isinstance(readiness_payload, dict) or readiness_payload.get("transitioning") is not False:
        errors.append("portrait_view_initial_transition_did_not_settle")

    request = client.command(
        name="view_player_field",
        playerId=target_player_id,
        requestNavigation=True,
    ) if not errors else {}
    write_json(artifact_dir / "post-migration-portrait-view-command.json", request)
    if request.get("success") is not True:
        error = request.get("error") or {}
        errors.append(f"portrait_view_command_failed:{error.get('code') or request.get('message')}")

    inspection: dict[str, Any] = request
    deadline = time.time() + timeout
    while not errors and time.time() < deadline:
        inspection = client.command(
            name="view_player_field",
            playerId=target_player_id,
            requestNavigation=False,
        )
        payload = inspection.get("data") if isinstance(inspection, dict) else None
        if (
            inspection.get("success") is True and
            isinstance(payload, dict) and
            payload.get("viewingPlayerId") == target_player_id and
            payload.get("currentViewingMatchesRegistry") is True and
            payload.get("targetOnCurrentRunner") is True and
            payload.get("transitioning") is False and
            payload.get("switched") is True
        ):
            break
        time.sleep(0.25)

    payload = inspection.get("data") if isinstance(inspection, dict) else None
    if not errors:
        if inspection.get("success") is not True:
            error = inspection.get("error") or {}
            errors.append(f"portrait_view_inspection_failed:{error.get('code') or inspection.get('message')}")
        elif not isinstance(payload, dict):
            errors.append("portrait_view_payload_missing")
        else:
            if payload.get("viewingPlayerId") != target_player_id:
                errors.append(
                    f"portrait_viewing_player_mismatch expected={target_player_id} actual={payload.get('viewingPlayerId')}"
                )
            if payload.get("currentViewingMatchesRegistry") is not True:
                errors.append("portrait_view_not_bound_to_current_registry")
            if payload.get("targetOnCurrentRunner") is not True:
                errors.append("portrait_view_target_not_on_current_runner")
            if payload.get("transitioning") is not False:
                errors.append("portrait_view_transition_did_not_complete")
            if payload.get("switched") is not True:
                errors.append("portrait_view_not_switched")

    report = {
        "success": not errors,
        "errors": errors,
        "targetPlayerId": target_player_id,
        "readiness": readiness,
        "request": request,
        "inspection": inspection,
    }
    write_json(artifact_dir / "post-migration-portrait-view-result.json", report)
    return report


def battle_takeover_assertions(
    snapshot: dict[str, Any],
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    scene: str,
    expected_players: int,
) -> dict[str, Any]:
    body = state(snapshot)
    runner = body.get("runner") if isinstance(body, dict) else {}
    target = player_by_id(snapshot, target_player_id)
    ai = target.get("ai") if isinstance(target, dict) else {}
    preservation = battle_preservation_assertions(
        checkpoint_snapshot,
        snapshot,
        "post_battle_disconnect_takeover",
        expected_players=expected_players,
    )
    errors: list[str] = []
    if not scene_matches(body.get("scene"), scene):
        errors.append(f"scene expected={scene} actual={body.get('scene')}")
    if runner.get("activePlayerCount") != expected_players - 1:
        errors.append(f"activePlayerCount expected={expected_players - 1} actual={runner.get('activePlayerCount')}")
    if len(players(snapshot)) != expected_players:
        errors.append(f"player_count expected={expected_players} actual={len(players(snapshot))}")
    if not unique_player_ids(snapshot):
        errors.append("player_ids_not_unique")
    if target is None:
        errors.append(f"target_missing:{target_player_id}")
    else:
        if target.get("isAI") is not True:
            errors.append(f"target_is_ai expected=True actual={target.get('isAI')}")
        if target.get("isConnected") is not False:
            errors.append(f"target_connected expected=False actual={target.get('isConnected')}")
        if ai.get("controllerRegistered") is not True:
            errors.append(f"target_ai_controller_registered expected=True actual={ai.get('controllerRegistered')}")
    errors.extend(preservation.get("errors") or [])
    return {
        "success": not errors,
        "errors": errors,
        "warnings": preservation.get("warnings") or [],
        "runner": runner,
        "targetPlayerId": target_player_id,
        "target": target,
        "battlePreservation": preservation,
    }


def wait_battle_takeover(
    host: AutomationClient,
    artifact_dir: pathlib.Path,
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    snapshot: dict[str, Any] = {}
    latest: dict[str, Any] = {"success": False, "errors": ["not_started"]}
    stable = 0
    while time.time() < deadline:
        snapshot = dump_state(host, artifact_dir, "build-host", "post-disconnect-latest")
        latest = battle_takeover_assertions(snapshot, checkpoint_snapshot, target_player_id, scene, expected_players)
        write_json(artifact_dir / "post-battle-disconnect-takeover-latest.json", {
            "ready": latest["success"],
            "assertions": latest,
            "stableMatches": stable,
        })
        if latest["success"]:
            stable += 1
            if stable >= 2:
                return snapshot, latest, True
        else:
            stable = 0
        time.sleep(2)
    return snapshot, latest, False


def reconnect_after_battle_assertions(
    host_snapshot: dict[str, Any],
    client_snapshot: dict[str, Any],
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
    scene: str,
    expected_players: int,
) -> dict[str, Any]:
    host_body = state(host_snapshot)
    client_body = state(client_snapshot)
    host_runner = host_body.get("runner") if isinstance(host_body, dict) else {}
    client_runner = client_body.get("runner") if isinstance(client_body, dict) else {}
    host_target = player_by_id(host_snapshot, target_player_id)
    client_target = player_by_id(client_snapshot, target_player_id)
    client_local = local_player(client_snapshot)
    comparison = compare_snapshots(host_snapshot, client_snapshot) if (
        snapshot_ready(host_snapshot, expected_players, scene)
        and snapshot_ready(client_snapshot, expected_players, scene)
    ) else {"success": False, "errors": ["snapshot_not_ready"], "warnings": []}
    preservation = battle_preservation_assertions(
        checkpoint_snapshot,
        host_snapshot,
        "post_battle_reconnect",
        expected_players=expected_players,
    )
    errors: list[str] = []
    if not scene_matches(host_body.get("scene"), scene) or not scene_matches(client_body.get("scene"), scene):
        errors.append(f"scene_mismatch host={host_body.get('scene')} client={client_body.get('scene')} expected={scene}")
    if host_runner.get("activePlayerCount") != expected_players:
        errors.append(f"host.activePlayerCount expected={expected_players} actual={host_runner.get('activePlayerCount')}")
    if client_runner.get("activePlayerCount") != expected_players:
        errors.append(f"client.activePlayerCount expected={expected_players} actual={client_runner.get('activePlayerCount')}")
    if not unique_player_ids(host_snapshot):
        errors.append("host.player_ids_not_unique")
    if not unique_player_ids(client_snapshot):
        errors.append("client.player_ids_not_unique")
    if host_target is None:
        errors.append(f"host_target_missing:{target_player_id}")
    else:
        if host_target.get("isAI") is not False:
            errors.append(f"host_target_is_ai expected=False actual={host_target.get('isAI')}")
        if host_target.get("isConnected") is not True:
            errors.append(f"host_target_connected expected=True actual={host_target.get('isConnected')}")
        if nested(host_target, "ai", "controllerRegistered") is not False:
            errors.append(f"host_target_ai_controller_registered actual={nested(host_target, 'ai', 'controllerRegistered')}")
    if client_target is None:
        errors.append(f"client_target_missing:{target_player_id}")
    else:
        if client_target.get("isAI") is not False:
            errors.append(f"client_target_is_ai expected=False actual={client_target.get('isAI')}")
        if client_target.get("isConnected") is not True:
            errors.append(f"client_target_connected expected=True actual={client_target.get('isConnected')}")
        if nested(client_target, "ai", "controllerRegistered") is not False:
            errors.append(f"client_target_ai_controller_registered actual={nested(client_target, 'ai', 'controllerRegistered')}")
    if not isinstance(client_local, dict):
        errors.append("client_local_player_missing")
    else:
        if client_local.get("playerId") != target_player_id:
            errors.append(f"client_local_playerId expected={target_player_id} actual={client_local.get('playerId')}")
        if client_local.get("connectionTokenHash") != expected_token_hash:
            errors.append(
                "client_connection_token_hash "
                f"expected={expected_token_hash} actual={client_local.get('connectionTokenHash')}"
            )
        if client_local.get("isConnected") is not True:
            errors.append(f"client_local_connected expected=True actual={client_local.get('isConnected')}")
        if client_local.get("isAI") is not False:
            errors.append(f"client_local_is_ai expected=False actual={client_local.get('isAI')}")
        if nested(client_local, "ai", "controllerRegistered") is not False:
            errors.append(f"client_local_ai_controller_registered actual={nested(client_local, 'ai', 'controllerRegistered')}")
    if isinstance(host_target, dict) and isinstance(client_target, dict):
        for field, path in (
            ("isConnected", ("isConnected",)),
            ("isAI", ("isAI",)),
            ("ai.controllerRegistered", ("ai", "controllerRegistered")),
        ):
            host_value = nested(host_target, *path)
            client_value = nested(client_target, *path)
            if host_value != client_value:
                errors.append(f"target_role_state.{field} host={host_value} client={client_value}")
    if comparison.get("success") is not True:
        errors.extend(f"snapshot_comparison:{err}" for err in comparison.get("errors") or ["failed"])
    errors.extend(preservation.get("errors") or [])
    return {
        "success": not errors,
        "errors": errors,
        "warnings": list(comparison.get("warnings") or []) + list(preservation.get("warnings") or []),
        "targetPlayerId": target_player_id,
        "expectedTokenHash": expected_token_hash,
        "hostRunner": host_runner,
        "clientRunner": client_runner,
        "clientLocalPlayer": client_local,
        "hostTarget": host_target,
        "clientTarget": client_target,
        "fullComparison": comparison,
        "battlePreservation": preservation,
    }


def wait_reconnect_after_battle(
    host: AutomationClient,
    client: AutomationClient,
    artifact_dir: pathlib.Path,
    checkpoint_snapshot: dict[str, Any],
    target_player_id: int,
    expected_token_hash: str,
    timeout: int,
    scene: str,
    expected_players: int,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any], bool]:
    deadline = time.time() + timeout
    host_state: dict[str, Any] = {}
    client_state: dict[str, Any] = {}
    latest: dict[str, Any] = {"success": False, "errors": ["not_started"]}
    stable = 0
    while time.time() < deadline:
        host_state = dump_state(host, artifact_dir, "build-host", "reconnect-latest")
        client_state = dump_state(client, artifact_dir, "build-client-b", "reconnect-latest")
        latest = reconnect_after_battle_assertions(
            host_state,
            client_state,
            checkpoint_snapshot,
            target_player_id,
            expected_token_hash,
            scene,
            expected_players,
        )
        write_json(artifact_dir / "post-battle-reconnect-latest.json", {
            "ready": latest["success"],
            "assertions": latest,
            "stableMatches": stable,
        })
        write_json(artifact_dir / "full-comparison-latest.json", latest["fullComparison"])
        write_json(artifact_dir / "battle-preservation-latest.json", latest["battlePreservation"])
        if latest["success"]:
            stable += 1
            if stable >= 2:
                return host_state, client_state, latest, True
        else:
            stable = 0
        time.sleep(2)
    return host_state, client_state, latest, False


def run_battle_client_lifecycle_case(
    args: argparse.Namespace,
    *,
    case_name: str,
    reconnect: bool,
) -> int:
    normalize_common_args(args)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(case_name, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("battle-life")
    host_token = new_token()
    client_a_token = new_token()
    client_b_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    client_connection_hash = hash_for_log(client_connection)
    host_port = free_port()
    client_a_port = free_port()
    client_b_port = free_port()
    host_bot_seed = args.host_bot_seed if args.host_bot_seed is not None else args.seed
    host_journal_path = artifact_dir / "build-host-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_a_proc: PlayerProcess | None = None
    client_b_proc: PlayerProcess | None = None
    failures: list[str] = []
    target_player_id = -1
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        [host_token, client_a_token, client_b_token, host_connection, client_connection],
    )

    write_json(artifact_dir / "run.json", {
        "case": case_name,
        "session": session,
        "playerPath": str(player_path),
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "seed": args.seed,
        "hostBotSeed": host_bot_seed,
        "botPrepareMode": args.bot_prepare_mode,
        "preferScrollAugment": bool(args.prefer_scroll_augment),
        "reconnect": reconnect,
        "clientConnectionTokenHash": client_connection_hash,
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
        "orphanPressure": orphan_gate,
    })
    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")
        cleanup_report = {
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "cleanupSuccess": False,
            "orphanedPids": [
                proc.get("pid")
                for proc in orphan_gate.get("processes", [])
                if isinstance(proc, dict) and isinstance(proc.get("pid"), int)
            ],
        }
        write_json(artifact_dir / "result.json", {
            "case": case_name,
            "artifactDir": str(artifact_dir),
            "success": False,
            "functionalSuccess": False,
            "cleanupSuccess": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "orphanPressure": orphan_gate,
            "headlessPlayer": args.headless_player,
            "failures": failures,
        })
        failure_summary(artifact_dir / "failure-summary.md", f"{case_name} blocked", failures)
        print(json.dumps({
            "artifactDir": str(artifact_dir),
            "failures": failures,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
        }, indent=2))
        return 2
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": case_name, "dryRun": True}, indent=2))
        return 0

    try:
        host_extra_args = [
            "--mpDisableAiFill",
            "--mpHumanBot",
            "--mpBotPersona", args.host_bot_persona,
            "--mpBotSeed", str(host_bot_seed),
            "--mpBotDurationSeconds", str(args.bot_duration_seconds),
            "--mpBotStopAtRound", "0",
            "--mpBotMaxCommands", str(args.bot_max_commands),
            "--mpBotRecordJournal", str(host_journal_path),
        ]
        if args.bot_prepare_mode == "skip":
            host_extra_args.append("--mpBotSkipPrepare")
        elif args.bot_prepare_mode == "augment-only":
            host_extra_args.append("--mpBotPrepareAugmentOnly")
        if args.prefer_scroll_augment:
            host_extra_args.append("--mpBotPreferScrollAugment")

        host_proc = launch_player(
            player_path,
            "host",
            session,
            host_port,
            host_token,
            host_connection,
            artifact_dir,
            "build-host",
            max_players=3,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario=case_name,
            extra_args=host_extra_args,
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")
        write_json(artifact_dir / "build-host-bot-paused.json", host.bot_stop(reason="phase12_pre_checkpoint_pause"))

        client_a_proc = launch_player(
            player_path,
            "client",
            session,
            client_a_port,
            client_a_token,
            client_connection,
            artifact_dir,
            "build-client-a",
            max_players=3,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario=case_name,
            extra_args=["--mpDisableAiFill"],
            headless_player=args.headless_player,
        )
        client_a = AutomationClient(client_a_port, client_a_token, timeout=args.request_timeout)
        client_a_ping = client_a.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-a-ping.json", client_a_ping)
        if not client_a_ping.get("success"):
            failures.append("client_a_automation_ping_timeout")

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, 3, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client_a.join, artifact_dir, "build-client-a", session, args.lobby_scene, 3, args.start_timeout):
            failures.append("client_a_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(host, client_a, artifact_dir, args.lobby_timeout, args.lobby_scene)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-a-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_before, client_before, before_comparison, before_ready = wait_stable_states(
            host,
            client_a,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-battle-bot",
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-battle-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-a-before-battle-bot.json", client_before)
        write_json(artifact_dir / "before-battle-bot-comparison.json", before_comparison)
        if not before_ready:
            failures.append("before_battle_bot_state_ready_timeout")

        write_json(artifact_dir / "build-host-bot-start.json", host.bot_start(
            persona=args.host_bot_persona,
            seed=host_bot_seed,
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=0,
            maxCommands=args.bot_max_commands,
            skipPrepare=args.bot_prepare_mode == "skip",
            prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
            preferScrollAugment=args.prefer_scroll_augment,
            journalPath=str(host_journal_path),
        ))

        host_battle, client_battle, evidence, progressed = wait_battle_progression(
            host,
            client_a,
            artifact_dir,
            args.battle_timeout,
            args.scene,
            args.min_bot_commands,
            args.stable_samples,
            True,
            False,
            True,
            False,
        )
        write_json(artifact_dir / "snapshots" / "build-host-battle-progressed.json", host_battle)
        write_json(artifact_dir / "snapshots" / "build-client-a-battle-progressed.json", client_battle)
        write_json(artifact_dir / "battle-command-evidence.json", evidence)
        if not progressed:
            failures.append("battle_progression_timeout")
            failures.extend(evidence.get("errors") or [])
            failure_summary(artifact_dir / "failure-summary.md", f"{case_name} failed", failures)
            write_json(artifact_dir / "result.json", {"success": False, "failures": failures, "artifactDir": str(artifact_dir)})
            return 1

        host_checkpoint, client_checkpoint, checkpoint_comparison, freeze_ok = freeze_battle_checkpoint(
            host,
            client_a,
            artifact_dir,
            args.scene,
            False,
            args.stable_samples,
            "phase12_pre_client_lifecycle_battle_checkpoint",
            client_peer="build-client-a",
        )
        write_json(artifact_dir / "snapshots" / "build-host-battle-checkpoint-frozen.json", host_checkpoint)
        write_json(artifact_dir / "snapshots" / "build-client-a-battle-checkpoint-frozen.json", client_checkpoint)
        write_json(artifact_dir / "battle-checkpoint-frozen-comparison.json", checkpoint_comparison)
        if not freeze_ok:
            failures.append("battle_checkpoint_freeze_timeout")
            failures.extend(f"battle_checkpoint_freeze:{err}" for err in checkpoint_comparison.get("errors") or [])

        client_local = local_player(client_checkpoint)
        if isinstance(client_local, dict) and isinstance(client_local.get("playerId"), int):
            target_player_id = int(client_local["playerId"])
        else:
            failures.append("target_player_id_invalid")
        write_json(artifact_dir / "client-lifecycle-target.json", {
            "playerId": target_player_id,
            "connectionTokenHash": client_local.get("connectionTokenHash") if isinstance(client_local, dict) else "unknown",
            "expectedConnectionTokenHash": client_connection_hash,
        })
        write_json(artifact_dir / "checkpoint-summary.json", {
            "battleProgressed": {
                "host": "snapshots/build-host-battle-progressed.json",
                "client": "snapshots/build-client-a-battle-progressed.json",
                "evidence": "battle-command-evidence.json",
            },
            "battleCheckpointFrozen": {
                "host": "snapshots/build-host-battle-checkpoint-frozen.json",
                "client": "snapshots/build-client-a-battle-checkpoint-frozen.json",
                "comparison": "battle-checkpoint-frozen-comparison.json",
            },
        })

        if client_a_proc is not None:
            client_a_proc.process.kill()
            client_a_proc.process.wait(timeout=10)
            client_a_proc.close_logs()

        if target_player_id >= 0:
            host_takeover, takeover, takeover_ok = wait_battle_takeover(
                host,
                artifact_dir,
                host_checkpoint,
                target_player_id,
                args.takeover_timeout,
                args.scene,
                2,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-disconnect-takeover.json", host_takeover)
            write_json(artifact_dir / "post-battle-disconnect-takeover-assertions.json", takeover)
            write_json(artifact_dir / "battle-preservation-assertions.json", takeover.get("battlePreservation"))
            if not takeover_ok:
                failures.append("post_battle_disconnect_ai_takeover_timeout")
                failures.extend(takeover.get("errors") or [])

        if reconnect and not failures and target_player_id >= 0:
            client_b_proc = launch_player(
                player_path,
                "client",
                session,
                client_b_port,
                client_b_token,
                client_connection,
                artifact_dir,
                "build-client-b",
                max_players=3,
                scene=args.lobby_scene,
                case_name=case_name,
                auto_start=False,
                load_game=False,
                seed=args.seed + 2,
                scenario=case_name,
                extra_args=["--mpDisableAiFill", "--mpFreezeGameFlow"],
                headless_player=args.headless_player,
            )
            client_b = AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout)
            client_b_ping = client_b.wait_ping(timeout_seconds=args.ping_timeout)
            write_json(artifact_dir / "build-client-b-ping.json", client_b_ping)
            if not client_b_ping.get("success"):
                failures.append("client_b_automation_ping_timeout")

            if not wait_build_peer_started(client_b.join, artifact_dir, "build-client-b", session, args.scene, 3, args.start_timeout):
                failures.append("client_b_rejoin_timeout")

            host_final, client_final, reconnect_assertions, reconnect_ok = wait_reconnect_after_battle(
                host,
                client_b,
                artifact_dir,
                host_checkpoint,
                target_player_id,
                client_connection_hash,
                args.reconnect_timeout,
                args.scene,
                2,
            )
            write_json(artifact_dir / "snapshots" / "build-host-post-reconnect.json", host_final)
            write_json(artifact_dir / "snapshots" / "build-client-b-post-reconnect.json", client_final)
            write_json(artifact_dir / "post-battle-reconnect-assertions.json", reconnect_assertions)
            write_json(artifact_dir / "full-comparison.json", reconnect_assertions.get("fullComparison"))
            write_json(artifact_dir / "battle-preservation-assertions.json", reconnect_assertions.get("battlePreservation"))
            if not reconnect_ok:
                failures.append("post_battle_same_token_reconnect_timeout")
                failures.extend(reconnect_assertions.get("errors") or [])

        if args.headless_player:
            write_json(artifact_dir / "build-host-screenshot.json", {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            })
        else:
            write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
        if reconnect and client_b_proc is not None:
            client_b = AutomationClient(client_b_port, client_b_token, timeout=args.request_timeout)
            if args.headless_player:
                write_json(artifact_dir / "build-client-b-screenshot.json", {
                    "success": True,
                    "skipped": True,
                    "reason": "headless_player",
                    "headlessPlayer": True,
                })
            else:
                write_json(artifact_dir / "build-client-b-screenshot.json", client_b.screenshot())
            client_logs = client_b.logs_recent()
            write_json(artifact_dir / "build-client-b-logs-recent.json", client_logs)
        else:
            client_logs = {"success": True, "data": {"lines": []}}
        host_logs = host.logs_recent()
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_logs))

        write_json(artifact_dir / "result.json", {
            "case": case_name,
            "success": not failures,
            "failures": failures,
            "artifactDir": str(artifact_dir),
            "targetPlayerId": target_player_id,
            "reconnect": reconnect,
            "headlessPlayer": args.headless_player,
        })
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{case_name} failed", failures)
    finally:
        functional_failures = list(failures)
        cleanup_processes = [proc for proc in (client_b_proc, host_proc, client_a_proc) if proc is not None]
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            cleanup_processes,
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(functional_failures),
            strict_cleanup=args.strict_cleanup,
        )
        apply_cleanup_result(artifact_dir, cleanup_report, args.strict_cleanup, functional_failures, failures)

        host_log = collect_player_log(artifact_dir, "build-host-or-last")
        logs = [
            artifact_dir / "build-host.Player.log",
            artifact_dir / "build-client-a.Player.log",
            artifact_dir / "build-client-b.Player.log",
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client-a.stdout.log",
            artifact_dir / "build-client-a.stderr.log",
            artifact_dir / "build-client-b.stdout.log",
            artifact_dir / "build-client-b.stderr.log",
        ]
        if host_log:
            logs.append(host_log)
        write_timeline(artifact_dir, logs)
        write_bot_metrics_artifact(artifact_dir)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "failures": failures,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
    }, indent=2))
    return 0 if not failures else 1


def run_battle_case(
    args: argparse.Namespace,
    *,
    case_name: str,
    require_spawn: bool = False,
    require_scroll: bool = False,
    require_any_battle_command: bool = True,
    migrate_after_battle: bool = False,
    apply_status_before_migration: bool = False,
    apply_stat_buff_before_migration: bool = False,
    apply_zone_before_migration: bool = False,
    inject_pending_load_before_migration: bool = False,
    probe_portrait_after_migration: bool = False,
) -> int:
    normalize_common_args(args)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(case_name, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    session = args.session or new_session("battle")
    host_token = new_token()
    client_token = new_token()
    host_connection = new_token()
    client_connection = new_token()
    host_port = free_port()
    client_port = free_port()
    host_bot_seed = args.host_bot_seed if args.host_bot_seed is not None else args.seed
    client_bot_seed = args.client_bot_seed if args.client_bot_seed is not None else args.seed + 1
    client_human_bot = bool(getattr(args, "client_human_bot", False))
    verify_king_skill = bool(getattr(args, "verify_king_skill", False))
    host_journal_path = artifact_dir / "build-host-bot.jsonl"
    client_journal_path = artifact_dir / "build-client-bot.jsonl"
    host_proc: PlayerProcess | None = None
    client_proc: PlayerProcess | None = None
    host_was_killed = False
    portrait_view_result: dict[str, Any] | None = None
    failures: list[str] = []
    cleanup_baseline_pids = mdf_player_pids()
    cleanup_report: dict[str, Any] = {
        "cleanupStatus": "PASS",
        "cleanupSuccess": True,
        "orphanedPids": [],
    }
    orphan_gate = write_orphan_pressure_report(
        artifact_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
        [host_token, client_token, host_connection, client_connection],
    )

    run_config = {
        "case": case_name,
        "session": session,
        "playerPath": str(player_path),
        "scene": args.scene,
        "lobbyScene": args.lobby_scene,
        "seed": args.seed,
        "hostBotSeed": host_bot_seed,
        "clientBotSeed": client_bot_seed,
        "clientHumanBot": client_human_bot,
        "botPrepareMode": args.bot_prepare_mode,
        "preferScrollAugment": bool(args.prefer_scroll_augment),
        "requireSpawn": require_spawn,
        "requireScroll": require_scroll,
        "requireAnyBattleCommand": require_any_battle_command,
        "migrateAfterBattle": migrate_after_battle,
        "applyStatusBeforeMigration": apply_status_before_migration,
        "applyStatBuffBeforeMigration": apply_stat_buff_before_migration,
        "applyZoneBeforeMigration": apply_zone_before_migration,
        "injectPendingLoadBeforeMigration": inject_pending_load_before_migration,
        "probePortraitAfterMigration": probe_portrait_after_migration,
        "pendingFireCount": int(getattr(args, "pending_fire_count", 0)),
        "pendingHitCount": int(getattr(args, "pending_hit_count", 0)),
        "pendingDelayTicks": int(getattr(args, "pending_delay_ticks", 0)),
        "dryRun": args.dry_run,
        "headlessPlayer": args.headless_player,
        "cleanup": {
            "baselinePids": sorted(cleanup_baseline_pids),
            "timeoutSeconds": args.cleanup_timeout_seconds,
            "leaveProcessesOnFail": args.leave_processes_on_fail,
            "strictCleanup": args.strict_cleanup,
        },
        "orphanPressure": orphan_gate,
    }
    if verify_king_skill:
        run_config["verifyKingSkill"] = True
    write_json(artifact_dir / "run.json", run_config)
    if orphan_gate.get("blocked") and not args.dry_run:
        failures.append("orphan_pressure_gate_blocked")
        cleanup_report = {
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "cleanupSuccess": False,
            "orphanedPids": [
                proc.get("pid")
                for proc in orphan_gate.get("processes", [])
                if isinstance(proc, dict) and isinstance(proc.get("pid"), int)
            ],
        }
        write_json(artifact_dir / "result.json", {
            "case": case_name,
            "artifactDir": str(artifact_dir),
            "success": False,
            "functionalSuccess": False,
            "cleanupSuccess": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "orphanPressure": orphan_gate,
            "headlessPlayer": args.headless_player,
            "failures": failures,
        })
        failure_summary(artifact_dir / "failure-summary.md", f"{case_name} blocked", failures)
        print(json.dumps({
            "artifactDir": str(artifact_dir),
            "failures": failures,
            "cleanupStatus": cleanup_report.get("cleanupStatus"),
            "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
        }, indent=2))
        return 2
    if args.dry_run:
        print(json.dumps({"artifactDir": str(artifact_dir), "case": case_name, "dryRun": True}, indent=2))
        return 0

    try:
        host_extra_args = [
            "--mpHumanBot",
            "--mpBotPersona", args.host_bot_persona,
            "--mpBotSeed", str(host_bot_seed),
            "--mpBotDurationSeconds", str(args.bot_duration_seconds),
            "--mpBotStopAtRound", "0",
            "--mpBotMaxCommands", str(args.bot_max_commands),
            "--mpBotRecordJournal", str(host_journal_path),
        ]
        if args.bot_prepare_mode == "skip":
            host_extra_args.append("--mpBotSkipPrepare")
        elif args.bot_prepare_mode == "augment-only":
            host_extra_args.append("--mpBotPrepareAugmentOnly")
        if args.prefer_scroll_augment:
            host_extra_args.append("--mpBotPreferScrollAugment")

        host_proc = launch_player(
            player_path,
            "host",
            session,
            host_port,
            host_token,
            host_connection,
            artifact_dir,
            "build-host",
            max_players=2,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed,
            scenario=case_name,
            extra_args=host_extra_args,
            headless_player=args.headless_player,
        )
        host = AutomationClient(host_port, host_token, timeout=args.request_timeout)
        host_ping = host.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-host-ping.json", host_ping)
        if not host_ping.get("success"):
            failures.append("host_automation_ping_timeout")
        write_json(artifact_dir / "build-host-bot-paused.json", host.bot_stop(reason="phase11_pre_checkpoint_pause"))

        client_extra_args: list[str] = []
        if client_human_bot:
            client_extra_args = [
                "--mpHumanBot",
                "--mpBotPersona", args.client_bot_persona,
                "--mpBotSeed", str(client_bot_seed),
                "--mpBotDurationSeconds", str(args.bot_duration_seconds),
                "--mpBotStopAtRound", "0",
                "--mpBotMaxCommands", str(args.bot_max_commands),
                "--mpBotRecordJournal", str(client_journal_path),
            ]
            if args.bot_prepare_mode == "skip":
                client_extra_args.append("--mpBotSkipPrepare")
            elif args.bot_prepare_mode == "augment-only":
                client_extra_args.append("--mpBotPrepareAugmentOnly")
            if args.prefer_scroll_augment:
                client_extra_args.append("--mpBotPreferScrollAugment")

        client_proc = launch_player(
            player_path,
            "client",
            session,
            client_port,
            client_token,
            client_connection,
            artifact_dir,
            "build-client",
            max_players=2,
            scene=args.lobby_scene,
            case_name=case_name,
            auto_start=False,
            load_game=False,
            seed=args.seed + 1,
            scenario=case_name,
            extra_args=client_extra_args,
            headless_player=args.headless_player,
        )
        client = AutomationClient(client_port, client_token, timeout=args.request_timeout)
        client_ping = client.wait_ping(timeout_seconds=args.ping_timeout)
        write_json(artifact_dir / "build-client-ping.json", client_ping)
        if not client_ping.get("success"):
            failures.append("client_automation_ping_timeout")
        if client_human_bot:
            write_json(artifact_dir / "build-client-bot-paused.json", client.bot_stop(reason="phase11_pre_checkpoint_pause"))
        else:
            write_json(artifact_dir / "build-client-bot-paused.json", {
                "success": True,
                "message": "client HumanBot disabled; observer peer",
            })

        if not wait_build_peer_started(host.start_host, artifact_dir, "build-host", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("host_start_timeout")
        if not wait_build_peer_started(client.join, artifact_dir, "build-client", session, args.lobby_scene, 2, args.start_timeout):
            failures.append("client_join_timeout")

        host_lobby, client_lobby, lobby_ready = wait_session_states(host, client, artifact_dir, args.lobby_timeout, args.lobby_scene)
        write_json(artifact_dir / "snapshots" / "build-host-lobby.json", host_lobby)
        write_json(artifact_dir / "snapshots" / "build-client-lobby.json", client_lobby)
        if not lobby_ready:
            failures.append("session_join_timeout")

        load_result = host.load_game(args.scene)
        write_json(artifact_dir / "build-host-load-game.json", load_result)
        if not load_result.get("success"):
            failures.append("host_load_game_failed")

        host_before, client_before, before_comparison, before_ready = wait_stable_states(
            host,
            client,
            artifact_dir,
            args.state_timeout,
            args.scene,
            "before-battle-bot",
        )
        write_json(artifact_dir / "snapshots" / "build-host-before-battle-bot.json", host_before)
        write_json(artifact_dir / "snapshots" / "build-client-before-battle-bot.json", client_before)
        write_json(artifact_dir / "before-battle-bot-comparison.json", before_comparison)
        if not before_ready:
            failures.append("before_battle_bot_state_ready_timeout")

        write_json(artifact_dir / "build-host-bot-start.json", host.bot_start(
            persona=args.host_bot_persona,
            seed=host_bot_seed,
            durationSeconds=args.bot_duration_seconds,
            stopAtRound=0,
            maxCommands=args.bot_max_commands,
            skipPrepare=args.bot_prepare_mode == "skip",
            prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
            preferScrollAugment=args.prefer_scroll_augment,
            journalPath=str(host_journal_path),
        ))
        if client_human_bot:
            write_json(artifact_dir / "build-client-bot-start.json", client.bot_start(
                persona=args.client_bot_persona,
                seed=client_bot_seed,
                durationSeconds=args.bot_duration_seconds,
                stopAtRound=0,
                maxCommands=args.bot_max_commands,
                skipPrepare=args.bot_prepare_mode == "skip",
                prepareAugmentOnly=args.bot_prepare_mode == "augment-only",
                preferScrollAugment=args.prefer_scroll_augment,
                journalPath=str(client_journal_path),
            ))
        else:
            write_json(artifact_dir / "build-client-bot-start.json", {
                "success": True,
                "message": "client HumanBot disabled; observer peer",
            })

        host_battle, client_battle, assertions, progressed = wait_battle_progression(
            host,
            client,
            artifact_dir,
            args.battle_timeout,
            args.scene,
            args.min_bot_commands,
            args.stable_samples,
            require_spawn,
            require_scroll,
            require_any_battle_command,
            client_human_bot,
            verify_king_skill=verify_king_skill,
        )
        write_json(artifact_dir / "snapshots" / "build-host-battle-progressed.json", host_battle)
        write_json(artifact_dir / "snapshots" / "build-client-battle-progressed.json", client_battle)
        write_json(artifact_dir / "battle-command-evidence.json", assertions)
        checkpoint_summary = {
            "beforeBattleBot": {
                "host": "snapshots/build-host-before-battle-bot.json",
                "client": "snapshots/build-client-before-battle-bot.json",
                "comparison": "before-battle-bot-comparison.json",
            },
            "battleStart": {
                "host": "snapshots/build-host-battle-start.json",
                "client": "snapshots/build-client-battle-start.json",
                "comparison": "battle-start-comparison.json",
            },
            "battleProgressed": {
                "host": "snapshots/build-host-battle-progressed.json",
                "client": "snapshots/build-client-battle-progressed.json",
                "evidence": "battle-command-evidence.json",
            },
        }
        if verify_king_skill:
            checkpoint_summary["kingSkillVerification"] = {
                "evidence": "king-skill-verification.json",
            }
        write_json(artifact_dir / "checkpoint-summary.json", checkpoint_summary)
        if not progressed:
            failures.append("battle_progression_timeout")
            failures.extend(assertions.get("errors") or [])

        if migrate_after_battle and progressed:
            status_effect_payload = {
                "targetKind": "monster",
                "effectType": "Slowed",
                "durationSeconds": 180,
                "slowMultiplier": 0.5,
            } if apply_status_before_migration else None
            stat_buff_payload = {
                "targetKind": "monster",
                "statType": "MoveSpeed",
                "durationSeconds": 180,
                "value": 0.5,
                "isPercentage": True,
            } if apply_stat_buff_before_migration else None
            zone_payload = {
                "targetKind": "monster",
                "durationSeconds": 180,
                "tickIntervalSeconds": 3,
                "range": 3,
            } if apply_zone_before_migration else None
            pending_load_payload = {
                "pendingFireCount": int(getattr(args, "pending_fire_count", 48)),
                "pendingHitCount": int(getattr(args, "pending_hit_count", 72)),
                "delayTicks": int(getattr(args, "pending_delay_ticks", 3600)),
                "clearExisting": True,
            } if inject_pending_load_before_migration else None
            host_battle, client_battle, freeze_comparison, freeze_ok = freeze_battle_checkpoint(
                host,
                client,
                artifact_dir,
                args.scene,
                client_human_bot,
                args.stable_samples,
                "phase12_pre_host_migration_battle_checkpoint",
                status_effect_payload=status_effect_payload,
                require_active_status=apply_status_before_migration,
                stat_buff_payload=stat_buff_payload,
                require_active_buff=apply_stat_buff_before_migration,
                zone_payload=zone_payload,
                require_active_zone=apply_zone_before_migration,
                pending_load_payload=pending_load_payload,
                require_pending_load=inject_pending_load_before_migration,
            )
            write_json(artifact_dir / "snapshots" / "build-host-battle-checkpoint-frozen.json", host_battle)
            write_json(artifact_dir / "snapshots" / "build-client-battle-checkpoint-frozen.json", client_battle)
            write_json(artifact_dir / "battle-checkpoint-frozen-comparison.json", freeze_comparison)
            if not freeze_ok:
                failures.append("battle_checkpoint_freeze_timeout")
                failures.extend(f"battle_checkpoint_freeze:{err}" for err in freeze_comparison.get("errors") or [])
            host_local = local_player(host_battle)
            survivor_local = local_player(client_battle)
            killed_host_player_id = host_local.get("playerId") if isinstance(host_local, dict) else None
            survivor_player_id = survivor_local.get("playerId") if isinstance(survivor_local, dict) else None
            write_json(artifact_dir / "host-migration-role-targets.json", {
                "killedHostPlayerId": killed_host_player_id,
                "survivorPlayerId": survivor_player_id,
                "hostLocal": host_local,
                "survivorLocal": survivor_local,
            })
            if not isinstance(killed_host_player_id, int) or killed_host_player_id < 0:
                failures.append("killed_host_player_id_invalid")
            if not isinstance(survivor_player_id, int) or survivor_player_id < 0:
                failures.append("survivor_player_id_invalid")

            host_proc.process.kill()
            host_proc.process.wait(timeout=10)
            host_proc.close_logs()
            host_was_killed = True
            post_migration_snapshot, migration_result, migration_ok = poll_host_migration_after_battle(
                client,
                artifact_dir,
                client_battle,
                int(killed_host_player_id) if isinstance(killed_host_player_id, int) else -1,
                int(survivor_player_id) if isinstance(survivor_player_id, int) else -1,
                args.host_migration_timeout,
                args.scene,
                int(pending_load_payload.get("pendingFireCount", 0)) if pending_load_payload else 0,
                int(pending_load_payload.get("pendingHitCount", 0)) if pending_load_payload else 0,
            )
            write_json(artifact_dir / "snapshots" / "build-client-post-host-migration.json", post_migration_snapshot)
            write_json(artifact_dir / "post-battle-host-migration-result.json", migration_result)
            if not migration_ok:
                failures.append("post_battle_host_migration_failed")
                failures.extend(migration_result.get("errors") or [])
            elif probe_portrait_after_migration and isinstance(killed_host_player_id, int):
                portrait_view_result = probe_post_migration_portrait_navigation(
                    client,
                    artifact_dir,
                    killed_host_player_id,
                )
                failures.extend(
                    f"post_migration_portrait_view:{error}"
                    for error in portrait_view_result.get("errors") or []
                )

        if args.headless_player:
            skipped_screenshot = {
                "success": True,
                "skipped": True,
                "reason": "headless_player",
                "headlessPlayer": True,
            }
            write_json(artifact_dir / "build-host-screenshot.json", skipped_screenshot)
            write_json(artifact_dir / "build-client-screenshot.json", skipped_screenshot)
        elif host_was_killed:
            write_json(artifact_dir / "build-host-screenshot.json", {
                "success": False,
                "error": {"code": "host_process_killed_for_migration"},
            })
            write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        else:
            write_json(artifact_dir / "build-host-screenshot.json", host.screenshot())
            write_json(artifact_dir / "build-client-screenshot.json", client.screenshot())
        host_logs = host.logs_recent() if not host_was_killed else {"success": False, "data": {"lines": []}}
        client_logs = client.logs_recent()
        write_json(artifact_dir / "build-host-logs-recent.json", host_logs)
        write_json(artifact_dir / "build-client-logs-recent.json", client_logs)
        failures.extend(f"mptest_failure_log:{line}" for line in mptest_failures(host_logs, client_logs))

        result = {
            "success": not failures,
            "failures": failures,
            "artifactDir": str(artifact_dir),
            "battleCommandEvidence": assertions,
            "postMigrationPortraitView": portrait_view_result,
            "headlessPlayer": args.headless_player,
        }
        if verify_king_skill:
            result["kingSkillVerification"] = assertions.get("kingSkillVerification")
        write_json(artifact_dir / "result.json", result)
        if failures:
            failure_summary(artifact_dir / "failure-summary.md", f"{case_name} failed", failures)
    finally:
        functional_failures = list(failures)
        cleanup_processes = [proc for proc in (client_proc, host_proc) if proc is not None]
        cleanup_report = write_case_cleanup_report(
            artifact_dir,
            cleanup_processes,
            baseline_pids=cleanup_baseline_pids,
            timeout_seconds=args.cleanup_timeout_seconds,
            leave_processes=args.leave_processes_on_fail and bool(functional_failures),
            strict_cleanup=args.strict_cleanup,
        )
        apply_cleanup_result(artifact_dir, cleanup_report, args.strict_cleanup, functional_failures, failures)

        host_log = collect_player_log(artifact_dir, "build-host-or-last")
        logs = [
            artifact_dir / "build-host.Player.log",
            artifact_dir / "build-client.Player.log",
            artifact_dir / "build-host.stdout.log",
            artifact_dir / "build-host.stderr.log",
            artifact_dir / "build-client.stdout.log",
            artifact_dir / "build-client.stderr.log",
        ]
        if host_log:
            logs.append(host_log)
        write_timeline(artifact_dir, logs)
        write_bot_metrics_artifact(artifact_dir)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "failures": failures,
        "cleanupStatus": cleanup_report.get("cleanupStatus"),
        "cleanupSuccess": cleanup_report.get("cleanupSuccess"),
    }, indent=2))
    return 0 if not failures else 1
