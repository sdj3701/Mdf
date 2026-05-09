## precommit-windows-ascii-output: Keep hook output CP949-safe

Status: active
Pinned: false
Category: harness
Created: 2026-05-05
Last used: 2026-05-10
Last verified: 2026-05-05
Use count: 2
Review after: 2026-08-03
Triggers: precommit, hooks, Windows, UnicodeEncodeError
Applies to: Windows PowerShell, Python 3.11, tools/harness/precommit.py
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
PowerShell using the default CP949 console can raise `UnicodeEncodeError` when Python hook output contains emoji characters.

Recipe:
- Keep `tools/harness/precommit.py` output ASCII-only for `BLOCK`, `WARN`, and summary lines.
- If a hook must print non-ASCII text, first verify the active console encoding or force UTF-8 explicitly.

Verification:
- `python tools/harness/precommit.py --self-test` passed after replacing emoji prefixes with ASCII.
- `python tools/harness/precommit.py --all` completed and reported warnings instead of crashing.

Pitfalls:
- Do not treat hook output styling as harmless; failed output encoding can block verification before checks finish.

Lifecycle notes:
- 2026-05-10: Consulted while keeping hook and precommit output ASCII-safe during hook noise cleanup.
