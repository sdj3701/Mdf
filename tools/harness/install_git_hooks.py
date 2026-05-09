#!/usr/bin/env python3
"""Install MDF repo-local Git hooks."""

from __future__ import annotations

import argparse
import pathlib
import stat
import subprocess
import sys


MDF_HOOK_MARKER = "MDF-HARNESS-PRECOMMIT"
LFS_HOOK_NAMES = ("pre-push", "post-checkout", "post-commit", "post-merge")


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


def hook_text(preserved_hook_name: str | None = None) -> str:
    preserved_block = ""
    if preserved_hook_name:
        preserved_block = f"""HOOK_DIR="$(dirname "$0")"
PRESERVED_HOOK="$HOOK_DIR/{preserved_hook_name}"
if [ -x "$PRESERVED_HOOK" ]; then
  "$PRESERVED_HOOK" "$@"
fi

"""

    return (
        "#!/usr/bin/env sh\n"
        f"# {MDF_HOOK_MARKER}\n"
        "set -eu\n"
        f"{preserved_block}"
        'ROOT="$(git rev-parse --show-toplevel)"\n'
        'cd "$ROOT"\n'
        "\n"
        "run_python() {\n"
        '  if "$@" -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >/dev/null 2>&1; then\n'
        '    exec "$@" tools/harness/precommit.py\n'
        "  fi\n"
        "}\n"
        "\n"
        "run_python python\n"
        "run_python python3\n"
        "run_python py -3\n"
        "\n"
        'echo "MDF pre-commit hook requires Python 3.10+ on PATH." >&2\n'
        "exit 1\n"
    )


def is_mdf_hook(path: pathlib.Path) -> bool:
    if not path.exists():
        return False
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return False
    return MDF_HOOK_MARKER in text or "tools/harness/precommit.py" in text


def unique_preserve_path(hook_path: pathlib.Path) -> pathlib.Path:
    base = hook_path.with_name("pre-commit.mdf-preserved")
    if not base.exists():
        return base
    for index in range(1, 100):
        candidate = hook_path.with_name(f"pre-commit.mdf-preserved.{index}")
        if not candidate.exists():
            return candidate
    raise SystemExit("Could not find a free backup name for existing pre-commit hook.")


def git_lfs_hooks(hook_dir: pathlib.Path) -> list[str]:
    found: list[str] = []
    for name in LFS_HOOK_NAMES:
        path = hook_dir / name
        if not path.exists():
            continue
        try:
            text = path.read_text(encoding="utf-8", errors="replace").lower()
        except OSError:
            continue
        if "git lfs" in text or "git-lfs" in text:
            found.append(name)
    return found


def install_hook(root: pathlib.Path, dry_run: bool) -> tuple[pathlib.Path, list[str]]:
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
    preserved_name: str | None = None
    if hook_path.exists() and not is_mdf_hook(hook_path):
        preserve_path = unique_preserve_path(hook_path)
        preserved_name = preserve_path.name
        if dry_run:
            print(f"DRY-RUN would preserve existing pre-commit hook as {preserve_path}")
        else:
            hook_path.replace(preserve_path)
            mode = preserve_path.stat().st_mode
            preserve_path.chmod(mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)

    lfs_hooks = git_lfs_hooks(hook_dir)
    if dry_run:
        print(f"DRY-RUN would install {hook_path}")
        print("DRY-RUN hook command: python/python3/py -3 tools/harness/precommit.py")
        if lfs_hooks:
            print(f"DRY-RUN would leave Git LFS hooks unchanged: {', '.join(lfs_hooks)}")
        else:
            print("DRY-RUN found no Git LFS sibling hooks to preserve.")
        return hook_path, lfs_hooks

    hook_dir.mkdir(parents=True, exist_ok=True)
    hook_path.write_text(hook_text(preserved_name), encoding="utf-8", newline="\n")
    mode = hook_path.stat().st_mode
    hook_path.chmod(mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    return hook_path, lfs_hooks


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="Print planned hook path without writing")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    root = find_repo_root()
    hook_path, lfs_hooks = install_hook(root, args.dry_run)
    action = "Would install" if args.dry_run else "Installed"
    print(f"{action} MDF harness pre-commit hook: {hook_path}")
    if lfs_hooks:
        print(f"Preserved Git LFS hooks: {', '.join(lfs_hooks)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
