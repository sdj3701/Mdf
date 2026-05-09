from hook_common import read_input, emit, repo_root
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import os
import re


REQUIRED_REPORT_FIELDS = [
    ('files changed', ['files changed', 'changed files']),
    ('commands run', ['commands run', 'commands executed']),
    ('compile/console result', ['compile', 'console']),
    ('tests run or skipped reason', ['tests run', 'test results', 'skipped tests', 'tests skipped', 'reason skipped']),
    ('E2E profile/case run or skipped reason', ['e2e', 'profile', 'case', 'skipped e2e']),
    ('remaining risks', ['remaining risk', 'remaining risks']),
    ('learned recipe updated/touched or no new reusable recipe discovered', [
        'learned recipe', 'learned-recipes', 'recipe touched', 'recipe updated',
        'no new reusable recipe', 'no reusable recipe discovered', 'no reusable method',
    ]),
]


def read_transcript_text(data):
    path = data.get('transcript_path')
    if not path:
        return ''
    try:
        p = Path(path)
        if not p.exists() or p.stat().st_size > 10_000_000:
            return ''
        return p.read_text(encoding='utf-8', errors='ignore')[-250000:]
    except Exception:
        return ''


def patched_paths(text):
    paths = []
    for match in re.finditer(r'\*\*\*\s+(?:Add|Update|Delete)\s+File:\s+([^\r\n]+)', text):
        paths.append(match.group(1).strip().replace('\\', '/'))
    return paths


def is_runtime_feature_path(path):
    if path.startswith('Mdfproject/Assets/Scripts/') and path.endswith('.cs'):
        return True
    return path.endswith(('.prefab', '.unity', '.asset', '.mat', '.controller', '.anim'))


def is_docs_or_tooling_path(path):
    return (
        path.startswith('docs/')
        or path.startswith('.agents/')
        or path.startswith('.codex/')
        or path.startswith('tools/harness/')
        or path == 'AGENTS.md'
    )


def ran_e2e_or_build(text):
    return bool(re.search(
        r'tools[/\\]harness[/\\]mp[/\\](run_matrix|run_human_bot|run_3round|run_progressed|build_player)\.py|--mpTest|mp_build_player',
        text,
        re.I,
    ))


def likely_feature_work(data, transcript, msg):
    combined = f'{transcript}\n{msg}'
    paths = patched_paths(combined)
    if any(is_runtime_feature_path(path) for path in paths):
        return True
    if ran_e2e_or_build(combined):
        return True
    if '[MDF CONTENT FEATURE]' in combined:
        return True
    docs_feature_paths = [
        path for path in paths
        if path.startswith('docs/ai-design/')
        or path in {
            'docs/ai-harness/content-development-routine.md',
            'docs/ai-harness/feature-implementation-loop.md',
            'docs/ai-harness/verification-profile-selector.md',
        }
    ]
    docs_only = paths and all(is_docs_or_tooling_path(path) for path in paths)
    if docs_feature_paths and not docs_only:
        return True
    return False


def missing_report_fields(msg):
    normalized = msg.lower()
    missing = []
    for label, tokens in REQUIRED_REPORT_FIELDS:
        if not any(token in normalized for token in tokens):
            missing.append(label)
    e2e_skipped = e2e_skip_reason_present(normalized)
    e2e_skip_mentioned = e2e_skip_mentioned_without_reason(normalized)
    if e2e_skip_mentioned and not e2e_skipped:
        missing.append('E2E skipped exact reason')
    if not e2e_skipped:
        if not any(token in normalized for token in ['artifact', 'artifacts']):
            missing.append('artifact paths for E2E')
        if 'cleanupstatus' not in normalized:
            missing.append('cleanupStatus for E2E')
        elif not re.search(r'cleanupstatus(?:\s+for\s+e2e)?\s*(=|:)\s*pass\b', normalized):
            missing.append('cleanupStatus=PASS for E2E')
        if 'orphanedpids' not in normalized:
            missing.append('orphanedPids for E2E')
        elif not re.search(r'orphanedpids(?:\s+for\s+e2e)?\s*(=|:)\s*\[\s*\]', normalized):
            missing.append('orphanedPids=[] for E2E')
        if re.search(r'(?<!no\s)(?<!no\s\[mptest\]\s)phase=error', normalized):
            missing.append('no [MPTEST] phase=error')
        if not any(token in normalized for token in [
            'no [mptest] phase=error',
            'no mptest phase=error',
            'no phase=error',
            'phase=error: none',
        ]):
            missing.append('no [MPTEST] phase=error statement')
        if not any(token in normalized for token in [
            'snapshot comparison success',
            'snapshot comparison: pass',
            'snapshot comparison pass',
            'snapshot comparisons passed',
            'comparison success',
            'comparison passed',
        ]):
            missing.append('snapshot comparison success')
    if 'needs_environment' in normalized and not re.search(
        r'needs_environment.{0,160}(because|reason|blocked|orphan|no unity|environment|timeout)',
        normalized,
        re.S,
    ):
        missing.append('exact NEEDS_ENVIRONMENT reason')
    return missing


def e2e_skip_mentioned_without_reason(normalized):
    return any(token in normalized for token in [
        'skipped e2e',
        'e2e skipped',
        'e2e profile/case skipped',
        'e2e not run',
    ])


def e2e_skip_reason_present(normalized):
    if not e2e_skip_mentioned_without_reason(normalized):
        return False
    return bool(re.search(
        r'(e2e(?:\s+profile/case)?\s+(?:skipped|not run)|skipped\s+e2e).{0,120}(reason|because|:|-)\s*\S',
        normalized,
        re.S,
    ))


def hard_semantic_failures(msg):
    normalized = msg.lower()
    e2e_skipped = e2e_skip_reason_present(normalized)
    if e2e_skipped:
        return []
    failures = []
    if 'cleanupstatus' in normalized and not re.search(r'cleanupstatus(?:\s+for\s+e2e)?\s*(=|:)\s*pass\b', normalized):
        failures.append('cleanupStatus=PASS for E2E')
    if 'orphanedpids' in normalized and not re.search(r'orphanedpids(?:\s+for\s+e2e)?\s*(=|:)\s*\[\s*\]', normalized):
        failures.append('orphanedPids=[] for E2E')
    if re.search(r'(?<!no\s)(?<!no\s\[mptest\]\s)phase=error', normalized):
        failures.append('no [MPTEST] phase=error')
    if any(token in normalized for token in [
        'snapshot comparison fail',
        'snapshot comparison: fail',
        'snapshot comparison failed',
        'comparison failed',
    ]):
        failures.append('snapshot comparison success')
    return failures


def block_once(data, reason):
    root = repo_root()
    state_dir = root / '.codex' / 'session-state'
    try:
        state_dir.mkdir(parents=True, exist_ok=True)
    except Exception:
        pass
    key_source = str(data.get('session_id') or data.get('transcript_path') or data.get('cwd') or 'unknown')
    key = hashlib.sha1(key_source.encode('utf-8', errors='ignore')).hexdigest()[:16]
    flag = state_dir / f'stop-feature-gate-{key}.json'
    if flag.exists():
        return False
    try:
        flag.write_text(json.dumps({
            'blocked_at': datetime.now(timezone.utc).isoformat(),
            'session_id': data.get('session_id'),
            'reason': reason,
        }, indent=2), encoding='utf-8')
    except Exception:
        pass
    return True


def main():
    data = read_input()
    raw_msg = data.get('last_assistant_message') or ''
    msg = raw_msg.lower()
    transcript = read_transcript_text(data)

    strict = os.environ.get('HARNESS_VERIFY_STRICT') == '1'
    claim = any(k in msg for k in ['pass', 'done', 'implemented', 'complete'])
    needs_evidence = claim and not any(k in msg for k in ['artifact', 'artifacts', 'command output', 'commands run'])
    if strict and needs_evidence:
        emit({
            "decision": "block",
            "reason": "[MDF HARNESS] Before claiming PASS/done, summarize command outputs and artifacts, or explicitly state what could not be run.",
        })
        return

    if not likely_feature_work(data, transcript, raw_msg):
        emit({})
        return

    hard_failures = hard_semantic_failures(raw_msg)
    if hard_failures:
        reason = (
            '[MDF FEATURE GATE] E2E PASS evidence is invalid. Fix or report failure for: '
            + ', '.join(hard_failures)
            + '. Do not claim PASS until E2E evidence is semantically PASS.'
        )
        emit({"decision": "block", "reason": reason})
        return

    missing = missing_report_fields(raw_msg)
    if missing:
        reason = (
            '[MDF FEATURE GATE] Continue and complete the MDF feature-loop final report. '
            'Include: ' + ', '.join(missing) + '. If E2E was not run, state the exact skipped reason; '
            'for environment-blocked E2E use NEEDS_ENVIRONMENT with the concrete blocker.'
        )
        if block_once(data, reason):
            emit({"decision": "block", "reason": reason})
            return

    emit({})


main()
