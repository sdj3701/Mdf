from hook_common import emit, add_context, repo_root
from pathlib import Path

root = repo_root()
recipes = root / 'docs/ai-harness/learned-recipes.md'
content_routine = root / 'docs/ai-harness/content-development-routine.md'
content_skill = root / '.agents/skills/mdf-content-feature/SKILL.md'
parts = ['[MDF HARNESS] Read AGENTS.md before work.']
if recipes.exists():
    parts.append('Search docs/ai-harness/learned-recipes.md and docs/ai-harness/recipes/ before Unity CLI, build, screenshot, Photon, or multiplayer automation work.')
if content_routine.exists() and content_skill.exists():
    parts.append('For short feature requests, use docs/ai-harness/content-development-routine.md and .agents/skills/mdf-content-feature/SKILL.md automatically.')
parts.append('Use docs/ai-harness/archive/codex-full-phase-prompts.md only for explicit harness-bootstrap or full overlay rebuild audit work; it is historical, not the default workflow.')
add_context('SessionStart', '\n'.join(parts))
