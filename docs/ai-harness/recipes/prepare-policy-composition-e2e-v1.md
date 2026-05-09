## prepare-policy-composition-e2e-v1: Freeze prepare flow and compare stable unit semantics

Status: active
Pinned: false
Category: AI/HumanBot
Created: 2026-05-07
Last used: 2026-05-07
Last verified: 2026-05-07
Use count: 1
Review after: 2026-08-05
Triggers: prepare-phase AI/HumanBot economy, buy/reroll tuning, composition-aware purchase scoring
Applies to: `PrepareDecisionPolicy`, `MPTestHumanBotDriver`, `run_human_bot_prepare_progression.py`, `MPTestStateSnapshot`
Verified by: see Verification section below; migrated from old Status: verified
Replacement: none
Archive policy: archive only after explicit review when unused for 180 days and no active docs/scripts reference it

Problem:
Prepare-only HumanBot E2E can be polluted by normal phase timers and by unstable client-local presentation state. A bot that issues many prepare commands may reach Battle or disconnect before the harness captures a stable checkpoint. Unit prefab defaults can also leave client-side star/merge state stale unless registration and destruction paths clean up field registries.

Recipe:
- After host/client reach a clean `Game` prepare checkpoint, call `/test/freezeGameFlow` on both peers before starting the prepare HumanBot. This keeps the test focused on prepare commands while still allowing the bot to emit real client requests.
- Prepare policies should read networked augment/shop snapshots from `PlayerManager` before trusting local `AugmentManager` or `ShopManager` lists. Local presentation lists can lag and cause repeated `SelectAugment` or same-slot `BuyUnit`.
- For prepare snapshots, compare stable unit semantics. Use UnitData/star multisets and avoid NetworkObject IDs, exact HP, battle-only skill readiness, or local placement details that are not the purpose of the economy test. Keep battle snapshots stricter for battle state.
- When `RPC_RegisterUnitAt` receives a unit whose prefab already has `UnitData`, still reinitialize if the authoritative star differs. Otherwise a star-2 shop purchase can remain star-1 on clients.
- On `Unit.OnDestroy`, remove the unit from the owner `FieldManager` registry so client snapshots do not retain despawned merge ingredients after State Authority combines units.
- Treat wall focus as a late prepare action. Do not run expensive maze/wall planning until the basic target composition is actually satisfied; otherwise HumanBot can block the client main thread or compare unsynced wall presentation instead of the buy/reroll behavior under test.

Verification:
- `python tools/harness/precommit.py --all` passed with `0 errors, 0 warnings`.
- `unity-cli --project Mdfproject editor refresh --compile` completed.
- `unity-cli --project Mdfproject console --type error --stacktrace user` returned `[]`.
- `unity-cli --project Mdfproject test --mode EditMode` passed `43/43`.
- `artifacts/builds/20260507-151458/MDF-MPTest.exe` passed prepare E2E functional assertions for balanced/shop/unit/maze and seed sweep `5101,5102,5103`.

Prepare v2 functional PASS evidence:
- Build: `artifacts/builds/20260507-151458/MDF-MPTest.exe` (`build-metadata.json` result `Succeeded`, Development, AllowDebugging).
- Balanced artifact `artifacts/mp/20260507-151658-human-bot-prepare`: `BuyUnit=4`, `RerollShop=1`, first reroll `soldSlotCount=4`, `rerollBeforeThreeSold=false`, final composition `M1/R3/H0`, final field unit count `4`.
- Shop artifact `artifacts/mp/20260507-151819-human-bot-prepare`: `BuyUnit=4`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M2/R2/H0`, final field unit count `4`.
- Unit artifact `artifacts/mp/20260507-151937-human-bot-prepare`: `BuyUnit=4`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M2/R1/H1`, final field unit count `4`.
- Maze artifact `artifacts/mp/20260507-151539-human-bot-prepare`: `BuyUnit=5`, `RerollShop=0`, `rerollBeforeThreeSold=false`, final composition `M1/R2/H2`, final field unit count `5`. Maze did not wall-only while the field unit count was zero.
- Seed sweep artifact `artifacts/mp/20260507-152054-human-bot-seed-sweep`: seeds `5101,5102,5103`, aggregate `BuyUnit=15`, `RerollShop=3`, first reroll `soldSlotCount=3`, `rerollBeforeThreeSold=false`, final field unit total max `4`.
- The target composition `M2/R2/H1` is soft scoring, not a hard pass condition. Do not fail `M1/R3/H0` or `M2/R2/H0` when no healer appeared in the available shop sequence. A future improvement is explicit `UnitData` AI role metadata; current healer detection is name/skill-token heuristic via `UnitCompositionAnalyzer`.
- Cleanup is separate from functional PASS. In this batch, E2E cleanup did not reliably terminate `MDF-MPTest.exe`; `Stop-Process`, CIM terminate, and `/quit` could fail or time out. Treat this as cleanup `NEEDS_ENVIRONMENT` / harness hardening, not a Prepare gameplay failure.
- Next action: harden E2E process cleanup and split matrix profiles so smoke/regression/random-aware/nightly runs have explicit cost and cleanup reporting.

Pitfalls:
- Do not treat `build-host-quit.json` or `build-client-quit.json` with `success=true` as proof the player process exited; it only proves the automation endpoint accepted a quit request. For final artifact review, also check live processes, for example `Get-CimInstance Win32_Process -Filter "name = 'MDF-MPTest.exe'" | Where-Object { $_.CommandLine -match '<artifact timestamp>' } | Select-Object ProcessId,CommandLine`. A clean `result.json` plus live peer processes or a missing quit artifact is a cleanup failure until the harness records actual process exit or kills leftovers.
