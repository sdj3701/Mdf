from hook_common import read_input, emit, deny_pre_tool, tool_command
import re


data = read_input()
cmd = tool_command(data)

BLOCKS = [
    (r'git\s+commit\b.*(--no-verify|\s-n\b)', 'MDF harness blocks git commit --no-verify / -n. Fix checks instead of bypassing them.'),
    (r'git\s+clean\s+-x?f?d?f?\b|git\s+clean\b.*-[^\s]*x[^\s]*f[^\s]*d', 'Destructive git clean -xfd is blocked. Ask for explicit approval with exact paths.'),
    (r'(rm\s+-rf|remove-item\b.*-(recurse|r)\b|rmdir\b.*(/s|-r|/q)|del\b.*(/s|-r))[\s\S]{0,200}(Mdfproject[\\/])?(Assets|ProjectSettings|Packages|Library)\b', 'Destructive Unity project path removal blocked. Ask for explicit approval with exact paths.'),
    (r'(sed|perl|python|ruby|node|tee|cat\s+>|rm|mv|cp|move-item|copy-item|set-content|add-content|out-file|remove-item)[\s\S]{0,200}Mdfproject[\\/]Assets[\\/]Photon[\\/]Fusion[\\/]', 'Photon Fusion vendor edits are blocked unless explicitly approved.'),
    (r'(sed|perl|python|ruby|node|tee|cat\s+>|rm|mv|cp|move-item|copy-item|set-content|add-content|out-file|remove-item)[\s\S]{0,200}Mdfproject[\\/]Assets[\\/](Firebase|TextMesh Pro|Samples|Toon|Resource[\\/]Fonts[\\/]TextMesh Pro|Resource[\\/]Shaders[\\/]JMO Assets[\\/]Toony Colors Pro)', 'Vendor/sample edits are blocked unless explicitly approved.'),
]

for pat, reason in BLOCKS:
    if re.search(pat, cmd, re.I):
        deny_pre_tool(reason)
        raise SystemExit(0)

emit({})
