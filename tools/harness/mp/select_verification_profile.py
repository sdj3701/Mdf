#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
from typing import Any


ROOT = pathlib.Path(__file__).resolve().parents[3]

CATEGORIES = {
    "code-only",
    "asset",
    "multiplayer",
    "command",
    "battle",
    "ai",
    "prepare",
    "random",
    "persistent",
    "lifecycle",
    "long",
    "endurance",
}

HEADLESS_DEFAULT_PROFILES = {"smoke", "long", "endurance", "long-lifecycle", "full-regression", "nightly"}


def rel_path(value: str) -> str:
    path = value.strip().strip('"').replace("\\", "/")
    if not path:
        return ""
    try:
        return pathlib.Path(path).resolve().relative_to(ROOT).as_posix()
    except Exception:
        return path


def git_changed_files() -> list[str]:
    try:
        proc = subprocess.run(
            ["git", "status", "--porcelain=v1", "-uall"],
            cwd=ROOT,
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            check=False,
        )
    except Exception:
        return []
    files: list[str] = []
    for line in proc.stdout.splitlines():
        if not line.strip():
            continue
        path = line[3:]
        if " -> " in path:
            path = path.split(" -> ", 1)[1]
        normalized = rel_path(path)
        if normalized:
            files.append(normalized)
    return files


def add_unique(items: list[str], value: str) -> None:
    if value not in items:
        items.append(value)


def infer_categories_from_files(files: list[str]) -> tuple[set[str], list[str]]:
    inferred: set[str] = set()
    reasons: list[str] = []
    for path in files:
        lower = path.lower()
        runtime_script = lower.startswith("mdfproject/assets/scripts/")
        mp_harness = lower.startswith("tools/harness/mp/")
        design_doc = lower.startswith("docs/ai-design/")
        unity_asset = lower.endswith((".prefab", ".unity", ".asset", ".mat", ".controller", ".anim"))
        asset_content = lower.startswith("mdfproject/assets/") and unity_asset
        if lower.endswith(".cs"):
            inferred.add("code-only")
            reasons.append(f"{path}: C# change requires compile/console/EditMode baseline")
        if unity_asset:
            inferred.add("asset")
            reasons.append(f"{path}: Unity serialized asset requires reserialize/asset review")
        if runtime_script and any(token in lower for token in ["/network/", "/commands/", "/managers/", "/testing/mp/"]):
            inferred.add("multiplayer")
            reasons.append(f"{path}: multiplayer/network/harness surface")
        if mp_harness:
            inferred.add("multiplayer")
            reasons.append(f"{path}: multiplayer harness surface")
        if "/commands/" in lower or "command" in lower:
            inferred.add("command")
            reasons.append(f"{path}: command or authority path")
        if (runtime_script or mp_harness or design_doc or asset_content) and any(token in lower for token in ["battle", "monster", "scroll", "skill"]):
            inferred.add("battle")
            reasons.append(f"{path}: battle/monster/scroll/skill path")
        if (runtime_script or mp_harness or design_doc) and any(token in lower for token in ["/ai/", "humanbot", "human_bot", "planning", "behavior"]):
            inferred.add("ai")
            reasons.append(f"{path}: AI/HumanBot/planning path")
        prepare_tokens = [
            "prepare",
            "shop",
            "augment",
            "wall",
            "maze",
            "placement",
            "buyunit",
            "sellunit",
            "moveunit",
            "reroll",
            "placewall",
            "removewall",
            "unitdata",
        ]
        if (runtime_script or mp_harness or asset_content) and any(token in lower for token in prepare_tokens):
            inferred.add("prepare")
            reasons.append(f"{path}: Prepare/shop/augment/wall path")
        if (runtime_script or mp_harness or asset_content) and any(token in lower for token in ["random", "seed", "shop", "augment", "wall", "reward"]):
            inferred.add("random")
            reasons.append(f"{path}: randomized outcome path")
        if any(token in lower for token in ["hostmigration", "host_migration", "reconnect", "playermanager", "fieldmanager", "migration"]):
            inferred.add("lifecycle")
            inferred.add("persistent")
            reasons.append(f"{path}: persistent lifecycle/reconnect/migration path")
        if any(token in lower for token in ["3round", "3-round", "long_progression", "run_human_bot_3round"]):
            inferred.add("long")
            reasons.append(f"{path}: long progression path")
        if "game_to_end" in lower or "endurance" in lower:
            inferred.add("endurance")
            reasons.append(f"{path}: explicit endurance/game-to-end path")
    return inferred, reasons


def select(changed_files: list[str], categories: set[str]) -> dict[str, Any]:
    required_unity_commands: list[str] = ["python tools/harness/precommit.py --all"]
    recommended_profiles: list[str] = []
    targeted_cases: list[str] = []
    reasons: list[str] = []

    inferred, file_reasons = infer_categories_from_files(changed_files)
    effective = set(categories) | inferred
    reasons.extend(file_reasons)
    if categories:
        reasons.append("explicit categories: " + ", ".join(sorted(categories)))

    requires_reserialize = "asset" in effective
    requires_asset_guard = requires_reserialize
    requires_fusion_reviewer = bool({"command", "persistent", "lifecycle", "multiplayer", "battle"} & effective)
    requires_mp_test_runner = bool({"multiplayer", "battle", "ai", "prepare", "random", "persistent", "lifecycle", "long", "endurance"} & effective)

    gameplay_categories = {"multiplayer", "command", "battle", "ai", "prepare", "random", "persistent", "lifecycle", "long", "endurance"}
    if "code-only" in effective or any(path.endswith(".cs") for path in changed_files) or bool(gameplay_categories & effective):
        for command in [
            "unity-cli --project Mdfproject status",
            "unity-cli --project Mdfproject editor refresh --compile",
            "unity-cli --project Mdfproject console --type error --stacktrace user",
            "unity-cli --project Mdfproject test --mode EditMode",
        ]:
            add_unique(required_unity_commands, command)

    if requires_reserialize:
        add_unique(required_unity_commands, "unity-cli --project Mdfproject reserialize <changed Unity asset paths>")
        add_unique(required_unity_commands, "unity-cli --project Mdfproject editor refresh --compile")
        add_unique(required_unity_commands, "unity-cli --project Mdfproject console --type error --stacktrace user")

    if "multiplayer" in effective or "command" in effective:
        add_unique(recommended_profiles, "smoke")
        reasons.append("multiplayer/command changes require smoke or targeted E2E")

    if "battle" in effective:
        add_unique(recommended_profiles, "battle")
        if "ai" in effective:
            add_unique(targeted_cases, "human-bot-battle-progression")
        else:
            for case in ["battle-spawn-monster-command", "magic-scroll-command", "human-bot-battle-progression"]:
                add_unique(targeted_cases, case)
        reasons.append("battle-visible changes require battle profile")

    if "prepare" in effective:
        add_unique(targeted_cases, "human-bot-prepare")
        reasons.append("Prepare changes require targeted HumanBot prepare proof")
        if "random" in effective:
            add_unique(recommended_profiles, "random-aware")
            reasons.append("Random Prepare changes need same-player random-aware assertions")
    elif "ai" in effective and "battle" not in effective:
        reasons.append("AI-only changes need compile plus the smallest targeted profile chosen from code-path review; random-aware is not automatic")

    if "random" in effective:
        add_unique(recommended_profiles, "random-aware")
        reasons.append("random outcome changes require random-aware profile")

    if "persistent" in effective or "lifecycle" in effective:
        add_unique(recommended_profiles, "lifecycle")
        for case in [
            "progressed-reconnect-after-battle",
            "progressed-disconnect-after-battle",
            "progressed-host-migration-after-battle",
            "status-effect-host-migration",
            "stat-buff-host-migration",
            "zone-host-migration",
        ]:
            add_unique(targeted_cases, case)
        reasons.append("persistent/reconnect/migration changes require lifecycle proof")

    if "long" in effective and ("lifecycle" in effective or "persistent" in effective):
        add_unique(recommended_profiles, "long-lifecycle")
        for case in ["3round-reconnect", "3round-disconnect-ai-takeover", "3round-host-migration"]:
            add_unique(targeted_cases, case)
        reasons.append("3-round lifecycle changes require long-lifecycle")
    elif "long" in effective:
        add_unique(recommended_profiles, "long")
        add_unique(targeted_cases, "human-bot-3round-progression")
        reasons.append("3-round or midgame confidence requires long profile")

    if "endurance" in categories:
        add_unique(recommended_profiles, "endurance")
        add_unique(targeted_cases, "human-bot-game-to-end")
        reasons.append("endurance selected only because it was explicitly requested")
    elif "endurance" in inferred:
        reasons.append("endurance-related file changed; run endurance only if the user explicitly requested game-to-end proof")

    if not recommended_profiles and requires_mp_test_runner:
        add_unique(recommended_profiles, "smoke")

    headless_default = any(profile in HEADLESS_DEFAULT_PROFILES for profile in recommended_profiles)

    return {
        "changedFiles": changed_files,
        "categories": sorted(effective),
        "requiredUnityCommands": required_unity_commands,
        "recommendedProfiles": recommended_profiles,
        "targetedCases": targeted_cases,
        "headlessDefault": headless_default,
        "requiresReserialize": requires_reserialize,
        "requiresAssetGuard": requires_asset_guard,
        "requiresFusionReviewer": requires_fusion_reviewer,
        "requiresMpTestRunner": requires_mp_test_runner,
        "reasons": reasons,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--changed-files", nargs="*", default=None)
    parser.add_argument("--category", action="append", choices=sorted(CATEGORIES), default=[])
    args = parser.parse_args()

    if args.changed_files is not None:
        changed = [rel_path(path) for path in args.changed_files]
    elif args.category:
        changed = []
    else:
        changed = git_changed_files()
    changed = [path for path in changed if path]
    result = select(changed, set(args.category))
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
