# Stop wrapper: save session state, then apply final evidence gate.
# Codex Stop hooks do not support additionalContext; use decision:block only when strict mode requires continuation.
import subprocess
import sys
from pathlib import Path

raw = sys.stdin.read()
root = Path(__file__).resolve().parents[2]
python = sys.executable or 'python3'

save = root / '.codex/hooks/stop_session_save.py'
gate = root / '.codex/hooks/stop_verify_gate.py'

try:
    subprocess.run([python, str(save)], input=raw, text=True, capture_output=True, timeout=20)
except Exception:
    pass
try:
    res = subprocess.run([python, str(gate)], input=raw, text=True, capture_output=True, timeout=20)
    out = (res.stdout or '{}').strip()
    print(out if out else '{}')
except Exception:
    print('{}')
