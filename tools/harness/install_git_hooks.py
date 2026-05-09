#!/usr/bin/env python3
"""Install MDF repo-local Git hooks."""

from __future__ import annotations

import argparse
import pathlib
import stat
import subprocess
import sys


def find_repo_root() -> pathlib.Path:
    try:
        output = subprocess.check_output(
            ["git", "rev-parse", "--show-toplevel"],
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
        if output:
            return pathlib.Path(output).resolve()
    except Exception:
        pass

    current = pathlib.Path.cwd().resolve()
    for path in [current, *current.parents]:
        if (path / ".git").exists():
            return path
    raise SystemExit("Could not find git repository root. Run this from inside the MDF clone.")


def hook_text() -> str:
    return """#!/usr/bin/env sh
set -eu
ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

run_python() {
  if "$@" -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >/dev/null 2>&1; then
    exec "$@" tools/harness/precommit.py
  fi
}

run_python python
run_python python3
run_python py -3

echo "MDF pre-commit hook requires Python 3.10+ on PATH." >&2
exit 1
"""


def install_hook(root: pathlib.Path, dry_run: bool) -> pathlib.Path:
    try:
        git_dir = subprocess.check_output(
            ["git", "rev-parse", "--git-dir"],
            cwd=root,
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
        if not git_dir:
            raise RuntimeError("empty git dir")
        hook_rel = subprocess.check_output(
            ["git", "rev-parse", "--git-path", "hooks/pre-commit"],
            cwd=root,
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
        hook_path = (root / hook_rel).resolve() if hook_rel else (root / ".git/hooks/pre-commit")
    except Exception as exc:
        raise SystemExit(f"Could not resolve git hook path: {type(exc).__name__}: {exc}") from exc

    hook_dir = hook_path.parent
    if dry_run:
        print(f"DRY-RUN would create {hook_path}")
        print("DRY-RUN hook command: python/python3/py -3 tools/harness/precommit.py")
        return hook_path

    hook_dir.mkdir(parents=True, exist_ok=True)
    hook_path.write_text(hook_text(), encoding="utf-8", newline="\n")
    mode = hook_path.stat().st_mode
    hook_path.chmod(mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    return hook_path


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Print planned hook path without writing")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    root = find_repo_root()
    hook_path = install_hook(root, args.dry_run)
    action = "Would install" if args.dry_run else "Installed"
    print(f"{action} MDF harness pre-commit hook: {hook_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
