#!/usr/bin/env python3
"""MDF harness static checks.

Run:
  python tools/harness/precommit.py --all
  python tools/harness/precommit.py --self-test
"""
from __future__ import annotations
import argparse
import json
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
    ('networkrunner_instances', re.compile(r'NetworkRunner\.Instances'), 'Avoid NetworkRunner.Instances unless no project singleton/runner reference exists.'),
    ('playerref_durable', re.compile(r'(owner|durable|reconnect|playerId).*PlayerRef|PlayerRef.*(owner|durable|reconnect|playerId)', re.I), 'PlayerRef is not durable gameplay identity.'),
    ('client_trust', re.compile(r'(playerId|gold|health|hp|wallCount|augment|shop|spawn|cooldown).*(fromClient|requested|client|intParams\[|stringParams\[)', re.I), 'Check client-supplied gameplay data is authority-validated.'),
]

SECRET_LOG_PATTERN = re.compile(r'Debug\.Log(?:Error|Warning)?[^\n]*(mpAutomationToken|AutomationToken|ConnectionToken|connectionToken|AppId|PhotonAppSettings)', re.I)


def normalize_glob(value: object) -> str:
    return str(value or '').replace('\\', '/').strip().strip('/')


def profile_includes_codex_session_state(profile: dict) -> bool:
    includes = [normalize_glob(item) for item in profile.get('include') or []]
    excludes = [normalize_glob(item) for item in profile.get('exclude') or []]
    includes_codex = any(item in {'.codex', '.codex/**', '**/.codex/**'} for item in includes)
    excludes_session_state = any(
        item in {'.codex/session-state', '.codex/session-state/**', '**/.codex/session-state/**'}
        or item.startswith('.codex/session-state/')
        or item.startswith('**/.codex/session-state/')
        for item in excludes
    )
    return includes_codex and not excludes_session_state


def check_context_packer_defaults() -> list[tuple[str, str, str]]:
    errors: list[tuple[str, str, str]] = []
    config_path = ROOT / '_context_packer/mdf_context_pack.config.json'
    if config_path.exists():
        try:
            config = json.loads(config_path.read_text(encoding='utf-8'))
        except Exception as exc:
            errors.append((rel(config_path), 'context_packer_config_invalid', f'Could not parse context packer config: {type(exc).__name__}: {exc}'))
            config = {}
        profiles = config.get('profiles') if isinstance(config, dict) else None
        if isinstance(profiles, dict):
            for name, profile in profiles.items():
                if isinstance(profile, dict) and profile_includes_codex_session_state(profile):
                    errors.append((
                        rel(config_path),
                        'context_packer_session_state',
                        f'Profile {name} includes .codex/** but does not exclude .codex/session-state/**.'
                    ))

    gitignore_path = ROOT / '.gitignore'
    if gitignore_path.exists():
        gitignore = gitignore_path.read_text(encoding='utf-8', errors='ignore').replace('\\', '/')
        required_ignores = {
            '_context_packer/output/': 'context_packer_output_not_ignored',
            '_context_bundles/': 'context_bundles_not_ignored',
        }
        for required, code in required_ignores.items():
            if required not in gitignore:
                errors.append((rel(gitignore_path), code, f'Generated context output must be ignored: {required}'))
    return errors


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


def strip_comments(txt: str) -> str:
    txt = re.sub(r'/\*[\s\S]*?\*/', '', txt)
    return re.sub(r'//.*', '', txt)


def strip_inactive_false_blocks(txt: str) -> str:
    return re.sub(r'#if\s+false[\s\S]*?#endif', '', txt)


def find_matching_brace(txt: str, open_index: int) -> int:
    depth = 0
    for i in range(open_index, len(txt)):
        ch = txt[i]
        if ch == '{':
            depth += 1
        elif ch == '}':
            depth -= 1
            if depth == 0:
                return i
    return -1


def iter_rpc_blocks(txt: str):
    for match in re.finditer(r'\[Rpc\s*\(([^\]]*)\)\]', txt):
        open_index = txt.find('{', match.end())
        if open_index < 0:
            continue
        close_index = find_matching_brace(txt, open_index)
        if close_index < 0:
            continue
        attr = match.group(1)
        signature = txt[match.end():open_index]
        body = txt[open_index + 1:close_index]
        yield attr, signature, body


def has_manual_rpc_source_validation(signature: str, body: str) -> bool:
    if 'RpcInfo' not in signature:
        return False
    return (
        'Object.InputAuthority' in body
        or 'IsRpcSourceAuthorizedForPlayer' in body
        or 'ValidateClientCommandRequest' in body
        or 'RejectDeprecated' in body
    )


PERSISTENT_RPC_PATTERN = re.compile(r'\b(gold|health|hp|wall|shop|augment|currentState|currentRound)\b', re.I)

BATTLE_COMMAND_REQUIRED_TOKENS = {
    'BattleSpawnMonsterCommand.cs': [
        'ServerBattleCommandExecutor.TryExecuteAsync',
        'BattleCommandValidator.ResolveActorPlayer',
        'BattleCommandValidator.ResolveOpponent',
        'BattleCommandValidator.IsCurrentBattleAttacker',
        'BattleCommandValidator.IsCurrentBattleDefender',
        'BattleCommandValidator.IsAuthorizedClientSource',
        'BattleCommandValidator.IsServerAiOrTestAuthority',
        'BattleCommandValidator.IsFiniteTargetPosition',
        'BattleCommandValidator.IsInsideBattleSpawnZone',
        'TryConsumeMonsterPoolSlot',
        'SpawnMonsterAtPositionAsync',
    ],
    'UseMagicScrollCommand.cs': [
        'ServerBattleCommandExecutor.TryExecuteAsync',
        'BattleCommandValidator.ResolveActorPlayer',
        'BattleCommandValidator.ResolveOpponent',
        'BattleCommandValidator.IsCurrentBattleAttacker',
        'BattleCommandValidator.IsCurrentBattleDefender',
        'BattleCommandValidator.IsAuthorizedClientSource',
        'BattleCommandValidator.IsServerAiOrTestAuthority',
        'BattleCommandValidator.IsFiniteTargetPosition',
        'BattleCommandValidator.IsInsideScrollTargetDomain',
        'TryConsumeMagicScrollSlot',
        'CastGameplay',
    ],
}

BATTLE_COMMAND_ALLOWED_DIRECT_SPAWN = {
    'Mdfproject/Assets/Scripts/Game/Monsters/MonsterSpawner.cs',
    'Mdfproject/Assets/Scripts/Commands/Battle/BattleSpawnMonsterCommand.cs',
}


def custom_warns(txt: str, r: str) -> list[tuple[str, str, str]]:
    warns = []
    for attr, signature, body in iter_rpc_blocks(txt):
        rpc_text = f'{signature}\n{body}'
        manual_source_validation = has_manual_rpc_source_validation(signature, body)
        if 'RpcSources.All' in attr and not manual_source_validation:
            warns.append((r, 'rpc_all', 'RpcSources.All requires explicit RpcInfo/authority validation.'))
        if not PERSISTENT_RPC_PATTERN.search(rpc_text):
            continue
        if 'RpcSources.StateAuthority' in attr:
            continue
        if manual_source_validation:
            continue
        warns.append((r, 'rpc_persistent_state', 'Persistent state touched near RPC; verify it is Networked/snapshot-backed, not RPC-only.'))

    for match in re.finditer(r'(?:public|private|protected)?\s*(?:override\s+)?void\s+(FixedUpdateNetwork|Render|Update)\s*\([^)]*\)\s*\{', txt):
        open_index = txt.find('{', match.end() - 1)
        close_index = find_matching_brace(txt, open_index)
        if open_index < 0 or close_index < 0:
            continue
        body = txt[open_index + 1:close_index]
        if re.search(r'Debug\.Log', body):
            warns.append((r, 'tick_debug_log', 'Debug.Log in tick/update path may be noisy; keep if diagnostic and intentional.'))
    return warns


def method_body_contains(txt: str, method_name: str, pattern: str) -> bool:
    for match in re.finditer(r'\b' + re.escape(method_name) + r'\s*\([^)]*\)\s*\{', txt):
        open_index = txt.find('{', match.end() - 1)
        close_index = find_matching_brace(txt, open_index)
        if open_index < 0 or close_index < 0:
            continue
        body = txt[open_index + 1:close_index]
        if re.search(pattern, body):
            return True
    return False


def custom_errors(txt: str, r: str) -> list[tuple[str, str, str]]:
    errors = []
    if '/Editor/' in r:
        return errors

    if pathlib.PurePosixPath(r).name == 'Unit.cs' and method_body_contains(txt, 'OnDisable', r'\bUnitDied\s*\('):
        errors.append((r, 'unit_disable_field_registry_removal', 'Unit.OnDisable must not remove field registry entries; battle death disables units that must respawn later.'))

    if re.search(r'ComponentRegistry\.Register\s*<\s*AIPlayerController\s*>|AddComponent\s*<\s*AIPlayerController\s*>', txt):
        if 'HumanBot' in txt or r.startswith('Mdfproject/Assets/Scripts/Testing/MP/'):
            errors.append((r, 'humanbot_ai_registration', 'HumanBot/test human peers must not register or attach AIPlayerController.'))

    if r.startswith('Mdfproject/Assets/Scripts/AI/') and re.search(r'\bSpawnMonsterAtPositionAsync\s*\(', txt):
        errors.append((r, 'ai_direct_monster_spawn', 'AI strategic monster spawns must emit BattleSpawnMonsterCommand, not call SpawnMonsterAtPositionAsync directly.'))

    if r.startswith('Mdfproject/Assets/Scripts/AI/') and re.search(r'\.ActivateSkill\s*\(|\bApplyEffect\s*\(', txt):
        errors.append((r, 'ai_direct_skill_effect', 'AI strategic skill decisions must emit ActivateSkillCommand, not call ActivateSkill or ApplyEffect directly.'))

    if r.startswith('Mdfproject/Assets/Scripts/Testing/MP/') and re.search(r'\.ActivateSkill\s*\(|\bApplyEffect\s*\(', txt):
        errors.append((r, 'humanbot_direct_skill_effect', 'HumanBot must use the real command path for strategic skills, not direct ActivateSkill or ApplyEffect calls.'))

    for _, signature, body in iter_rpc_blocks(txt):
        if 'RPC_BroadcastMagicScrollUsed' in signature and re.search(r'CastGameplay|CastSkill|ApplyEffect|TryConsumeMagicScrollSlot', body):
            errors.append((r, 'scroll_broadcast_gameplay_effect', 'Magic scroll broadcast RPC must be presentation-only and must not apply durable gameplay effects.'))

    if (
        method_body_contains(txt, 'CreateScrollPresentationLocal', r'CastGameplay|CastSkill|ApplyEffect|TryConsumeMagicScrollSlot')
        or method_body_contains(txt, 'PlayPresentation', r'CastGameplay|CastSkill|ApplyEffect|TryConsumeMagicScrollSlot')
    ):
        errors.append((r, 'scroll_presentation_gameplay_effect', 'Scroll presentation helpers must not apply gameplay effects or consume scroll inventory.'))

    if r.endswith('/Commands/Battle/BattleSpawnMonsterCommand.cs') or r.endswith('/Commands/Battle/UseMagicScrollCommand.cs'):
        required = BATTLE_COMMAND_REQUIRED_TOKENS.get(pathlib.PurePosixPath(r).name, [])
        missing = [token for token in required if token not in txt]
        if missing:
            errors.append((r, 'battle_command_validation_missing', 'Battle command is missing required authority validation/execution tokens: ' + ', '.join(missing)))

    if r.endswith('/Commands/PlayerActions/ActivateSkillCommand.cs'):
        required = [
            'BattleCommandValidator.ResolveActorPlayer',
            'BattleCommandValidator.IsBattlePhase',
            'IsManualOrAiStrategicSkill',
            'UnitBelongsToPlayer',
            'unit.Object.HasStateAuthority',
            'unit.ActivateSkill()',
        ]
        missing = [token for token in required if token not in txt]
        if missing:
            errors.append((r, 'activate_skill_validation_missing', 'ActivateSkillCommand is missing required strategic/manual skill validation tokens: ' + ', '.join(missing)))

    return errors


def battle_guardrail_warns(txt: str, r: str) -> list[tuple[str, str, str]]:
    warns = []
    if '/Editor/' in r:
        return warns
    if 'SpawnMonsterAtPositionAsync' in txt and r not in BATTLE_COMMAND_ALLOWED_DIRECT_SPAWN:
        warns.append((r, 'direct_monster_spawn_review', 'Direct SpawnMonsterAtPositionAsync use must be non-strategic or wrapped by BattleSpawnMonsterCommand.'))
    if re.search(r'\.\s*RPC_RequestSpawnMonster\s*\(', txt):
        warns.append((r, 'legacy_spawn_rpc_call', 'Legacy RPC_RequestSpawnMonster call detected; use BattleSpawnMonsterCommand/RPC_RequestBattleSpawnMonster.'))
    if re.search(r'\.\s*RPC_RequestUseMagicScroll\s*\(', txt):
        warns.append((r, 'legacy_scroll_rpc_call', 'Legacy RPC_RequestUseMagicScroll call detected; use UseMagicScrollCommand/RPC_RequestUseMagicScrollCommand.'))
    if re.search(r'\.\s*CastGameplay\s*\(', txt) and not r.endswith('/Commands/Battle/UseMagicScrollCommand.cs'):
        warns.append((r, 'scroll_gameplay_direct_call', 'Scroll gameplay casting should route through UseMagicScrollCommand on State Authority.'))
    if re.search(r'\.\s*TryConsumeMagicScrollSlot\s*\(', txt) and not r.endswith('/Commands/Battle/UseMagicScrollCommand.cs'):
        warns.append((r, 'scroll_inventory_direct_consume', 'Scroll inventory consumption should occur after UseMagicScrollCommand validation.'))
    if re.search(r'\.\s*TryConsumeMonsterPoolSlot\s*\(', txt) and not r.endswith('/Commands/Battle/BattleSpawnMonsterCommand.cs'):
        warns.append((r, 'attack_pool_direct_consume', 'Attack monster pool consumption should occur after BattleSpawnMonsterCommand validation.'))
    return warns


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
        warn_txt = strip_inactive_false_blocks(strip_comments(txt))
        if 'UnityEditor' in txt and not has_unity_editor_guard(txt, r):
            errors.append((r, 'unityeditor_runtime', 'UnityEditor reference outside Editor folder must be guarded by #if UNITY_EDITOR.'))
        if re.search(r'class\s+MPTestAutomationServer|HttpListener|TcpListener', txt):
            problems = check_automation_server(txt)
            if problems:
                errors.append((r, 'automation_server_unsafe', 'Test automation server safety missing: ' + ', '.join(problems) + '.'))
        if logs_secret(txt):
            errors.append((r, 'secret_logging', 'Do not log raw automation tokens, connection tokens, or Photon AppId. Log hashes only.'))
        errors.extend(custom_errors(warn_txt, r))
        for name, pat, msg in WARN_PATTERNS:
            if pat.search(warn_txt):
                warns.append((r, name, msg))
        warns.extend(custom_warns(warn_txt, r))
        warns.extend(battle_guardrail_warns(warn_txt, r))
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
    errors.extend(check_context_packer_defaults())
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
        bad5 = d / 'UnvalidatedRpcAll.cs'
        bad5.write_text('class X { [Rpc(RpcSources.All, RpcTargets.StateAuthority)] void R(){ gold=1; } }')
        cases.append(('unvalidated rpc all warning', any(w[1] == 'rpc_all' for w in check_file(bad5)[1])))
        ok2 = d / 'ValidatedRpcAll.cs'
        ok2.write_text('class X { [Rpc(RpcSources.All, RpcTargets.StateAuthority)] void R(RpcInfo info = default){ if(!IsRpcSourceAuthorizedForPlayer(p, info.Source)) return; gold=1; } }')
        cases.append(('validated rpc all no warning', not any(w[1] == 'rpc_all' for w in check_file(ok2)[1])))
        bad6 = d / 'HumanBotBad.cs'
        bad6.write_text('class MPTestHumanBotDriver { void X(){ gameObject.AddComponent<AIPlayerController>(); } }')
        cases.append(('humanbot ai registration block', any(e[1] == 'humanbot_ai_registration' for e in check_file(bad6)[0])))
        bad7 = d / 'ScrollPresentationBad.cs'
        bad7.write_text('class X { [Rpc(RpcSources.StateAuthority, RpcTargets.All)] void RPC_BroadcastMagicScrollUsed(){ caster.CastGameplay(skill, out n); } }')
        cases.append(('scroll presentation gameplay block', any(e[1] == 'scroll_broadcast_gameplay_effect' for e in check_file(bad7)[0])))
        bad8 = ROOT / 'Mdfproject/Assets/Scripts/AI/Planning/__precommit_self_test_BadAiSpawn.cs'
        bad8.write_text('class BadAiSpawn { void X(){ spawner.SpawnMonsterAtPositionAsync(data, pos, field); } }')
        try:
            cases.append(('ai direct spawn block', any(e[1] == 'ai_direct_monster_spawn' for e in check_file(bad8)[0])))
        finally:
            try:
                bad8.unlink()
            except Exception:
                pass
        cases.append(('context packer session-state block', profile_includes_codex_session_state({
            'include': ['.codex/**'],
            'exclude': ['_context_packer/output/**'],
        })))
        cases.append(('context packer session-state exclude ok', not profile_includes_codex_session_state({
            'include': ['.codex/**'],
            'exclude': ['.codex/session-state/**'],
        })))
        cases.append(('unit ondisable registry removal helper', method_body_contains(
            'class Unit { void OnDisable(){ owner.fieldManager.UnitDied(this); } }',
            'OnDisable',
            r'\bUnitDied\s*\('
        )))
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
