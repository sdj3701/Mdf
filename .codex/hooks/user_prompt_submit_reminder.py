from hook_common import add_context, read_input


GENERIC_REMINDER = (
    '[MDF HARNESS] For non-trivial code work: read AGENTS.md, use the feature loop, '
    'search learned-recipes, and do not claim PASS without command output + artifacts. '
    'Use mdf_* subagents explicitly when needed.'
)

CONTENT_FEATURE_REMINDER = (
    '[MDF CONTENT FEATURE]\n'
    'This looks like a gameplay/content/AI feature request. Use '
    '.agents/skills/mdf-content-feature/SKILL.md and '
    'docs/ai-harness/content-development-routine.md automatically. Do not ask the '
    'user to paste the long template. Classify the feature, map State '
    'Authority/command/snapshot impact, choose the smallest relevant matrix '
    'profile, and require artifacts plus cleanupStatus=PASS/orphanedPids=[] for '
    'E2E PASS. Use mdf_* subagents explicitly when needed.'
)

PROMPT_KEYS = {
    'prompt',
    'user_prompt',
    'userPrompt',
    'message',
    'text',
    'content',
    'last_user_message',
    'lastUserMessage',
}

ACTION_TERMS = [
    'add',
    'implement',
    'create',
    'change',
    'improve',
    'balance',
    'fix',
    'make',
    '\ucd94\uac00',
    '\ub9cc\ub4e4\uc5b4',
    '\uad6c\ud604',
    '\uc218\uc815',
    '\uac1c\uc120',
    '\ubc14\uafd4',
    '\ubc38\ub7f0\uc2a4',
    '\uc0dd\uc131',
    '\uace0\uccd0',
]

DOMAIN_TERMS = [
    'unit',
    'monster',
    'scroll',
    'augment',
    'skill',
    'behavior tree',
    'ai',
    'shop',
    'battle',
    'boss',
    'round',
    'content',
    'feature',
    '3 rounds',
    'game to end',
    'endurance',
    '\uc720\ub2db',
    '\ubaac\uc2a4\ud130',
    '\uc2a4\ud06c\ub864',
    '\uc99d\uac15',
    '\uc2a4\ud0ac',
    '\ud589\ub3d9\ud2b8\ub9ac',
    '\uc0c1\uc810',
    '\uc804\ud22c',
    '\ubcf4\uc2a4',
    '\ub77c\uc6b4\ub4dc',
    '\ubbf8\ub85c',
    '\ubcbd',
    '\ubc30\uce58',
    '3\ub77c\uc6b4\ub4dc',
    '\ub05d\uae4c\uc9c0',
    '\uac8c\uc784\uc624\ubc84',
    '\ucee8\ud150\uce20',
]

QUESTION_TERMS = [
    'what is',
    'explain',
    'why',
    'how does',
    '\ubb50\uc57c',
    '\uc65c',
    '\uc124\uba85',
    '\ubd84\uc11d',
    '\uacb0\uacfc',
    '\uc758\ubbf8',
]


def collect_prompt_text(value, depth=0):
    if depth > 4 or value is None:
        return ''
    if isinstance(value, str):
        return value
    if isinstance(value, list):
        return ' '.join(collect_prompt_text(item, depth + 1) for item in value)
    if isinstance(value, dict):
        parts = []
        for key, item in value.items():
            if key in PROMPT_KEYS or depth > 0:
                parts.append(collect_prompt_text(item, depth + 1))
        if not parts:
            parts = [collect_prompt_text(item, depth + 1) for item in value.values()]
        return ' '.join(part for part in parts if part)
    return ''


def looks_like_feature_request(prompt):
    text = ' '.join(str(prompt or '').lower().split())
    if not text:
        return False
    if any(term in text for term in QUESTION_TERMS):
        return False
    has_action = any(term.lower() in text for term in ACTION_TERMS)
    has_domain = any(term.lower() in text for term in DOMAIN_TERMS)
    return has_action and has_domain


def main():
    try:
        data = read_input()
        prompt = collect_prompt_text(data)
        add_context(
            'UserPromptSubmit',
            CONTENT_FEATURE_REMINDER if looks_like_feature_request(prompt) else GENERIC_REMINDER,
        )
    except Exception:
        add_context('UserPromptSubmit', GENERIC_REMINDER)


main()
