#!/usr/bin/env python3
"""
unity_context_pack.py

Create small, AI-friendly context bundles from a Unity project.
Designed for MDF, but reusable for other Unity projects through a JSON config.

Examples:
  python _context_packer/unity_context_pack.py --profile scripts-plus-context
  python _context_packer/unity_context_pack.py --profile trimmed-project --copy --no-zip
  python _context_packer/unity_context_pack.py --profile failure-artifacts --artifact-path artifacts/mp/20260505-case
  python _context_packer/unity_context_pack.py --list-profiles
"""

from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import os
import re
import shutil
import sys
import time
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Set, Tuple

TOOL_VERSION = "1.2.0"

GLOB_CHARS = set("*?[")

DEFAULT_CONFIG: Dict[str, Any] = {
    "project_subdir": "Mdfproject",
    "output_dir": "_context_packer/output",
    "max_file_mb": 25,
    "include_unity_meta": True,
    "include_unity_folder_meta": False,
    "sensitive_excludes": [
        "**/.env", "**/.env.*", "**/*secret*", "**/*Secret*", "**/*token*", "**/*Token*",
        "**/*apikey*", "**/*ApiKey*", "**/*password*", "**/*Password*", "**/*.pfx", "**/*.pem",
        "**/*PhotonAppSettings*.asset", "**/*PhotonServerSettings*.asset", "**/*AppId*.asset",
    ],
    "global_excludes": [
        "**/.git/**", "**/.svn/**", "**/.hg/**",
        "**/Library/**", "**/Temp/**", "**/Logs/**", "**/obj/**", "**/Obj/**",
        "**/Build/**", "**/Builds/**", "**/MemoryCaptures/**", "**/UserSettings/**", "**/.vs/**",
        "**/_context_packer/output/**", "**/_context_bundles/**", "**/.codex/session-state/**",
        "**/node_modules/**", "**/__pycache__/**", "**/*.pyc", "**/.DS_Store", "**/Thumbs.db",
        "**/*.csproj", "**/*.sln", "**/*.user", "**/*.pidb", "**/*.booproj",
        "**/*.apk", "**/*.aab", "**/*.exe", "**/*.dll", "**/*.pdb",
        "**/*.zip", "**/*.tar", "**/*.tar.gz", "**/*.7z", "**/*.rar",
    ],
    "profiles": {
        "scripts": {
            "description": "Only C# scripts plus tiny project instructions. Fastest for code design/review.",
            "include": [
                "AGENTS.md",
                ".agent/rules/projectrull.md",
                "Mdfproject/Assets/Scripts/**/*.cs",
                "Mdfproject/Assets/Scripts/**/*.asmdef",
            ],
            "exclude": [],
        },
        "scripts-plus-context": {
            "description": "Recommended default. Scripts plus harness docs, Codex config, packages, key settings, and targeted gameplay assets.",
            "include": [
                "AGENTS.md",
                ".agent/rules/projectrull.md",
                "docs/ai-harness/**",
                "docs/ai-design/**",
                ".agents/**",
                ".codex/**",
                "tools/harness/**",
                "_context_packer/**",
                "tools/context_pack/**",
                "Mdfproject/Assets/Scripts/**",
                "Mdfproject/Packages/manifest.json",
                "Mdfproject/Packages/packages-lock.json",
                "Mdfproject/ProjectSettings/ProjectVersion.txt",
                "Mdfproject/ProjectSettings/EditorBuildSettings.asset",
                "Mdfproject/ProjectSettings/TagManager.asset",
                "Mdfproject/ProjectSettings/ProjectSettings.asset",
                "Mdfproject/Assets/Photon/Fusion/build_info.txt",
                "Mdfproject/Assets/**/*NetworkProjectConfig*.asset",
                "Mdfproject/Assets/**/NetworkProjectConfig*.asset",
                "Mdfproject/Assets/**/*MagicScroll*.asset",
                "Mdfproject/Assets/**/*Scroll*.asset",
                "Mdfproject/Assets/**/*Skill*.asset",
                "Mdfproject/Assets/**/*Augment*.asset",
                "Mdfproject/Assets/**/*Unit*.asset",
                "Mdfproject/Assets/**/*Monster*.asset",
                "Mdfproject/Assets/**/AddressableAssetsData/**",
            ],
            "exclude": [
                "**/artifacts/**", "**/Artifacts/**",
                ".codex/session-state/**", "**/.codex/session-state/**",
                "Mdfproject/Assets/StreamingAssets/aa/**",
            ],
        },
        "trimmed-project": {
            "description": "Unity project context without generated/cache/build folders. Use for prefab/scene/ScriptableObject/Fusion config questions.",
            "include": [
                "AGENTS.md", ".agent/**", ".agents/**", ".codex/**",
                "docs/**", "tools/**",
                "Mdfproject/Assets/**", "Mdfproject/Packages/**", "Mdfproject/ProjectSettings/**",
            ],
            "exclude": [
                "**/artifacts/**", "**/Artifacts/**",
                ".codex/session-state/**", "**/.codex/session-state/**",
                "Mdfproject/Assets/StreamingAssets/aa/**",
                "Mdfproject/Assets/Photon/Fusion/Assemblies/**",
            ],
        },
        "harness-only": {
            "description": "Harness docs/scripts/config only. Use when asking about the Codex harness itself.",
            "include": [
                "AGENTS.md", ".agent/rules/projectrull.md", ".agents/**", ".codex/**",
                "docs/ai-harness/**", "docs/ai-design/**", "tools/harness/**", "_context_packer/**",
                "tools/context_pack/**",
                "Mdfproject/Assets/Scripts/Testing/MP/**",
                "Mdfproject/Assets/Scripts/Testing/MP.meta",
            ],
            "exclude": [".codex/session-state/**", "**/.codex/session-state/**"],
        },
        "failure-artifacts": {
            "description": "Failure artifacts plus minimal harness/scripts context. Requires --artifact-path.",
            "include": [
                "AGENTS.md", ".agent/rules/projectrull.md",
                "docs/ai-harness/**", "docs/ai-design/**",
                "tools/harness/mp/**",
                "Mdfproject/Assets/Scripts/Testing/MP/**",
                "Mdfproject/Assets/Scripts/Network/**",
                "Mdfproject/Assets/Scripts/Managers/**",
                "Mdfproject/Assets/Scripts/Commands/**",
                "Mdfproject/Assets/Scripts/AI/**",
                "Mdfproject/Assets/Scripts/Game/**",
            ],
            "exclude": [],
        },
    },
}


def rel_posix(path: Path, root: Path) -> str:
    return path.resolve().relative_to(root.resolve()).as_posix()


def norm_pattern(pattern: str) -> str:
    return pattern.replace("\\", "/").strip()


def has_glob(pattern: str) -> bool:
    return any(ch in pattern for ch in GLOB_CHARS)


def path_matches(rel: str, patterns: Sequence[str]) -> bool:
    rel = rel.replace("\\", "/")
    for raw in patterns:
        pat = norm_pattern(raw)
        if not pat:
            continue
        # Direct directory pattern without ** should match children too.
        if pat.endswith("/"):
            if rel.startswith(pat):
                return True
        if fnmatch.fnmatch(rel, pat):
            return True
        if pat.endswith("/**"):
            base = pat[:-3]
            if rel == base or rel.startswith(base + "/"):
                return True
    return False


def load_config(root: Path, config_path: Optional[Path]) -> Dict[str, Any]:
    config = json.loads(json.dumps(DEFAULT_CONFIG))
    candidates: List[Path] = []
    if config_path:
        candidates.append(config_path)
    else:
        candidates.append(root / "_context_packer" / "mdf_context_pack.config.json")
        candidates.append(root / "tools" / "context_pack" / "mdf_context_pack.config.json")
        candidates.append(root / "context_pack.config.json")
    for path in candidates:
        if path.exists():
            with path.open("r", encoding="utf-8") as f:
                user = json.load(f)
            deep_merge(config, user)
            break
    return config


def deep_merge(base: Dict[str, Any], override: Dict[str, Any]) -> None:
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(base.get(key), dict):
            deep_merge(base[key], value)
        else:
            base[key] = value


def iter_pattern_paths(root: Path, pattern: str) -> Iterable[Path]:
    pattern = norm_pattern(pattern)
    # Exact path: include file or recurse directory.
    if not has_glob(pattern):
        target = root / pattern
        if target.is_file():
            yield target
        elif target.is_dir():
            for p in target.rglob("*"):
                if p.is_file():
                    yield p
        return

    # pathlib.Path.glob supports ** patterns relative to root.
    try:
        for p in root.glob(pattern):
            if p.is_file():
                yield p
            elif p.is_dir():
                for child in p.rglob("*"):
                    if child.is_file():
                        yield child
    except re.error:  # type: ignore[name-defined]
        return


def add_unity_meta(paths: Set[Path], root: Path, include_folder_meta: bool) -> None:
    additions: Set[Path] = set()
    for p in list(paths):
        if p.suffix == ".meta":
            continue
        sidecar = Path(str(p) + ".meta")
        if sidecar.exists() and sidecar.is_file():
            additions.add(sidecar)
        if include_folder_meta:
            # Include folder.meta for each parent inside Assets.
            try:
                rel = rel_posix(p, root)
            except ValueError:
                continue
            if "/Assets/" not in rel and not rel.startswith("Mdfproject/Assets/"):
                continue
            parent = p.parent
            while parent != root and parent.name:
                meta = Path(str(parent) + ".meta")
                if meta.exists() and meta.is_file():
                    additions.add(meta)
                if parent == root:
                    break
                parent = parent.parent
    paths.update(additions)


@dataclass
class CollectResult:
    files: List[Path]
    skipped_excluded: List[str] = field(default_factory=list)
    skipped_large: List[str] = field(default_factory=list)
    missing_patterns: List[str] = field(default_factory=list)


def collect_files(root: Path, config: Dict[str, Any], profile_name: str, artifact_paths: Sequence[str], include_sensitive: bool) -> CollectResult:
    profiles = config.get("profiles", {})
    if profile_name not in profiles:
        raise SystemExit(f"Unknown profile '{profile_name}'. Use --list-profiles.")
    profile = profiles[profile_name]
    includes = list(profile.get("include", []))
    if profile_name == "failure-artifacts":
        if not artifact_paths:
            raise SystemExit("Profile 'failure-artifacts' requires --artifact-path <path>.")
        includes.extend(artifact_paths)

    excludes = list(config.get("global_excludes", [])) + list(profile.get("exclude", []))
    if not include_sensitive:
        excludes += list(config.get("sensitive_excludes", []))

    max_bytes = int(float(config.get("max_file_mb", 25)) * 1024 * 1024)
    collected: Set[Path] = set()
    result = CollectResult(files=[])

    for pattern in includes:
        before = len(collected)
        for p in iter_pattern_paths(root, pattern):
            try:
                rel = rel_posix(p, root)
            except ValueError:
                continue
            if path_matches(rel, excludes):
                result.skipped_excluded.append(rel)
                continue
            try:
                size = p.stat().st_size
            except OSError:
                continue
            if size > max_bytes:
                result.skipped_large.append(f"{rel} ({size} bytes)")
                continue
            collected.add(p.resolve())
        if len(collected) == before:
            # Only mark exact-ish important patterns as missing to avoid noise from optional globs.
            if not has_glob(pattern) or any(k in pattern.lower() for k in ["networkprojectconfig", "build_info", "projectversion", "manifest.json"]):
                result.missing_patterns.append(pattern)

    if config.get("include_unity_meta", True):
        add_unity_meta(collected, root, bool(config.get("include_unity_folder_meta", False)))

    # Re-filter meta additions.
    final: List[Path] = []
    for p in sorted(collected, key=lambda x: rel_posix(x, root).lower()):
        rel = rel_posix(p, root)
        if path_matches(rel, excludes):
            result.skipped_excluded.append(rel)
            continue
        if p.exists() and p.is_file():
            final.append(p)
    result.files = final
    return result


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def safe_rmtree(path: Path) -> None:
    if path.exists():
        shutil.rmtree(path)


def copy_files(root: Path, files: Sequence[Path], dest: Path) -> None:
    safe_rmtree(dest)
    dest.mkdir(parents=True, exist_ok=True)
    for src in files:
        rel = rel_posix(src, root)
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, target)


def write_zip(root: Path, files: Sequence[Path], zip_path: Path) -> None:
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zf:
        for src in files:
            rel = rel_posix(src, root)
            zf.write(src, arcname=rel)


def make_manifest(root: Path, profile: str, result: CollectResult, output_dir: Path, zip_path: Optional[Path], copy_dir: Optional[Path], args: argparse.Namespace) -> Dict[str, Any]:
    files_info = []
    total_bytes = 0
    for p in result.files:
        st = p.stat()
        total_bytes += st.st_size
        files_info.append({
            "path": rel_posix(p, root),
            "bytes": st.st_size,
            "sha256": sha256_file(p),
        })
    return {
        "tool": "unity_context_pack.py",
        "toolVersion": TOOL_VERSION,
        "createdUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "root": str(root),
        "profile": profile,
        "fileCount": len(result.files),
        "totalBytes": total_bytes,
        "zipPath": str(zip_path) if zip_path else None,
        "copyDir": str(copy_dir) if copy_dir else None,
        "includeSensitive": bool(args.include_sensitive),
        "skippedExcludedCount": len(result.skipped_excluded),
        "skippedLarge": result.skipped_large,
        "missingPatterns": result.missing_patterns,
        "files": files_info,
    }


def find_repo_root(start: Path) -> Path:
    start = start.resolve()
    for p in [start] + list(start.parents):
        if (p / "AGENTS.md").exists() or (p / "Mdfproject").exists() or (p / "Assets").exists():
            return p
    return start


def list_profiles(config: Dict[str, Any]) -> None:
    print("Available profiles:")
    for name, profile in config.get("profiles", {}).items():
        print(f"  {name:22} {profile.get('description', '')}")


def parse_args(argv: Optional[Sequence[str]] = None) -> argparse.Namespace:
    ap = argparse.ArgumentParser(description="Create AI-friendly Unity project context bundles.")
    ap.add_argument("--root", default=None, help="Repository/project root. Default: auto-detect from current directory.")
    ap.add_argument("--config", default=None, help="Optional JSON config path.")
    ap.add_argument("--profile", default="scripts-plus-context", help="Bundle profile to use.")
    ap.add_argument("--out", default=None, help="Output directory. Default: config output_dir or _context_bundles.")
    ap.add_argument("--name", default=None, help="Output bundle base name. Default: <profile>-<timestamp>.")
    ap.add_argument("--copy", action="store_true", help="Also create an unpacked copy directory.")
    ap.add_argument("--no-zip", action="store_true", help="Do not create zip file.")
    ap.add_argument("--dry-run", action="store_true", help="Only print what would be included.")
    ap.add_argument("--list-profiles", action="store_true", help="List profiles and exit.")
    ap.add_argument("--artifact-path", action="append", default=[], help="Artifact directory/file to include for failure-artifacts profile. Can be repeated.")
    ap.add_argument("--include-sensitive", action="store_true", help="Disable sensitive-file exclusion. Use only for local/private bundles.")
    ap.add_argument("--max-file-mb", type=float, default=None, help="Override maximum single-file size.")
    return ap.parse_args(argv)


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = parse_args(argv)
    root = find_repo_root(Path(args.root) if args.root else Path.cwd())
    config_path = Path(args.config).resolve() if args.config else None
    config = load_config(root, config_path)
    if args.max_file_mb is not None:
        config["max_file_mb"] = args.max_file_mb
    if args.list_profiles:
        list_profiles(config)
        return 0

    profile = args.profile
    result = collect_files(root, config, profile, args.artifact_path, args.include_sensitive)
    timestamp = time.strftime("%Y%m%d-%H%M%S", time.localtime())
    base_name = args.name or f"{profile}-{timestamp}"
    out_dir = Path(args.out).resolve() if args.out else (root / config.get("output_dir", "_context_bundles")).resolve()
    copy_dir = out_dir / base_name if args.copy else None
    zip_path = None if args.no_zip else out_dir / f"{base_name}.zip"
    manifest_path = out_dir / f"{base_name}.manifest.json"

    print(f"Root: {root}")
    print(f"Profile: {profile}")
    print(f"Files: {len(result.files)}")
    print(f"Skipped excluded: {len(result.skipped_excluded)}")
    if result.skipped_large:
        print(f"Skipped large: {len(result.skipped_large)}")
    if result.missing_patterns:
        print(f"Missing optional/exact patterns: {len(result.missing_patterns)}")
        for pat in result.missing_patterns[:20]:
            print(f"  - {pat}")

    if args.dry_run:
        for p in result.files[:300]:
            print(rel_posix(p, root))
        if len(result.files) > 300:
            print(f"... {len(result.files) - 300} more files")
        return 0

    out_dir.mkdir(parents=True, exist_ok=True)
    if copy_dir:
        copy_files(root, result.files, copy_dir)
        print(f"Copied to: {copy_dir}")
    if zip_path:
        write_zip(root, result.files, zip_path)
        print(f"ZIP: {zip_path}")

    manifest = make_manifest(root, profile, result, out_dir, zip_path, copy_dir, args)
    with manifest_path.open("w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print(f"Manifest: {manifest_path}")
    if zip_path:
        print(f"SHA256: {sha256_file(zip_path)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        print("Interrupted.", file=sys.stderr)
        raise SystemExit(130)
