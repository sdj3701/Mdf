from hook_common import read_input, deny_pre_tool, emit, tool_command
import re


data = read_input()
cmd = tool_command(data)

if re.search(
    r'--no-verify|git\s+clean\b.*-[^\s]*x[^\s]*f[^\s]*d|'
    r'(rm\s+-rf|remove-item\b.*-(recurse|r)\b|rmdir\b.*(/s|-r|/q)|del\b.*(/s|-r)).*(Assets|ProjectSettings|Packages|Library)',
    cmd,
    re.I,
):
    deny_pre_tool('MDF harness does not approve bypass/destructive requests without explicit user instruction naming exact paths.')
else:
    emit({})
