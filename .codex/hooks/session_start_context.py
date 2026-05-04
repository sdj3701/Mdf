from hook_common import emit, add_context, repo_root
from pathlib import Path

root = repo_root()
recipes = root / 'docs/ai-harness/learned-recipes.md'
phase = root / 'docs/ai-harness/codex-full-phase-prompts.md'
parts = ['[MDF HARNESS] Read AGENTS.md before work.']
if recipes.exists():
    parts.append('Search docs/ai-harness/learned-recipes.md before Unity CLI, build, screenshot, Photon, or multiplayer automation work.')
if phase.exists():
    parts.append('Use docs/ai-harness/codex-full-phase-prompts.md for phase-by-phase harness implementation.')
add_context('SessionStart', '\n'.join(parts))
