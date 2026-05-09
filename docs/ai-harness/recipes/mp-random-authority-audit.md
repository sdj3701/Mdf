## mp-random-authority-audit: Harden boundaries, not replay determinism

Status: active
Pinned: true
Category: host-migration, AI/HumanBot, battle
Created: 2026-05-06
Last used: 2026-05-06
Last verified: 2026-05-06
Use count: 1
Review after: 2026-08-04
Triggers: `UnityEngine.Random`, `System.Random`, `Environment.TickCount`, HumanBot random-aware sync, Host Migration random state
Applies to: Phase 25, `docs/ai-harness/random-authority-audit.md`, shop/augment/wall/battle RNG
Verified by: see Verification section below; migrated from old Status: verified-local
Replacement: none
Archive policy: never auto-archive pinned/protected recipe; review only by explicit human direction

Problem:
Random-aware progression only works if durable random outcomes are decided by State Authority and then synced, snapshotted, and migrated. A seed sweep is useful evidence, but it does not prove deterministic replay while shop, augment, wall, battle, survivor boss, and AI planning randomness are still mixed across gameplay and policy code.

Recipe:
- Audit random and time-derived calls with:
  `rg -n "UnityEngine\\.Random|Random\\.Range|Random\\.value|new System\\.Random|System\\.Random|Environment\\.TickCount|DateTime\\.UtcNow|DateTime\\.Now|Guid\\.NewGuid|Stopwatch|Time\\.realtimeSinceStartup" Mdfproject/Assets/Scripts -g "*.cs"`.
- Classify each hit as authoritative gameplay random, client visual/identity random, AI/bot decision pacing, or test-only.
- For authoritative gameplay random, verify the owner, sync path, reconnect path, Host Migration path, and snapshot hash/revision before claiming PASS.
- Prefer narrow client-peer guards on authoritative outcome generators over broad RNG rewrites.
- Keep `UnityEngine.Random.InitState` under `--mpTest` as a diagnostic seed tool; do not claim command replay unless every authoritative RNG source has a ledger or deterministic stream.
- Document remaining un-hashed authoritative risks in `random-authority-audit.md` so later battle phases know what to target.

Verification:
- Phase 25 added `docs/ai-harness/random-authority-audit.md` and guarded shop reroll, augment presentation/selection, and survivor boss pending/target assignment against running non-server client peers.
- `python tools/harness/precommit.py --all` reported `0 errors, 12 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed compilation.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `8/8`.
- Rebuilt Development player at `artifacts/builds/20260505-192828/MDF-MPTest.exe`; launch smoke exited `0`.
- `artifacts/mp/20260505-192927-human-bot-prepare` passed `run_human_bot_prepare_progression.py --seed 1001` with `lastCommandType=SelectAugment`, a durable selected augment hash delta, and no failures.
- `artifacts/mp/20260505-193030-progressed-host-migration-e2e` passed progressed Host Migration after HumanBot state, with `failures=[]`.
- `artifacts/mp/20260505-193157-human-bot-seed-sweep` passed seeds `5101,5102,5103` with `success=true` and `failures=[]`.

Pitfalls:
- Do not weaken Host Migration comparisons with "randomness changed" explanations. Randomness before migration is allowed; divergence after migration is a failure unless gameplay intentionally advanced under a documented test gate.
- Survivor boss assignment and monster spawn positions still need deeper battle-state snapshot coverage if late-game HumanBot tests start exercising those paths.
