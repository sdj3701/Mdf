#!/usr/bin/env python3
"""Validate MDF harness overlay references."""
from __future__ import annotations
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
REQUIRED = [
    'AGENTS.md',
    '.agent/rules/projectrull.md',
    'docs/ai-harness/index.md',
    'docs/ai-harness/project-structure.md',
    'docs/ai-harness/developer-onboarding.md',
    'docs/ai-harness/content-development-routine.md',
    'docs/ai-harness/verification-profile-selector.md',
    'docs/ai-harness/feature-implementation-loop.md',
    'docs/ai-harness/fusion-sync-rules.md',
    'docs/ai-harness/unity-cli-recipes.md',
    'docs/ai-harness/mp-test-protocol.md',
    'docs/ai-harness/automation-server-contract.md',
    'docs/ai-harness/state-snapshot-schema.md',
    'docs/ai-harness/host-migration-test-plan.md',
    'docs/ai-harness/randomized-progression-test-plan.md',
    'docs/ai-harness/human-bot-driver-design.md',
    'docs/ai-harness/learned-recipes.md',
    '.codex/config.toml',
    '.codex/hooks.json',
    '.codex/agents/mdf_code_mapper.toml',
    '.codex/agents/mdf_fusion_reviewer.toml',
    '.codex/agents/mdf_unity_verifier.toml',
    '.codex/agents/mdf_mp_test_runner.toml',
    '.codex/agents/mdf_asset_guard.toml',
    '.agents/skills/use-learned-recipes/SKILL.md',
    '.agents/skills/capture-learning/SKILL.md',
    '.agents/skills/verify-unity/SKILL.md',
    '.agents/skills/review-fusion-sync/SKILL.md',
    '.agents/skills/mp-harness-test/SKILL.md',
    '.agents/skills/build-player/SKILL.md',
    '.agents/skills/asset-safe-edit/SKILL.md',
    '.agents/skills/entropy-gc/SKILL.md',
    '.agents/skills/feature-loop/SKILL.md',
    '.agents/skills/mdf-content-feature/SKILL.md',
    'tools/harness/precommit.py',
    'SETUP_MDF_HARNESS.bat',
    'tools/harness/install_git_hooks.py',
    'tools/harness/bootstrap_dev_env.py',
    'tools/harness/bootstrap_harness_windows.py',
    'tools/harness/mp/select_verification_profile.py',
]

HISTORICAL_OPTIONAL = [
    'docs/ai-harness/archive/codex-full-phase-prompts.md',
    'docs/ai-harness/archive/codex-phase-prompts.md',
    'docs/ai-harness/archive/implementation-phases.md',
    'docs/ai-harness/archive/review-findings.md',
]

ACTIVE_DOCS = [
    'AGENTS.md',
    'docs/ai-harness/index.md',
    'docs/ai-harness/content-development-routine.md',
    'docs/ai-harness/feature-implementation-loop.md',
    'docs/ai-harness/verification-profile-selector.md',
    '.codex/hooks/session_start_context.py',
]

errors = []
for rel in REQUIRED:
    if not (ROOT / rel).exists():
        errors.append(f'missing: {rel}')

active_text = ''
for rel in ACTIVE_DOCS:
    path = ROOT / rel
    if path.exists():
        active_text += '\n' + path.read_text(encoding='utf-8', errors='ignore').lower()
for rel in HISTORICAL_OPTIONAL:
    missing = not (ROOT / rel).exists()
    if not missing:
        continue
    marker = rel.lower()
    if marker in active_text and 'source-of-truth' in active_text:
        errors.append(f'missing historical doc still referenced as current source-of-truth: {rel}')

agents = ROOT / 'AGENTS.md'
if agents.exists():
    lines = agents.read_text(encoding='utf-8', errors='ignore').splitlines()
    if len(lines) > 70:
        errors.append(f'AGENTS.md line count {len(lines)} > 70')

hooks = ROOT / '.codex/hooks.json'
if hooks.exists():
    try:
        json.loads(hooks.read_text(encoding='utf-8'))
    except Exception as e:
        errors.append(f'hooks.json invalid JSON: {e}')

for toml in (ROOT / '.codex/agents').glob('*.toml'):
    text = toml.read_text(encoding='utf-8', errors='ignore')
    for key in ['name', 'description', 'developer_instructions']:
        if key not in text:
            errors.append(f'{toml.relative_to(ROOT)} missing {key}')

if errors:
    print('MDF overlay validation: FAIL')
    for e in errors:
        print(' -', e)
    sys.exit(1)
print('MDF overlay validation: PASS')
print(f'Checked {len(REQUIRED)} required paths; AGENTS.md <= 70 lines.')
