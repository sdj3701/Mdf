#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
from typing import Any

from common import (
    ROOT,
    failure_summary,
    latest_player_path,
    make_artifact_dir,
    parse_json_object,
    read_json,
    run_command,
    write_json,
)
from summarize_bot_metrics import aggregate_seed_metrics, write_metrics_summary


CASE_NAME = "human-bot-seed-sweep"


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


def write_result(artifact_dir: pathlib.Path, summary: dict[str, Any]) -> None:
    failures = summary.get("failures") or []
    write_json(artifact_dir / "result.json", {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "success": not failures,
        "functionalSuccess": not failures,
        "cleanupSuccess": summary.get("cleanupSuccess"),
        "cleanupStatus": summary.get("cleanupStatus"),
        "failures": failures,
        "seeds": summary.get("seeds"),
        "include4p": summary.get("include4p"),
        "botMetricsSummaryPath": summary.get("botMetricsSummaryPath"),
        "botMetrics": summary.get("botMetrics"),
    })


def child_script(name: str) -> pathlib.Path:
    return pathlib.Path(__file__).with_name(name)


def child_command(
    script_name: str,
    seed: int,
    artifact_root: pathlib.Path,
    player_path: pathlib.Path | None,
    extra_args: list[str] | None = None,
) -> list[str]:
    command = [
        sys.executable,
        str(child_script(script_name)),
        "--seed",
        str(seed),
        "--artifact-root",
        str(artifact_root),
    ]
    if player_path is not None:
        command.extend(["--player-path", str(player_path)])
    if extra_args:
        command.extend(extra_args)
    return command


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
        path = pathlib.Path(artifact)
        return path if path.exists() else path
    return None


def command_types_from_assertions(assertions: Any) -> list[str]:
    if not isinstance(assertions, dict):
        return []
    types: list[str] = []
    last = assertions.get("lastCommandType")
    if isinstance(last, str) and last:
        types.append(last)
    bots = assertions.get("bots")
    if isinstance(bots, dict):
        for bot in bots.values():
            if isinstance(bot, dict):
                command_type = bot.get("lastCommandType")
                if isinstance(command_type, str) and command_type:
                    types.append(command_type)
    return types


def commands_issued_from_assertions(assertions: Any) -> int:
    if not isinstance(assertions, dict):
        return 0
    total = assertions.get("commandsIssued")
    if isinstance(total, int):
        return total
    total = assertions.get("totalBotCommands")
    if isinstance(total, int):
        return total
    bots = assertions.get("bots")
    if isinstance(bots, dict):
        return sum(int(bot.get("commandsIssued") or 0) for bot in bots.values() if isinstance(bot, dict))
    return 0


def first_failure_path(artifact_dir: pathlib.Path | None, assertions: Any, result: dict[str, Any]) -> str | None:
    if result.get("exitCode") == 0 and isinstance(assertions, dict) and assertions.get("success") is True:
        return None
    if artifact_dir is not None:
        failure_file = artifact_dir / "failure-summary.md"
        if failure_file.exists():
            return str(failure_file)
        for candidate in (
            "human-bot-prepare-assertions.json",
            "human-bot-4p-assertions.json",
            "comparison-latest.json",
            "before-bot-comparison.json",
        ):
            path = artifact_dir / candidate
            if path.exists():
                return str(path)
    return "child_process"


def screenshot_paths(artifact_dir: pathlib.Path | None) -> list[str]:
    if artifact_dir is None:
        return []
    paths: list[str] = []
    for path in sorted(artifact_dir.glob("*-screenshot.json")):
        paths.append(str(path))
    for path in sorted((artifact_dir / "screenshots").glob("*")):
        paths.append(str(path))
    return paths


def journal_paths(artifact_dir: pathlib.Path | None) -> list[str]:
    if artifact_dir is None:
        return []
    return sorted(str(path) for path in artifact_dir.glob("*bot*.jsonl"))


def prepare_summary(seed: int, artifact_dir: pathlib.Path | None, result: dict[str, Any]) -> dict[str, Any]:
    assertions = read_if_exists(artifact_dir / "human-bot-prepare-assertions.json") if artifact_dir else None
    random_summary = read_if_exists(artifact_dir / "random-outcome-summary.json") if artifact_dir else None
    comparison = read_if_exists(artifact_dir / "comparison-latest.json") if artifact_dir else None
    child_result = read_if_exists(artifact_dir / "result.json") if artifact_dir else None
    cleanup_report = read_if_exists(artifact_dir / "cleanup-report.json") if artifact_dir else None
    bot_metrics = write_metrics_summary(artifact_dir) if artifact_dir else None
    summary = {
        "case": "human-bot-prepare",
        "seed": seed,
        "artifactDir": str(artifact_dir) if artifact_dir else None,
        "exitCode": result.get("exitCode"),
        "success": result.get("exitCode") == 0 and isinstance(assertions, dict) and assertions.get("success") is True,
        "functionalSuccess": (isinstance(child_result, dict) and child_result.get("functionalSuccess") is True)
        or (result.get("exitCode") == 0 and isinstance(assertions, dict) and assertions.get("success") is True),
        "cleanupStatus": child_result.get("cleanupStatus") if isinstance(child_result, dict) else None,
        "cleanupSuccess": child_result.get("cleanupSuccess") if isinstance(child_result, dict) else None,
        "cleanupReportPath": str(artifact_dir / "cleanup-report.json") if artifact_dir and cleanup_report is not None else None,
        "orphanedPids": child_result.get("orphanedPids") if isinstance(child_result, dict) else None,
        "commandsIssued": commands_issued_from_assertions(assertions),
        "commandTypes": command_types_from_assertions(assertions),
        "randomOutcomes": random_summary,
        "comparison": comparison,
        "checkpointSummary": read_if_exists(artifact_dir / "checkpoint-summary.json") if artifact_dir else None,
        "journals": journal_paths(artifact_dir),
        "botMetricsSummaryPath": str(artifact_dir / "bot-metrics-summary.json") if artifact_dir else None,
        "botMetrics": bot_metrics.get("summary") if isinstance(bot_metrics, dict) else None,
        "screenshots": screenshot_paths(artifact_dir),
        "failurePath": first_failure_path(artifact_dir, assertions, result),
    }
    return summary


def aggregate_cleanup_status(results: list[dict[str, Any]]) -> tuple[str, bool]:
    statuses: list[str] = []
    for seed in results:
        for case in seed.get("cases") or []:
            status = case.get("cleanupStatus") if isinstance(case, dict) else None
            if isinstance(status, str):
                statuses.append(status)
    if not statuses:
        return "PASS", True
    if "FAIL" in statuses:
        return "FAIL", False
    if "NEEDS_ENVIRONMENT" in statuses:
        return "NEEDS_ENVIRONMENT", False
    return "PASS", True


def four_player_summary(seed: int, artifact_dir: pathlib.Path | None, result: dict[str, Any]) -> dict[str, Any]:
    assertions = read_if_exists(artifact_dir / "human-bot-4p-assertions.json") if artifact_dir else None
    random_summary = read_if_exists(artifact_dir / "random-outcome-summary.json") if artifact_dir else None
    comparisons = sorted(str(path) for path in artifact_dir.glob("comparison-host-vs-*.json")) if artifact_dir else []
    return {
        "case": "human-bot-4p-progression",
        "seed": seed,
        "artifactDir": str(artifact_dir) if artifact_dir else None,
        "exitCode": result.get("exitCode"),
        "success": result.get("exitCode") == 0 and isinstance(assertions, dict) and assertions.get("success") is True,
        "commandsIssued": commands_issued_from_assertions(assertions),
        "commandTypes": command_types_from_assertions(assertions),
        "randomOutcomes": random_summary,
        "comparisons": comparisons,
        "checkpointSummary": read_if_exists(artifact_dir / "checkpoint-summary.json") if artifact_dir else None,
        "journals": journal_paths(artifact_dir),
        "screenshots": screenshot_paths(artifact_dir),
        "failurePath": first_failure_path(artifact_dir, assertions, result),
    }


def run_child_case(
    artifact_dir: pathlib.Path,
    seed_dir: pathlib.Path,
    seed: int,
    script_name: str,
    label: str,
    player_path: pathlib.Path | None,
    timeout: int,
    extra_args: list[str] | None = None,
) -> tuple[dict[str, Any], pathlib.Path | None]:
    seed_dir.mkdir(parents=True, exist_ok=True)
    command = child_command(script_name, seed, seed_dir, player_path, extra_args)
    try:
        proc = run_command(command, artifact_dir, timeout=timeout, label=f"seed-{seed}-{label}")
        result = {
            "label": label,
            "seed": seed,
            "command": command,
            "exitCode": proc.returncode,
            "stdout": proc.stdout,
            "stderr": proc.stderr,
        }
        artifact = child_artifact(proc.stdout)
    except subprocess.TimeoutExpired as exc:
        result = {
            "label": label,
            "seed": seed,
            "command": command,
            "exitCode": -1,
            "stdout": exc.stdout or "",
            "stderr": exc.stderr or "",
            "timeoutSeconds": timeout,
            "error": "timeout",
        }
        artifact = child_artifact(result["stdout"])
    write_json(seed_dir / f"{label}-process-result.json", result | {"artifactDir": str(artifact) if artifact else None})
    return result, artifact


def run(args: argparse.Namespace) -> int:
    seeds = resolve_seeds(args)
    player_path = pathlib.Path(args.player_path) if args.player_path else latest_player_path()
    if player_path is None or not player_path.exists():
        raise SystemExit("No built Development player found. Run tools/harness/mp/build_player.py or pass --player-path.")

    artifact_dir = make_artifact_dir(CASE_NAME, pathlib.Path(args.artifact_root) if args.artifact_root else None)
    failures: list[str] = []
    seed_results: list[dict[str, Any]] = []
    seed_metrics: list[dict[str, Any]] = []
    summary: dict[str, Any] = {
        "case": CASE_NAME,
        "artifactDir": str(artifact_dir),
        "playerPath": str(player_path),
        "seeds": seeds,
        "include4p": args.include_4p,
        "continueOnFail": args.continue_on_fail,
        "seedSemantics": {
            "deterministicReplayClaim": False,
            "meaning": "diagnostic run label; random-aware assertions compare replicated outcomes, not fixed values",
            "knownUncontrolledSources": [
                "UnityEngine.Random gameplay calls unless individually authority-ledgered",
                "System.Random or wall-clock pacing outside explicit test seed paths",
                "Photon/network scheduling and process timing",
            ],
        },
        "results": seed_results,
        "failures": failures,
    }
    write_json(artifact_dir / "run.json", {
        "case": CASE_NAME,
        "playerPath": str(player_path),
        "seeds": seeds,
        "include4p": args.include_4p,
        "dryRun": args.dry_run,
    })

    if args.dry_run:
        write_json(artifact_dir / "seed-sweep-summary.json", summary)
        write_result(artifact_dir, summary)
        print(json.dumps({"artifactDir": str(artifact_dir), "dryRun": True, "seeds": seeds}, indent=2))
        return 0

    for seed in seeds:
        seed_dir = artifact_dir / f"seed-{seed}"
        per_seed: dict[str, Any] = {"seed": seed, "cases": []}
        prepare_extra_args = [
            "--bot-persona",
            args.prepare_persona,
            "--bot-max-commands",
            str(args.prepare_bot_max_commands),
            "--min-commands",
            str(args.prepare_min_commands),
            "--cleanup-timeout-seconds",
            str(args.cleanup_timeout_seconds),
        ]
        if args.leave_processes_on_fail:
            prepare_extra_args.append("--leave-processes-on-fail")
        if args.strict_cleanup:
            prepare_extra_args.append("--strict-cleanup")
        prepare_result, prepare_artifact = run_child_case(
            artifact_dir,
            seed_dir,
            seed,
            "run_human_bot_prepare_progression.py",
            "prepare",
            player_path,
            args.prepare_timeout,
            extra_args=prepare_extra_args,
        )
        prepare = prepare_summary(seed, prepare_artifact, prepare_result)
        if prepare_artifact:
            metrics = read_if_exists(prepare_artifact / "bot-metrics-summary.json")
            if isinstance(metrics, dict):
                seed_metrics.append(metrics)
        per_seed["cases"].append(prepare)
        if not prepare["success"]:
            failures.append(f"seed_{seed}_prepare_failed:{prepare.get('failurePath')}")

        if args.include_4p and (prepare["success"] or args.continue_on_fail):
            four_p_result, four_p_artifact = run_child_case(
                artifact_dir,
                seed_dir,
                seed,
                "run_human_bot_4p_progression.py",
                "4p",
                player_path,
                args.four_p_timeout,
            )
            four_p = four_player_summary(seed, four_p_artifact, four_p_result)
            per_seed["cases"].append(four_p)
            if not four_p["success"]:
                failures.append(f"seed_{seed}_4p_failed:{four_p.get('failurePath')}")

        per_seed["success"] = all(case.get("success") is True for case in per_seed["cases"])
        per_seed["totalCommandsIssued"] = sum(int(case.get("commandsIssued") or 0) for case in per_seed["cases"])
        seed_results.append(per_seed)
        cleanup_status, cleanup_success = aggregate_cleanup_status(seed_results)
        summary["cleanupStatus"] = cleanup_status
        summary["cleanupSuccess"] = cleanup_success
        if seed_metrics:
            metrics_aggregate = aggregate_seed_metrics(seed_metrics)
            write_json(artifact_dir / "bot-metrics-seed-sweep-summary.json", metrics_aggregate)
            summary["botMetricsSummaryPath"] = "bot-metrics-seed-sweep-summary.json"
            summary["botMetrics"] = metrics_aggregate.get("summary")
        write_json(seed_dir / "seed-result.json", per_seed)
        write_json(artifact_dir / "seed-sweep-summary.json", summary)
        write_result(artifact_dir, summary)

        if failures and not args.continue_on_fail:
            break

    summary["success"] = not failures
    cleanup_status, cleanup_success = aggregate_cleanup_status(seed_results)
    summary["cleanupStatus"] = cleanup_status
    summary["cleanupSuccess"] = cleanup_success
    if seed_metrics:
        metrics_aggregate = aggregate_seed_metrics(seed_metrics)
        write_json(artifact_dir / "bot-metrics-seed-sweep-summary.json", metrics_aggregate)
        summary["botMetricsSummaryPath"] = "bot-metrics-seed-sweep-summary.json"
        summary["botMetrics"] = metrics_aggregate.get("summary")
    write_json(artifact_dir / "seed-sweep-summary.json", summary)
    write_result(artifact_dir, summary)
    if failures:
        failure_summary(artifact_dir / "failure-summary.md", f"{CASE_NAME} failed", failures)

    print(json.dumps({
        "artifactDir": str(artifact_dir),
        "success": not failures,
        "cleanupStatus": summary.get("cleanupStatus"),
        "cleanupSuccess": summary.get("cleanupSuccess"),
        "failures": failures,
        "seedsRun": [item["seed"] for item in seed_results],
    }, indent=2))
    return 0 if not failures else 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--seeds", help="Comma, semicolon, or space separated seed list, for example 5101,5102,5103")
    parser.add_argument("--seed-start", type=int)
    parser.add_argument("--seed-count", type=int, default=3)
    parser.add_argument("--include-4p", action="store_true")
    parser.add_argument("--continue-on-fail", action="store_true")
    parser.add_argument("--player-path")
    parser.add_argument("--artifact-root")
    parser.add_argument("--prepare-persona", default="balanced")
    parser.add_argument("--prepare-bot-max-commands", type=int, default=1)
    parser.add_argument("--prepare-min-commands", type=int, default=1)
    parser.add_argument("--prepare-timeout", type=int, default=240)
    parser.add_argument("--four-p-timeout", type=int, default=360)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    args = parser.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
