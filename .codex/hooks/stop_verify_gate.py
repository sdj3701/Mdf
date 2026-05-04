from hook_common import read_input, emit
import os


data = read_input()
msg = (data.get('last_assistant_message') or '').lower()
strict = os.environ.get('HARNESS_VERIFY_STRICT') == '1'
claim = any(k in msg for k in ['pass', '완료', '성공', 'done', 'implemented'])
needs_evidence = claim and not any(k in msg for k in ['artifact', 'artifacts', '아티팩트', 'command output', '명령 출력', 'commands run', '실행한 명령'])
if strict and needs_evidence:
    emit({"decision": "block", "reason": "[MDF HARNESS] Before claiming PASS/done, summarize command outputs and artifacts, or explicitly state what could not be run."})
else:
    emit({})
