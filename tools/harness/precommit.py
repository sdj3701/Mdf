#!/usr/bin/env python3
"""MDF harness static checks.

Run:
  python tools/harness/precommit.py --all
  python tools/harness/precommit.py --self-test
"""
from __future__ import annotations
import argparse
import json
import os
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
SELF_TEST_SUBPROCESS_TIMEOUT_SECONDS = 30


def self_test_subprocess(args, **kwargs):
    kwargs.setdefault('timeout', SELF_TEST_SUBPROCESS_TIMEOUT_SECONDS)
    return subprocess.run(args, **kwargs)


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
    required_context_excludes = {
        '**/__pycache__/**': 'context_packer_pycache_not_excluded',
        '**/*.pyc': 'context_packer_pyc_not_excluded',
        'AGENTS.md.meta': 'context_packer_agents_meta_not_excluded',
        'docs/ai-harness/archive/**': 'context_packer_historical_docs_not_excluded',
        '.codex/session-state/**': 'context_packer_session_state_not_excluded',
        '_context_packer/output/**': 'context_packer_output_not_excluded',
        '_context_bundles/**': 'context_bundles_not_excluded',
    }
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
                excludes = {normalize_glob(item) for item in (profile.get('exclude') or [])} if isinstance(profile, dict) else set()
                for required, code in required_context_excludes.items():
                    if required not in excludes:
                        errors.append((rel(config_path), code, f'Profile {name} must exclude generated/noise path: {required}'))

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


def read_repo_text(rel_path: str) -> str:
    path = ROOT / rel_path
    if not path.exists():
        return ''
    return path.read_text(encoding='utf-8', errors='ignore')


def python_syntax_error(path: pathlib.Path) -> str | None:
    try:
        source = path.read_text(encoding='utf-8', errors='ignore')
        compile(source, str(path), 'exec')
        return None
    except Exception as exc:
        return f'{type(exc).__name__}: {exc}'


def frontmatter_value(text: str, key: str) -> str:
    if not text.startswith('---'):
        return ''
    end = text.find('\n---', 3)
    if end < 0:
        return ''
    frontmatter = text[3:end]
    for line in frontmatter.splitlines():
        if line.strip().startswith(key + ':'):
            return line.split(':', 1)[1].strip().strip('"').strip("'")
    return ''


def check_content_workflow_guardrails() -> tuple[list[tuple[str, str, str]], list[tuple[str, str, str]]]:
    errors: list[tuple[str, str, str]] = []
    warns: list[tuple[str, str, str]] = []

    agents = read_repo_text('AGENTS.md')
    skill_path = ROOT / '.agents/skills/mdf-content-feature/SKILL.md'
    routine_path = ROOT / 'docs/ai-harness/content-development-routine.md'
    selector_doc_path = ROOT / 'docs/ai-harness/verification-profile-selector.md'
    selector_script_path = ROOT / 'tools/harness/mp/select_verification_profile.py'
    run_matrix_path = ROOT / 'tools/harness/mp/run_matrix.py'
    session_start_path = ROOT / '.codex/hooks/session_start_context.py'
    required_korean_skill_triggers = [
        '새 유닛',
        '새 스크롤',
        'AI 개선',
        '증강 추가',
        '몬스터 추가',
        '전투 개선',
        '3라운드 진행',
        '게임 끝까지 테스트',
    ]
    required_routine_examples = [
        '새 유닛 추가해줘',
        '화염 스크롤 만들어줘',
        'AI가 보스를 아껴 쓰게 해줘',
        '새 증강 추가해줘',
        '상점 밸런스 바꿔줘',
        '몬스터 하나 추가해줘',
        '3라운드까지 버티는 AI 만들어줘',
    ]

    if 'mdf-content-feature' in agents and not skill_path.exists():
        errors.append(('AGENTS.md', 'content_workflow_skill_missing', 'AGENTS.md references mdf-content-feature but the skill file is missing.'))
    if 'content-development-routine.md' in agents and not routine_path.exists():
        errors.append(('AGENTS.md', 'content_workflow_doc_missing', 'AGENTS.md references content-development-routine.md but the doc is missing.'))

    if skill_path.exists():
        skill_text = skill_path.read_text(encoding='utf-8', errors='ignore')
        name = frontmatter_value(skill_text, 'name')
        description = frontmatter_value(skill_text, 'description')
        if name != 'mdf-content-feature' or not description or 'TODO' in description:
            errors.append((rel(skill_path), 'content_workflow_skill_metadata', 'mdf-content-feature skill must have required name and non-TODO description.'))
        missing_triggers = [trigger for trigger in required_korean_skill_triggers if trigger not in description]
        if missing_triggers:
            errors.append((rel(skill_path), 'content_workflow_skill_korean_triggers', 'mdf-content-feature description is missing Korean trigger phrases: ' + ', '.join(missing_triggers)))
        if 'verification-profile-selector.md' in skill_text and not selector_doc_path.exists():
            errors.append((rel(skill_path), 'verification_selector_doc_missing', 'Skill references verification-profile-selector.md but the doc is missing.'))

    for hook_rel in ['.codex/hooks/hook_common.py', '.codex/hooks/user_prompt_submit_reminder.py', '.codex/hooks/stop_verify_gate.py']:
        hook_path = ROOT / hook_rel
        if hook_path.exists():
            syntax_error = python_syntax_error(hook_path)
            if syntax_error:
                errors.append((hook_rel, 'hook_python_syntax', f'Hook has invalid Python syntax: {syntax_error}'))

    if selector_script_path.exists():
        syntax_error = python_syntax_error(selector_script_path)
        if syntax_error:
            errors.append((rel(selector_script_path), 'verification_selector_python_syntax', f'select_verification_profile.py has invalid Python syntax: {syntax_error}'))

    if run_matrix_path.exists():
        run_matrix_text = run_matrix_path.read_text(encoding='utf-8', errors='ignore')
        dry_run_index = run_matrix_text.find('if args.dry_run:')
        child_run_index = run_matrix_text.find('subprocess.run(command')
        if dry_run_index < 0 or child_run_index < 0 or dry_run_index > child_run_index:
            errors.append((rel(run_matrix_path), 'run_matrix_dry_run_child_exec', 'run_matrix dry-run must return planned cases before executing child scripts.'))

    if session_start_path.exists():
        session_start_text = session_start_path.read_text(encoding='utf-8', errors='ignore').lower()
        if 'codex-full-phase-prompts.md' in session_start_text and 'only for explicit harness-bootstrap' not in session_start_text:
            errors.append((rel(session_start_path), 'session_start_full_phase_default', 'SessionStart must not recommend codex-full-phase-prompts.md as the default workflow.'))

    if ('verification-profile-selector.md' in agents or (skill_path.exists() and 'verification-profile-selector.md' in read_repo_text(rel(skill_path)))) and not selector_doc_path.exists():
        errors.append(('docs/ai-harness/verification-profile-selector.md', 'verification_selector_doc_missing', 'verification-profile-selector.md is referenced but missing.'))

    feature_loop = read_repo_text('docs/ai-harness/feature-implementation-loop.md')
    feature_loop_lower = feature_loop.lower()
    if 'nightly' in feature_loop_lower and not all(token in feature_loop_lower for token in ['full-regression', 'long', 'endurance']):
        warns.append(('docs/ai-harness/feature-implementation-loop.md', 'nightly_only_heavy_profile', 'Feature loop mentions nightly without current full-regression/long/endurance guidance.'))

    routine = read_repo_text('docs/ai-harness/content-development-routine.md')
    routine_lower = routine.lower()
    docs_text = (routine + '\n' + feature_loop).lower()
    missing_examples = [example for example in required_routine_examples if example not in routine]
    if missing_examples:
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_korean_examples_missing', 'Content routine is missing Korean short-request examples: ' + ', '.join(missing_examples)))
    manual_template_lines = []
    for line in docs_text.splitlines():
        if 'paste' not in line or 'template' not in line:
            continue
        if any(ok in line for ok in ['do not ask', 'does not need', 'reference only', 'internal expansion']):
            continue
        if re.search(r'(ask|tell|require|must).{0,80}user.{0,80}paste|user.{0,80}must.{0,80}paste', line):
            manual_template_lines.append(line)
    if manual_template_lines:
        warns.append(('<content-docs>', 'manual_template_normal_path', 'Docs appear to ask the user to paste the long template as the normal path.'))
    if 'cleanupstatus=pass' not in routine_lower or 'orphanedpids=[]' not in routine_lower:
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_cleanup_contract', 'Content routine should require cleanupStatus=PASS and orphanedPids=[] for E2E PASS.'))
    if '--headless-player' not in routine_lower:
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_headless_default', 'Content routine should document --headless-player guidance.'))
    if 'long' not in routine_lower or 'endurance' not in routine_lower:
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_long_endurance_missing', 'Content routine should document long and endurance profiles.'))
    if 'full-regression' not in routine_lower:
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_full_regression_missing', 'Content routine should document full-regression guidance.'))
    if 'endurance' in routine_lower and not all(token in routine_lower for token in ['gameover', 'timeout', 'stalled']):
        warns.append(('docs/ai-harness/content-development-routine.md', 'content_docs_endurance_classification', 'Content routine should distinguish endurance GameOver/TIMEOUT/STALLED outcomes.'))

    return errors, warns


def check_recipe_lifecycle_guardrails() -> tuple[list[tuple[str, str, str]], list[tuple[str, str, str]]]:
    errors: list[tuple[str, str, str]] = []
    warns: list[tuple[str, str, str]] = []

    learned_path = ROOT / 'docs/ai-harness/learned-recipes.md'
    policy_path = ROOT / 'docs/ai-harness/recipe-lifecycle.md'
    touch_path = ROOT / 'tools/harness/recipes/touch_recipe.py'
    scan_path = ROOT / 'tools/harness/recipes/scan_stale_recipes.py'

    if 'recipe-lifecycle.md' in read_repo_text('docs/ai-harness/learned-recipes.md') and not policy_path.exists():
        errors.append((rel(policy_path), 'recipe_lifecycle_policy_missing', 'learned-recipes.md references recipe-lifecycle.md but the policy file is missing.'))

    if learned_path.exists():
        learned_text = learned_path.read_text(encoding='utf-8', errors='ignore')
        learned_lines = learned_text.splitlines()
        recipe_heading_count = len(re.findall(r'^##\s+[A-Za-z0-9][A-Za-z0-9_.-]*:', learned_text, re.M))
        if len(learned_lines) > 250 or recipe_heading_count > 10:
            warns.append((
                rel(learned_path),
                'learned_recipes_index_too_large',
                'Keep learned-recipes.md as a compact index; move detailed recipe bodies to docs/ai-harness/recipes/*.md.'
            ))

    for script_path in [touch_path, scan_path]:
        if not script_path.exists():
            errors.append((rel(script_path), 'recipe_lifecycle_script_missing', 'Recipe lifecycle script is missing.'))
            continue
        syntax_error = python_syntax_error(script_path)
        if syntax_error:
            errors.append((rel(script_path), 'recipe_lifecycle_python_syntax', f'Recipe lifecycle script has invalid Python syntax: {syntax_error}'))

    if scan_path.exists() and not python_syntax_error(scan_path):
        try:
            proc = subprocess.run(
                [sys.executable, str(scan_path), '--json'],
                cwd=ROOT,
                text=True,
                capture_output=True,
                encoding='utf-8',
                errors='replace',
                timeout=60,
            )
        except Exception as exc:
            errors.append((rel(scan_path), 'recipe_lifecycle_scan_crash', f'scan_stale_recipes.py could not run: {type(exc).__name__}: {exc}'))
        else:
            if proc.returncode != 0:
                errors.append((rel(scan_path), 'recipe_lifecycle_scan_crash', 'scan_stale_recipes.py exited non-zero:\n' + proc.stderr[:1000]))
            else:
                try:
                    result = json.loads(proc.stdout)
                except Exception as exc:
                    errors.append((rel(scan_path), 'recipe_lifecycle_scan_json', f'scan_stale_recipes.py --json emitted invalid JSON: {type(exc).__name__}: {exc}'))
                else:
                    for warning in result.get('warnings') or []:
                        warns.append((rel(learned_path), 'recipe_lifecycle_metadata', str(warning)))
                    archive_ids = {str(item.get('id', '')) for item in (result.get('archiveCandidates') or []) if isinstance(item, dict)}
                    pinned_archive_ids = [
                        recipe_id for recipe_id in archive_ids
                        if recipe_id and re.search(
                            rf'^##\s+{re.escape(recipe_id)}\b[\s\S]*?\nPinned:\s*true\b',
                            read_repo_text('docs/ai-harness/learned-recipes.md'),
                            re.I,
                        )
                    ]
                    if pinned_archive_ids:
                        warns.append((rel(learned_path), 'recipe_lifecycle_pinned_archive_candidate', 'Pinned recipes must not be archive candidates: ' + ', '.join(pinned_archive_ids)))

    return errors, warns


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
        'BattleCommandValidator.TryResolveExactBattleSpawnPosition',
        'TryReserveBattleSpawnResource',
        'CommitBattleSpawnReservation',
        'TryRefundBattleSpawnReservation',
        'SpawnMonsterAtExactPositionAsync',
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

    if r.startswith('Mdfproject/Assets/Scripts/AI/') and re.search(r'\bSpawnMonsterAt(?:Exact)?PositionAsync\s*\(', txt):
        errors.append((r, 'ai_direct_monster_spawn', 'AI strategic monster spawns must emit BattleSpawnMonsterCommand, not call low-level spawn APIs directly.'))

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
    if re.search(r'\bSpawnMonsterAt(?:Exact)?PositionAsync\s*\(', txt) and r not in BATTLE_COMMAND_ALLOWED_DIRECT_SPAWN:
        warns.append((r, 'direct_monster_spawn_review', 'Direct low-level monster spawn use must be non-strategic or wrapped by BattleSpawnMonsterCommand.'))
    if re.search(r'\.\s*RPC_RequestSpawnMonster\s*\(', txt):
        warns.append((r, 'legacy_spawn_rpc_call', 'Legacy RPC_RequestSpawnMonster call detected; use BattleSpawnMonsterCommand/RPC_RequestBattleSpawnMonster.'))
    if re.search(r'\.\s*RPC_RequestUseMagicScroll\s*\(', txt):
        warns.append((r, 'legacy_scroll_rpc_call', 'Legacy RPC_RequestUseMagicScroll call detected; use UseMagicScrollCommand/RPC_RequestUseMagicScrollCommand.'))
    if re.search(r'\.\s*CastGameplay\s*\(', txt) and not r.endswith('/Commands/Battle/UseMagicScrollCommand.cs'):
        warns.append((r, 'scroll_gameplay_direct_call', 'Scroll gameplay casting should route through UseMagicScrollCommand on State Authority.'))
    if re.search(r'\.\s*TryConsumeMagicScrollSlot\s*\(', txt) and not r.endswith('/Commands/Battle/UseMagicScrollCommand.cs'):
        warns.append((r, 'scroll_inventory_direct_consume', 'Scroll inventory consumption should occur after UseMagicScrollCommand validation.'))
    if re.search(r'\.\s*(?:TryReserveBattleSpawnResource|CommitBattleSpawnReservation|TryRefundBattleSpawnReservation)\s*\(', txt) and not r.endswith('/Commands/Battle/BattleSpawnMonsterCommand.cs'):
        warns.append((r, 'attack_resource_direct_transaction', 'Attack pool/Black Magic transactions should occur only inside BattleSpawnMonsterCommand.'))
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
    content_errors, content_warns = check_content_workflow_guardrails()
    errors.extend(content_errors)
    warns.extend(content_warns)
    recipe_errors, recipe_warns = check_recipe_lifecycle_guardrails()
    errors.extend(recipe_errors)
    warns.extend(recipe_warns)
    warns.extend(check_phase_change_set(files))
    for r, name, msg in errors:
        print(f'BLOCK {name} {r}\n   {msg}\n   See docs/ai-harness/fusion-sync-rules.md and automation-server-contract.md')
    for r, name, msg in warns:
        print(f'WARN  {name} {r}\n   {msg}')
    print(f'MDF harness precommit: {len(errors)} errors, {len(warns)} warnings')
    return 1 if errors else 0


def recipe_lifecycle_self_test_cases(temp_root: pathlib.Path) -> list[tuple[str, bool]]:
    cases: list[tuple[str, bool]] = []
    learned_dir = temp_root / 'docs/ai-harness'
    learned_dir.mkdir(parents=True, exist_ok=True)
    learned = learned_dir / 'learned-recipes.md'
    recipe_dir = learned_dir / 'recipes'
    recipe_dir.mkdir(parents=True, exist_ok=True)
    split_recipe = recipe_dir / 'split-recipe.md'
    learned.write_text(
        '''# Learned Recipes

## active-old: Active old recipe

Status: active
Pinned: false
Category: harness
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 2
Review after: 2026-02-01
Triggers: active
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected

Recipe:
- Existing active recipe.

## stale-old: Stale old recipe

Status: active
Pinned: false
Category: harness
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 1
Review after: 2026-02-01
Triggers: stale
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected

## pinned-old: Pinned old recipe

Status: active
Pinned: true
Category: cleanup
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 1
Review after: 2026-02-01
Triggers: cleanup
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: never auto-archive pinned cleanup recipes

## deprecated-missing: Deprecated missing replacement

Status: deprecated
Pinned: false
Category: harness
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 1
Review after: 2026-02-01
Triggers: deprecated
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: archive after replacement review

## invalid-status: Invalid status fixture

Status: verified-local
Pinned: false
Category: harness
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 1
Review after: 2026-02-01
Triggers: invalid
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected

## missing-metadata: Missing metadata fixture

Status: active
Pinned: false
Category: harness
''',
        encoding='utf-8',
    )
    split_recipe.write_text(
        '''# Split Recipe

## split-recipe: Split recipe file

Status: active
Pinned: false
Category: harness
Created: 2026-01-01
Last used: 2026-01-01
Last verified: 2026-01-01
Use count: 1
Review after: 2026-02-01
Triggers: split
Applies to: tests
Verified by: old artifact
Replacement: none
Archive policy: archive when unused for 180 days and not pinned/protected
''',
        encoding='utf-8',
    )

    touch = ROOT / 'tools/harness/recipes/touch_recipe.py'
    scan = ROOT / 'tools/harness/recipes/scan_stale_recipes.py'
    used = self_test_subprocess(
        [sys.executable, str(touch), '--root', str(temp_root), '--id', 'active-old', '--used', '--today', '2026-05-08'],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    text_after_used = learned.read_text(encoding='utf-8')
    cases.append(('touch recipe used updates Last used and count', used.returncode == 0 and 'Last used: 2026-05-08' in text_after_used and 'Use count: 3' in text_after_used))

    verified = self_test_subprocess(
        [
            sys.executable,
            str(touch),
            '--root',
            str(temp_root),
            '--id',
            'active-old',
            '--verified',
            '--artifact',
            'artifacts/recipe-proof.json',
            '--today',
            '2026-05-08',
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    text_after_verified = learned.read_text(encoding='utf-8')
    cases.append(('touch recipe verified updates Last verified', verified.returncode == 0 and 'Last verified: 2026-05-08' in text_after_verified and 'artifacts/recipe-proof.json' in text_after_verified))
    cases.append(('touch recipe keeps blank line before next heading', '\n\n## stale-old:' in text_after_verified))

    note_again = self_test_subprocess(
        [
            sys.executable,
            str(touch),
            '--root',
            str(temp_root),
            '--id',
            'active-old',
            '--used',
            '--note',
            'second lifecycle note',
            '--today',
            '2026-05-08',
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    text_after_second_note = learned.read_text(encoding='utf-8')
    active_old_section = text_after_second_note.split('## stale-old:', 1)[0]
    cases.append((
        'touch recipe appends to existing lifecycle notes',
        note_again.returncode == 0
        and active_old_section.count('Lifecycle notes:') == 1
        and 'second lifecycle note' in active_old_section,
    ))
    cases.append((
        'touch recipe lifecycle notes stay contiguous',
        '\n\n- 2026-05-08: second lifecycle note' not in active_old_section,
    ))

    verified_without_evidence = self_test_subprocess(
        [sys.executable, str(touch), '--root', str(temp_root), '--id', 'active-old', '--verified', '--today', '2026-05-08'],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    cases.append(('touch recipe verified requires evidence', verified_without_evidence.returncode != 0))

    split_used = self_test_subprocess(
        [sys.executable, str(touch), '--root', str(temp_root), '--id', 'split-recipe', '--used', '--today', '2026-05-08'],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    split_text_after_used = split_recipe.read_text(encoding='utf-8')
    learned_text_after_split = learned.read_text(encoding='utf-8')
    cases.append((
        'touch recipe updates split recipe file without duplicating index',
        split_used.returncode == 0
        and 'Last used: 2026-05-08' in split_text_after_used
        and 'Use count: 2' in split_text_after_used
        and '## split-recipe:' not in learned_text_after_split,
    ))

    new_recipe = temp_root / 'docs/ai-harness/recipes/new-recipe.md'
    created_new = self_test_subprocess(
        [
            sys.executable,
            str(touch),
            '--root',
            str(temp_root),
            '--id',
            'new-recipe',
            '--used',
            '--note',
            'created from touch_recipe',
            '--today',
            '2026-05-08',
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    cases.append((
        'touch recipe creates new recipe file instead of growing index',
        created_new.returncode == 0
        and new_recipe.exists()
        and '## new-recipe:' in new_recipe.read_text(encoding='utf-8')
        and '## new-recipe:' not in learned.read_text(encoding='utf-8'),
    ))

    scanned = self_test_subprocess(
        [
            sys.executable,
            str(scan),
            '--root',
            str(temp_root),
            '--today',
            '2026-05-08',
            '--stale-days',
            '1',
            '--archive-days',
            '1',
            '--json',
        ],
        cwd=ROOT,
        text=True,
        capture_output=True,
        encoding='utf-8',
        errors='replace',
    )
    try:
        result = json.loads(scanned.stdout)
    except Exception:
        result = {}
    archive_ids = {item.get('id') for item in result.get('archiveCandidates', []) if isinstance(item, dict)}
    stale_ids = {item.get('id') for item in result.get('staleRecipes', []) if isinstance(item, dict)}
    warnings = result.get('warnings') or []
    cases.append(('scan stale recipes reports expected stale recipe', scanned.returncode == 0 and 'stale-old' in stale_ids))
    cases.append(('scan stale recipes keeps pinned out of archive candidates', 'pinned-old' not in archive_ids))
    cases.append(('scan stale recipes warns deprecated missing replacement', any('deprecated-missing' in str(w) for w in warnings)))
    cases.append(('scan stale recipes warns invalid status', any('invalid-status' in str(w) and 'invalid Status' in str(w) for w in warnings)))
    cases.append(('scan stale recipes warns missing active metadata', any('missing-metadata' in str(w) and 'missing required metadata' in str(w) for w in warnings)))
    return cases


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
        bad9 = ROOT / 'Mdfproject/Assets/Scripts/AI/Planning/__precommit_self_test_BadAiExactSpawn.cs'
        bad9.write_text('class BadAiExactSpawn { void X(){ spawner.SpawnMonsterAtExactPositionAsync(data, pos, field); } }')
        try:
            cases.append(('ai direct exact spawn block', any(e[1] == 'ai_direct_monster_spawn' for e in check_file(bad9)[0])))
        finally:
            try:
                bad9.unlink()
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
        bad_py = d / 'bad_hook.py'
        bad_py.write_text('def broken(:\n    pass\n')
        cases.append(('python syntax guardrail helper', python_syntax_error(bad_py) is not None))
        cases.append(('skill frontmatter helper', frontmatter_value(
            '---\nname: mdf-content-feature\ndescription: Use when needed.\n---\n',
            'name',
        ) == 'mdf-content-feature'))
        cases.append(('content workflow guardrails current no block', not check_content_workflow_guardrails()[0]))
        cases.append(('recipe lifecycle guardrails current no block', not check_recipe_lifecycle_guardrails()[0]))
        cases.extend(recipe_lifecycle_self_test_cases(d))
        hook_path = ROOT / '.codex/hooks/user_prompt_submit_reminder.py'
        if hook_path.exists():
            korean_payload = '{"prompt":"\\uc0c8 \\uc720\\ub2db \\ucd94\\uac00"}'
            question_payload = '{"prompt":"what is the AI behavior tree?"}'
            scroll_payload = '{"prompt":"\\uc0c8 \\ubc88\\uac1c \\uc2a4\\ud06c\\ub864 \\ub9cc\\ub4e4\\uc5b4\\uc918"}'
            result_analysis_payload = '{"prompt":"\\uc804\\ud22c \\ud14c\\uc2a4\\ud2b8 \\uacb0\\uacfc \\uc124\\uba85\\ud574\\uc918"}'
            routine_explain_payload = '{"prompt":"\\ucee8\\ud150\\uce20 \\uac1c\\ubc1c \\ub8e8\\ud2f4 \\uc124\\uba85\\ud574\\uc918"}'
            hook_feature = self_test_subprocess([sys.executable, str(hook_path)], input=korean_payload, cwd=ROOT, text=True, capture_output=True, encoding='utf-8', errors='replace')
            hook_question = self_test_subprocess([sys.executable, str(hook_path)], input=question_payload, cwd=ROOT, text=True, capture_output=True, encoding='utf-8', errors='replace')
            hook_scroll = self_test_subprocess([sys.executable, str(hook_path)], input=scroll_payload, cwd=ROOT, text=True, capture_output=True, encoding='utf-8', errors='replace')
            hook_result = self_test_subprocess([sys.executable, str(hook_path)], input=result_analysis_payload, cwd=ROOT, text=True, capture_output=True, encoding='utf-8', errors='replace')
            hook_routine = self_test_subprocess([sys.executable, str(hook_path)], input=routine_explain_payload, cwd=ROOT, text=True, capture_output=True, encoding='utf-8', errors='replace')
            hook_raw_korean = self_test_subprocess(
                [sys.executable, str(hook_path)],
                input=json.dumps({'prompt': '새 유닛 추가해줘'}, ensure_ascii=False).encode('utf-8'),
                cwd=ROOT,
                capture_output=True,
            )
            hook_raw_korean_stdout = hook_raw_korean.stdout.decode('utf-8', errors='replace')
            cases.append(('user prompt hook korean feature route', '[MDF CONTENT FEATURE]' in hook_feature.stdout))
            cases.append(('user prompt hook raw utf8 korean feature route', '[MDF CONTENT FEATURE]' in hook_raw_korean_stdout))
            cases.append(('user prompt hook korean scroll feature route', '[MDF CONTENT FEATURE]' in hook_scroll.stdout))
            cases.append(('user prompt hook pure question generic', '[MDF CONTENT FEATURE]' not in hook_question.stdout and '[MDF HARNESS]' in hook_question.stdout))
            cases.append(('user prompt hook result analysis generic', '[MDF CONTENT FEATURE]' not in hook_result.stdout and '[MDF HARNESS]' in hook_result.stdout))
            cases.append(('user prompt hook routine explanation generic', '[MDF CONTENT FEATURE]' not in hook_routine.stdout and '[MDF HARNESS]' in hook_routine.stdout))
        stop_path = ROOT / '.codex/hooks/stop_verify_gate.py'
        if stop_path.exists():
            env = os.environ.copy()
            env['CODEX_PROJECT_DIR'] = str(d)
            bad_stop = {
                'session_id': 'self-test-stop-bad',
                'last_assistant_message': (
                    '*** Update File: Mdfproject/Assets/Scripts/Foo.cs\n'
                    'Files changed: Foo.cs\nCommands run: precommit, compile, console, tests, E2E profile smoke\n'
                    'Compile PASS; console []\nTests run: EditMode PASS\nE2E profile/case run: smoke\n'
                    'Artifact paths for E2E: artifacts/mp/sample\ncleanupStatus for E2E: FAIL\n'
                    'orphanedPids for E2E: [123]\n[MPTEST] phase=error\n'
                    'Snapshot comparison success\nRemaining risks: none'
                ),
            }
            good_stop = {
                'session_id': 'self-test-stop-good',
                'last_assistant_message': (
                    '*** Update File: Mdfproject/Assets/Scripts/Foo.cs\n'
                    'Files changed: Foo.cs\nCommands run: precommit, compile, console, tests, E2E profile smoke\n'
                    'Compile PASS; console []\nTests run: EditMode PASS\nE2E profile/case run: smoke\n'
                    'Artifact paths for E2E: artifacts/mp/sample\ncleanupStatus for E2E: PASS\n'
                    'orphanedPids for E2E: []\nNo [MPTEST] phase=error\n'
                    'Snapshot comparison success\nRemaining risks: none\n'
                    'Learned recipe: no new reusable recipe discovered'
                ),
            }
            skipped_stop = {
                'session_id': 'self-test-stop-e2e-skipped',
                'last_assistant_message': (
                    '*** Update File: Mdfproject/Assets/Scripts/Foo.cs\n'
                    'Files changed: Mdfproject/Assets/Scripts/Foo.cs\n'
                    'Commands run: py_compile, precommit, unity-cli compile, console, tests\n'
                    'Compile PASS; console []\nTests run: EditMode PASS\n'
                    'E2E skipped: exact reason no multiplayer-visible behavior in this fixture\n'
                    'Remaining risks: none\n'
                    'Learned recipe: no new reusable recipe discovered'
                ),
            }
            stop_bad = self_test_subprocess([sys.executable, str(stop_path)], input=json.dumps(bad_stop), cwd=ROOT, env=env, text=True, capture_output=True, encoding='utf-8', errors='replace')
            stop_bad_repeat = self_test_subprocess([sys.executable, str(stop_path)], input=json.dumps(bad_stop), cwd=ROOT, env=env, text=True, capture_output=True, encoding='utf-8', errors='replace')
            stop_good = self_test_subprocess([sys.executable, str(stop_path)], input=json.dumps(good_stop), cwd=ROOT, env=env, text=True, capture_output=True, encoding='utf-8', errors='replace')
            stop_skipped = self_test_subprocess([sys.executable, str(stop_path)], input=json.dumps(skipped_stop), cwd=ROOT, env=env, text=True, capture_output=True, encoding='utf-8', errors='replace')
            cases.append(('stop gate rejects false e2e pass', '"decision": "block"' in stop_bad.stdout))
            cases.append(('stop gate repeatedly rejects false e2e pass', '"decision": "block"' in stop_bad_repeat.stdout))
            cases.append(('stop gate accepts complete e2e report', stop_good.stdout.strip() == '{}'))
            cases.append(('stop gate accepts exact e2e skipped reason', stop_skipped.stdout.strip() == '{}'))
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
