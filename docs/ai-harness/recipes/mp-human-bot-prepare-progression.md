## mp-human-bot-prepare-progression: Prove client bot progress by durable delta

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: HumanBot, `/bot/start`, `/bot/status`, selected augment hash, random-aware comparison
Applies to: `tools/harness/mp/run_human_bot_prepare_progression.py`, Phase 20 HumanBot prepare progression
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
`test.bot.commandsIssued` only proves the bot submitted a request. Phase 20 needs proof that State Authority accepted a meaningful command and that the resulting randomized state replicated to host and client.

Recipe:
- Launch build host and build client through the real lobby flow, not direct Game autostart.
- Start the client with `--mpHumanBot --mpBotPersona balanced`, then pause it before Game load to capture a clean `before-bot` checkpoint.
- Resume with `/bot/start` and bounded `--mpBotMaxCommands`; for first-command proof use `maxCommands=1` to avoid extra policy work after the accepted delta.
- Treat a command as accepted only when a host snapshot shows a durable delta from the checkpoint, such as `augment.selectedCount/hash`, shop revision/hash, gold, wall count/hash, or placed-unit hash.
- Require host/client snapshot comparison success after the delta, and require the bot player to remain `isConnected=true`, `isAI=false`, and `ai.controllerRegistered=false`.
- Use longer automation request timeouts for this scenario; the main thread can be briefly busy while bot policy or Game scene setup runs.

Verification:
- `artifacts/mp/20260505-171515-human-bot-prepare/human-bot-prepare-assertions.json` reported `success=true`.
- `accepted-command-evidence.json` proved `SelectAugment`, `commandsIssued=1`, and `augment.selectedCount 0 -> 1` for bot player `1`.
- `comparison-latest.json` and `random-outcome-summary.json` showed host/client agreement for same-player shop, augment, and field hashes.
- Build used `artifacts/builds/20260505-171218/MDF-MPTest.exe`, a Development Build with launch-smoke evidence.

Pitfalls:
- `NotifyAugmentSelectedCommand` must update non-server `chosenAugments`; otherwise host selected augment hashes diverge from client snapshots after an accepted selection.
- `parse_mptest_logs.py` splits multiple `[MPTEST]` markers from the same Unity log line and tolerates truncated quoted values, so `mptest.timeline.jsonl` should not produce `parseError` rows from normal Unity log concatenation. It stores the artifact file path as `logSource` so event fields such as `source=[Player:2]` remain intact. Still check raw logs for `result=fail` or `phase=error`.
- Current command sequence fields may be `null`; until accepted command journaling exists, use durable snapshot deltas as the acceptance proof.
