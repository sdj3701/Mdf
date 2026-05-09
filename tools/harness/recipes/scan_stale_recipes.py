#!/usr/bin/env python3
"""Scan learned recipes for stale, archive, and deprecated-reference signals.

The scan is advisory. It never deletes, moves, or edits recipes.
"""

from __future__ import annotations

import argparse
import datetime as _dt
import json
import pathlib
import re
import sys
from dataclasses import dataclass
from typing import Dict, Iterable, List, Optional


REPO_ROOT = pathlib.Path(__file__).resolve().parents[3]
KNOWN_KEYS = {
    "status": "Status",
    "pinned": "Pinned",
    "category": "Category",
    "created": "Created",
    "last used": "Last used",
    "last verified": "Last verified",
    "use count": "Use count",
    "review after": "Review after",
    "triggers": "Triggers",
    "applies to": "Applies to",
    "verified by": "Verified by",
    "replacement": "Replacement",
    "archive policy": "Archive policy",
}
VALID_STATUSES = {"active", "stale", "archived", "deprecated", "pinned"}
REQUIRED_METADATA = tuple(KNOWN_KEYS.values())
HEADING_RE = re.compile(r"^##\s+([A-Za-z0-9][A-Za-z0-9_.-]*)(?::\s*(.*))?\s*$")
META_RE = re.compile(r"^([A-Za-z][A-Za-z ]+):\s*(.*)$")
PROTECTED_RE = re.compile(
    r"safety|cleanup|security|token|host[- ]?migration|reconnect|automation[- ]server[- ]gate|automation-server-gates",
    re.IGNORECASE,
)
REFERENCE_SKIP_PARTS = {
    "archive",
    "deprecated",
}


@dataclass
class Recipe:
    recipe_id: str
    title: str
    path: pathlib.Path
    line: int
    metadata: Dict[str, str]
    body: str

    @property
    def status(self) -> str:
        return self.metadata.get("Status", "").strip().lower()

    @property
    def pinned(self) -> bool:
        return self.status == "pinned" or self.metadata.get("Pinned", "").strip().lower() == "true"

    @property
    def protected(self) -> bool:
        haystack = " ".join(
            [
                self.metadata.get("Category", ""),
                self.metadata.get("Triggers", ""),
                self.metadata.get("Applies to", ""),
                self.metadata.get("Archive policy", ""),
                self.title,
                self.recipe_id,
            ]
        )
        return self.pinned or bool(PROTECTED_RE.search(haystack))


def parse_date(value: str) -> Optional[_dt.date]:
    value = value.strip()
    if not value:
        return None
    try:
        return _dt.date.fromisoformat(value)
    except ValueError:
        return None


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


def parse_recipes(path: pathlib.Path) -> List[Recipe]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    starts: List[tuple[int, re.Match[str]]] = []
    for i, line in enumerate(lines):
        match = HEADING_RE.match(line)
        if match:
            starts.append((i, match))

    recipes: List[Recipe] = []
    for idx, (start, match) in enumerate(starts):
        end = starts[idx + 1][0] if idx + 1 < len(starts) else len(lines)
        section = lines[start:end]
        metadata: Dict[str, str] = {}
        for line in section[1:]:
            meta_match = META_RE.match(line)
            if not meta_match:
                continue
            key = KNOWN_KEYS.get(meta_match.group(1).strip().lower())
            if key and key not in metadata:
                metadata[key] = meta_match.group(2).strip()
        recipes.append(
            Recipe(
                recipe_id=match.group(1),
                title=match.group(2) or "",
                path=path,
                line=start + 1,
                metadata=metadata,
                body="\n".join(section),
            )
        )
    return recipes


def all_recipes(root: pathlib.Path) -> List[Recipe]:
    recipes: List[Recipe] = []
    for path in recipe_files(root):
        recipes.extend(parse_recipes(path))
    return recipes


def age_days(recipe: Recipe, today: _dt.date) -> Optional[int]:
    dates = [
        parse_date(recipe.metadata.get("Last used", "")),
        parse_date(recipe.metadata.get("Last verified", "")),
        parse_date(recipe.metadata.get("Created", "")),
    ]
    dates = [d for d in dates if d is not None]
    if not dates:
        return None
    return (today - max(dates)).days


def active_doc_files(root: pathlib.Path) -> Iterable[pathlib.Path]:
    docs = root / "docs/ai-harness"
    if not docs.exists():
        return []
    result: List[pathlib.Path] = []
    for path in sorted(docs.rglob("*.md")):
        rel_parts = set(path.relative_to(docs).parts)
        if rel_parts & REFERENCE_SKIP_PARTS:
            continue
        result.append(path)
    return result


def deprecated_references(root: pathlib.Path, deprecated: List[Recipe]) -> List[Dict[str, str]]:
    if not deprecated:
        return []
    texts = []
    for path in active_doc_files(root):
        try:
            texts.append((path, path.read_text(encoding="utf-8")))
        except UnicodeDecodeError:
            continue
    refs: List[Dict[str, str]] = []
    for recipe in deprecated:
        for path, text in texts:
            if path == recipe.path:
                continue
            if recipe.recipe_id in text:
                refs.append(
                    {
                        "id": recipe.recipe_id,
                        "path": str(path.relative_to(root)),
                        "recipePath": str(recipe.path.relative_to(root)),
                    }
                )
    return refs


def deprecated_has_reason(recipe: Recipe) -> bool:
    replacement = recipe.metadata.get("Replacement", "").strip().lower()
    if replacement and replacement != "none":
        return True
    return bool(
        re.search(
            r"\b(reason|no replacement|superseded|replaced by|deprecated because)\b",
            recipe.body,
            re.IGNORECASE,
        )
    )


def scan(root: pathlib.Path, today: _dt.date, stale_days: int, archive_days: int) -> Dict[str, object]:
    recipes = all_recipes(root)
    stale: List[Dict[str, object]] = []
    archive: List[Dict[str, object]] = []
    invalid_statuses: List[Dict[str, str]] = []
    missing_metadata: List[Dict[str, object]] = []
    warnings: List[str] = []
    deprecated: List[Recipe] = []

    for recipe in recipes:
        status = recipe.status
        age = age_days(recipe, today)
        location = f"{recipe.path.relative_to(root)}:{recipe.line}"

        if status and status not in VALID_STATUSES:
            invalid_statuses.append(
                {
                    "id": recipe.recipe_id,
                    "status": recipe.metadata.get("Status", ""),
                    "path": location,
                }
            )
            warnings.append(f"{recipe.recipe_id} invalid Status={recipe.metadata.get('Status', '')!r} at {location}")

        if status in {"active", "pinned"} or recipe.pinned:
            missing = [key for key in REQUIRED_METADATA if not recipe.metadata.get(key)]
            if missing:
                missing_metadata.append({"id": recipe.recipe_id, "missing": missing, "path": location})
                warnings.append(
                    f"{recipe.recipe_id} active/pinned missing required metadata at {location}: "
                    + ", ".join(missing)
                )

        if status == "deprecated":
            deprecated.append(recipe)
            if not deprecated_has_reason(recipe):
                warnings.append(f"{recipe.recipe_id} deprecated without replacement or reason at {location}")

        if status in {"active", "stale"} and age is not None and age >= stale_days:
            stale.append({"id": recipe.recipe_id, "ageDays": age, "path": location})
        if (
            status in {"active", "stale", "deprecated"}
            and age is not None
            and age >= archive_days
            and not recipe.protected
        ):
            archive.append({"id": recipe.recipe_id, "ageDays": age, "path": location})

    deprecated_refs = deprecated_references(root, deprecated)
    return {
        "recipeCount": len(recipes),
        "invalidStatuses": invalid_statuses,
        "missingMetadata": missing_metadata,
        "staleRecipes": stale,
        "archiveCandidates": archive,
        "deprecatedReferences": deprecated_refs,
        "warnings": warnings,
    }


def parse_args(argv: List[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stale-days", type=int, default=90)
    parser.add_argument("--archive-days", type=int, default=180)
    parser.add_argument("--today", help="Override current date for tests, YYYY-MM-DD")
    parser.add_argument("--root", default=str(REPO_ROOT), help="Repo root")
    parser.add_argument("--json", action="store_true", help="Emit JSON")
    return parser.parse_args(argv)


def parse_today(value: Optional[str]) -> _dt.date:
    if not value:
        return _dt.date.today()
    try:
        return _dt.date.fromisoformat(value)
    except ValueError as exc:
        raise SystemExit(f"--today must be YYYY-MM-DD, got {value!r}") from exc


def print_text(result: Dict[str, object]) -> None:
    print(f"recipes: {result['recipeCount']}")
    for label, key in [
        ("stale recipes", "staleRecipes"),
        ("archive candidates", "archiveCandidates"),
        ("deprecated references", "deprecatedReferences"),
        ("warnings", "warnings"),
    ]:
        values = result[key]
        print(f"{label}: {len(values)}")
        for value in values[:20]:
            if isinstance(value, dict):
                print("  - " + ", ".join(f"{k}={v}" for k, v in value.items()))
            else:
                print(f"  - {value}")
        if len(values) > 20:
            print(f"  - ... {len(values) - 20} more")


def main(argv: List[str]) -> int:
    args = parse_args(argv)
    root = pathlib.Path(args.root).resolve()
    result = scan(root, parse_today(args.today), args.stale_days, args.archive_days)
    if args.json:
        print(json.dumps(result, indent=2, sort_keys=True))
    else:
        print_text(result)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
