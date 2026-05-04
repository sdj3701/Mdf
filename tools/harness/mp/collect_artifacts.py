#!/usr/bin/env python3
from __future__ import annotations

import argparse
import pathlib
import shutil

from parse_mptest_logs import parse_files


def copy_if_exists(src: pathlib.Path, dst: pathlib.Path) -> bool:
    if not src.exists():
        return False
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    return True


def collect_player_log(artifact_dir: pathlib.Path, label: str) -> pathlib.Path | None:
    source = pathlib.Path.home() / "AppData" / "LocalLow" / "DefaultCompany" / "Mdfproject" / "Player.log"
    target = artifact_dir / f"{label}.Player.log"
    return target if copy_if_exists(source, target) else None


def write_timeline(artifact_dir: pathlib.Path, logs: list[pathlib.Path]) -> pathlib.Path:
    events = parse_files(logs)
    out = artifact_dir / "mptest.timeline.jsonl"
    out.write_text(
        "".join(__import__("json").dumps(event, ensure_ascii=False) + "\n" for event in events),
        encoding="utf-8",
    )
    return out


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifact-dir", required=True)
    parser.add_argument("--logs", nargs="*", default=[])
    args = parser.parse_args()
    artifact_dir = pathlib.Path(args.artifact_dir)
    copied = collect_player_log(artifact_dir, "player")
    timeline = write_timeline(artifact_dir, [pathlib.Path(p) for p in args.logs] + ([copied] if copied else []))
    print(timeline)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
