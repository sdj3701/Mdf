## windows-e2e-orphan-pressure-v1: Treat high orphan counts as an environment blocker

Status: active
Pinned: true
Category: cleanup, security
Created: 2026-05-08
Last used: 2026-05-10
Last verified: 2026-05-08
Use count: 2
Review after: 2026-08-06
Triggers: many live `MDF-MPTest.exe` processes, D3D resource errors, lobby start timeouts, cleanup report token redaction
Applies to: `tools/harness/mp/launch_player.py`, cleanup reports, final audit E2E retries
Verified by: see Verification section below; migrated from old Status: verified with environment blocker; blocker note preserved in recipe body
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
When many old `MDF-MPTest.exe` processes remain alive, new E2E runs can fail before gameplay starts. In the final audit, 75 live MDF test players remained after cleanup attempts. A fresh HumanBot battle retry then failed with D3D `0x887A0005` resource creation errors, host `NetworkManager.JoinLobby` null reference after a duplicate `NetworkRunner`, missing `GameManagers`, and zero bot commands. This is an environment cleanup blocker, not Prepare v2 or battle command gameplay evidence.

Recipe:
- Before optional heavy E2E, count live players with `Get-Process -Name MDF-MPTest` and `Get-CimInstance Win32_Process -Filter "Name = 'MDF-MPTest.exe'"`.
- If the count is high, do not continue piling on matrix, Host Migration, or seed sweep cases unless the goal is specifically to reproduce cleanup pressure. Record `NEEDS_ENVIRONMENT` with the count, failed cleanup methods, and the last artifact path.
- `Stop-Process -Name MDF-MPTest -Force` can be insufficient in this environment; the observed count stayed `75 -> 75`. Do not claim the environment is clean from the command alone.
- Cleanup reports must redact both current-process secrets and unrelated baseline process command lines. Use generic CLI argument redaction for `--mpAutomationToken` and `--mpConnectionToken`, not only exact per-run secret replacement.
- When generated cleanup reports were written before the generic redaction fix, scrub ignored artifact JSON before sharing bundles or logs.

Verification:
- `python -m py_compile tools\harness\mp\launch_player.py` PASS after adding generic token redaction.
- A direct `_redact_text` probe converted `--mpAutomationToken abc --mpConnectionToken=def` to redacted token placeholders.
- `Select-String -Path artifacts\**\*.json -Pattern '--mpAutomationToken\s+[^<\s]|--mpConnectionToken\s+[^<\s]|--mpAutomationToken=[^<\s]|--mpConnectionToken=[^<\s]'` returned no matches after scrubbing generated JSON artifacts.
- `artifacts/mp/20260507-182237-human-bot-battle-progression/result.json` recorded functional failure from environment startup pressure: `host_start_timeout`, `client_join_timeout`, `GameManagers` missing, `commandsIssued=0`, and cleanup `NEEDS_ENVIRONMENT` with orphaned PIDs `28024,9812`.

Pitfalls:
- Do not use a failed high-pressure retry to regress the previously clean functional PASS evidence. Keep the last known functional artifacts and the environment-blocked retry separate in reports.
- Do not print raw live process command lines in final summaries; redact token arguments before showing process diagnostics.
