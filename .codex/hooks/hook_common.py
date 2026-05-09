import json
import os
import subprocess
import sys
from pathlib import Path


def read_input():
    try:
        raw_bytes = sys.stdin.buffer.read()
        if not raw_bytes.strip():
            return {}
        for encoding in ['utf-8-sig', 'utf-8', sys.stdin.encoding or 'utf-8']:
            try:
                raw = raw_bytes.decode(encoding)
                return json.loads(raw) if raw.strip() else {}
            except UnicodeDecodeError:
                continue
            except json.JSONDecodeError:
                continue
        raw = raw_bytes.decode('utf-8', errors='replace')
        return json.loads(raw) if raw.strip() else {}
    except Exception:
        return {}


def emit(obj):
    print(json.dumps(obj, ensure_ascii=False))


def repo_root():
    env = os.environ.get('CODEX_PROJECT_DIR') or os.environ.get('GIT_WORK_TREE')
    if env:
        return Path(env).resolve()
    try:
        out = subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip()
        if out:
            return Path(out).resolve()
    except Exception:
        pass
    return Path(os.environ.get('PWD') or '.').resolve()


def rel(path):
    try:
        return str(Path(path).resolve().relative_to(repo_root()))
    except Exception:
        return str(path)


def tool_command(data):
    ti = data.get('tool_input') or {}
    return ti.get('command') or ti.get('cmd') or ''


def deny_pre_tool(reason):
    emit({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    })


def add_context(event, text):
    emit({"hookSpecificOutput": {"hookEventName": event, "additionalContext": text}})
