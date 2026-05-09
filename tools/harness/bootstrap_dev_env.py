#!/usr/bin/env python3
"""Audit clone portability prerequisites for the MDF harness.

This script intentionally avoids build/player E2E. It checks source files,
local tools, overlay validity, and self-tests needed before feature work.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass


ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Mdfproject"
CONNECTOR_PACKAGE = "com.youngwoocho02.unity-cli-connector"
REQUIRED_GITIGNORE_LINES = [
    "Library/",
    "Temp/",
    "Obj/",
    "Build/",
    "Builds/",
    "Logs/",
    "UserSettings/",
    "artifacts/",
    "_context_packer/output/",
    "_context_bundles/",
    ".codex/session-state/",
    "**/__pycache__/",
    "**/*.pyc",
    "*.csproj",
    "*.sln",
    ".vs/",
]
PROTECTED_IGNORE_LINES = {
    "Mdfproject/Assets/",
    "Mdfproject/ProjectSettings/",
    "Mdfproject/Packages/",
}
ASSETS_META_IGNORE_RE = re.compile(
    r"^mdfproject/(?:assets|\[aa\]ssets)/.*\.meta$",
    re.IGNORECASE,
)


@dataclass
class Check:
    name: str
    status: str
    detail: str
    next_action: str = ""


def run_command(command: list[str], timeout: int = 60) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
    )


def normalize_line(line: str) -> str:
    return line.strip().replace("\\", "/")


def file_exists(rel_path: str, label: str, next_action: str) -> Check:
    path = ROOT / rel_path
    if path.exists():
        return Check(label, "PASS", rel_path)
    return Check(label, "FAIL", f"missing {rel_path}", next_action)


def check_python() -> Check:
    version = f"{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}"
    if sys.version_info >= (3, 10):
        return Check("python", "PASS", version)
    return Check("python", "FAIL", version, "Install Python 3.10 or newer and put it on PATH.")


def check_project_version() -> Check:
    path = PROJECT / "ProjectSettings/ProjectVersion.txt"
    if not path.exists():
        return Check("unity project version", "FAIL", "ProjectVersion.txt missing", "Open or restore the Unity project under Mdfproject.")
    text = path.read_text(encoding="utf-8", errors="ignore")
    if "2021.3.45f1" in text:
        return Check("unity project version", "PASS", "2021.3.45f1")
    return Check("unity project version", "FAIL", text.strip(), "Install Unity 2021.3.45f1 for this project.")


def check_packages_lock() -> list[Check]:
    manifest = PROJECT / "Packages/manifest.json"
    lock = PROJECT / "Packages/packages-lock.json"
    checks = [
        file_exists("Mdfproject/Packages/manifest.json", "manifest.json", "Restore the Unity Packages directory from git."),
        file_exists("Mdfproject/Packages/packages-lock.json", "packages-lock.json", "Commit or restore packages-lock.json."),
    ]
    if not manifest.exists() or not lock.exists():
        return checks
    try:
        manifest_data = json.loads(manifest.read_text(encoding="utf-8"))
        lock_data = json.loads(lock.read_text(encoding="utf-8"))
    except Exception as exc:
        checks.append(Check("package json parse", "FAIL", f"{type(exc).__name__}: {exc}", "Fix manifest.json/packages-lock.json JSON."))
        return checks

    manifest_dep = (manifest_data.get("dependencies") or {}).get(CONNECTOR_PACKAGE)
    if manifest_dep:
        checks.append(Check("unity-cli connector manifest", "PASS", str(manifest_dep)))
    else:
        checks.append(Check("unity-cli connector manifest", "FAIL", "dependency missing", f"Restore {CONNECTOR_PACKAGE} in manifest.json."))

    lock_dep = (lock_data.get("dependencies") or {}).get(CONNECTOR_PACKAGE) or {}
    connector_hash = lock_dep.get("hash")
    if connector_hash:
        checks.append(Check("unity-cli connector lock hash", "PASS", str(connector_hash)))
    else:
        checks.append(Check("unity-cli connector lock hash", "FAIL", "hash missing", "Open Unity to resolve packages, then commit packages-lock.json."))
    return checks


def check_unity_cli(allow_unavailable: bool) -> list[Check]:
    path = shutil.which("unity-cli")
    if not path:
        return [Check("unity-cli on PATH", "FAIL", "not found", "Install the unity-cli CLI binary and put it on PATH.")]

    checks = [Check("unity-cli on PATH", "PASS", path)]
    try:
        proc = run_command(["unity-cli", "--project", "Mdfproject", "status"], timeout=45)
    except subprocess.TimeoutExpired:
        status = "WARN" if allow_unavailable else "FAIL"
        checks.append(Check("unity-cli status", status, "timeout", "Open Mdfproject in Unity and rerun the bootstrap check."))
        return checks

    output = (proc.stdout + "\n" + proc.stderr).strip()
    if proc.returncode == 0:
        status_line = next((line.strip() for line in output.splitlines() if "ready" in line.lower()), None)
        checks.append(Check("unity-cli status", "PASS", status_line or (output.splitlines()[0] if output else "ready")))
        return checks

    lower = output.lower()
    open_editor_signals = ["no unity instances", "not responding", "connection", "connect", "open unity", "no editor"]
    if any(signal in lower for signal in open_editor_signals):
        status = "WARN" if allow_unavailable else "FAIL"
        checks.append(Check("unity-cli status", status, output or "Unity Editor unavailable", "Open Mdfproject in Unity, wait for import/compile, then rerun unity-cli --project Mdfproject status."))
    else:
        checks.append(Check("unity-cli status", "FAIL", output or f"exit {proc.returncode}", "Fix unity-cli installation or connector errors."))
    return checks


def check_subprocess(name: str, command: list[str], timeout: int, next_action: str) -> Check:
    try:
        proc = run_command(command, timeout=timeout)
    except subprocess.TimeoutExpired:
        return Check(name, "FAIL", "timeout", next_action)
    output = (proc.stdout + "\n" + proc.stderr).strip()
    if proc.returncode == 0:
        return Check(name, "PASS", output.splitlines()[-1] if output else "ok")
    return Check(name, "FAIL", output[-1000:] if output else f"exit {proc.returncode}", next_action)


def check_gitignore() -> list[Check]:
    path = ROOT / ".gitignore"
    if not path.exists():
        return [Check(".gitignore", "FAIL", "missing", "Create .gitignore with Unity and MDF generated path ignores.")]
    raw_lines = path.read_text(encoding="utf-8", errors="ignore").splitlines()
    lines = {normalize_line(line) for line in raw_lines if normalize_line(line) and not normalize_line(line).startswith("#")}
    missing = [line for line in REQUIRED_GITIGNORE_LINES if line not in lines]
    checks: list[Check] = []
    if missing:
        checks.append(Check(".gitignore generated ignores", "FAIL", ", ".join(missing), "Add the missing generated path ignore patterns."))
    else:
        checks.append(Check(".gitignore generated ignores", "PASS", "all required generated paths ignored"))

    protected = sorted(line for line in lines if line in PROTECTED_IGNORE_LINES)
    meta_under_assets = sorted(line for line in lines if ASSETS_META_IGNORE_RE.match(line))
    ignored_probe_paths = git_check_ignored([
        "Mdfproject/Assets/__mdf_bootstrap_probe__.asset",
        "Mdfproject/Assets/__mdf_bootstrap_probe__.asset.meta",
        "Mdfproject/ProjectSettings/ProjectSettings.asset",
        "Mdfproject/Packages/manifest.json",
    ])
    if protected or meta_under_assets or ignored_probe_paths:
        detail = ", ".join(protected + meta_under_assets + ignored_probe_paths)
        checks.append(Check(".gitignore protected source paths", "FAIL", detail, "Do not ignore project source roots or Unity .meta files under Assets."))
    else:
        checks.append(Check(".gitignore protected source paths", "PASS", "Assets/ProjectSettings/Packages roots and Assets .meta files are not ignored"))
    return checks


def git_check_ignored(paths: list[str]) -> list[str]:
    try:
        proc = subprocess.run(
            ["git", "check-ignore", "--stdin"],
            cwd=ROOT,
            input="\n".join(paths) + "\n",
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
            timeout=10,
        )
    except Exception:
        return []
    return [line.strip().replace("\\", "/") for line in proc.stdout.splitlines() if line.strip()]


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--no-unity-if-unavailable",
        action="store_true",
        help="Do not fail when unity-cli or an open Unity Editor is unavailable; print the next action instead.",
    )
    return parser.parse_args(argv)


def print_checks(checks: list[Check]) -> int:
    failures = [check for check in checks if check.status == "FAIL"]
    for check in checks:
        print(f"{check.status:4} {check.name}: {check.detail}")
        if check.next_action and check.status != "PASS":
            print(f"     next: {check.next_action}")
    print("BOOTSTRAP " + ("FAIL" if failures else "PASS"))
    if failures:
        print("Next action: " + failures[0].next_action)
    return 1 if failures else 0


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    checks: list[Check] = []
    checks.append(check_python())
    checks.append(check_project_version())
    checks.extend(check_packages_lock())
    checks.extend(check_unity_cli(args.no_unity_if_unavailable))
    checks.append(file_exists("AGENTS.md", "AGENTS.md", "Restore AGENTS.md from git."))
    checks.extend(check_gitignore())
    checks.append(check_subprocess("validate_overlay.py", [sys.executable, "tools/harness/validate_overlay.py"], 60, "Fix missing overlay docs/scripts listed by validate_overlay.py."))
    checks.append(check_subprocess("precommit self-test", [sys.executable, "tools/harness/precommit.py", "--self-test"], 120, "Fix failing precommit self-test before feature work."))
    return print_checks(checks)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
