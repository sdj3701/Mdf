#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
import shlex
from typing import Any


def parse_line(line: str) -> dict[str, Any] | None:
    marker = "[MPTEST]"
    index = line.find(marker)
    if index < 0:
        return None
    payload = line[index + len(marker) :].strip()
    result: dict[str, Any] = {"raw": line.rstrip("\n")}
    for token in shlex.split(payload):
        if "=" not in token:
            continue
        key, value = token.split("=", 1)
        result[key] = value
    return result


def parse_files(paths: list[pathlib.Path]) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for path in paths:
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            parsed = parse_line(line)
            if parsed is not None:
                parsed["source"] = str(path)
                events.append(parsed)
    return events


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("logs", nargs="+")
    parser.add_argument("--output")
    args = parser.parse_args()
    events = parse_files([pathlib.Path(p) for p in args.logs])
    text = "\n".join(json.dumps(event, ensure_ascii=False) for event in events) + ("\n" if events else "")
    if args.output:
        pathlib.Path(args.output).write_text(text, encoding="utf-8")
    else:
        print(text, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
