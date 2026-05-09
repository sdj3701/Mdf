from hook_common import read_input, emit, add_context, tool_command
import re


data = read_input()
cmd = tool_command(data)
# For Edit/Write/apply_patch, command is empty; inspect file path when available.
ti = data.get('tool_input') or {}
path = ti.get('file_path') or ti.get('filePath') or ti.get('path') or ''
cmd_lower = cmd.lower()
path_lower = path.lower()
text = (cmd + ' ' + path).lower()


def is_low_signal_read(command: str) -> bool:
    return bool(re.match(
        r'^\s*(rg|grep|findstr|select-string|get-content|gc|cat|type|'
        r'git\s+(show|diff|status|ls-files|grep)|ls|dir|get-childitem|pwd)\b',
        command,
        re.IGNORECASE,
    ))


def is_high_signal_command(command_text: str) -> bool:
    patterns = [
        r'\bunity-cli\b',
        r'\bscreenshot\b',
        r'\brun[_-]matrix\b',
        r'\brun[_-]editor[_-]host\b',
        r'\brun[_-]build[_-]host\b',
        r'\bbuild[_-]player\b',
        r'\bmp_(build_player|screenshot|dump_state|assert_state|command)\b',
        r'\bmptest\b',
        r'--mptest\b',
        r'\bmpautomation(token)?\b',
        r'\bautomation[-_ ]?server\b',
        r'\bphoton\b',
        r'\bfusion\b',
        r'\bhost[-_ ]?migration\b',
        r'\bbuildautomation\b',
        r'\breserialize\b',
    ]
    return any(re.search(pattern, command_text, re.IGNORECASE) for pattern in patterns)


def is_asset_edit(candidate_path: str, command: str) -> bool:
    if command:
        return False
    return candidate_path.endswith(('.prefab', '.unity', '.asset', '.mat', '.controller'))


if (cmd_lower and not is_low_signal_read(cmd_lower) and is_high_signal_command(text)) or is_asset_edit(path_lower, cmd_lower):
    add_context(
        'PostToolUse',
        '[MDF RECIPE LIFECYCLE] If this used an existing learned recipe, run '
        '`python tools/harness/recipes/touch_recipe.py --id <recipe-id> --used`. '
        'If this command/test/artifact verified a recipe, use `--verified --artifact <path>`. '
        'Do not mark Last verified without evidence. If no clear recipe id applies, say no new reusable recipe was discovered in the final report.'
    )
else:
    emit({})
