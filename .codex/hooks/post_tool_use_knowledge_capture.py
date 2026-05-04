from hook_common import read_input, emit, add_context, tool_command
import re


data = read_input()
cmd = tool_command(data)
# For Edit/Write/apply_patch, command is empty; inspect file path when available.
ti = data.get('tool_input') or {}
path = ti.get('file_path') or ti.get('filePath') or ti.get('path') or ''
text = (cmd + ' ' + path).lower()
watched = [
    'unity-cli', 'screenshot', 'mp_', 'run_matrix', 'run_editor_host',
    'run_build_host', 'hostmigration', 'host migration', 'photon', 'fusion',
    'buildautomation', 'mptest', 'automationserver', '.prefab', '.unity', '.asset'
]
if any(w.lower() in text for w in watched):
    add_context('PostToolUse', '[MDF HARNESS] If this revealed a reusable command, timing, screenshot, build, Photon, or failure pattern, update docs/ai-harness/learned-recipes.md or the relevant harness doc before final response.')
else:
    emit({})
