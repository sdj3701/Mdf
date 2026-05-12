#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from collections import OrderedDict
import pathlib
import subprocess
import sys
import time

from common import ROOT, make_artifact_dir, parse_json_object, read_json, unity_cli_json, wait_unity_ready, write_json
from launch_player import write_orphan_pressure_report


CASES = {
    "editor-host-build-client": "run_editor_host_build_client.py",
    "build-host-editor-client": "run_build_host_editor_client.py",
    "build-host-build-client": "run_build_host_build_client.py",
    "human-bot-prepare": "run_human_bot_prepare_progression.py",
    "battle-spawn-monster-command": "run_battle_spawn_monster_command.py",
    "magic-scroll-command": "run_magic_scroll_command.py",
    "human-bot-battle-progression": "run_human_bot_battle_progression.py",
    "human-bot-3round-progression": "run_human_bot_3round_progression.py",
    "human-bot-game-to-end": "run_human_bot_game_to_end.py",
    "3round-reconnect": "run_3round_reconnect.py",
    "3round-disconnect-ai-takeover": "run_3round_disconnect_ai_takeover.py",
    "3round-host-migration": "run_3round_host_migration.py",
    "progressed-host-migration-after-battle": "run_progressed_host_migration_after_battle.py",
    "progressed-reconnect-after-battle": "run_progressed_reconnect_after_battle.py",
    "progressed-disconnect-after-battle": "run_progressed_disconnect_after_battle.py",
    "battle-seed-sweep": "run_battle_seed_sweep.py",
    "human-bot-seed-sweep": "run_human_bot_seed_sweep.py",
    "human-bot-4p-progression": "run_human_bot_4p_progression.py",
    "ai-fill-smoke": "run_ai_fill_smoke.py",
    "disconnect-ai-takeover": "run_disconnect_ai_takeover.py",
    "same-token-reconnect": "run_same_token_reconnect.py",
    "four-player-smoke": "run_four_player_smoke.py",
}

DEFAULT_CASES = [
    "editor-host-build-client",
    "build-host-editor-client",
    "build-host-build-client",
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
]

SMOKE_CASES = [
    "editor-host-build-client",
    "build-host-editor-client",
    "build-host-build-client",
]

REGRESSION_CASES = SMOKE_CASES + [
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
    "human-bot-prepare",
]

BATTLE_CASES = [
    "battle-spawn-monster-command",
    "magic-scroll-command",
    "human-bot-battle-progression",
]

LIFECYCLE_CASES = [
    "progressed-reconnect-after-battle",
    "progressed-disconnect-after-battle",
    "progressed-host-migration-after-battle",
]

LONG_CASES = [
    "human-bot-3round-progression",
]

ENDURANCE_CASES = [
    "human-bot-game-to-end",
]

LONG_LIFECYCLE_CASES = [
    "3round-reconnect",
    "3round-disconnect-ai-takeover",
    "3round-host-migration",
]

RANDOM_AWARE_CASES = [
    "human-bot-prepare",
    "human-bot-4p-progression",
    "progressed-reconnect-after-battle",
    "progressed-disconnect-after-battle",
    "progressed-host-migration-after-battle",
    "human-bot-seed-sweep",
]

NIGHTLY_CASES = REGRESSION_CASES + BATTLE_CASES + LIFECYCLE_CASES + [
    "battle-seed-sweep",
    "human-bot-seed-sweep",
]

FULL_REGRESSION_CASES = REGRESSION_CASES + BATTLE_CASES + LIFECYCLE_CASES + LONG_CASES

PROFILES = OrderedDict(
    [
        ("smoke", SMOKE_CASES),
        ("regression", REGRESSION_CASES),
        ("battle", BATTLE_CASES),
        ("lifecycle", LIFECYCLE_CASES),
        ("long", LONG_CASES),
        ("endurance", ENDURANCE_CASES),
        ("long-lifecycle", LONG_LIFECYCLE_CASES),
        ("random-aware", RANDOM_AWARE_CASES),
        ("full-regression", FULL_REGRESSION_CASES),
        ("nightly", NIGHTLY_CASES),
    ]
)

CLEANUP_FLAG_CASES = {
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
    "human-bot-prepare",
    "battle-spawn-monster-command",
    "magic-scroll-command",
    "human-bot-battle-progression",
    "human-bot-3round-progression",
    "human-bot-game-to-end",
    "3round-reconnect",
    "3round-disconnect-ai-takeover",
    "3round-host-migration",
    "progressed-host-migration-after-battle",
    "progressed-reconnect-after-battle",
    "progressed-disconnect-after-battle",
    "human-bot-seed-sweep",
    "battle-seed-sweep",
}

ORPHAN_FLAG_CASES = {
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
    "human-bot-prepare",
    "battle-spawn-monster-command",
    "magic-scroll-command",
    "human-bot-battle-progression",
    "human-bot-3round-progression",
    "human-bot-game-to-end",
    "3round-reconnect",
    "3round-disconnect-ai-takeover",
    "3round-host-migration",
    "progressed-host-migration-after-battle",
    "progressed-reconnect-after-battle",
    "progressed-disconnect-after-battle",
    "human-bot-seed-sweep",
    "battle-seed-sweep",
}

HEADLESS_FLAG_CASES = {
    "editor-host-build-client",
    "build-host-editor-client",
    "build-host-build-client",
    "ai-fill-smoke",
    "disconnect-ai-takeover",
    "same-token-reconnect",
    "four-player-smoke",
    "human-bot-prepare",
    "battle-spawn-monster-command",
    "magic-scroll-command",
    "human-bot-battle-progression",
    "human-bot-3round-progression",
    "human-bot-game-to-end",
    "3round-reconnect",
    "3round-disconnect-ai-takeover",
    "3round-host-migration",
    "progressed-reconnect-after-battle",
    "progressed-disconnect-after-battle",
    "progressed-host-migration-after-battle",
    "human-bot-seed-sweep",
    "battle-seed-sweep",
}

CASE_DEFAULT_ARGS = {
    "human-bot-seed-sweep": ["--seeds", "5101,5102,5103"],
    "battle-seed-sweep": ["--seeds", "7101,7102,7103"],
}


def unique_cases(cases: list[str]) -> list[str]:
    seen: set[str] = set()
    selected: list[str] = []
    for case in cases:
        if case not in seen:
            seen.add(case)
            selected.append(case)
    return selected


def list_cases_payload() -> dict:
    return {
        "cases": sorted(CASES.keys()),
        "caseAllAlias": DEFAULT_CASES,
        "caseDefaults": CASE_DEFAULT_ARGS,
    }


def list_profiles_payload() -> dict:
    return {
        "profiles": {name: cases for name, cases in PROFILES.items()},
        "caseAllAlias": DEFAULT_CASES,
        "notes": {
            "all": "--case all preserves the existing default case subset; use --profile nightly for heavy coverage.",
            "smoke": "--profile smoke is the cheap three-direction Editor/Build smoke.",
            "long": "--profile long runs bounded multi-round progression and is excluded from smoke.",
            "endurance": "--profile endurance runs GameOver-or-timeout classification and is explicit opt-in only.",
            "long-lifecycle": "--profile long-lifecycle runs reconnect, disconnect/AI takeover, and Host Migration after a 3-round checkpoint.",
            "nightly": "--profile nightly remains the pre-long nightly set; use --profile full-regression for long progression. Endurance is excluded unless run explicitly with --profile endurance.",
            "headless": "--profile smoke defaults build players to --headless-player; use --no-headless-player for screenshot/visual debugging.",
        },
    }


def case_script_path(case: str) -> pathlib.Path:
    return pathlib.Path(__file__).with_name(CASES[case])


def case_integrity_errors() -> list[str]:
    errors: list[str] = []
    for case, script_name in sorted(CASES.items()):
        path = pathlib.Path(__file__).with_name(script_name)
        if not path.exists():
            errors.append(f"case {case} references missing script {script_name}")
    for profile, cases in PROFILES.items():
        for case in cases:
            if case not in CASES:
                errors.append(f"profile {profile} references unknown case {case}")
    for case in DEFAULT_CASES:
        if case not in CASES:
            errors.append(f"default cases reference unknown case {case}")
    return errors


def select_cases(args: argparse.Namespace, parser: argparse.ArgumentParser) -> tuple[list[str], str | None, str]:
    if args.profile and args.case and args.case != "all":
        parser.error("use either --profile or a specific --case, not both")
    if args.profile:
        return unique_cases(list(PROFILES[args.profile])), args.profile, f"profile:{args.profile}"
    if args.case is None or args.case == "all":
        return list(DEFAULT_CASES), None, "case:all"
    return [args.case], None, f"case:{args.case}"


def child_artifact(stdout: str) -> pathlib.Path | None:
    try:
        parsed = parse_json_object(stdout)
    except Exception:
        parsed = {}
    artifact = parsed.get("artifactDir") if isinstance(parsed, dict) else None
    if isinstance(artifact, str) and artifact:
        return pathlib.Path(artifact)
    return None


def case_result_from_artifact(artifact_dir: pathlib.Path | None) -> dict:
    if artifact_dir is None:
        return {}
    result_path = artifact_dir / "result.json"
    if not result_path.exists():
        return {}
    try:
        result = read_json(result_path)
        return result if isinstance(result, dict) else {}
    except Exception as exc:
        return {"resultReadError": f"{type(exc).__name__}: {exc}"}


def aggregate_cleanup_status(results: list[dict]) -> tuple[str, bool]:
    statuses = [item.get("cleanupStatus") for item in results if isinstance(item.get("cleanupStatus"), str)]
    if not statuses:
        return "UNKNOWN", False
    if all(status == "PASS" for status in statuses):
        return "PASS", True
    if "FAIL" in statuses:
        return "FAIL", False
    if "NEEDS_ENVIRONMENT" in statuses:
        return "NEEDS_ENVIRONMENT", False
    return "UNKNOWN", False


def run_case(case: str, matrix_dir: pathlib.Path, args: argparse.Namespace) -> dict:
    script = case_script_path(case)
    command = [sys.executable, str(script), "--artifact-root", str(matrix_dir)]
    command.extend(CASE_DEFAULT_ARGS.get(case, []))
    if args.player_path:
        command.extend(["--player-path", args.player_path])
    if args.dry_run:
        command.append("--dry-run")
    if case in CLEANUP_FLAG_CASES:
        command.extend(["--cleanup-timeout-seconds", str(args.cleanup_timeout_seconds)])
        if args.leave_processes_on_fail:
            command.append("--leave-processes-on-fail")
        if args.strict_cleanup:
            command.append("--strict-cleanup")
    if case in ORPHAN_FLAG_CASES:
        command.extend(["--orphan-threshold", str(args.orphan_threshold)])
        if args.force_run_with_orphans:
            command.append("--force-run-with-orphans")
    if args.effective_headless_player and case in HEADLESS_FLAG_CASES:
        command.append("--headless-player")
    if args.dry_run:
        return {
            "case": case,
            "command": command,
            "dryRun": True,
            "functionalSuccess": True,
            "cleanupStatus": "PASS",
            "cleanupSuccess": True,
            "orphanedPids": [],
            "overallSuccess": True,
            "headlessPlayer": bool(args.effective_headless_player and case in HEADLESS_FLAG_CASES),
        }
    proc = subprocess.run(command, cwd=ROOT, text=True, capture_output=True, encoding="utf-8", errors="replace")
    artifact_dir = child_artifact(proc.stdout)
    child_result = case_result_from_artifact(artifact_dir)
    failures = list(child_result.get("failures") or []) if isinstance(child_result.get("failures"), list) else []
    if artifact_dir is None:
        failures.append("artifact_dir_missing")
    if not child_result:
        failures.append("child_result_json_missing")
    cleanup_status = child_result.get("cleanupStatus") if isinstance(child_result.get("cleanupStatus"), str) else "UNKNOWN"
    orphaned_pids = child_result.get("orphanedPids") if isinstance(child_result.get("orphanedPids"), list) else None
    if cleanup_status == "UNKNOWN":
        failures.append("cleanupStatus_missing")
    if orphaned_pids is None:
        failures.append("orphanedPids_missing")
    cleanup_success = child_result.get("cleanupSuccess") is True
    functional_success = child_result.get("functionalSuccess")
    if functional_success is None:
        functional_success = child_result.get("success") if child_result else proc.returncode == 0
    overall_success = (
        proc.returncode == 0
        and child_result.get("success") is True
        and functional_success is True
        and cleanup_status == "PASS"
        and cleanup_success
        and orphaned_pids == []
        and not failures
    )
    result = {
        "case": case,
        "command": command,
        "exitCode": proc.returncode,
        "stdout": proc.stdout,
        "stderr": proc.stderr,
        "artifactDir": str(artifact_dir) if artifact_dir else None,
        "functionalSuccess": functional_success is True,
        "cleanupStatus": cleanup_status,
        "cleanupSuccess": cleanup_success,
        "cleanupReportPath": str(artifact_dir / "cleanup-report.json") if artifact_dir and (artifact_dir / "cleanup-report.json").exists() else None,
        "orphanedPids": orphaned_pids,
        "overallSuccess": overall_success,
        "headlessPlayer": bool(args.effective_headless_player and case in HEADLESS_FLAG_CASES),
        "failures": failures,
    }
    return result


def cleanup_editor_between_cases(matrix_dir: pathlib.Path, label: str, args: argparse.Namespace) -> dict:
    cleanup_dir = matrix_dir / f"_cleanup-{label}"
    cleanup_dir.mkdir(parents=True, exist_ok=True)
    if args.dry_run:
        result = {"label": label, "dryRun": True, "skipped": True, "steps": [], "ready": None}
        write_json(cleanup_dir / "cleanup.json", result)
        return result
    result: dict = {"label": label, "steps": []}
    for command in (["mp_stop"], ["editor", "stop"]):
        step = {"command": command}
        try:
            step["response"] = unity_cli_json(command, cleanup_dir, timeout=90)
            step["ok"] = True
        except Exception as exc:
            step["ok"] = False
            step["error"] = str(exc)
        result["steps"].append(step)

    result["ready"] = wait_unity_ready(cleanup_dir, timeout_seconds=60)
    if args.inter_case_delay_seconds > 0:
        time.sleep(args.inter_case_delay_seconds)
    write_json(cleanup_dir / "cleanup.json", result)
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--case", choices=list(CASES.keys()) + ["all"])
    parser.add_argument("--profile", choices=list(PROFILES.keys()))
    parser.add_argument("--list-cases", action="store_true")
    parser.add_argument("--list-profiles", action="store_true")
    parser.add_argument("--check-cases", action="store_true")
    parser.add_argument("--player-path")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--inter-case-delay-seconds", type=float, default=2.0)
    parser.add_argument("--cleanup-timeout-seconds", type=float, default=15.0)
    parser.add_argument("--leave-processes-on-fail", action="store_true")
    parser.add_argument("--strict-cleanup", action="store_true")
    parser.add_argument("--orphan-threshold", type=int, default=0)
    parser.add_argument("--force-run-with-orphans", action="store_true")
    parser.add_argument("--headless-player", action=argparse.BooleanOptionalAction, default=None)
    args = parser.parse_args()

    if args.list_cases:
        print(json.dumps(list_cases_payload(), indent=2))
        return 0
    if args.list_profiles:
        print(json.dumps(list_profiles_payload(), indent=2))
        return 0
    integrity_errors = case_integrity_errors()
    if args.check_cases:
        payload = {"success": not integrity_errors, "errors": integrity_errors, "cases": list_cases_payload()}
        print(json.dumps(payload, indent=2))
        return 0 if payload["success"] else 2
    if integrity_errors:
        print(json.dumps({"success": False, "errors": integrity_errors}, indent=2))
        return 2

    matrix_dir = make_artifact_dir("matrix")
    selected, profile_name, selection_name = select_cases(args, parser)
    default_headless = profile_name in {"smoke", "long", "endurance", "long-lifecycle", "full-regression", "nightly"}
    args.effective_headless_player = default_headless if args.headless_player is None else args.headless_player
    orphan_gate = write_orphan_pressure_report(
        matrix_dir,
        args.orphan_threshold,
        args.force_run_with_orphans,
    )
    if orphan_gate.get("blocked") and not args.dry_run:
        summary = {
            "matrixDir": str(matrix_dir),
            "profileName": profile_name,
            "dryRun": args.dry_run,
            "caseSelection": selection_name,
            "selectedCases": selected,
            "headlessPlayer": args.effective_headless_player,
            "headlessPlayerDefault": default_headless,
            "cleanups": [],
            "results": [],
            "artifacts": [],
            "functionalSuccess": False,
            "cleanupStatus": "NEEDS_ENVIRONMENT",
            "cleanupSuccess": False,
            "overallSuccess": False,
            "success": False,
            "orphanPressure": orphan_gate,
            "failures": ["orphan_pressure_gate_blocked"],
        }
        write_json(matrix_dir / "matrix-summary.json", summary)
        print(json.dumps(summary, indent=2))
        return 2
    cleanups: list[dict] = []
    results: list[dict] = []
    for index, case in enumerate(selected):
        cleanups.append(cleanup_editor_between_cases(matrix_dir, f"before-{index + 1}-{case}", args))
        results.append(run_case(case, matrix_dir, args))
        cleanups.append(cleanup_editor_between_cases(matrix_dir, f"after-{index + 1}-{case}", args))

    cleanup_status, cleanup_success = aggregate_cleanup_status(results)
    functional_success = all(item.get("functionalSuccess") is True for item in results)
    overall_success = all(item.get("overallSuccess") is True for item in results) and cleanup_success
    artifacts = [item.get("artifactDir") for item in results if item.get("artifactDir")]
    summary = {
        "matrixDir": str(matrix_dir),
        "profileName": profile_name,
        "dryRun": args.dry_run,
        "caseSelection": selection_name,
        "selectedCases": selected,
        "headlessPlayer": args.effective_headless_player,
        "headlessPlayerDefault": default_headless,
        "cleanups": cleanups,
        "results": results,
        "artifacts": artifacts,
        "functionalSuccess": functional_success,
        "cleanupStatus": cleanup_status,
        "cleanupSuccess": cleanup_success,
        "overallSuccess": overall_success,
        "success": overall_success,
        "orphanPressure": orphan_gate,
    }
    write_json(matrix_dir / "matrix-summary.json", summary)
    print(json.dumps(summary, indent=2))
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
