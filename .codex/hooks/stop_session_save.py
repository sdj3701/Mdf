from hook_common import read_input, emit, repo_root
from pathlib import Path
import json
from datetime import datetime, timezone


data = read_input()
root = repo_root()
out_dir = root / '.codex' / 'session-state'
out_dir.mkdir(parents=True, exist_ok=True)
sid = str(data.get('session_id') or 'unknown').replace('/', '_').replace('\\', '_')
path = out_dir / f'session-{sid}.json'
summary = data.get('last_assistant_message') or data.get('summary') or ''
path.write_text(json.dumps({
    'saved_at': datetime.now(timezone.utc).isoformat(),
    'session_id': data.get('session_id'),
    'cwd': data.get('cwd'),
    'transcript_path': data.get('transcript_path'),
    'summary': summary[-4000:],
}, ensure_ascii=False, indent=2), encoding='utf-8')
emit({})
