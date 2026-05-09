#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import pathlib
from typing import Any


MARKER = "[MPTEST]"


def parse_payload(payload: str) -> dict[str, Any]:
    result: dict[str, Any] = {}
    index = 0
    length = len(payload)

    while index < length:
        while index < length and payload[index].isspace():
            index += 1
        if index >= length:
            break

        key_start = index
        while index < length and not payload[index].isspace() and payload[index] != "=":
            index += 1

        if index >= length or payload[index] != "=":
            while index < length and not payload[index].isspace():
                index += 1
            continue

        key = payload[key_start:index]
        index += 1

        if index < length and payload[index] == '"':
            index += 1
            value_start = index
            while index < length and payload[index] != '"':
                index += 1
            value = payload[value_start:index]
            if index < length and payload[index] == '"':
                index += 1
        else:
            value_start = index
            while index < length and not payload[index].isspace():
                index += 1
            value = payload[value_start:index]

        if key:
            result[key] = value

    return result


def iter_line_events(line: str) -> list[dict[str, Any]]:
    starts: list[int] = []
    search = 0
    while True:
        found = line.find(MARKER, search)
        if found < 0:
            break
        starts.append(found)
        search = found + len(MARKER)

    events: list[dict[str, Any]] = []
    for offset, start in enumerate(starts):
        end = starts[offset + 1] if offset + 1 < len(starts) else len(line)
        raw = line[start:end].rstrip("\n")
        payload = raw[len(MARKER) :].strip()
        parsed: dict[str, Any] = {"raw": raw}
        parsed.update(parse_payload(payload))
        events.append(parsed)
    return events


def parse_line(line: str) -> dict[str, Any] | None:
    events = iter_line_events(line)
    return events[0] if events else None


def parse_files(paths: list[pathlib.Path]) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for path in paths:
        if not path.exists():
            continue
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            for parsed in iter_line_events(line):
                parsed["logSource"] = str(path)
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
