#!/usr/bin/env python3
"""Update learned recipe lifecycle metadata.

This script intentionally edits only the selected recipe. It can create a new
detailed recipe file when the id does not exist, but it never deletes or
archives recipes.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import pathlib
import re
import sys
from typing import Dict, List, Optional, Tuple


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
INDEX_REL = pathlib.Path("docs/ai-harness/learned-recipes.md")

KNOWN_KEYS = [
    "Status",
    "Pinned",
    "Category",
    "Created",
    "Last used",
    "Last verified",
    "Use count",
    "Review after",
    "Triggers",
    "Applies to",
    "Verified by",
    "Replacement",
    "Archive policy",
]
KEY_LOOKUP = {key.lower(): key for key in KNOWN_KEYS}
HEADING_RE = re.compile(r"^##\s+([A-Za-z0-9][A-Za-z0-9_.-]*)(?::\s*(.*))?\s*$")
META_RE = re.compile(r"^([A-Za-z][A-Za-z ]+):\s*(.*)$")
VALID_STATUSES = {"active", "stale", "archived", "deprecated", "pinned"}
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


def validate_date(value: str, label: str) -> str:
    if not DATE_RE.match(value):
        raise SystemExit(f"{label} must be YYYY-MM-DD, got {value!r}")
    try:
        _dt.date.fromisoformat(value)
    except ValueError as exc:
        raise SystemExit(f"{label} must be a valid date, got {value!r}") from exc
    return value


def today_iso(args: argparse.Namespace) -> str:
    if args.today:
        return validate_date(args.today, "--today")
    return _dt.date.today().isoformat()


def index_path(root: pathlib.Path) -> pathlib.Path:
    return root / INDEX_REL


def recipe_path(root: pathlib.Path, recipe_id: str) -> pathlib.Path:
    if not HEADING_RE.match(f"## {recipe_id}:"):
        raise SystemExit(f"Invalid recipe id {recipe_id!r}; use letters, numbers, dot, underscore, or dash.")
    return root / "docs/ai-harness/recipes" / f"{recipe_id}.md"


def recipe_files(root: pathlib.Path) -> List[pathlib.Path]:
    docs_root = root / "docs/ai-harness"
    files: List[pathlib.Path] = []
    index = docs_root / "learned-recipes.md"
    if index.exists():
        files.append(index)
    recipe_dir = docs_root / "recipes"
    if recipe_dir.exists():
        files.extend(
            p
            for p in sorted(recipe_dir.rglob("*.md"))
            if p.name.lower() != "readme.md"
        )
    return files


def find_section(lines: List[str], recipe_id: str) -> Optional[Tuple[int, int, str]]:
    starts: List[Tuple[int, re.Match[str]]] = []
    for i, line in enumerate(lines):
        match = HEADING_RE.match(line)
        if match:
            starts.append((i, match))
    for idx, (start, match) in enumerate(starts):
        if match.group(1) == recipe_id:
            end = starts[idx + 1][0] if idx + 1 < len(starts) else len(lines)
            return start, end, match.group(2) or ""
    return None


def parse_metadata(section_lines: List[str]) -> Dict[str, str]:
    metadata: Dict[str, str] = {}
    for line in section_lines[1:]:
        match = META_RE.match(line)
        if not match:
            continue
        key = KEY_LOOKUP.get(match.group(1).strip().lower())
        if key and key not in metadata:
            metadata[key] = match.group(2).strip()
    return metadata


def remove_metadata_lines(section_lines: List[str]) -> List[str]:
    kept = [section_lines[0]]
    for line in section_lines[1:]:
        match = META_RE.match(line)
        key = KEY_LOOKUP.get(match.group(1).strip().lower()) if match else None
        if key:
            continue
        kept.append(line)
    return kept


def parse_count(value: str) -> int:
    try:
        return max(0, int(value.strip()))
    except (TypeError, ValueError):
        return 0


def metadata_block(metadata: Dict[str, str]) -> List[str]:
    return [f"{key}: {metadata.get(key, '')}".rstrip() for key in KNOWN_KEYS]


def insert_lifecycle_note(body: List[str], today: str, note: Optional[str], artifacts: List[str]) -> List[str]:
    if not note and not artifacts:
        return body
    additions: List[str] = []
    has_lifecycle_notes = any(line.strip() == "Lifecycle notes:" for line in body)
    if has_lifecycle_notes:
        while body and body[-1] == "":
            body = body[:-1]
    if body and body[-1] != "" and not has_lifecycle_notes:
        additions.append("")
    if not has_lifecycle_notes:
        additions.append("Lifecycle notes:")
    if note:
        additions.append(f"- {today}: {note}")
    for artifact in artifacts:
        additions.append(f"- {today}: verified artifact `{artifact}`")
    return body + additions


def default_metadata(recipe_id: str, today: str) -> Dict[str, str]:
    return {
        "Status": "active",
        "Pinned": "false",
        "Category": "other",
        "Created": today,
        "Last used": "",
        "Last verified": "",
        "Use count": "0",
        "Review after": "",
        "Triggers": recipe_id,
        "Applies to": "",
        "Verified by": "",
        "Replacement": "none",
        "Archive policy": "archive when unused for 180 days and not pinned/protected",
    }


def update_metadata(metadata: Dict[str, str], args: argparse.Namespace, today: str) -> Dict[str, str]:
    updated = default_metadata(args.id, today) | metadata
    status = args.status.strip().lower() if args.status else updated.get("Status", "active").strip().lower()
    if not args.status and status not in VALID_STATUSES:
        status = "active"
    if status not in VALID_STATUSES:
        raise SystemExit(f"--status must be one of {sorted(VALID_STATUSES)}, got {status!r}")
    updated["Status"] = status
    if status == "pinned":
        updated["Pinned"] = "true"

    changed_usage = bool(args.used or args.verified)
    if changed_usage:
        updated["Last used"] = today
        updated["Use count"] = str(parse_count(updated.get("Use count", "0")) + 1)
    if args.verified:
        updated["Last verified"] = today
        if args.artifact:
            existing = updated.get("Verified by", "").strip()
            artifacts = ", ".join(args.artifact)
            updated["Verified by"] = f"{existing}; {artifacts}" if existing else artifacts

    for key in ("Created", "Last used", "Last verified", "Review after"):
        value = updated.get(key, "").strip()
        if value:
            validate_date(value, key)
    return updated


def load_target(root: pathlib.Path, recipe_id: str) -> Tuple[pathlib.Path, List[str], Optional[Tuple[int, int, str]]]:
    for path in recipe_files(root):
        lines = path.read_text(encoding="utf-8").splitlines()
        found = find_section(lines, recipe_id)
        if found:
            return path, lines, found

    path = recipe_path(root, recipe_id)
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = path.read_text(encoding="utf-8").splitlines() if path.exists() else []
    return path, lines, None


def rewrite_recipe(root: pathlib.Path, args: argparse.Namespace) -> pathlib.Path:
    today = today_iso(args)
    if args.verified and not args.artifact and not args.note:
        raise SystemExit("--verified requires --artifact or --note evidence")

    path, lines, found = load_target(root, args.id)
    if found:
        start, end, title = found
        section = lines[start:end]
    else:
        title = args.id.replace("-", " ")
        if lines and lines[-1] != "":
            lines.append("")
        start = len(lines)
        lines.extend([f"## {args.id}: {title}", ""])
        end = len(lines)
        section = lines[start:end]

    metadata = update_metadata(parse_metadata(section), args, today)
    body = remove_metadata_lines(section)
    body = insert_lifecycle_note(body, today, args.note, args.artifact or [])
    new_section = [body[0], "", *metadata_block(metadata)]
    remainder = body[1:]
    while remainder and remainder[0] == "":
        remainder.pop(0)
    if remainder:
        new_section.extend(["", *remainder])
    if end < len(lines) and new_section and new_section[-1] != "":
        new_section.append("")
    lines[start:end] = new_section
    path.write_text("\n".join(lines).rstrip() + "\n", encoding="utf-8")
    return path


def parse_args(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--id", required=True, help="Recipe id to update")
    parser.add_argument("--used", action="store_true", help="Mark recipe as consulted or applied")
    parser.add_argument("--verified", action="store_true", help="Mark recipe as verified by evidence")
    parser.add_argument("--artifact", action="append", default=[], help="Artifact path proving verification")
    parser.add_argument("--status", choices=sorted(VALID_STATUSES), help="Set lifecycle status")
    parser.add_argument("--note", help="Append a lifecycle note")
    parser.add_argument("--today", help="Override current date for tests, YYYY-MM-DD")
    parser.add_argument("--root", default=str(REPO_ROOT), help="Repo root")
    return parser.parse_args(argv)


def main(argv: List[str]) -> int:
    args = parse_args(argv)
    root = pathlib.Path(args.root).resolve()
    changed = rewrite_recipe(root, args)
    print(f"updated {changed.relative_to(root)} recipe={args.id}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
