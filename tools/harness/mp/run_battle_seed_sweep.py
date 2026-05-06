#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
from typing import Any

from common import (
    failure_summary,
    latest_player_path,
    make_artifact_dir,
    parse_json_object,
    read_json,
    run_command,
    write_json,
)


CASE_NAME = "battle-seed-sweep"


def parse_seed_list(value: str | None) -> list[int]:
    if not value:
        return []
    seeds: list[int] = []
    for part in value.replace(";", ",").replace(" ", ",").split(","):
        item = part.strip()
        if not item:
            continue
        try:
            seeds.append(int(item))
        except ValueError as exc:
            raise argparse.ArgumentTypeError(f"invalid seed '{item}'") from exc
    return seeds


def resolve_seeds(args: argparse.Namespace) -> list[int]:
    seeds = parse_seed_list(args.seeds)
    if seeds:
        return seeds
    if args.seed_start is None:
        raise SystemExit("Pass --seeds or --seed-start/--seed-count.")
    if args.seed_count <= 0:
        raise SystemExit("--seed-count must be positive.")
    return [args.seed_start + offset for offset in range(args.seed_count)]


def child_script(name: str) -> pathlib.Path:
    return pathlib.Path(__file__).with_name(name)


def read_if_exists(path: pathlib.Path) -> Any:
    if not path.exists():
        return None
    try:
        return read_json(path)
    except Exception as exc:
        return {"readError": f"{type(exc).__name__}: {exc}"}


def child_artifact(stdout: str) -> pathlib.Path | None:
    parsed = parse_json_object(stdout)
    artifact = parsed.get("artifactDir") if isinstance(parsed, dict) else None
    if isinstance(artifact, str) and artifact:
        return pathlib.Path(artifact)
    return None


def player_hash_summary(snapshot: Any) -> list[dict[str, Any]]:
    body = snapshot.get("data") if isinstance(snapshot, dict) and isinstance(snapshot.get("data"), dict) else snapshot
    players = body.get("players") if isinstance(body, dict) else None
    if not isinstance(players, list):
        return []
    rows: list[dict[str, Any]] = []
    for player in players:
        if not isinstance(player, dict):
            continue
        monsters = player.get("monsters") if isinstance(player.get("monsters"), dict) else {}
        rows.append({
            "playerId": player.get("playerId"),
            "attackMonsterPoolHash": player.get("attackMonsterPoolHash"),
            "ownedScrollsHash": player.get("ownedScrollsHash"),
            "manualSkillReadyHash": player.get("manualSkillReadyHash"),
            "monsterAliveCount": monsters.get("aliveCount"),
            "monsterTypeHash": monsters.get("typeHash"),
            "monsterOriginHash": monsters.get("ownerOriginHash"),
            "monsterTargetHash": monsters.get("targetPlayerHash"),
            "monsterHpBucketHash": monsters.get("hpBucketHash"),
        })
    return rows


def battle_summary(seed: int, artifact_dir: pathlib.Path | None, result: dict[str, Any]) -> dict[str, Any]:
    evidence = read_if_exists(artifact_dir / "battle-command-evidence.json") if artifact_dir else None
    comparison = read_if_exists(artifact_dir / "battle-comparison-latest.json") if artifact_dir else None
    host_snapshot = read_if_exists(artifact_dir / "snapshots" / "build-host-battle-progressed.json") if artifact_dir else None
    client_snapshot = read_if_exists(artifact_dir / "snapshots" / "build-client-battle-progressed.json") if artifact_dir else None
    command_state = evidence.get("commands") if isinstance(evidence, dict) and isinstance(evidence.get("commands"), dict) else {}
    return {
        "case": "human-bot-battle-progression",
        "seed": seed,
        "artifactDir": str(artifact_dir) if artifact_dir else None,
        "exitCode": result.get("exitCode"),
        "success": result.get("exitCode") == 0 and isinstance(evidence, dict) and evidence.get("success") is True,
        "commands": {
            "acceptedBattleCommandSeq": command_state.get("acceptedBattleCommandSeq"),
            "spawnMonsterSeq": command_state.get("spawnMonsterSeq"),
            "useMagicScrollSeq": command_state.get("useMagicScrollSeq"),
            "activateSkillSeq": command_state.get("activateSkillSeq"),
            "rejectedBattleCommandCount": command_state.get("rejectedBattleCommandCount"),
            "lastCommand": command_state.get("lastCommand"),
        },
        "hostGame": evidence.get("hostGame") if isinstance(evidence, dict) else None,
        "clientGame": evidence.get("clientGame") if isinstance(evidence, dict) else None,
        "hostPlayerHashes": player_hash_summary(host_snapshot) if isinstance(host_snapshot, dict) else [],
        "clientPlayerHashes": player_hash_summary(client_snapshot) if isinstance(client_snapshot, dict) else [],
        "comparison": comparison,
        "checkpointSummary": read_if_exists(artifact_dir / "checkpoint-summary.json") if artifact_dir else None,
        "failurePath": str(artifact_dir / "failure-summary.md") if artifact_dir and (artifact_dir / "failure-summary.md").exists() else None,
    }


def run_child_case(
    artifact_dir: pathlib.Path,
    seed_dir: pathlib.Path,
    seed: int,
    player_path: pathlib.Path,
    timeout: int,
    prefer_scroll_augment: bool,
) -> tuple[dict[str, Any], pathlib.Path | None]:
    seed_dir.mkdir(parents=True, exist_ok=True)
    command = [
        sys.executable,
        str(child_script("run_human_bot_battle_progression.py")),
        "--seed",
        str(seed),
        "--artifact-root",
        str(seed_dir),
        "--player-path",
        str(player_path),
        "--bot-prepare-mode",
        "augment-only",
    ]
    if prefer_scroll_augment:
        command.append("--prefer-scroll-augment")
    try:
        proc = run_command(command, artifact_dir, timeout=timeout, label=f"seed-{seed}-battle")
        result = {
            "seed": seed,
            "command": command,
            "exitCode": proc.returncode,
            "stdout": proc.stdout,
            "stderr": proc.stderr,
        }
        artifact = child_artifact(proc.stdout)
    except subprocess.TimeoutExpired as exc:
        result = {
            "seed": seed,
            "command": command,
            "exitCode": -1,
            "stdout": exc.stdout or "",
            "stderr": exc.stderr or "",
            "timeoutSeconds": timeout,
            "error": "timeout",
        }
        artifact = child_artifact(result["stdout"])
    write_json(seed_dir / "battle-process-result.json", result | {"artifactDir": str(artifact) if artifact else None})
    return result, artifact


def run(args: argparse.Namespace) -> int:
    seeds = resolve_seeds(args)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    failures: list[str] = []
    results: list[dict[str, Any]] = []
    summary: dict[str, Any] = {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "playerPath": str(player_path),
        "seeds": seeds,
        "continueOnFail": args.continue_on_fail,
        "preferScrollAugment": args.prefer_scroll_augment,
        "seedSemantics": {
            "deterministicReplayClaim": False,
            "meaning": "diagnostic run label; random-aware assertions compare replicated battle outcomes, not fixed values",
        },
        "results": results,
        "failures": failures,
    }
    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "playerPath": str(player_path),
        "seeds": seeds,
        "continueOnFail": args.continue_on_fail,
        "preferScrollAugment": args.prefer_scroll_augment,
        "dryRun": args.dry_run,
    })
    if args.dry_run:
        write_json(artifact_dir / "battle-seed-sweep-summary.json", summary)
        write_json(artifact_dir / "result.json", summary | {"success": True})
        print(json.dumps({"artifactDir": str(artifact_dir), "dryRun": True, "seeds": seeds}, indent=2))
        return 0

    for seed in seeds:
        seed_dir = artifact_dir / f"seed-{seed}"
        child_result, child_artifact_dir = run_child_case(
            artifact_dir,
            seed_dir,
            seed,
            player_path,
            args.case_timeout,
            args.prefer_scroll_augment,
        )
        seed_summary = battle_summary(seed, child_artifact_dir, child_result)
        results.append(seed_summary)
        write_json(seed_dir / "seed-result.json", seed_summary)
        if not seed_summary["success"]:
            failures.append(f"seed_{seed}_battle_failed:{seed_summary.get('failurePath') or 'child_process'}")
        write_json(artifact_dir / "battle-seed-sweep-summary.json", summary)
        write_json(artifact_dir / "result.json", summary | {"success": not failures})
        if failures and not args.continue_on_fail:
            break

    summary["success"] = not failures
    write_json(artifact_dir / "battle-seed-sweep-summary.json", summary)
    write_json(artifact_dir / "result.json", summary)
    if failures:
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "success": not failures,
        "failures": failures,
        "seedsRun": [item["seed"] for item in results],
    }, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seeds", help="Comma, semicolon, or space separated seed list, for example 7101,7102")
    parser.add_argument("--seed-start", type=int)
    parser.add_argument("--seed-count", type=int, default=3)
    parser.add_argument("--continue-on-fail", action="store_true")
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--case-timeout", type=int, default=360)
    parser.add_argument("--prefer-scroll-augment", action="store_true", default=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
