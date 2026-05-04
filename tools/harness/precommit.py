#!/usr/bin/env python3
"""MDF harness static checks.

Run:
  python tools/harness/precommit.py --all
  python tools/harness/precommit.py --self-test
"""
from __future__ import annotations
import argparse
import pathlib
import re
import subprocess
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
CS_EXT = {'.cs'}
ASSET_EXT = {'.prefab', '.unity', '.asset', '.mat', '.controller', '.anim'}

VENDOR_PREFIXES = [
    'Mdfproject/Assets/Photon/Fusion/',
    'Mdfproject/Assets/Firebase/',
    'Mdfproject/Assets/Samples/',
    'Mdfproject/Assets/TextMesh Pro/',
    'Mdfproject/Assets/Resource/Fonts/TextMesh Pro/',
    'Mdfproject/Assets/Toon',
    'Mdfproject/Assets/Resource/Shaders/JMO Assets/Toony Colors Pro/',
]

PROTECTED_PATHS = [
    'Mdfproject/Assets',
    'Mdfproject/ProjectSettings',
    'Mdfproject/Packages',
    'Mdfproject/Library',
    'Assets',
    'ProjectSettings',
    'Packages',
    'Library',
]

HOST_MIGRATION_CRITICAL = {
    'Mdfproject/Assets/Scripts/Network/NetworkManager.cs',
    'Mdfproject/Assets/Scripts/Network/HostMigrationHandler.cs',
    'Mdfproject/Assets/Scripts/Managers/GameManagers.MigrationRecovery.cs',
    'Mdfproject/Assets/Scripts/Managers/PlayerManager.cs',
    'Mdfproject/Assets/Scripts/Managers/FieldManager.cs',
}

WARN_PATTERNS = [
    ('rpc_all', re.compile(r'\[Rpc\s*\(\s*RpcSources\.All'), 'RpcSources.All requires explicit RpcInfo/authority validation.'),
    ('networkrunner_instances', re.compile(r'NetworkRunner\.Instances'), 'Avoid NetworkRunner.Instances unless no project singleton/runner reference exists.'),
    ('playerref_durable', re.compile(r'(owner|durable|reconnect|playerId).*PlayerRef|PlayerRef.*(owner|durable|reconnect|playerId)', re.I), 'PlayerRef is not durable gameplay identity.'),
    ('client_trust', re.compile(r'(playerId|gold|health|hp|wallCount|augment|shop|spawn|cooldown).*(fromClient|requested|client|intParams\[|stringParams\[)', re.I), 'Check client-supplied gameplay data is authority-validated.'),
    ('rpc_persistent_state', re.compile(r'\[Rpc[^\n]*\][\s\S]{0,800}(gold|health|hp|wall|shop|augment|currentState|currentRound)', re.I), 'Persistent state touched near RPC; verify it is Networked/snapshot-backed, not RPC-only.'),
    ('tick_debug_log', re.compile(r'(FixedUpdateNetwork|Render|Update)\s*\([^)]*\)\s*\{[\s\S]{0,1200}Debug\.Log', re.I), 'Debug.Log in tick/update path may be noisy; keep if diagnostic and intentional.'),
]

SECRET_LOG_PATTERN = re.compile(r'Debug\.Log(?:Error|Warning)?[^\n]*(mpAutomationToken|AutomationToken|ConnectionToken|connectionToken|AppId|PhotonAppSettings)', re.I)


def rel(p: pathlib.Path) -> str:
    try:
        return p.relative_to(ROOT).as_posix()
    except Exception:
        return str(p)


def staged_files() -> list[pathlib.Path]:
    try:
        out = subprocess.check_output(['git', 'diff', '--cached', '--name-only', '--diff-filter=ACM'], cwd=ROOT, text=True)
        return [ROOT / line.strip() for line in out.splitlines() if line.strip()]
    except Exception:
        return []


def changed_files() -> list[pathlib.Path]:
    files: list[pathlib.Path] = []
    try:
        out = subprocess.check_output(['git', 'status', '--porcelain=v1', '-uno'], cwd=ROOT, text=True)
    except Exception:
        return files
    for line in out.splitlines():
        if not line.strip():
            continue
        # Porcelain v1 paths begin after XY and a space. Renames use "old -> new".
        path = line[3:]
        if ' -> ' in path:
            path = path.split(' -> ', 1)[1]
        if path:
            files.append(ROOT / path)
    return files


def all_files() -> list[pathlib.Path]:
    files: dict[str, pathlib.Path] = {}
    for p in ROOT.rglob('*'):
        if not p.is_file() or '.git' in p.parts or 'Library' in p.parts:
            continue
        r = rel(p)
        if any(r.startswith(v) for v in VENDOR_PREFIXES):
            continue
        if p.suffix in CS_EXT or p.suffix in ASSET_EXT or r.startswith('docs/ai-harness') or r.startswith('.codex') or r.startswith('.agents') or r in {'AGENTS.md', '.agent/rules/projectrull.md'}:
            files[r] = p
    for p in changed_files():
        r = rel(p)
        if any(r.startswith(v) for v in VENDOR_PREFIXES):
            files[r] = p
    return list(files.values())


def has_unity_editor_guard(txt: str, r: str) -> bool:
    return '/Editor/' in r or r.endswith('/Editor.cs') or re.search(r'#if\s+UNITY_EDITOR', txt) is not None


def check_automation_server(txt: str) -> list[str]:
    problems: list[str] = []
    if not re.search(r'UNITY_EDITOR', txt) or not re.search(r'DEVELOPMENT_BUILD', txt):
        problems.append('missing UNITY_EDITOR || DEVELOPMENT_BUILD compile gate')
    if not re.search(r'--mpTest|mpTest|IsEnabled|MPTestCommandLine', txt):
        problems.append('missing --mpTest runtime gate')
    if not re.search(r'127\.0\.0\.1|localhost|IPAddress\.Loopback', txt):
        problems.append('missing loopback bind')
    if not re.search(r'AutomationToken|mpAutomationToken|X-MPTest-Token|Authorization|token', txt, re.I):
        problems.append('missing per-run token/auth')
    return problems


def logs_secret(txt: str) -> bool:
    for line in txt.splitlines():
        if SECRET_LOG_PATTERN.search(line) and not re.search(r'hash|Hash|BuildTokenHash', line):
            return True
    return False


def check_file(p: pathlib.Path):
    errors = []
    warns = []
    r = rel(p)
    if any(r.startswith(v) for v in VENDOR_PREFIXES):
        errors.append((r, 'vendor_edit', 'Vendor/sample file edit blocked. Use project wrapper code unless explicitly approved.'))
    if not p.exists():
        return errors, warns
    if p.suffix == '.cs':
        txt = p.read_text(errors='ignore')
        if 'UnityEditor' in txt and not has_unity_editor_guard(txt, r):
            errors.append((r, 'unityeditor_runtime', 'UnityEditor reference outside Editor folder must be guarded by #if UNITY_EDITOR.'))
        if re.search(r'class\s+MPTestAutomationServer|HttpListener|TcpListener', txt):
            problems = check_automation_server(txt)
            if problems:
                errors.append((r, 'automation_server_unsafe', 'Test automation server safety missing: ' + ', '.join(problems) + '.'))
        if logs_secret(txt):
            errors.append((r, 'secret_logging', 'Do not log raw automation tokens, connection tokens, or Photon AppId. Log hashes only.'))
        for name, pat, msg in WARN_PATTERNS:
            if pat.search(txt):
                warns.append((r, name, msg))
        if '/Commands/' in r and r.endswith('Command.cs') and 'class ' in txt and 'ICommand' in txt:
            enum_path = ROOT / 'Mdfproject/Assets/Scripts/Enums/CommandType.cs'
            proc_path = ROOT / 'Mdfproject/Assets/Scripts/Commands/Core/CommandProcessor.cs'
            cname = p.stem
            enum_txt = enum_path.read_text(errors='ignore') if enum_path.exists() else ''
            proc_txt = proc_path.read_text(errors='ignore') if proc_path.exists() else ''
            base = cname.removesuffix('Command')
            if base and base not in enum_txt and 'Sync' not in r and 'Notify' not in r and 'Request' not in r:
                warns.append((r, 'command_enum_missing', f'Command {cname} may need CommandType entry.'))
            if cname not in proc_txt and 'ICommand' in txt:
                warns.append((r, 'command_processor_missing', f'Command {cname} may need CommandProcessor serialize/deserialize coverage.'))
    return errors, warns


def check_phase_change_set(files: list[pathlib.Path]):
    warns = []
    changed = {rel(p) for p in files}
    touched = changed & HOST_MIGRATION_CRITICAL
    if touched and touched != HOST_MIGRATION_CRITICAL:
        missing = sorted(HOST_MIGRATION_CRITICAL - touched)
        warns.append((
            '<change-set>',
            'host_migration_partial',
            'Host Migration/reconnect critical files touched without the full recovery set. Missing: ' + ', '.join(missing)
        ))
    return warns


def run(files: list[pathlib.Path]) -> int:
    errors = []
    warns = []
    for p in files:
        if not p.exists():
            continue
        e, w = check_file(p)
        errors.extend(e)
        warns.extend(w)
    warns.extend(check_phase_change_set(files))
    for r, name, msg in errors:
        print(f'BLOCK {name} {r}\n   {msg}\n   See docs/ai-harness/fusion-sync-rules.md and automation-server-contract.md')
    for r, name, msg in warns:
        print(f'WARN  {name} {r}\n   {msg}')
    print(f'MDF harness precommit: {len(errors)} errors, {len(warns)} warnings')
    return 1 if errors else 0


def self_test() -> int:
    with tempfile.TemporaryDirectory() as td:
        d = pathlib.Path(td)
        cases = []
        bad = d / 'MPTestAutomationServer.cs'
        bad.write_text('class MPTestAutomationServer { TcpListener l; }')
        cases.append(('unsafe automation server', bool(check_file(bad)[0])))
        bad2 = d / 'Leak.cs'
        bad2.write_text('class Leak { void X(){ Debug.Log(mpAutomationToken); } }')
        cases.append(('secret logging', bool(check_file(bad2)[0])))
        ok = d / 'Safe.cs'
        ok.write_text('#if UNITY_EDITOR || DEVELOPMENT_BUILD\nclass MPTestAutomationServer { string token; string host="127.0.0.1"; bool mpTest; }\n#endif')
        cases.append(('safe automation server', not bool(check_file(ok)[0])))
        bad3 = ROOT / 'Mdfproject/Assets/Photon/Fusion/BadVendorEdit.cs'
        cases.append(('vendor edit path', bool(check_file(bad3)[0])))
        bad4 = d / 'RuntimeEditorLeak.cs'
        bad4.write_text('using UnityEditor;\nclass RuntimeEditorLeak {}')
        cases.append(('runtime UnityEditor leak', bool(check_file(bad4)[0])))
        passed = True
        for name, result in cases:
            print(f'self-test {name}:', 'PASS' if result else 'FAIL')
            passed = passed and result
        return 0 if passed else 1


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--self-test', action='store_true')
    ns = ap.parse_args()
    if ns.self_test:
        sys.exit(self_test())
    files = all_files() if ns.all else staged_files()
    if not files:
        print('MDF harness precommit: no files to check')
        sys.exit(0)
    sys.exit(run(files))
